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

/// <summary>Checks the served schema for the adapted level request.</summary>
/// <param name="fixture">The shared route fixture used to create adapted and native development hosts serving the level request OpenAPI schema.</param>
/// <param name="output">The test output sink that records the native and adapted canonical level schemas for comparison.</param>
[Collection("Career shared level")]
public sealed class CareerLevelOpenApiTests(CareerRouteFixture fixture, ITestOutputHelper output)
    : IClassFixture<CareerRouteFixture>
{
    /// <summary>Checks level request names, required properties, and nullability in actual OpenAPI output.</summary>
    /// <returns>A task that completes after asserting that the served adapted level request schema matches native property names, requiredness, nullability, and schema metadata.</returns>
    [Fact]
    public async Task ActualDocument_PreservesNamedLevelRequestPropertiesRequirednessAndNullability()
    {
        await using var candidate = fixture.Factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        await using var native = fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(CareerLevelNativeOptions.RemoveOwned);
        });
        using var candidateClient = candidate.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var nativeClient = native.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        CareerLevelNativeOptions.AssertNative(native.Services.GetRequiredService<IOptions<MvcJsonOptions>>()
            .Value.JsonSerializerOptions);
        var actual = Projection(await Document(candidateClient));
        var expected = Projection(await Document(nativeClient));
        output.WriteLine("CAREER-LEVEL-SCHEMA-NATIVE " + expected);
        output.WriteLine("CAREER-LEVEL-SCHEMA-CANDIDATE " + actual);
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
        var schema = document.GetProperty("components").GetProperty("schemas").GetProperty("UpsertJobLevelRequest");
        Assert.True(schema.TryGetProperty("properties", out var properties), "Level request schema must not become opaque.");
        Assert.Equal(new[] { "description", "name" }, properties.EnumerateObject().Select(item => item.Name)
            .Order(StringComparer.Ordinal).ToArray());
        foreach (var property in properties.EnumerateObject())
            Assert.Contains("string", Types(property.Value));
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
