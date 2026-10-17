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

[Collection("Career shared level")]
public sealed class CareerOfferOpenApiTests(CareerRouteFixture fixture, ITestOutputHelper output)
    : IClassFixture<CareerRouteFixture>
{
    [Fact]
    public async Task ActualDocument_PreservesNamedOfferRequestPropertiesRequirednessAndNullability()
    {
        await using var candidate = fixture.Factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        await using var native = fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(CareerOfferNativeOptions.RemoveOwned);
        });
        using var candidateClient = candidate.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var nativeClient = native.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        CareerOfferNativeOptions.AssertNative(native.Services.GetRequiredService<IOptions<MvcJsonOptions>>()
            .Value.JsonSerializerOptions);
        var actual = Projection(await Document(candidateClient));
        var expected = Projection(await Document(nativeClient));
        output.WriteLine("CAREER-OFFER-SCHEMA-NATIVE " + expected);
        output.WriteLine("CAREER-OFFER-SCHEMA-CANDIDATE " + actual);
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
        var schema = document.GetProperty("components").GetProperty("schemas").GetProperty("UpsertJobOfferRequest");
        Assert.True(schema.TryGetProperty("properties", out var properties), "Offer request schema must not become opaque.");
        Assert.Equal(new[] { "description", "introduction", "isFilled", "levelId", "location", "prerequisites", "title", "whatWeOffer" }, properties.EnumerateObject().Select(item => item.Name)
            .Order(StringComparer.Ordinal).ToArray());
        foreach (var name in new[] { "title", "description", "prerequisites", "introduction", "whatWeOffer", "location" })
            Assert.Contains("string", Types(properties.GetProperty(name)));
        Assert.Contains("integer", Types(properties.GetProperty("levelId")));
        Assert.Contains("boolean", Types(properties.GetProperty("isFilled")));
        // Compare the actual native schema, including requiredness, nullability, descriptions and examples.
        return Canonical(schema);
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
