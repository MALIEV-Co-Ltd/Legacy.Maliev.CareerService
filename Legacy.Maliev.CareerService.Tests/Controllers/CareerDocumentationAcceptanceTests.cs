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
            // Match the controller contracts exactly; the shared health routes
            // also use the /Jobs service prefix but are not business operations.
            if (path.Name is not ("/Jobs" or "/Jobs/{offerId}" or "/Jobs/job-opening-status"
                or "/jobs/Levels" or "/jobs/Levels/{levelId}")) continue;
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

    [Fact]
    public async Task Documentation_DescribesActualCreationAndConcurrencyResults()
    {
        await using var host = fixture.Factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var body = JsonDocument.Parse(await client.GetStringAsync("/Jobs/openapi/v1.json"));
        var paths = body.RootElement.GetProperty("paths");
        foreach (var (collection, item) in new[] { ("/Jobs", "/Jobs/{offerId}"), ("/jobs/Levels", "/jobs/Levels/{levelId}") })
        {
            var created = paths.GetProperty(collection).GetProperty("post").GetProperty("responses").GetProperty("201");
            Assert.True(created.GetProperty("content").TryGetProperty("application/json", out _));
            Assert.False(string.IsNullOrWhiteSpace(created.GetProperty("description").GetString()));
            foreach (var method in new[] { "put", "delete" })
                foreach (var status in new[] { "204", "404", "409" })
                    Assert.False(string.IsNullOrWhiteSpace(paths.GetProperty(item).GetProperty(method).GetProperty("responses").GetProperty(status).GetProperty("description").GetString()));
        }
    }

    [Fact]
    public async Task Documentation_ExplainsJobPayloadAndItsExistingLevelReference()
    {
        await using var host = fixture.Factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var document = JsonDocument.Parse(await client.GetStringAsync("/Jobs/openapi/v1.json"));
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var request = schemas.GetProperty("UpsertJobOfferRequest");
        Assert.Equal("Legacy-compatible job offer create/update payload.", request.GetProperty("description").GetString());
        foreach (var name in new[] { "levelId", "title", "introduction", "description", "prerequisites", "whatWeOffer", "location", "isFilled" })
            Assert.True(request.GetProperty("properties").GetProperty(name).TryGetProperty("description", out var description)
                && !string.IsNullOrWhiteSpace(description.GetString()), $"Missing job field guidance for {name}: {request}");
        var level = schemas.GetProperty("JobOfferResponse").GetProperty("properties").GetProperty("level");
        var alternatives = level.GetProperty("oneOf").EnumerateArray().ToArray();
        Assert.Equal(2, alternatives.Length);
        Assert.Single(alternatives, item => item.TryGetProperty("type", out var type) && type.GetString() == "null");
        var reference = Assert.Single(alternatives, item => item.TryGetProperty("$ref", out _));
        Assert.Equal("#/components/schemas/JobLevelResponse", reference.GetProperty("$ref").GetString());
        Assert.True(reference.TryGetProperty("description", out var levelDescription) && !string.IsNullOrWhiteSpace(levelDescription.GetString()),
            $"Missing existing level-reference guidance: {level}");
    }

    [Fact]
    public async Task Documentation_ExplainsExactPublicOpenPositionBoolean()
    {
        await using var host = fixture.Factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var document = JsonDocument.Parse(await client.GetStringAsync("/Jobs/openapi/v1.json"));
        var response = document.RootElement.GetProperty("paths").GetProperty("/Jobs/job-opening-status").GetProperty("get")
            .GetProperty("responses").GetProperty("200");
        Assert.Equal("True if any job offer has IsFilled set to false; false otherwise.", response.GetProperty("description").GetString());
        Assert.Equal("boolean", response.GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("type").GetString());
    }

    [Fact]
    public async Task Documentation_ExplainsExistingLiveAuthorizationForCriticalDeletions()
    {
        await using var host = fixture.Factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var document = JsonDocument.Parse(await client.GetStringAsync("/Jobs/openapi/v1.json"));
        foreach (var path in new[] { "/Jobs/{offerId}", "/jobs/Levels/{levelId}" })
        {
            var deletion = document.RootElement.GetProperty("paths").GetProperty(path).GetProperty("delete");
            Assert.True(deletion.TryGetProperty("description", out var description), "The existing live authorization requirement must be documented.");
            Assert.Equal("Deletion requires a fresh authorization decision for the existing delete permission; cached permission claims do not authorize this critical operation.", description.GetString());
        }
    }

    [Fact]
    public async Task Documentation_DescribesPayloadInsteadOfCancellationForBodyOperations()
    {
        await using var host = fixture.Factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var document = JsonDocument.Parse(await client.GetStringAsync("/Jobs/openapi/v1.json"));
        foreach (var (path, method) in new[] { ("/Jobs", "post"), ("/Jobs/{offerId}", "put"), ("/jobs/Levels", "post"), ("/jobs/Levels/{levelId}", "put") })
        {
            var body = document.RootElement.GetProperty("paths").GetProperty(path).GetProperty(method).GetProperty("requestBody");
            Assert.True(body.TryGetProperty("description", out var description));
            Assert.Contains("job", description.GetString(), StringComparison.Ordinal);
            Assert.DoesNotContain("cancellation", description.GetString(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
