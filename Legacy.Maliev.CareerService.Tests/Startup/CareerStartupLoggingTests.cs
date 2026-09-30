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

    private sealed class CareerFactory(string? databaseConnection = null) : WebApplicationFactory<Program>
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
                ["ConnectionStrings:CareerDbContext"] = databaseConnection ?? "Host=127.0.0.1;Port=1;Database=acceptance;Username=acceptance;Password=test-only", // gitleaks:allow
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

    [HttpGet("Jobs/acceptance-started/{value}")]
    public async Task GetAsync()
    {
        Response.StatusCode = StatusCodes.Status202Accepted;
        await Response.WriteAsync("accepted");
        await Response.Body.FlushAsync();
        throw new Exception("candidate-private@example.invalid");
    }
}
