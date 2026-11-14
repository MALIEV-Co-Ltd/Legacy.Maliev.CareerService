using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Builder;
using Xunit.Abstractions;

namespace Legacy.Maliev.CareerService.Tests.Startup;

/// <summary>Verifies the shared frameworks loaded by each qualification test host.</summary>
public sealed class RuntimeCompatibilityIdentityTests(ITestOutputHelper output)
{
    /// <summary>Rejects a misconfigured qualification host before filtered tests execute.</summary>
    [ModuleInitializer]
    internal static void RequireQualifiedRuntimeWhenRequested()
    {
        var required = Environment.GetEnvironmentVariable("CAREER_REQUIRED_RUNTIME_MAJOR");
        if (required is null) return;
        var assembly = typeof(WebApplication).Assembly;
        if (required != "11" || Environment.Version.Major != 11
            || !RuntimeInformation.FrameworkDescription.StartsWith(".NET 11.", StringComparison.Ordinal)
            || assembly.GetName().Version?.Major != 11
            || !assembly.Location.Replace('\\', '/').Contains("/shared/Microsoft.AspNetCore.App/11.", StringComparison.Ordinal))
            throw new InvalidOperationException("The Career runtime qualification test host did not load the required shared frameworks.");
    }

    /// <summary>Exercises the identity check in the ordinary full test suite.</summary>
    [Fact]
    public async Task Loaded_shared_frameworks_match_the_qualification_lane()
    {
        RequireQualifiedRuntimeWhenRequested();
        var fields = string.Join(" ", Enumerable.Repeat("0", 18));
        Assert.Equal(456UL, CareerRuntimeProcessIdentity.ParseStartTicks($"123 (name with ) bracket) S {fields} 456"));
        Assert.Throws<InvalidOperationException>(() => CareerRuntimeProcessIdentity.ParseStartTicks("malformed"));
        Assert.Throws<InvalidOperationException>(() => CareerRuntimeProcessIdentity.ParseStartTicks($"123 (name) S {fields} 0"));
        CareerRuntimeProcessIdentity.RequireAcknowledgement(() => "run|123|456", "run|123|456", TimeSpan.FromSeconds(1));
        Assert.Throws<InvalidOperationException>(() => CareerRuntimeProcessIdentity.RequireAcknowledgement(
            () => "wrong", "run|123|456", TimeSpan.FromSeconds(1)));
        Assert.Throws<TimeoutException>(() => CareerRuntimeProcessIdentity.RequireAcknowledgement(
            () => "run|123|456", "run|123|456", TimeSpan.Zero));
        await Assert.ThrowsAsync<InvalidOperationException>(() => CareerRuntimeProcessIdentity.AwaitReceiptAsync(
            () => false, () => true, CancellationToken.None));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CareerRuntimeProcessIdentity.AwaitReceiptAsync(
            () => false, () => false, cancelled.Token));
        if (Environment.GetEnvironmentVariable("CAREER_REQUIRED_RUNTIME_MAJOR") is not null)
        {
            var assembly = typeof(WebApplication).Assembly;
            output.WriteLine($"Framework={RuntimeInformation.FrameworkDescription}; Core={Environment.Version}; ASP.NET={assembly.GetName().Version}; Location={assembly.Location}");
        }
    }
}
