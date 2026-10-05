using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace Legacy.Maliev.CareerService.Tests.Controllers;

public sealed class CareerDocumentationAcceptanceTests(CareerRouteFixture fixture)
    : IClassFixture<CareerRouteFixture>
{
    [Theory]
    [InlineData("Development", true)]
    [InlineData("Staging", true)]
    [InlineData("Production", false)]
    public async Task Documentation_RespectsEnvironmentBoundary(string environment, bool exposed)
    {
        await using var host = fixture.Factory.WithWebHostBuilder(builder => builder.UseEnvironment(environment));
        using var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var response = await client.GetAsync("/Jobs/openapi/v1.json");
        Assert.Equal(exposed ? HttpStatusCode.OK : HttpStatusCode.NotFound, response.StatusCode);
        if (!exposed) return;
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Legacy MALIEV JobOffer Service API", body.RootElement.GetProperty("info").GetProperty("title").GetString());
    }

    [Fact]
    public async Task Documentation_DescribesMaintainedOperationsAndActualAuthorization()
    {
        await using var host = fixture.Factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync("/Jobs/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = body.RootElement.GetProperty("paths");
        var count = 0;
        foreach (var path in paths.EnumerateObject())
        {
            if (!path.Name.StartsWith("/Jobs", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var operation in path.Value.EnumerateObject().Where(item => item.Name is "get" or "post" or "put" or "delete"))
            {
                count++;
                Assert.True(operation.Value.TryGetProperty("summary", out var summary) && !string.IsNullOrWhiteSpace(summary.GetString()),
                    $"Missing maintained summary for {path.Name} {operation.Name}");
                var protectedOperation = operation.Value.TryGetProperty("security", out var requirements) && requirements.GetArrayLength() > 0;
                Assert.Equal(operation.Name != "get", protectedOperation);
            }
        }
        Assert.Equal(11, count);
        var bearer = body.RootElement.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("http", bearer.GetProperty("type").GetString());
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());
    }

    [Fact]
    public async Task Documentation_ExplainsPageAndLiteralSearchParameters()
    {
        await using var host = fixture.Factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var body = JsonDocument.Parse(await client.GetStringAsync("/Jobs/openapi/v1.json"));
        var operation = body.RootElement.GetProperty("paths").GetProperty("/Jobs").GetProperty("get");
        foreach (var name in new[] { "sort", "search", "index", "size" })
        {
            var parameter = Assert.Single(operation.GetProperty("parameters").EnumerateArray(), item => item.GetProperty("name").GetString() == name);
            Assert.True(parameter.TryGetProperty("description", out var description) && !string.IsNullOrWhiteSpace(description.GetString()));
        }
    }
}
