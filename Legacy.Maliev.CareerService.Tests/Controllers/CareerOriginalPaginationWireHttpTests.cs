using System.Net;
using System.Text.Json;
using Legacy.Maliev.CareerService.Domain;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.CareerService.Tests.Controllers;

/// <summary>Checks the original filtered page wire contract through normal PostgreSQL HTTP requests.</summary>
/// <param name="fixture">The owned production-pipeline fixture.</param>
[Collection("Career shared level")]
public sealed class CareerOriginalPaginationWireHttpTests(CareerLifecycleFixture fixture)
    : IClassFixture<CareerLifecycleFixture>
{
    /// <summary>Preserves original mixed-cohort counts, exact page wire fields, and unchanged physical rows.</summary>
    /// <param name="field">The original searchable field exercised by the cohort.</param>
    /// <param name="distractors">The original nonmatching row count.</param>
    /// <returns>A task that completes after all route and persistence assertions.</returns>
    [Theory]
    [InlineData("description", 60)]
    [InlineData("title", 81)]
    public async Task AnonymousFilter_ReturnsAllTwentyOriginalMatchesFromExactMixedCohortWithoutChangingRows(string field, int distractors)
    {
        await fixture.ResetAsync();
        var offers = Enumerable.Range(0, 20 + distractors).Select(index => new JobOffer
        {
            Level = new JobLevel { Name = "test name", Description = "test description" },
            Title = field == "title" ? (index < 20 ? "HelloWorld" : "Something else") : null,
            Description = field == "description" ? (index < 20 ? "HelloWorld" : "Something else") : null
        }).ToArray();
        await using (var db = fixture.Context())
        {
            db.Offers.AddRange(offers);
            await db.SaveChangesAsync();
        }
        int[] matchedIds;
        Dictionary<int, int> levelIds;
        await using (var db = fixture.Context())
        {
            var stored = await db.Offers.AsNoTracking().OrderBy(offer => offer.Id).ToArrayAsync();
            Assert.Equal(20 + distractors, stored.Length);
            Assert.Equal(20 + distractors, await db.Levels.CountAsync());
            Assert.Equal(20 + distractors, stored.Select(offer => offer.LevelId).Distinct().Count());
            matchedIds = stored.Where(offer => (field == "title" ? offer.Title : offer.Description) == "HelloWorld")
                .Select(offer => offer.Id).ToArray();
            Assert.Equal(20, matchedIds.Length);
            Assert.Equal(distractors, stored.Count(offer => (field == "title" ? offer.Title : offer.Description) == "Something else"));
            levelIds = stored.ToDictionary(offer => offer.Id, offer => offer.LevelId);
        }
        var before = await PhysicalStateAsync();
        using var anonymous = fixture.Client();
        foreach (var route in new[] { "/Jobs", "/jobs/" })
        {
            await AssertFilteredPageAsync(anonymous, route + "?search=HelloWorld", field, matchedIds, levelIds, 1, 1, false);
            // The filtered cardinality, rather than the unfiltered cohort, must determine the final page.
            await AssertFilteredPageAsync(anonymous, route + "?search=HelloWorld&index=3&size=7", field,
                matchedIds.Skip(14).ToArray(), levelIds, 3, 3, true);
            using var exhausted = await anonymous.GetAsync(route + "?search=HelloWorld&index=4&size=7");
            Assert.Equal(HttpStatusCode.NotFound, exhausted.StatusCode);
        }
        Assert.Equal(before, await PhysicalStateAsync());
    }

    private static async Task AssertFilteredPageAsync(HttpClient client, string path, string field,
        int[] expectedIds, IReadOnlyDictionary<int, int> levelIds, int pageIndex, int totalPages, bool previous)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var page = json.RootElement;
        Assert.Equal(new[] { "hasNextPage", "hasPreviousPage", "items", "pageIndex", "totalPages", "totalRecords" },
            page.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(20, page.GetProperty("totalRecords").GetInt32());
        Assert.Equal(pageIndex, page.GetProperty("pageIndex").GetInt32());
        Assert.Equal(totalPages, page.GetProperty("totalPages").GetInt32());
        Assert.Equal(previous, page.GetProperty("hasPreviousPage").GetBoolean());
        Assert.False(page.GetProperty("hasNextPage").GetBoolean());
        var items = page.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(expectedIds, items.Select(item => item.GetProperty("id").GetInt32()).ToArray());
        foreach (var item in items)
        {
            var id = item.GetProperty("id").GetInt32();
            Assert.Equal("HelloWorld", item.GetProperty(field).GetString());
            Assert.Equal(levelIds[id], item.GetProperty("levelId").GetInt32());
            var level = item.GetProperty("level");
            Assert.Equal(levelIds[id], level.GetProperty("id").GetInt32());
            Assert.Equal("test name", level.GetProperty("name").GetString());
            Assert.Equal("test description", level.GetProperty("description").GetString());
            Assert.False(level.TryGetProperty("offers", out _));
        }
    }

    private async Task<string> PhysicalStateAsync()
    {
        await using var db = fixture.Context();
        var offers = await db.Offers.AsNoTracking().OrderBy(offer => offer.Id)
            .Select(offer => new { Row = offer, Version = EF.Property<uint>(offer, "Version") }).ToArrayAsync();
        var levels = await db.Levels.AsNoTracking().OrderBy(level => level.Id)
            .Select(level => new { Row = level, Version = EF.Property<uint>(level, "Version") }).ToArrayAsync();
        return JsonSerializer.Serialize(new { Offers = offers, Levels = levels });
    }
}
