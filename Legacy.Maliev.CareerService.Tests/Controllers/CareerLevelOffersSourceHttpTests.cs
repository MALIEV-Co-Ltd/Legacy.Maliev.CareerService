using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Legacy.Maliev.CareerService.Domain;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.CareerService.Tests.Controllers;

[Collection("Career shared level")]
public sealed class CareerLevelOffersSourceHttpTests(CareerLifecycleFixture fixture)
    : IClassFixture<CareerLifecycleFixture>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnonymousLevelRead_PreservesOriginalEmptyOffersCollection(bool detail)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var cancellation = lifetime.Token;
        await fixture.ResetAsync();
        int id;
        await using (var db = fixture.Context())
        {
            var level = new JobLevel
            {
                Name = "วิศวกร",
                Description = "Synthetic original level navigation",
                CreatedDate = new DateTime(2020, 1, 1),
                ModifiedDate = new DateTime(2020, 1, 2),
            };
            db.Levels.Add(level);
            await db.SaveChangesAsync(cancellation);
            id = level.Id;
        }

        var before = await SnapshotAsync(cancellation);
        using var client = fixture.Client();
        using var response = await client.GetAsync(detail ? $"/jobs/Levels/{id}" : "/jobs/Levels", cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
        Assert.Equal(before, await SnapshotAsync(cancellation));
        var levelJson = detail ? json.RootElement : Assert.Single(json.RootElement.EnumerateArray().ToArray());
        Assert.Equal(id, levelJson.GetProperty("id").GetInt32());
        Assert.Equal("วิศวกร", levelJson.GetProperty("name").GetString());
        Assert.Equal("Synthetic original level navigation", levelJson.GetProperty("description").GetString());
        Assert.Equal(new DateTime(2020, 1, 1), levelJson.GetProperty("createdDate").GetDateTime());
        Assert.Equal(new DateTime(2020, 1, 2), levelJson.GetProperty("modifiedDate").GetDateTime());
        Assert.Empty(fixture.Authorities);
        AssertOriginalEmptyOffers(levelJson);
    }

    [Fact]
    public async Task AuthorizedLevelCreate_PreservesOriginalEmptyOffersCollection()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var cancellation = lifetime.Token;
        await fixture.ResetAsync();
        using var client = fixture.Client("legacy-career.levels.create");
        using var response = await client.PostAsJsonAsync("/jobs/Levels", new
        {
            name = "วิศวกร",
            description = "Synthetic original created level",
        }, cancellation);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
        var levelJson = json.RootElement;
        var id = levelJson.GetProperty("id").GetInt32();
        Assert.True(id > 0);
        Assert.EndsWith($"/jobs/Levels/{id}", response.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);
        var authority = Assert.Single(fixture.Authorities.Values);
        Assert.Equal("legacy-career.levels.create", authority.Permission);
        Assert.True(authority.Calls > 0);
        await using (var db = fixture.Context())
        {
            var stored = Assert.Single(await db.Levels.AsNoTracking().ToArrayAsync(cancellation));
            Assert.Equal(id, stored.Id);
            Assert.Equal("วิศวกร", stored.Name);
            Assert.Equal("Synthetic original created level", stored.Description);
            Assert.Equal(stored.Name, levelJson.GetProperty("name").GetString());
            Assert.Equal(stored.Description, levelJson.GetProperty("description").GetString());
            Assert.NotNull(stored.CreatedDate);
            Assert.NotNull(stored.ModifiedDate);
            var createdTicks = levelJson.GetProperty("createdDate").GetDateTime().Ticks;
            var modifiedTicks = levelJson.GetProperty("modifiedDate").GetDateTime().Ticks;
            Assert.Equal(createdTicks - createdTicks % 10, stored.CreatedDate.Value.Ticks);
            Assert.Equal(modifiedTicks - modifiedTicks % 10, stored.ModifiedDate.Value.Ticks);
            Assert.Empty(await db.Offers.AsNoTracking().ToArrayAsync(cancellation));
        }
        AssertOriginalEmptyOffers(levelJson);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LevelGraph_DirectReadKeepsUnloadedOffersEmptyWithPersistedSiblings(bool detail)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var cancellation = lifetime.Token;
        var graph = await SeedGraphAsync(cancellation);
        var before = await GraphSnapshotAsync(cancellation);
        using var client = fixture.Client();
        using var response = await client.GetAsync(detail ? $"/jobs/Levels/{graph.LevelId}" : "/jobs/Levels", cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
        var levels = detail ? new[] { json.RootElement } : json.RootElement.EnumerateArray().ToArray();
        Assert.Equal(detail ? 1 : 2, levels.Length);
        if (detail)
        {
            Assert.Equal(graph.LevelId, Assert.Single(levels).GetProperty("id").GetInt32());
        }
        else
        {
            Assert.Equal(
                new[] { graph.LevelId, graph.SecondLevelId }.OrderBy(id => id),
                levels.Select(level => level.GetProperty("id").GetInt32()).OrderBy(id => id));
        }
        foreach (var level in levels)
        {
            AssertOriginalEmptyOffers(level);
            var selected = level.GetProperty("id").GetInt32() == graph.LevelId;
            Assert.Equal(selected ? "วิศวกร graph" : "ช่าง graph", level.GetProperty("name").GetString());
            Assert.Equal(selected ? "Primary graph level" : "Secondary graph level", level.GetProperty("description").GetString());
            Assert.Equal(new DateTime(2020, 1, 1), level.GetProperty("createdDate").GetDateTime());
            Assert.Equal(new DateTime(2020, 1, 2), level.GetProperty("modifiedDate").GetDateTime());
        }
        Assert.Equal(before, await GraphSnapshotAsync(cancellation));
        Assert.Empty(fixture.Authorities);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task LevelGraph_PaginatedSiblingsOmitNestedOffersAndPreserveWholeRows(int index)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var cancellation = lifetime.Token;
        var graph = await SeedGraphAsync(cancellation);
        var before = await GraphSnapshotAsync(cancellation);
        using var client = fixture.Client();
        using var response = await client.GetAsync($"/Jobs?sort=JobId_Ascending&index={index}&size=1", cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
        var root = json.RootElement;
        Assert.Equal(index, root.GetProperty("pageIndex").GetInt32());
        Assert.Equal(3, root.GetProperty("totalItems").GetInt32());
        Assert.Equal(3, root.GetProperty("totalPages").GetInt32());
        Assert.Equal(index > 1, root.GetProperty("hasPreviousPage").GetBoolean());
        Assert.True(root.GetProperty("hasNextPage").GetBoolean());
        var offer = Assert.Single(root.GetProperty("items").EnumerateArray().ToArray());
        Assert.Equal(graph.OfferIds[index - 1], offer.GetProperty("id").GetInt32());
        Assert.Equal(graph.LevelId, offer.GetProperty("levelId").GetInt32());
        Assert.Equal($"Graph offer {index}", offer.GetProperty("title").GetString());
        Assert.Equal("Intro graph", offer.GetProperty("introduction").GetString());
        Assert.Equal("Description graph", offer.GetProperty("description").GetString());
        Assert.Equal("Prerequisite graph", offer.GetProperty("prerequisites").GetString());
        Assert.Equal("Offer graph", offer.GetProperty("whatWeOffer").GetString());
        Assert.Equal("Thailand graph", offer.GetProperty("location").GetString());
        Assert.Equal(index == 2, offer.GetProperty("isFilled").GetBoolean());
        Assert.Equal(new DateTime(2020, 2, 1), offer.GetProperty("createdDate").GetDateTime());
        Assert.Equal(new DateTime(2020, 2, 2), offer.GetProperty("modifiedDate").GetDateTime());
        var level = offer.GetProperty("level");
        Assert.Equal(graph.LevelId, level.GetProperty("id").GetInt32());
        Assert.Equal("วิศวกร graph", level.GetProperty("name").GetString());
        Assert.Equal("Primary graph level", level.GetProperty("description").GetString());
        Assert.Equal(new DateTime(2020, 1, 1), level.GetProperty("createdDate").GetDateTime());
        Assert.Equal(new DateTime(2020, 1, 2), level.GetProperty("modifiedDate").GetDateTime());
        Assert.False(level.TryGetProperty("offers", out _));
        Assert.Equal(before, await GraphSnapshotAsync(cancellation));
        Assert.Empty(fixture.Authorities);
    }

    private async Task<(int LevelId, int SecondLevelId, int[] OfferIds)> SeedGraphAsync(CancellationToken cancellation)
    {
        await fixture.ResetAsync();
        await using var db = fixture.Context();
        var first = new JobLevel
        {
            Name = "วิศวกร graph",
            Description = "Primary graph level",
            CreatedDate = new DateTime(2020, 1, 1),
            ModifiedDate = new DateTime(2020, 1, 2),
        };
        var second = new JobLevel
        {
            Name = "ช่าง graph",
            Description = "Secondary graph level",
            CreatedDate = new DateTime(2020, 1, 1),
            ModifiedDate = new DateTime(2020, 1, 2),
        };
        db.Levels.AddRange(first, second);
        await db.SaveChangesAsync(cancellation);
        var ids = new List<int>();
        for (var index = 1; index <= 3; index++)
        {
            var offer = new JobOffer
            {
                LevelId = index < 3 ? first.Id : second.Id,
                Title = $"Graph offer {index}",
                Introduction = "Intro graph",
                Description = "Description graph",
                Prerequisites = "Prerequisite graph",
                WhatWeOffer = "Offer graph",
                Location = "Thailand graph",
                IsFilled = index == 2,
                CreatedDate = new DateTime(2020, 2, 1),
                ModifiedDate = new DateTime(2020, 2, 2),
            };
            db.Offers.Add(offer);
            await db.SaveChangesAsync(cancellation);
            ids.Add(offer.Id);
        }
        return (first.Id, second.Id, ids.ToArray());
    }

    private async Task<string> GraphSnapshotAsync(CancellationToken cancellation)
    {
        await using var db = fixture.Context();
        return JsonSerializer.Serialize(new
        {
            Levels = await db.Levels.AsNoTracking().OrderBy(row => row.Id).Select(row => new
            {
                row.Id,
                row.Name,
                row.Description,
                row.CreatedDate,
                row.ModifiedDate,
                Version = EF.Property<uint>(row, "Version"),
            }).ToArrayAsync(cancellation),
            Offers = await db.Offers.AsNoTracking().OrderBy(row => row.Id).Select(row => new
            {
                row.Id,
                row.LevelId,
                row.Title,
                row.Introduction,
                row.Description,
                row.Prerequisites,
                row.WhatWeOffer,
                row.Location,
                row.IsFilled,
                row.CreatedDate,
                row.ModifiedDate,
                Version = EF.Property<uint>(row, "Version"),
            }).ToArrayAsync(cancellation),
        });
    }

    private static void AssertOriginalEmptyOffers(JsonElement level)
    {
        Assert.True(level.TryGetProperty("offers", out var offers),
            "Original Level exposes its public initialized Offers collection.");
        Assert.Equal(JsonValueKind.Array, offers.ValueKind);
        Assert.Equal(0, offers.GetArrayLength());
    }

    private async Task<string> SnapshotAsync(CancellationToken cancellation)
    {
        await using var db = fixture.Context();
        return JsonSerializer.Serialize(new
        {
            Levels = await db.Levels.AsNoTracking().OrderBy(row => row.Id).Select(row => new
            {
                row.Id,
                row.Name,
                row.Description,
                row.CreatedDate,
                row.ModifiedDate,
                Version = EF.Property<uint>(row, "Version"),
            }).ToArrayAsync(cancellation),
            OfferCount = await db.Offers.CountAsync(cancellation),
        });
    }
}
