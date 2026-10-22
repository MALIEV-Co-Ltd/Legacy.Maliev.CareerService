using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Legacy.Maliev.CareerService.Tests.Controllers;

/// <summary>Serializes production dependency-failure health tests.</summary>
[CollectionDefinition("Career unhealthy readiness", DisableParallelization = true)]
public sealed class CareerUnhealthyReadinessHttpTestsCollection;

/// <summary>Checks sanitized readiness failures while the process remains live.</summary>
/// <param name="fixture">The lifecycle fixture used to create a production host with a controlled failing readiness dependency.</param>
[Collection("Career unhealthy readiness")]
public sealed class CareerUnhealthyReadinessHttpTests(CareerLifecycleFixture fixture)
    : IClassFixture<CareerLifecycleFixture>
{
    /// <summary>Checks anonymous 503 readiness responses and healthy liveness after dependency failure.</summary>
    /// <param name="throwFailure">Whether the controlled health check throws an exception instead of returning an unhealthy result with private diagnostics.</param>
    /// <returns>A task that completes after anonymous readiness failure sanitization and continued healthy liveness have been asserted.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProductionFailedDependency_ReturnsAnonymousSanitized503AndKeepsLivenessHealthy(bool throwFailure)
    {
        const string descriptionMarker = "private-description-marker";
        const string exceptionMarker = "private-exception-marker";
        const string dataMarker = "private-data-marker";
        await using var baseHost = fixture.NewFactory();
        await using var host = baseHost.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["CORS:AllowedOrigins:0"] = "https://example.test" }));
            builder.ConfigureTestServices(services => services.AddHealthChecks().AddCheck("controlled-readiness", () =>
            {
                if (throwFailure) throw new InvalidOperationException(exceptionMarker);
                return HealthCheckResult.Unhealthy(descriptionMarker, new InvalidOperationException(exceptionMarker),
                    new Dictionary<string, object> { ["private-diagnostic"] = dataMarker });
            }));
        });
        Assert.Equal("Production", host.Services.GetRequiredService<IWebHostEnvironment>().EnvironmentName);
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Null(client.DefaultRequestHeaders.Authorization);
        using var response = await client.GetAsync("/Jobs/readiness");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        foreach (var marker in new[] { descriptionMarker, exceptionMarker, dataMarker, "private-diagnostic" })
            Assert.DoesNotContain(marker, body, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.Equal(new[] { "checks", "status", "totalDuration" },
            root.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal("Unhealthy", root.GetProperty("status").GetString());
        Assert.True(root.GetProperty("totalDuration").GetDouble() >= 0);
        var failedCheck = root.GetProperty("checks").GetProperty("controlled-readiness");
        Assert.Equal("Unhealthy", failedCheck.GetProperty("status").GetString());
        foreach (var check in root.GetProperty("checks").EnumerateObject())
        {
            Assert.Equal(new[] { "duration", "status" },
                check.Value.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
            Assert.True(check.Value.GetProperty("duration").GetDouble() >= 0);
        }
        using var liveness = await client.GetAsync("/Jobs/liveness");
        Assert.Equal(HttpStatusCode.OK, liveness.StatusCode);
        Assert.Equal("Healthy", await liveness.Content.ReadAsStringAsync());
    }
}
