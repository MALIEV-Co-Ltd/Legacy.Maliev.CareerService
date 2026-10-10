using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Legacy.Maliev.CareerService.Api.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Legacy.Maliev.CareerService.Tests.Startup;

public sealed class CareerDiagnosticConsoleFormatterTests
{
    [Theory]
    [InlineData(LogLevel.Trace, null)]
    [InlineData(LogLevel.Debug, null)]
    [InlineData(LogLevel.Information, null)]
    [InlineData(LogLevel.None, null)]
    [InlineData(LogLevel.Warning, "WARNING")]
    [InlineData(LogLevel.Error, "ERROR")]
    [InlineData(LogLevel.Critical, "CRITICAL")]
    public void PrivateFormatter_LevelGateNeverInvokesArbitraryMessage(LogLevel level, string? severity)
    {
        var line = Write(level, new Dictionary<string, object?>());
        if (severity is null)
        {
            Assert.Empty(line);
            return;
        }
        using var document = JsonDocument.Parse(line);
        Assert.Equal(severity, document.RootElement.GetProperty("severity").GetString());
        Assert.Equal("Application diagnostic; see event, exception and trace metadata", document.RootElement.GetProperty("message").GetString());
        Assert.Equal(1901, document.RootElement.GetProperty("eventId").GetInt32());
        Assert.Equal(TimeSpan.Zero, DateTimeOffset.Parse(document.RootElement.GetProperty("occurredAtUtc").GetString()!, CultureInfo.InvariantCulture).Offset);
    }

    [Fact]
    public void PrivateFormatter_PreservesRuntimeExceptionActivityAndDeploymentProvenance()
    {
        using var activity = new Activity("SourceMetadata").SetIdFormat(ActivityIdFormat.W3C).Start();
        Exception failure;
        try { ThrowSourceException(); throw new UnreachableException(); }
        catch (InvalidOperationException exception) { failure = exception; }
        var line = Write(LogLevel.Error, new Dictionary<string, object?> { ["exceptionType"] = "forged", ["service"] = "forged" }, failure);
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        Assert.Equal("System.InvalidOperationException", root.GetProperty("exceptionType").GetString());
        Assert.Equal("System.ArgumentException", root.GetProperty("innerExceptionType").GetString());
        Assert.EndsWith(".ThrowSourceException", root.GetProperty("sourceLocation").GetString());
        Assert.Equal(activity.TraceId.ToHexString(), root.GetProperty("traceId").GetString());
        Assert.Equal(activity.SpanId.ToHexString(), root.GetProperty("spanId").GetString());
        var entryAssembly = Assembly.GetEntryAssembly();
        Assert.NotNull(entryAssembly);
        Assert.Equal(entryAssembly.GetName().Name, root.GetProperty("service").GetString());
        Assert.Equal(entryAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            root.GetProperty("deploymentVersion").GetString());
        Assert.DoesNotContain("private@example.invalid", line);
    }

    [Fact]
    public async Task PrivateFormatter_PreservesAsyncAndGenericReflectionSourceLocations()
    {
        Exception failure;
        try { await ThrowAsyncSourceException(); throw new UnreachableException(); }
        catch (InvalidOperationException exception) { failure = exception; }
        var expected = failure.TargetSite!.DeclaringType!.FullName + "." + failure.TargetSite.Name;
        Assert.Contains("<", expected);
        using var asyncDocument = JsonDocument.Parse(Write(LogLevel.Error, new object(), failure));
        Assert.Equal(expected, asyncDocument.RootElement.GetProperty("sourceLocation").GetString());
        try { GenericSourceProbe<string>.Throw(); throw new UnreachableException(); }
        catch (InvalidOperationException exception) { failure = exception; }
        expected = failure.TargetSite!.DeclaringType!.FullName + "." + failure.TargetSite.Name;
        Assert.Contains("`", expected);
        using var genericDocument = JsonDocument.Parse(Write(LogLevel.Error, new object(), failure));
        Assert.Equal(expected, genericDocument.RootElement.GetProperty("sourceLocation").GetString());
        Assert.DoesNotContain("private@example.invalid", asyncDocument.RootElement.GetRawText());
        Assert.DoesNotContain("private@example.invalid", genericDocument.RootElement.GetRawText());
    }

    [Fact]
    public void PrivateFormatter_PreservesOriginalSafeFieldsAndModernTypeIncidentAdaptation()
    {
        var nonce = Guid.NewGuid();
        var fields = new Dictionary<string, object?>
        {
            ["EventName"] = "SourceWarning",
            ["Dependency"] = "CareerDb",
            ["Operation"] = "Readiness",
            ["Method"] = "GET",
            ["StatusCode"] = 503,
            ["ElapsedMs"] = 12L,
            ["AttemptCount"] = 2,
            ["Synthetic"] = false,
            ["DiagnosticId"] = nonce.ToString("N"),
            ["CorrelationId"] = nonce.ToString("D"),
            ["TraceId"] = "0123456789abcdef0123456789abcdef",
            ["SpanId"] = "0123456789abcdef",
            ["ExceptionType"] = "PostgresException",
            ["IncidentId"] = "0H_SOURCE:0001"
        };
        using var document = JsonDocument.Parse(Write(LogLevel.Warning, fields));
        var root = document.RootElement;
        Assert.Equal(nonce.ToString("N"), root.GetProperty("CorrelationId").GetString());
        Assert.Equal(nonce.ToString("N"), root.GetProperty("DiagnosticId").GetString());
        Assert.False(root.GetProperty("Synthetic").GetBoolean());
        Assert.Equal(503, root.GetProperty("StatusCode").GetInt32());
        Assert.Equal(12L, root.GetProperty("ElapsedMs").GetInt64());
        foreach (var field in fields.Where(field => field.Key != "CorrelationId"))
            Assert.Equal(JsonSerializer.Serialize(field.Value), root.GetProperty(field.Key).GetRawText());
    }

    [Theory]
    [InlineData("EventName")]
    [InlineData("Path")]
    [InlineData("RequestPath")]
    [InlineData("CustomerEmail")]
    [InlineData("CorrelationId")]
    [InlineData("DiagnosticId")]
    [InlineData("ExceptionType")]
    [InlineData("IncidentId")]
    public void PrivateFormatter_RejectsUnknownOrInvalidScalarFields(string key)
    {
        var line = Write(LogLevel.Warning, new Dictionary<string, object?> { [key] = "private@example.invalid", ["StatusCode"] = "503" });
        using var document = JsonDocument.Parse(line);
        Assert.False(document.RootElement.TryGetProperty(key, out _));
        Assert.False(document.RootElement.TryGetProperty("StatusCode", out _));
        Assert.DoesNotContain("private@example.invalid", line);
    }

    [Fact]
    public void PrivateFormatter_StateWinsWithoutDuplicateKeysAndScopeProcessingIsBounded()
    {
        var scopes = new LoggerExternalScopeProvider();
        var handles = new List<IDisposable>();
        try
        {
            for (int index = 0; index < 17; index++)
                handles.Add(scopes.Push(new Dictionary<string, object?> { ["EventName"] = "ForgedScope", ["Dependency"] = index == 16 ? "BeyondBudget" : null }));
            var fields = new[]
            {
                new KeyValuePair<string, object?>(null!, "private@example.invalid"),
                new KeyValuePair<string, object?>("EventName", "OriginalState"),
                new KeyValuePair<string, object?>("message", "private@example.invalid")
            };
            using var document = JsonDocument.Parse(Write(LogLevel.Warning, fields, scopes: scopes));
            var root = document.RootElement;
            Assert.Equal("OriginalState", root.GetProperty("EventName").GetString());
            Assert.False(root.TryGetProperty("Dependency", out _));
            Assert.Equal(root.EnumerateObject().Count(), root.EnumerateObject().Select(property => property.Name).Distinct(StringComparer.Ordinal).Count());
        }
        finally { foreach (var handle in handles.AsEnumerable().Reverse()) handle.Dispose(); }
    }

    [Fact]
    public void PrivateFormatter_StateEnumerationAndUntrustedCategoryFailClosed()
    {
        var fields = Enumerable.Range(0, 64).Select(index => new KeyValuePair<string, object?>("Unknown" + index, null))
            .Append(new KeyValuePair<string, object?>("EventName", "BeyondBudget"));
        using var document = JsonDocument.Parse(Write(LogLevel.Warning, fields, category: "private@example.invalid"));
        Assert.False(document.RootElement.TryGetProperty("EventName", out _));
        Assert.Equal("ConfiguredLogger", document.RootElement.GetProperty("logger").GetString());
        using var missingCategory = JsonDocument.Parse(Write(LogLevel.Warning, fields, category: null!));
        Assert.Equal("ConfiguredLogger", missingCategory.RootElement.GetProperty("logger").GetString());
    }

    [Fact]
    public void PrivateFormatter_NullWriterIsRejected()
    {
        var entry = new LogEntry<string>(LogLevel.Warning, "Career.Source", new EventId(1901), "", null, (_, _) => "");
        Assert.Throws<ArgumentNullException>(() => new CareerDiagnosticConsoleFormatter().Write(entry, null, null!));
    }

    private static string Write(LogLevel level, object state, Exception? failure = null, IExternalScopeProvider? scopes = null, string category = "Career.Source")
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        var entry = new LogEntry<object>(level, category, new EventId(1901), state, failure,
            (_, _) => throw new InvalidOperationException("Arbitrary formatter must never be invoked"));
        new CareerDiagnosticConsoleFormatter().Write(entry, scopes, writer);
        return writer.ToString();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowSourceException() => throw new InvalidOperationException("private@example.invalid", new ArgumentException("private@example.invalid"));

    private static async Task ThrowAsyncSourceException()
    {
        await Task.Yield();
        throw new InvalidOperationException("private@example.invalid");
    }

    private static class GenericSourceProbe<T>
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Throw() => throw new InvalidOperationException("private@example.invalid");
    }
}
