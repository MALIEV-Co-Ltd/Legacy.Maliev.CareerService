using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.CareerService.Domain;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.CareerService.Tests.Controllers;

[CollectionDefinition("Career shared level", DisableParallelization = true)]
public sealed class CareerSharedLevelCollection;

[Collection("Career shared level")]
public sealed class CareerSharedLevelHttpTests(CareerRouteFixture fixture) : IClassFixture<CareerRouteFixture>
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("  Literal level  ", "  Literal description  ")]
    [InlineData("วิศวกร", "รายละเอียดระดับ")]
    public async Task SharedLevelUpdate_PublicSourceConsumerReadsRefreshWithoutChangingImportedOfferFields(string? name, string? description)
    {
        var levelId = await SeedAsync();
        var offersBefore = await OffersAsync();
        await using var db = fixture.Context();
        var beforeLevel = await db.Levels.AsNoTracking().SingleAsync();
        using var initialReader = fixture.Client();
        using var originalListing = await initialReader.GetAsync("/jobs/");
        Assert.Equal(HttpStatusCode.OK, originalListing.StatusCode);
        using var originalJson = JsonDocument.Parse(await originalListing.Content.ReadAsStringAsync());
        foreach (var item in originalJson.RootElement.GetProperty("items").EnumerateArray())
            AssertLevelFields(item.GetProperty("level"), "Original level", "Original description");
        using var writer = fixture.Client("legacy-career.levels.update");
        using var response = await writer.PutAsJsonAsync($"/jobs/levels/{levelId}", new { Name = name, Description = description });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var changed = await db.Levels.AsNoTracking().SingleAsync();
        Assert.Equal(name, changed.Name);
        Assert.Equal(description, changed.Description);
        Assert.Equal(beforeLevel.CreatedDate, changed.CreatedDate);
        Assert.True(changed.ModifiedDate > beforeLevel.ModifiedDate);
        Assert.Equal(offersBefore, await OffersAsync());
        using var reader = fixture.Client();
        using var levels = await reader.GetAsync("/jobs/levels/");
        Assert.Equal(HttpStatusCode.OK, levels.StatusCode);
        using var levelsJson = JsonDocument.Parse(await levels.Content.ReadAsStringAsync());
        var level = Assert.Single(levelsJson.RootElement.EnumerateArray());
        Assert.Equal(levelId, level.GetProperty("id").GetInt32());
        AssertLevelFields(level, name, description);
        using var detail = await reader.GetAsync($"/jobs/levels/{levelId}/");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using var detailJson = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        AssertLevelFields(detailJson.RootElement, name, description);
        using var listing = await reader.GetAsync("/jobs/?index=1&size=10");
        Assert.Equal(HttpStatusCode.OK, listing.StatusCode);
        using var listingJson = JsonDocument.Parse(await listing.Content.ReadAsStringAsync());
        var items = listingJson.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(2, items.Length);
        foreach (var item in items)
        {
            Assert.Equal(levelId, item.GetProperty("levelId").GetInt32());
            AssertLevelFields(item.GetProperty("level"), name, description);
            using var offer = await reader.GetAsync($"/jobs/{item.GetProperty("id").GetInt32()}");
            Assert.Equal(HttpStatusCode.OK, offer.StatusCode);
            using var offerJson = JsonDocument.Parse(await offer.Content.ReadAsStringAsync());
            Assert.Equal("Imported introduction", offerJson.RootElement.GetProperty("introduction").GetString());
            Assert.Equal("Imported benefits", offerJson.RootElement.GetProperty("whatWeOffer").GetString());
            Assert.Equal("Bangkok fixture", offerJson.RootElement.GetProperty("location").GetString());
            Assert.Equal(levelId, offerJson.RootElement.GetProperty("levelId").GetInt32());
        }
        Assert.Equal("true", await reader.GetStringAsync("/jobs/job-opening-status/"));
        Assert.Equal(offersBefore, await OffersAsync());
    }

    [Theory]
    [InlineData("anonymous", HttpStatusCode.Unauthorized)]
    [InlineData("expired", HttpStatusCode.Unauthorized)]
    [InlineData("wrong-signature", HttpStatusCode.Unauthorized)]
    [InlineData("missing-permission", HttpStatusCode.Forbidden)]
    public async Task SharedLevelUpdate_InvalidAuthorityCannotChangeLevelOrEitherPublicOffer(string authority, HttpStatusCode expected)
    {
        var id = await SeedAsync();
        var before = await SnapshotAsync();
        using var writer = fixture.Client("legacy-career.levels.update", authority);
        using var response = await writer.PutAsJsonAsync($"/jobs/levels/{id}", new { Name = "Refused", Description = "Refused description" });
        Assert.Equal(expected, response.StatusCode);
        Assert.DoesNotContain("Original level", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(before, await SnapshotAsync());
        using var reader = fixture.Client();
        using var listing = await reader.GetAsync("/jobs/");
        Assert.Equal(HttpStatusCode.OK, listing.StatusCode);
        using var json = JsonDocument.Parse(await listing.Content.ReadAsStringAsync());
        foreach (var item in json.RootElement.GetProperty("items").EnumerateArray())
            Assert.Equal("Original level", item.GetProperty("level").GetProperty("name").GetString());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("{\"Name\":42}")]
    public async Task SharedLevelUpdate_InvalidBodyRejectsWithoutChangingRelatedPersistedRows(string body)
    {
        var id = await SeedAsync();
        var before = await SnapshotAsync();
        using var writer = fixture.Client("legacy-career.levels.update");
        using var response = await writer.PutAsync($"/jobs/levels/{id}", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await SnapshotAsync());
    }

    private async Task<int> SeedAsync()
    {
        await fixture.ResetAsync();
        await using var db = fixture.Context();
        var level = new JobLevel
        {
            Name = "Original level",
            Description = "Original description",
            CreatedDate = new DateTime(2020, 1, 1),
            ModifiedDate = new DateTime(2020, 1, 2)
        };
        db.Levels.Add(level);
        await db.SaveChangesAsync();
        db.Offers.AddRange(new JobOffer
        {
            LevelId = level.Id,
            Title = "ช่าง Fixture",
            Introduction = "Imported introduction",
            WhatWeOffer = "Imported benefits",
            Location = "Bangkok fixture",
            IsFilled = false
        }, new JobOffer
        {
            LevelId = level.Id,
            Title = "Second fixture",
            Introduction = "Imported introduction",
            WhatWeOffer = "Imported benefits",
            Location = "Bangkok fixture",
            IsFilled = null
        });
        await db.SaveChangesAsync();
        return level.Id;
    }

    private async Task<string> OffersAsync()
    {
        await using var db = fixture.Context();
        return JsonSerializer.Serialize(await db.Offers.AsNoTracking().OrderBy(row => row.Id)
            .Select(row => new { row.Id, row.LevelId, row.Title, row.Description, row.Prerequisites, row.Introduction, row.WhatWeOffer, row.Location, row.IsFilled, row.CreatedDate, row.ModifiedDate }).ToArrayAsync());
    }

    private async Task<string> SnapshotAsync()
    {
        await using var db = fixture.Context();
        return JsonSerializer.Serialize(new
        {
            Offers = await OffersAsync(),
            Level = await db.Levels.AsNoTracking().Select(row => new { row.Id, row.Name, row.Description, row.CreatedDate, row.ModifiedDate }).SingleAsync()
        });
    }

    private static void AssertLevelFields(JsonElement level, string? name, string? description)
    {
        if (name is null) Assert.False(level.TryGetProperty("name", out _));
        else Assert.Equal(name, level.GetProperty("name").GetString());
        if (description is null) Assert.False(level.TryGetProperty("description", out _));
        else Assert.Equal(description, level.GetProperty("description").GetString());
        Assert.False(level.TryGetProperty("Name", out _));
        Assert.False(level.TryGetProperty("offers", out _));
    }
}
