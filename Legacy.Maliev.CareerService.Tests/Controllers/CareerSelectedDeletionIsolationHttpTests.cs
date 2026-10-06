using System.Net;
using System.Text.Json;
using Legacy.Maliev.CareerService.Domain;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.CareerService.Tests.Controllers;

[Collection("Career shared level")]
public sealed class CareerSelectedDeletionIsolationHttpTests(CareerLifecycleFixture fixture)
    : IClassFixture<CareerLifecycleFixture>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectedOfferDeletion_PreservesSharedLevelsAndSurvivorsAndRecomputesGlobalOpenStatus(bool otherOpen)
    {
        await fixture.ResetAsync();
        JobOffer[] offers;
        JobLevel[] levels;
        await using (var db = fixture.Context())
        {
            levels =
            [
                new JobLevel { Name = "Shared level", Description = "Shared synthetic level", CreatedDate = new DateTime(2020, 1, 1), ModifiedDate = new DateTime(2020, 1, 2) },
                new JobLevel { Name = "Unrelated level", Description = "Unrelated synthetic level", CreatedDate = new DateTime(2020, 1, 1), ModifiedDate = new DateTime(2020, 1, 2) }
            ];
            db.Levels.AddRange(levels);
            await db.SaveChangesAsync();
            offers =
            [
                Offer("Selected วิศวกร", levels[0].Id, false),
                Offer("Filled survivor", levels[0].Id, true),
                Offer("Nullable survivor", levels[0].Id, null),
                Offer("Unrelated survivor", levels[1].Id, !otherOpen)
            ];
            db.Offers.AddRange(offers);
            await db.SaveChangesAsync();
            var nullableId = offers[2].Id;
            Assert.Equal(1, await db.Offers.Where(offer => offer.Id == nullableId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(offer => offer.IsFilled, (bool?)null)));
        }
        var target = offers[0];
        await using (var readback = fixture.Context())
        {
            var stored = await readback.Offers.AsNoTracking().OrderBy(offer => offer.Id).ToArrayAsync();
            Assert.Equal(offers.Select(offer => offer.Id).OrderBy(id => id).ToArray(), stored.Select(offer => offer.Id).ToArray());
            foreach (var expected in offers)
            {
                var row = Assert.Single(stored, offer => offer.Id == expected.Id);
                Assert.Equal(expected.Title, row.Title);
                Assert.Equal(expected.LevelId, row.LevelId);
                Assert.Equal(expected.IsFilled, row.IsFilled);
            }
            Assert.Null(Assert.Single(stored, offer => offer.Id == offers[2].Id).IsFilled);
            Assert.NotEqual(levels[0].Id, levels[1].Id);
            Assert.Equal(levels.Select(level => level.Id).OrderBy(id => id).ToArray(),
                await readback.Levels.AsNoTracking().OrderBy(level => level.Id).Select(level => level.Id).ToArrayAsync());
        }
        var expectedGraph = await SnapshotAsync(target.Id);
        using var reader = fixture.Client();
        await AssertOpenStatusAsync(reader, true);
        using var writer = fixture.Client("legacy-career.jobs.delete");
        var authority = Assert.Single(fixture.Authorities.Values);
        using (var deleted = await writer.DeleteAsync($"/Jobs/{target.Id}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }
        Assert.Equal("allow", authority.Decision);
        Assert.Equal("legacy-career.jobs.delete", authority.Permission);
        Assert.Equal(1, authority.LiveCalls);
        Assert.True(authority.Calls >= authority.LiveCalls);
        Assert.Equal(expectedGraph, await SnapshotAsync());
        await using (var readback = fixture.Context())
        {
            Assert.False(await readback.Offers.AsNoTracking().AnyAsync(offer => offer.Id == target.Id));
            Assert.Null((await readback.Offers.AsNoTracking().SingleAsync(offer => offer.Id == offers[2].Id)).IsFilled);
            Assert.Equal(otherOpen, await readback.Offers.AsNoTracking().AnyAsync(offer => offer.IsFilled == false));
        }
        using (var missing = await reader.GetAsync($"/Jobs/{target.Id}"))
        {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.DoesNotContain("Selected", await missing.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        await AssertOpenStatusAsync(reader, otherOpen);
        var survivors = offers.Where(offer => offer.Id != target.Id).OrderBy(offer => offer.Id).ToArray();
        foreach (var offer in survivors)
        {
            using var detail = await reader.GetAsync($"/Jobs/{offer.Id}");
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            using var json = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
            AssertOffer(json.RootElement, offer, levels.Single(level => level.Id == offer.LevelId));
        }
        foreach (var route in new[] { "/Jobs", "/jobs/" })
        {
            using var response = await reader.GetAsync(route);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var page = json.RootElement;
            Assert.Equal(3, page.GetProperty("totalItems").GetInt32());
            Assert.Equal(1, page.GetProperty("pageIndex").GetInt32());
            Assert.Equal(1, page.GetProperty("totalPages").GetInt32());
            Assert.False(page.GetProperty("hasPreviousPage").GetBoolean());
            Assert.False(page.GetProperty("hasNextPage").GetBoolean());
            Assert.False(page.TryGetProperty("Items", out _));
            var items = page.GetProperty("items").EnumerateArray().ToArray();
            Assert.Equal(survivors.Select(offer => offer.Id).ToArray(), items.Select(item => item.GetProperty("id").GetInt32()).ToArray());
            for (var index = 0; index < survivors.Length; index++)
                AssertOffer(items[index], survivors[index], levels.Single(level => level.Id == survivors[index].LevelId));
        }
        foreach (var level in levels)
        {
            using var detail = await reader.GetAsync($"/jobs/Levels/{level.Id}");
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            using var json = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
            AssertLevel(json.RootElement, level);
        }
        using (var list = await reader.GetAsync("/jobs/Levels"))
        {
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            using var json = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
            var items = json.RootElement.EnumerateArray().ToArray();
            Assert.Equal(levels.Select(level => level.Id).OrderBy(id => id).ToArray(), items.Select(item => item.GetProperty("id").GetInt32()).ToArray());
            foreach (var item in items) AssertLevel(item, levels.Single(level => level.Id == item.GetProperty("id").GetInt32()));
        }
        using var repeated = await writer.DeleteAsync($"/Jobs/{target.Id}");
        Assert.Equal(HttpStatusCode.NotFound, repeated.StatusCode);
        Assert.Equal(2, authority.LiveCalls); // A prior positive decision must not skip the second live check.
        await AssertOpenStatusAsync(reader, otherOpen);
        Assert.Equal(expectedGraph, await SnapshotAsync());
    }

    private static JobOffer Offer(string title, int levelId, bool? filled) => new()
    {
        LevelId = levelId,
        Title = title,
        Description = "Synthetic offer description",
        Prerequisites = "Synthetic prerequisites",
        WhatWeOffer = "Synthetic benefits",
        IsFilled = filled,
        CreatedDate = new DateTime(2020, 1, 1),
        ModifiedDate = new DateTime(2020, 1, 2)
    };

    private static async Task AssertOpenStatusAsync(HttpClient reader, bool expected)
    {
        foreach (var route in new[] { "/Jobs/job-opening-status", "/jobs/job-opening-status/" })
        {
            using var response = await reader.GetAsync(route);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(expected, document.RootElement.GetBoolean());
        }
    }

    private static void AssertOffer(JsonElement json, JobOffer offer, JobLevel level)
    {
        Assert.Equal(offer.Id, json.GetProperty("id").GetInt32());
        Assert.Equal(offer.LevelId, json.GetProperty("levelId").GetInt32());
        Assert.Equal(offer.Title, json.GetProperty("title").GetString());
        Assert.Equal(offer.Description, json.GetProperty("description").GetString());
        Assert.Equal(offer.Prerequisites, json.GetProperty("prerequisites").GetString());
        Assert.Equal(offer.WhatWeOffer, json.GetProperty("whatWeOffer").GetString());
        Assert.Equal(offer.CreatedDate, json.GetProperty("createdDate").GetDateTime());
        Assert.Equal(offer.ModifiedDate, json.GetProperty("modifiedDate").GetDateTime());
        if (offer.IsFilled is null) Assert.False(json.TryGetProperty("isFilled", out _));
        else Assert.Equal(offer.IsFilled.Value, json.GetProperty("isFilled").GetBoolean());
        Assert.False(json.TryGetProperty("introduction", out _));
        Assert.False(json.TryGetProperty("location", out _));
        Assert.False(json.TryGetProperty("Id", out _));
        AssertLevel(json.GetProperty("level"), level);
    }

    private static void AssertLevel(JsonElement json, JobLevel level)
    {
        Assert.Equal(level.Id, json.GetProperty("id").GetInt32());
        Assert.Equal(level.Name, json.GetProperty("name").GetString());
        Assert.Equal(level.Description, json.GetProperty("description").GetString());
        Assert.Equal(level.CreatedDate, json.GetProperty("createdDate").GetDateTime());
        Assert.Equal(level.ModifiedDate, json.GetProperty("modifiedDate").GetDateTime());
        Assert.False(json.TryGetProperty("offers", out _));
        Assert.False(json.TryGetProperty("Id", out _));
    }

    private async Task<string> SnapshotAsync(int? excludedOfferId = null)
    {
        await using var db = fixture.Context();
        return JsonSerializer.Serialize(new
        {
            Offers = await db.Offers.AsNoTracking().Where(offer => excludedOfferId == null || offer.Id != excludedOfferId)
                .OrderBy(offer => offer.Id).Select(offer => new
                {
                    offer.Id,
                    offer.LevelId,
                    offer.Title,
                    offer.Introduction,
                    offer.Description,
                    offer.Prerequisites,
                    offer.WhatWeOffer,
                    offer.Location,
                    offer.IsFilled,
                    offer.CreatedDate,
                    offer.ModifiedDate,
                    Version = EF.Property<uint>(offer, "Version")
                }).ToArrayAsync(),
            Levels = await db.Levels.AsNoTracking().OrderBy(level => level.Id).Select(level => new
            {
                level.Id,
                level.Name,
                level.Description,
                level.CreatedDate,
                level.ModifiedDate,
                Version = EF.Property<uint>(level, "Version")
            }).ToArrayAsync()
        });
    }
}
