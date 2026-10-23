using System.Net;
using System.Text.Json;
using Legacy.Maliev.CareerService.Tests.Controllers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;
using MvcJsonOptions = Microsoft.AspNetCore.Mvc.JsonOptions;

namespace Legacy.Maliev.CareerService.Tests.Serialization;

/// <summary>Checks served offer and level schemas after scalar input adaptation.</summary>
/// <param name="fixture">The shared Career route fixture providing the application factory, request clients, and test database access.</param>
/// <param name="output">The xUnit output sink for recording source, native, and candidate comparison evidence.</param>
[Collection("Career shared level")]
public sealed class CareerScalarBodyOpenApiTests(CareerRouteFixture fixture, ITestOutputHelper output)
    : IClassFixture<CareerRouteFixture>
{
    /// <summary>Checks both named request schemas in actual OpenAPI output.</summary>
    /// <returns>A task that completes when both served request schemas retain their named properties and native scalar metadata.</returns>
    [Fact]
    public async Task ActualDocument_PreservesBothNamedRequestSchemasAfterScalarAdapters()
    {
        await using var candidate = fixture.Factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        await using var native = fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                CareerOfferNativeOptions.RemoveOwned(services);
                CareerLevelNativeOptions.RemoveOwned(services);
            });
        });
        using var candidateClient = candidate.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var nativeClient = native.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        CareerLevelNativeOptions.AssertNative(native.Services.GetRequiredService<IOptions<MvcJsonOptions>>()
            .Value.JsonSerializerOptions);
        CareerOfferNativeOptions.AssertNative(native.Services.GetRequiredService<IOptions<MvcJsonOptions>>()
            .Value.JsonSerializerOptions);
        var actual = Projection(await Document(candidateClient));
        var expected = Projection(await Document(nativeClient));
        output.WriteLine("CAREER-SCALAR-SCHEMA-NATIVE " + expected);
        output.WriteLine("CAREER-SCALAR-SCHEMA-CANDIDATE " + actual);
        Assert.Equal(expected, actual);
    }

    private static async Task<JsonElement> Document(HttpClient client)
    {
        using var response = await client.GetAsync("/Jobs/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    private static string Projection(JsonElement document)
    {
        var schemas = document.GetProperty("components").GetProperty("schemas");
        var level = schemas.GetProperty("UpsertJobLevelRequest");
        var offer = schemas.GetProperty("UpsertJobOfferRequest");
        Assert.Equal(new[] { "description", "name" }, level.GetProperty("properties").EnumerateObject()
            .Select(item => item.Name).Order(StringComparer.Ordinal));
        Assert.Equal(new[] { "description", "introduction", "isFilled", "levelId", "location", "prerequisites", "title", "whatWeOffer" },
            offer.GetProperty("properties").EnumerateObject().Select(item => item.Name).Order(StringComparer.Ordinal));
        Assert.Contains("integer", Types(offer.GetProperty("properties").GetProperty("levelId")));
        Assert.Contains("boolean", Types(offer.GetProperty("properties").GetProperty("isFilled")));
        return Canonical(level) + "\\n" + Canonical(offer);
    }

    private static IEnumerable<string> Types(JsonElement schema)
    {
        if (schema.TryGetProperty("type", out var type))
        {
            if (type.ValueKind == JsonValueKind.String) yield return type.GetString()!;
            else if (type.ValueKind == JsonValueKind.Array)
                foreach (var item in type.EnumerateArray()) yield return item.GetString()!;
        }
        foreach (var keyword in new[] { "oneOf", "anyOf", "allOf" })
            if (schema.TryGetProperty(keyword, out var alternatives))
                foreach (var alternative in alternatives.EnumerateArray())
                    foreach (var value in Types(alternative)) yield return value;
    }

    private static string Canonical(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => "{" + string.Join(",", value.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal)
            .Select(item => JsonSerializer.Serialize(item.Name) + ":" + Canonical(item.Value))) + "}",
        JsonValueKind.Array => "[" + string.Join(",", value.EnumerateArray().Select(Canonical)) + "]",
        _ => value.GetRawText(),
    };
}
