using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.CareerService.Application.Interfaces;
using Legacy.Maliev.CareerService.Data;
using Legacy.Maliev.CareerService.Domain;
using Maliev.Aspire.ServiceDefaults.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Moq;
using Testcontainers.PostgreSql;

namespace Legacy.Maliev.CareerService.Tests.Startup;

public sealed class CareerStartupLoggingTests
{
    private const string Sensitive = "candidate-private@example.invalid";

    [Theory]
    [InlineData("/Jobs/job-opening-status", "PostgresException")]
    [InlineData("/Jobs/acceptance-save", "DbUpdateException")]
    public async Task ProductionDatabaseFailure_DoesNotEmitProviderExceptionTextOrStacks(string path, string exceptionType)
    {
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync();
        // Intentionally no schema: a real read fails without writing application data.
        using var factory = new CareerFactory(postgres.GetConnectionString());
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("PostgresException", body);
        using var failure = factory.Failure();
        Assert.Equal(exceptionType, failure.RootElement.GetProperty("State").GetProperty("ExceptionType").GetString());
        Assert.All(factory.Logs.Lines, line =>
        {
            Assert.DoesNotContain("42P01", line);
            Assert.DoesNotContain("does not exist", line);
            Assert.DoesNotContain("Npgsql.Internal", line);
            Assert.DoesNotContain("SELECT", line);
            Assert.DoesNotContain(Sensitive, line);
        });
    }

    [Fact]
    public async Task ProductionControllerFailure_ReturnsSafe500AndCorrelatedRedactedCriticalEvent()
    {
        using var factory = new CareerFactory();
        factory.Career.Setup(service => service.GetOfferByIdAsync(817263, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception(Sensitive));
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/Jobs/817263?search={Sensitive}");
        request.Headers.Add("traceparent", "00-0123456789abcdef0123456789abcdef-0123456789abcdef-01");
        request.Headers.Add("X-Correlation-ID", "career-acceptance-500");
        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("An internal server error occurred", document.RootElement.GetProperty("error").GetString());
        Assert.Equal(500, document.RootElement.GetProperty("statusCode").GetInt32());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("details").ValueKind);
        Assert.DoesNotContain(Sensitive, body);
        Assert.DoesNotContain("Exception", body);
        Assert.Equal("career-acceptance-500", Assert.Single(response.Headers.GetValues("X-Correlation-ID")));
        using var failure = factory.Failure();
        var root = failure.RootElement;
        var state = root.GetProperty("State");
        Assert.Equal("Critical", root.GetProperty("LogLevel").GetString());
        Assert.Equal("CRITICAL", root.GetProperty("severity").GetString());
        Assert.Equal("Legacy.Maliev.CareerService.Api", state.GetProperty("Service").GetString());
        Assert.Equal("GET", state.GetProperty("Method").GetString());
        Assert.Equal("Jobs/{offerId:int}", state.GetProperty("Path").GetString()?.TrimStart('/'));
        Assert.Equal(500, state.GetProperty("StatusCode").GetInt32());
        Assert.Equal("Exception", state.GetProperty("ExceptionType").GetString());
        Assert.Equal(document.RootElement.GetProperty("traceId").GetString(), state.GetProperty("IncidentId").GetString());
        Assert.Equal(TimeSpan.Zero, DateTimeOffset.Parse(state.GetProperty("OccurredAtUtc").GetString()!, CultureInfo.InvariantCulture).Offset);
        Assert.Equal(TimeSpan.Zero, DateTimeOffset.Parse(root.GetProperty("Timestamp").GetString()!, CultureInfo.InvariantCulture).Offset);
        Assert.Contains("0123456789abcdef0123456789abcdef", root.GetProperty("Scopes").GetRawText());
        Assert.Contains("career-acceptance-500", root.GetProperty("Scopes").GetRawText());
        Assert.All(factory.Logs.Lines, line =>
        {
            Assert.DoesNotContain(Sensitive, line);
            Assert.DoesNotContain("817263", line);
        });
        Assert.Contains(factory.Logs.Lines, line => line.Contains("responded 500", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProductionStartup_PreservesAnonymousReadsProtectedWritesAndNativeDependencies()
    {
        using var factory = new CareerFactory();
        factory.Career.Setup(service => service.HasOpenPositionsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        using var client = factory.CreateClient();
        Assert.Equal("true", await client.GetStringAsync("/Jobs/job-opening-status"));
        Assert.Equal("Healthy", await client.GetStringAsync("/Jobs/liveness"));
        using var denied = await client.DeleteAsync("/Jobs/1");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.DoesNotContain(factory.Logs.Lines, line => line.Contains("UnhandledRequestFailure", StringComparison.Ordinal));
        var providers = factory.Services.GetServices<ILoggerProvider>().Select(provider => provider.GetType().FullName!).ToArray();
        Assert.Contains(providers, name => name.Contains("ConsoleLoggerProvider", StringComparison.Ordinal));
        Assert.Contains(providers, name => name.Contains("OpenTelemetryLoggerProvider", StringComparison.Ordinal));
        Assert.DoesNotContain(providers, name => name.Contains("NLog", StringComparison.Ordinal));
        var options = factory.Services.GetRequiredService<IOptionsMonitor<ConsoleLoggerOptions>>();
        Assert.Equal(MalievCloudJsonConsoleFormatter.FormatterName, options.CurrentValue.FormatterName);
        var tracking = factory.Services.GetRequiredService<IOptions<LoggerFactoryOptions>>().Value.ActivityTrackingOptions;
        Assert.True(tracking.HasFlag(ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId | ActivityTrackingOptions.ParentId));
        using var dependencies = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Legacy.Maliev.CareerService.Api.deps.json")));
        var libraries = dependencies.RootElement.GetProperty("libraries").EnumerateObject().Select(property => property.Name).ToArray();
        Assert.Contains(libraries, name => name.StartsWith("Legacy.Maliev.ServiceDefaults/", StringComparison.Ordinal));
        Assert.DoesNotContain(libraries, name => name.StartsWith("Maliev.NativeLogging/", StringComparison.Ordinal)
            || name.Contains("NLog", StringComparison.Ordinal) || name.Contains("LoggerService", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProductionStartedResponse_DoesNotAppendExceptionBodyOrOverwriteStatus()
    {
        using var factory = new CareerFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"/Jobs/acceptance-started/{Sensitive}");
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("accepted", await response.Content.ReadAsStringAsync());
        using var failure = factory.Failure();
        Assert.Equal(202, failure.RootElement.GetProperty("State").GetProperty("StatusCode").GetInt32());
        Assert.All(factory.Logs.Lines, line => Assert.DoesNotContain(Sensitive, line));
        Assert.Contains(factory.Logs.Lines, line => line.Contains("Response has already started", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProductionDiagnostic_DirectLoopbackNonce_ReturnsOriginalEmptyProblemFailure()
    {
        using var factory = new CareerFactory(useDiagnosticPeer: true, diagnosticPeer: "127.0.0.1");
        using var client = factory.CreateClient();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        const string nonce = "0123456789abcdef0123456789abcdef";
        using var request = new HttpRequestMessage(HttpMethod.Get, "/internal/diagnostics/observability?private=" + Sensitive);
        request.Headers.Add("X-Maliev-Diagnostic-Id", nonce);
        using var response = await client.SendAsync(request, deadline.Token);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync(deadline.Token));
        Assert.Equal(nonce, Assert.Single(response.Headers.GetValues("X-Maliev-Diagnostic-Id")));
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("noindex", Assert.Single(response.Headers.GetValues("X-Robots-Tag")));
        Assert.Single(factory.Logs.Lines, line => line.Contains("ObservabilityPipelineProbe", StringComparison.Ordinal));
        Assert.Contains(factory.Logs.Lines, line => line.Contains("UnhandledRequestFailure", StringComparison.Ordinal));
        Assert.All(factory.Logs.Lines, line => Assert.DoesNotContain(Sensitive, line));
        factory.Career.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("GET", "127.0.0.1", null)]
    [InlineData("GET", "127.0.0.1", "invalid-nonce")]
    [InlineData("POST", "127.0.0.1", "0123456789abcdef0123456789abcdef")]
    [InlineData("GET", "203.0.113.17", "0123456789abcdef0123456789abcdef")]
    [InlineData("GET", null, "0123456789abcdef0123456789abcdef")]
    public async Task ProductionDiagnostic_InvalidMethodPeerOrNonce_IsPrivate404WithoutSyntheticFailure(
        string method, string? peer, string? nonce)
    {
        using var factory = new CareerFactory(useDiagnosticPeer: true, diagnosticPeer: peer);
        using var client = factory.CreateClient();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var request = new HttpRequestMessage(new HttpMethod(method), "/internal/diagnostics/observability");
        if (nonce is not null)
        {
            request.Headers.Add("X-Maliev-Diagnostic-Id", nonce);
        }
        using var response = await client.SendAsync(request, deadline.Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync(deadline.Token));
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("noindex", Assert.Single(response.Headers.GetValues("X-Robots-Tag")));
        Assert.False(response.Headers.Contains("X-Maliev-Diagnostic-Id"));
        Assert.DoesNotContain(factory.Logs.Lines, line => line.Contains("ObservabilityPipelineProbe", StringComparison.Ordinal)
            || line.Contains("UnhandledRequestFailure", StringComparison.Ordinal));
        factory.Career.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProductionDiagnostic_SecondNonceWithinOneMinute_Is429WithoutSecondSyntheticFailure()
    {
        using var factory = new CareerFactory(useDiagnosticPeer: true, diagnosticPeer: "127.0.0.1");
        using var client = factory.CreateClient();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var firstRequest = new HttpRequestMessage(HttpMethod.Get, "/internal/diagnostics/observability");
        firstRequest.Headers.Add("X-Maliev-Diagnostic-Id", "0123456789abcdef0123456789abcdef");
        using var first = await client.SendAsync(firstRequest, deadline.Token);
        Assert.Equal(HttpStatusCode.InternalServerError, first.StatusCode);
        Assert.Equal(string.Empty, await first.Content.ReadAsStringAsync(deadline.Token));
        using var secondRequest = new HttpRequestMessage(HttpMethod.Get, "/internal/diagnostics/observability");
        secondRequest.Headers.Add("X-Maliev-Diagnostic-Id", "abcdef0123456789abcdef0123456789");
        using var second = await client.SendAsync(secondRequest, deadline.Token);

        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal(string.Empty, await second.Content.ReadAsStringAsync(deadline.Token));
        Assert.Equal("no-store", second.Headers.CacheControl?.ToString());
        Assert.Equal("noindex", Assert.Single(second.Headers.GetValues("X-Robots-Tag")));
        Assert.False(second.Headers.Contains("X-Maliev-Diagnostic-Id"));
        Assert.Single(factory.Logs.Lines, line => line.Contains("ObservabilityPipelineProbe", StringComparison.Ordinal));
        factory.Career.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("/Jobs")]
    [InlineData("/jobs")]
    public async Task ProductionCompletedObservation_RegisteredHealthHeadersAreStablePerHostAndCaseInsensitive(string prefix)
    {
        using var factory = new CareerFactory(completedObservation: new CompletedObservationFixture());
        using var client = factory.CreateClient();
        using var liveness = await ObservedRequestAsync(client, prefix + "/liveness");
        using var readiness = await ObservedRequestAsync(client, prefix + "/readiness");
        Assert.Equal(HttpStatusCode.OK, liveness.StatusCode);
        Assert.Equal("Healthy", await liveness.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);
        var instance = AssertHealthInstance(liveness);
        Assert.Equal(instance, AssertHealthInstance(readiness));
        using var other = new CareerFactory(completedObservation: new CompletedObservationFixture());
        using var otherClient = other.CreateClient();
        using var otherLiveness = await ObservedRequestAsync(otherClient, prefix + "/liveness");
        Assert.Equal(HttpStatusCode.OK, otherLiveness.StatusCode);
        Assert.NotEqual(instance, AssertHealthInstance(otherLiveness));
        Assert.Empty(ObservationEvents(factory, "HealthProbeFailure"));
        Assert.Empty(ObservationEvents(factory, "HandledOperationFailure"));
        factory.Career.VerifyNoOtherCalls();
        other.Career.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProductionCompletedObservation_Handled5xxRecordsEveryResponseWithoutSensitiveValues()
    {
        var fixture = new CompletedObservationFixture();
        using var factory = new CareerFactory(completedObservation: fixture);
        using var client = factory.CreateClient();
        foreach (var status in new[] { 503, 503, 200, 400, 502 })
        {
            fixture.ResponseStatus = status;
            using var response = await ObservedRequestAsync(client, $"/Jobs/acceptance-observation/handled/{Sensitive}?search={Sensitive}");
            Assert.Equal((HttpStatusCode)status, response.StatusCode);
            Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
            Assert.False(response.Headers.Contains("X-Maliev-Health-Instance"));
        }
        var events = ObservationEvents(factory, "HandledOperationFailure");
        Assert.Equal(new[] { 503, 503, 502 }, events.Select(root => root.GetProperty("State").GetProperty("StatusCode").GetInt32()).ToArray());
        Assert.All(events, root =>
        {
            Assert.Equal("Error", root.GetProperty("LogLevel").GetString());
            Assert.Equal("HttpResponse", root.GetProperty("State").GetProperty("Operation").GetString());
        });
        Assert.Empty(ObservationEvents(factory, "HealthProbeFailure"));
        Assert.Empty(ObservationEvents(factory, "UnhandledRequestFailure"));
        Assert.All(factory.Logs.Lines, line => Assert.DoesNotContain(Sensitive, line));
        factory.Career.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProductionCompletedObservation_HealthSuffixBucketsThrottleAtFiveMinutesAndResetOnRecovery()
    {
        var fixture = new CompletedObservationFixture();
        using var factory = new CareerFactory(completedObservation: fixture);
        using var client = factory.CreateClient();
        async Task ProbeAsync(string operation, int status)
        {
            fixture.ResponseStatus = status;
            using var response = await ObservedRequestAsync(client, $"/Jobs/acceptance-observation/{operation}?search={Sensitive}");
            Assert.Equal((HttpStatusCode)status, response.StatusCode);
            Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
            // Suffix classification is distinct from the registered-health header policy.
            Assert.False(response.Headers.Contains("X-Maliev-Health-Instance"));
        }
        await ProbeAsync("readiness", 503);
        await ProbeAsync("readiness", 503);
        await ProbeAsync("liveness", 503);
        Assert.Equal(2, ObservationEvents(factory, "HealthProbeFailure").Length);
        fixture.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1));
        await ProbeAsync("readiness", 503);
        await ProbeAsync("liveness", 503);
        Assert.Equal(2, ObservationEvents(factory, "HealthProbeFailure").Length);
        fixture.Advance(TimeSpan.FromSeconds(1));
        await ProbeAsync("readiness", 503);
        await ProbeAsync("liveness", 503);
        Assert.Equal(4, ObservationEvents(factory, "HealthProbeFailure").Length);
        await ProbeAsync("readiness", 502);
        Assert.Equal(5, ObservationEvents(factory, "HealthProbeFailure").Length);
        await ProbeAsync("readiness", 200);
        await ProbeAsync("readiness", 502);
        var events = ObservationEvents(factory, "HealthProbeFailure");
        Assert.Equal(new[] { "Readiness", "Liveness", "Readiness", "Liveness", "Readiness", "Readiness" },
            events.Select(root => root.GetProperty("State").GetProperty("Operation").GetString()).ToArray());
        Assert.Equal(new[] { 503, 503, 503, 503, 502, 502 },
            events.Select(root => root.GetProperty("State").GetProperty("StatusCode").GetInt32()).ToArray());
        Assert.Empty(ObservationEvents(factory, "HandledOperationFailure"));
        Assert.Empty(ObservationEvents(factory, "UnhandledRequestFailure"));
        Assert.All(factory.Logs.Lines, line => Assert.DoesNotContain(Sensitive, line));
        factory.Career.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProductionCompletedObservation_RegisteredReadinessFailureRecoversWithoutChangingInstance()
    {
        var fixture = new CompletedObservationFixture();
        using var factory = new CareerFactory(completedObservation: fixture);
        using var client = factory.CreateClient();
        string? instance = null;
        foreach (var healthy in new[] { true, false, false, true, false })
        {
            fixture.ReadinessHealthy = healthy;
            using var response = await ObservedRequestAsync(client, $"/Jobs/readiness?search={Sensitive}");
            Assert.Equal(healthy ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable, response.StatusCode);
            var actualInstance = AssertHealthInstance(response);
            instance ??= actualInstance;
            Assert.Equal(instance, actualInstance);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(healthy ? "Healthy" : "Unhealthy", document.RootElement.GetProperty("status").GetString());
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        }
        var events = ObservationEvents(factory, "HealthProbeFailure");
        Assert.Equal(2, events.Length);
        Assert.All(events, root =>
        {
            Assert.Equal("Readiness", root.GetProperty("State").GetProperty("Operation").GetString());
            Assert.Equal(503, root.GetProperty("State").GetProperty("StatusCode").GetInt32());
        });
        Assert.Empty(ObservationEvents(factory, "UnhandledRequestFailure"));
        Assert.Empty(ObservationEvents(factory, "HandledOperationFailure"));
        Assert.All(factory.Logs.Lines, line => Assert.DoesNotContain(Sensitive, line));
        factory.Career.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("GET", "/Other/liveness", 404)]
    [InlineData("GET", "/Jobs/aspire-liveness", 200)]
    [InlineData("POST", "/Jobs/liveness", 405)]
    [InlineData("GET", "/Jobs/acceptance-observation/readiness", 200)]
    public async Task ProductionCompletedObservation_UnregisteredOrNonGetHealthHasNoInstanceHeader(string method, string path, int status)
    {
        using var factory = new CareerFactory(completedObservation: new CompletedObservationFixture { ResponseStatus = 200 });
        using var client = factory.CreateClient();
        using var response = await ObservedRequestAsync(client, path, new HttpMethod(method));
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        Assert.False(response.Headers.Contains("X-Maliev-Health-Instance"));
        Assert.False(response.Headers.Contains("X-Maliev-Diagnostic-Id"));
        Assert.Empty(ObservationEvents(factory, "HealthProbeFailure"));
        Assert.Empty(ObservationEvents(factory, "HandledOperationFailure"));
        Assert.Empty(ObservationEvents(factory, "UnhandledRequestFailure"));
        factory.Career.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProductionCompletedObservation_ThrownBusinessFailureHasOneOuterIncidentAndNoCompletedDuplicate()
    {
        using var factory = new CareerFactory(completedObservation: new CompletedObservationFixture());
        factory.Career.Setup(service => service.GetOfferByIdAsync(817263, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception(Sensitive));
        using var client = factory.CreateClient();
        using var response = await ObservedRequestAsync(client, $"/Jobs/817263?search={Sensitive}");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("An internal server error occurred", document.RootElement.GetProperty("error").GetString());
        Assert.Single(ObservationEvents(factory, "UnhandledRequestFailure"));
        Assert.Empty(ObservationEvents(factory, "HandledOperationFailure"));
        Assert.Empty(ObservationEvents(factory, "HealthProbeFailure"));
        Assert.All(factory.Logs.Lines, line => Assert.DoesNotContain(Sensitive, line));
        factory.Career.Verify(service => service.GetOfferByIdAsync(817263, It.IsAny<CancellationToken>()), Times.Once);
        factory.Career.VerifyNoOtherCalls();
    }

    private static async Task<HttpResponseMessage> ObservedRequestAsync(HttpClient client, string path, HttpMethod? method = null)
    {
        using var request = new HttpRequestMessage(method ?? HttpMethod.Get, path);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        return await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, deadline.Token);
    }

    private static string AssertHealthInstance(HttpResponseMessage response)
    {
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        var instance = Assert.Single(response.Headers.GetValues("X-Maliev-Health-Instance"));
        Assert.True(Guid.TryParseExact(instance, "N", out _));
        Assert.False(response.Headers.Contains("X-Maliev-Diagnostic-Id"));
        return instance;
    }

    private static JsonElement[] ObservationEvents(CareerFactory factory, string eventName)
    {
        var events = new List<JsonElement>();
        foreach (var line in factory.Logs.Lines)
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.TryGetProperty("State", out var state) && state.ValueKind == JsonValueKind.Object
                && state.TryGetProperty("EventName", out var name) && name.GetString() == eventName)
            {
                events.Add(root.Clone());
            }
        }
        return events.ToArray();
    }

    private sealed class ControlledDiagnosticPeerStartupFilter(string? peer) : IStartupFilter
    {
        public Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> Configure(
            Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> next)
        {
            return app =>
            {
                Microsoft.AspNetCore.Builder.UseExtensions.Use(app, async (context, continuation) =>
                {
                    context.Connection.RemoteIpAddress = peer is null ? null : IPAddress.Parse(peer);
                    await continuation(context);
                });
                next(app);
            };
        }
    }

    private sealed class CareerFactory(
        string? databaseConnection = null, bool useDiagnosticPeer = false, string? diagnosticPeer = null,
        CompletedObservationFixture? completedObservation = null)
        : WebApplicationFactory<Program>
    {
        private readonly RSA _rsa = RSA.Create(2048);
        public Mock<ICareerService> Career { get; } = new(MockBehavior.Strict);
        public CaptureProvider Logs { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureTestServices(services =>
            {
                if (databaseConnection is null)
                {
                    services.RemoveAll<ICareerService>();
                    services.AddSingleton(Career.Object);
                }
                if (useDiagnosticPeer)
                {
                    services.AddSingleton<IStartupFilter>(new ControlledDiagnosticPeerStartupFilter(diagnosticPeer));
                }
                if (completedObservation is { } observation)
                {
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton<TimeProvider>(observation);
                    services.AddSingleton(observation);
                    // Controlled health results only; this does not prove real dependency readiness.
                    services.Configure<HealthCheckServiceOptions>(options => options.Registrations.Clear());
                    services.AddHealthChecks().AddCheck("controlled-observation", () =>
                        observation.ReadinessHealthy ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("controlled"));
                }
                services.AddControllers().AddApplicationPart(typeof(StartedResponseController).Assembly);
                services.AddSingleton<ILoggerProvider>(provider =>
                {
                    Logs.Formatter = new MalievCloudJsonConsoleFormatter(provider.GetRequiredService<IOptionsMonitor<JsonConsoleFormatterOptions>>());
                    return Logs;
                });
            });
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            // Minimal hosting reads database/auth settings before ConfigureAppConfiguration runs.
            builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:PublicKey"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(_rsa.ExportSubjectPublicKeyInfoPem())),
                ["ConnectionStrings:CareerDbContext"] = databaseConnection ?? "Host=127.0.0.1;Port=1;Database=acceptance;Username=acceptance",
                ["Cache:RedisEnabled"] = "false",
                ["Observability:TracingEnabled"] = "true",
                ["Observability:RuntimeMetricsEnabled"] = "false",
                ["Logging:LogLevel:Default"] = "Information",
            }));
            return base.CreateHost(builder);
        }

        public JsonDocument Failure() => JsonDocument.Parse(Assert.Single(Logs.Lines, line =>
            line.Contains("UnhandledRequestFailure", StringComparison.Ordinal)));

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) _rsa.Dispose();
        }
    }

    private sealed class CaptureProvider : ILoggerProvider, ISupportExternalScope
    {
        private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();
        public ConcurrentQueue<string> Lines { get; } = new();
        public MalievCloudJsonConsoleFormatter Formatter { get; set; } = null!;
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(this, categoryName);
        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;
        public void Dispose() { }

        private sealed class CaptureLogger(CaptureProvider provider, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => provider._scopes.Push(state);
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                using var writer = new StringWriter(CultureInfo.InvariantCulture);
                provider.Formatter.Write(new LogEntry<TState>(level, category, eventId, state, exception, formatter), provider._scopes, writer);
                provider.Lines.Enqueue(writer.ToString());
            }
        }
    }
}

// Test-only endpoint exercises a started response through the actual production startup pipeline.
[ApiController]
public sealed class StartedResponseController : ControllerBase
{
    [HttpGet("Jobs/acceptance-save")]
    public async Task SaveAsync([FromServices] CareerDbContext database)
    {
        database.Levels.Add(new JobLevel { Name = "candidate-private@example.invalid" });
        await database.SaveChangesAsync();
    }

    [HttpGet("Jobs/acceptance-observation/handled/{value}")]
    [HttpGet("Jobs/acceptance-observation/readiness")]
    [HttpGet("Jobs/acceptance-observation/liveness")]
    public async Task CompleteObservationAsync([FromServices] CompletedObservationFixture fixture)
    {
        Response.StatusCode = fixture.ResponseStatus;
        await Response.StartAsync();
    }

    [HttpGet("Jobs/acceptance-started/{value}")]
    public async Task GetAsync()
    {
        Response.StatusCode = StatusCodes.Status202Accepted;
        await Response.WriteAsync("accepted");
        await Response.Body.FlushAsync();
        throw new Exception("candidate-private@example.invalid");
    }
}

// Test-owned clock and health-result control; no timer, worker, connection or production selector.
public sealed class CompletedObservationFixture : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
    public int ResponseStatus { get; set; } = 503;
    public bool ReadinessHealthy { get; set; } = true;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan duration) => _now += duration;
}
