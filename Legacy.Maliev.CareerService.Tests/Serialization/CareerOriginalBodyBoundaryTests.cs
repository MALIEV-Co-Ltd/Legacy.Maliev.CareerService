using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.CareerService.Application.Models;
using Legacy.Maliev.CareerService.Tests.Controllers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.CareerService.Tests.Serialization;

/// <summary>Checks the original-body formatter's registration, logging, and schema boundaries.</summary>
/// <param name="fixture">The shared Career route fixture providing the application factory, request clients, and test database access.</param>
[Collection("Career shared level")]
public sealed class CareerOriginalBodyBoundaryTests(CareerRouteFixture fixture) : IClassFixture<CareerRouteFixture>
{
    /// <summary>Checks exact DTO selection without changing other readers, output, or scalar metadata.</summary>
    [Fact]
    public void ExactDtoSelection_LeavesOtherReadersOutputAndScalarMetadataIntact()
    {
        var services = fixture.Factory.Services;
        var options = services.GetRequiredService<IOptions<MvcOptions>>().Value;
        var native = CareerOriginalBodyNativeOptions.Create(services);
        var formatter = Assert.Single(options.InputFormatters,
            item => item.GetType().FullName == CareerOriginalBodyNativeOptions.FormatterName);
        Assert.Same(formatter, options.InputFormatters[0]);
        Assert.Equal(native.InputFormatters.Select(item => item.GetType()),
            options.InputFormatters.Skip(1).Select(item => item.GetType()));
        Assert.Equal(native.OutputFormatters.Select(item => item.GetType()),
            options.OutputFormatters.Select(item => item.GetType()));
        Assert.Single(options.OutputFormatters, item => item is SystemTextJsonOutputFormatter);
        var policy = Assert.IsAssignableFrom<IInputFormatterExceptionPolicy>(formatter);
        Assert.Equal(InputFormatterExceptionPolicy.MalformedInputExceptions, policy.ExceptionPolicy);
        var media = Assert.IsAssignableFrom<IApiRequestFormatMetadataProvider>(formatter);
        var metadata = services.GetRequiredService<IModelMetadataProvider>();
        foreach (var type in new[] { typeof(UpsertJobOfferRequest), typeof(UpsertJobLevelRequest), typeof(JobOfferResponse), typeof(string), typeof(Dictionary<string, object>) })
        {
            var http = new DefaultHttpContext();
            http.Request.ContentType = "application/json";
            var context = new InputFormatterContext(http, "", new ModelStateDictionary(),
                metadata.GetMetadataForType(type), static (stream, encoding) => new HttpRequestStreamReader(stream, encoding));
            var supported = type == typeof(UpsertJobOfferRequest) || type == typeof(UpsertJobLevelRequest);
            Assert.Equal(supported, formatter.CanRead(context));
            if (supported) Assert.Contains("application/json", media.GetSupportedContentTypes(null, type)!);
            else Assert.Null(media.GetSupportedContentTypes(null, type));
        }
        Assert.Single(services.GetServices<IConfigureOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>(),
            CareerLevelNativeOptions.IsOwned);
        Assert.Single(services.GetServices<IConfigureOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>(),
            CareerOfferNativeOptions.IsOwned);
    }

    /// <summary>Checks safe debug diagnostics delivered to another logging provider.</summary>
    /// <returns>A task that completes when another logging provider receives safe debug formatter diagnostics without exception or input-value disclosure.</returns>
    [Fact]
    public async Task FormatterLogger_EmitsSafeDiagnosticAtDebugToAnotherProvider()
    {
        using var capture = new CaptureProvider();
        await using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureLogging(logging =>
        {
            logging.AddProvider(capture);
            logging.AddFilter<CaptureProvider>("Microsoft.AspNetCore.Mvc.Formatters.NewtonsoftJsonInputFormatter", LogLevel.Trace);
        }));
        var services = factory.Services;
        var result = await CareerOriginalBodyNativeOptions.ReadAsync(services,
            services.GetRequiredService<IOptions<MvcOptions>>().Value, true,
            Encoding.UTF8.GetBytes("""{"Offers":[{"LevelId":"private-value-marker"}]}"""));
        Assert.Equal("failure", result.Kind);
        var events = capture.Events.ToArray();
        Assert.NotEmpty(events);
        Assert.All(events, item =>
        {
            Assert.Null(item.Exception);
            Assert.DoesNotContain("private-value-marker", item.Message, StringComparison.Ordinal);
            Assert.Contains("Career input formatter diagnostic", item.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("private-value-marker", item.State, StringComparison.Ordinal);
        });
    }

    /// <summary>Checks that served schemas and request media match native DTO documentation.</summary>
    /// <returns>A task that completes when both named request schemas and every served request-body media definition match native DTO documentation.</returns>
    [Fact]
    public async Task ServedSchemasAndRequestMedia_RemainEqualToNativeDtoDocument()
    {
        await using var candidate = fixture.Factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        await using var native = fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(CareerOriginalBodyNativeOptions.RemoveOwned);
        });
        using var candidateClient = candidate.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var nativeClient = native.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var actual = await Document(candidateClient);
        var expected = await Document(nativeClient);
        foreach (var name in new[] { "UpsertJobOfferRequest", "UpsertJobLevelRequest" })
            Assert.Equal(Canonical(expected.GetProperty("components").GetProperty("schemas").GetProperty(name)),
                Canonical(actual.GetProperty("components").GetProperty("schemas").GetProperty(name)));
        foreach (var path in expected.GetProperty("paths").EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                if (!operation.Value.TryGetProperty("requestBody", out var body)) continue;
                var actualBody = actual.GetProperty("paths").GetProperty(path.Name).GetProperty(operation.Name).GetProperty("requestBody");
                Assert.Equal(Canonical(body), Canonical(actualBody));
            }
        }
    }

    private static async Task<JsonElement> Document(HttpClient client)
    {
        using var response = await client.GetAsync("/Jobs/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    private static string Canonical(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => "{" + string.Join(",", value.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal)
            .Select(item => JsonSerializer.Serialize(item.Name) + ":" + Canonical(item.Value))) + "}",
        JsonValueKind.Array => "[" + string.Join(",", value.EnumerateArray().Select(Canonical)) + "]",
        _ => value.GetRawText(),
    };

    private sealed record Captured(Exception? Exception, string Message, string State);

    private sealed class CaptureProvider : ILoggerProvider
    {
        internal ConcurrentQueue<Captured> Events { get; } = new();
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(this,
            categoryName == "Microsoft.AspNetCore.Mvc.Formatters.NewtonsoftJsonInputFormatter");
        public void Dispose() { }

        private sealed class CaptureLogger(CaptureProvider owner, bool selected) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => selected;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (!selected) return;
                var fields = state is IEnumerable<KeyValuePair<string, object?>> values
                    ? string.Join("|", values.Select(item => item.Key + "=" + item.Value)) : state?.ToString() ?? "";
                owner.Events.Enqueue(new Captured(exception, formatter(state, exception), fields));
            }
        }
    }
}
