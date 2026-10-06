using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.CareerService.Tests.Controllers;

[CollectionDefinition("Career production pipeline", DisableParallelization = true)]
public sealed class CareerProductionPipelineHttpTestsCollection;

[Collection("Career production pipeline")]
public sealed class CareerProductionPipelineHttpTests(CareerLifecycleFixture fixture)
    : IClassFixture<CareerLifecycleFixture>
{
    private const string AllowedOrigin = "https://example.test";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProductionPreflight_ContainsConfiguredOriginBeforeProtectedMutationAuthorization(bool allowed)
    {
        await using var host = NewHost();
        Assert.Equal("Production", host.Services.GetRequiredService<IWebHostEnvironment>().EnvironmentName);
        Assert.Contains(AllowedOrigin, host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions>>()
            .Value.GetPolicy("__DefaultCorsPolicy")!.Origins);
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Options, "/Jobs/1");
        request.Headers.Add("Origin", allowed ? AllowedOrigin : "https://blocked.example.invalid");
        request.Headers.Add("Access-Control-Request-Method", "PUT");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        if (allowed)
        {
            Assert.Equal(AllowedOrigin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
            Assert.Equal("true", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Credentials")));
            Assert.Contains("PUT", response.Headers.GetValues("Access-Control-Allow-Methods"));
            var headers = string.Join(",", response.Headers.GetValues("Access-Control-Allow-Headers"));
            Assert.Contains("authorization", headers, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("content-type", headers, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
            Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
        }
    }

    [Fact]
    public async Task ProductionAnonymousMutation_ReturnsUnauthorizedWithConfiguredOriginForBrowserErrorHandling()
    {
        await using var host = NewHost();
        Assert.Contains(AllowedOrigin, host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions>>()
            .Value.GetPolicy("__DefaultCorsPolicy")!.Origins);
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Put, "/Jobs/1")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Origin", AllowedOrigin);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(AllowedOrigin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Equal("true", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Credentials")));
    }

    [Theory]
    [InlineData("liveness")]
    [InlineData("readiness")]
    public async Task ProductionHealthyProbe_RemainsAnonymousAndUsesApprovedSanitizedWireShape(string probe)
    {
        await using var host = NewHost();
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var response = await client.GetAsync($"/Jobs/{probe}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        if (probe == "liveness")
        {
            Assert.Equal("Healthy", body);
            return;
        }
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("Healthy", json.RootElement.GetProperty("status").GetString());
        Assert.True(json.RootElement.GetProperty("totalDuration").GetDouble() >= 0);
        Assert.NotEmpty(json.RootElement.GetProperty("checks").EnumerateObject());
        foreach (var check in json.RootElement.GetProperty("checks").EnumerateObject())
        {
            Assert.Equal("Healthy", check.Value.GetProperty("status").GetString());
            Assert.True(check.Value.GetProperty("duration").GetDouble() >= 0);
            foreach (var privateField in new[] { "description", "data", "exception" })
                Assert.False(check.Value.TryGetProperty(privateField, out _));
        }
    }

    private WebApplicationFactory<Program> NewHost() => fixture.Factory.WithWebHostBuilder(builder =>
    {
        builder.UseSetting("CORS:AllowedOrigins:0", AllowedOrigin);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["CORS:AllowedOrigins:0"] = AllowedOrigin }));
    });
}
