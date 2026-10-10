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
    [InlineData("offer", "")]
    [InlineData("offer", "  Literal value  ")]
    [InlineData("offer", "วิศวกร")]
    [InlineData("level", "")]
    [InlineData("level", "  Literal value  ")]
    [InlineData("level", "วิศวกร")]
    public async Task SourceWriteWhitelist_IgnoresForgedIdentityDatesAndNestedGraphWhilePreservingLiteralFields(string kind, string value)
    {
        var levelId = await SeedAsync();
        var originalOffers = await OffersAsync();
        await using var db = fixture.Context();
        var originalLevel = await db.Levels.AsNoTracking().SingleAsync();
        const int forgedId = 2147483000;
        var forgedDate = new DateTime(1980, 1, 1);
        var route = kind == "offer" ? "/Jobs" : "/jobs/levels";
        var permission = kind == "offer" ? "legacy-career.jobs" : "legacy-career.levels";
        object Payload(string text) => kind == "offer"
            ? new
            {
                Id = forgedId,
                CreatedDate = forgedDate,
                ModifiedDate = forgedDate,
                LevelId = levelId,
                Title = text,
                Description = text,
                Prerequisites = text,
                IsFilled = true,
                Introduction = "Forged introduction",
                WhatWeOffer = "Forged benefits",
                Location = "Forged location",
                Level = new
                {
                    Id = forgedId,
                    Name = "Forged level",
                    Description = "Forged description"
                }
            }
            : (object)new
            {
                Id = forgedId,
                CreatedDate = forgedDate,
                ModifiedDate = forgedDate,
                Name = text,
                Description = text,
                Offers = new[]
                {
                    new
                    {
                        Id = forgedId,
                        Title = "Forged offer",
                        LevelId = forgedId
                    }
                }
            };
        var started = DateTime.UtcNow.AddSeconds(-5);
        using var creator = fixture.Client(permission + ".create");
        using var created = await creator.PostAsJsonAsync(route, Payload(value));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = createdJson.RootElement.GetProperty("id").GetInt32();
        Assert.True(id > 0);
        Assert.NotEqual(forgedId, id);
        Assert.EndsWith(route + "/" + id, created.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);
        DateTime? createdDate;
        if (kind == "offer")
        {
            var row = await db.Offers.AsNoTracking().SingleAsync(item => item.Id == id);
            Assert.Equal(value, row.Title);
            Assert.Equal(value, row.Description);
            Assert.Equal(value, row.Prerequisites);
            Assert.True(row.IsFilled);
            Assert.Equal(levelId, row.LevelId);
            Assert.Null(row.Introduction);
            Assert.Null(row.WhatWeOffer);
            Assert.Null(row.Location);
            createdDate = row.CreatedDate;
            Assert.NotNull(createdDate);
            Assert.NotEqual(forgedDate, row.CreatedDate);
            Assert.NotEqual(forgedDate, row.ModifiedDate);
            Assert.NotNull(row.ModifiedDate);
            Assert.InRange(row.ModifiedDate.Value, started, DateTime.UtcNow.AddMinutes(1));
            Assert.Equal(3, await db.Offers.CountAsync());
            Assert.Equal(originalLevel.Name, (await db.Levels.AsNoTracking().SingleAsync()).Name);
            // Imported rich fields are readable but remain outside both mutation whitelists.
            var imported = await db.Offers.SingleAsync(item => item.Id == id);
            imported.Introduction = "Imported introduction";
            imported.WhatWeOffer = "Imported benefits";
            imported.Location = "Imported location";
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }
        else
        {
            var row = await db.Levels.AsNoTracking().SingleAsync(item => item.Id == id);
            Assert.Equal(value, row.Name);
            Assert.Equal(value, row.Description);
            createdDate = row.CreatedDate;
            Assert.NotNull(createdDate);
            Assert.NotEqual(forgedDate, row.CreatedDate);
            Assert.NotEqual(forgedDate, row.ModifiedDate);
            Assert.NotNull(row.ModifiedDate);
            Assert.InRange(row.ModifiedDate.Value, started, DateTime.UtcNow.AddMinutes(1));
            Assert.Equal(2, await db.Levels.CountAsync());
            Assert.Equal(originalOffers, await OffersAsync());
        }
        Assert.InRange(createdDate!.Value, started, DateTime.UtcNow.AddMinutes(1));
        Assert.Equal(originalOffers, await OffersAsync(id, kind));
        var replacement = value + "Updated";
        var updateStarted = DateTime.UtcNow.AddSeconds(-5);
        using var writer = fixture.Client(permission + ".update");
        using var updated = await writer.PutAsJsonAsync(route + "/" + id, Payload(replacement));
        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
        Assert.Equal(string.Empty, await updated.Content.ReadAsStringAsync());
        if (kind == "offer")
        {
            var row = await db.Offers.AsNoTracking().SingleAsync(item => item.Id == id);
            Assert.Equal(replacement, row.Title);
            Assert.Equal(replacement, row.Description);
            Assert.Equal(replacement, row.Prerequisites);
            Assert.Equal(createdDate, row.CreatedDate);
            Assert.NotEqual(forgedDate, row.ModifiedDate);
            Assert.NotNull(row.ModifiedDate);
            Assert.InRange(row.ModifiedDate.Value, updateStarted, DateTime.UtcNow.AddMinutes(1));
            Assert.True(row.ModifiedDate >= createdDate);
            Assert.Equal(levelId, row.LevelId);
            Assert.Equal("Imported introduction", row.Introduction);
            Assert.Equal("Imported benefits", row.WhatWeOffer);
            Assert.Equal("Imported location", row.Location);
            Assert.Equal(3, await db.Offers.CountAsync());
            var unchanged = await db.Levels.AsNoTracking().SingleAsync();
            Assert.Equal(originalLevel.Name, unchanged.Name);
            Assert.Equal(originalLevel.Description, unchanged.Description);
            Assert.Equal(originalLevel.CreatedDate, unchanged.CreatedDate);
            Assert.Equal(originalLevel.ModifiedDate, unchanged.ModifiedDate);
        }
        else
        {
            var row = await db.Levels.AsNoTracking().SingleAsync(item => item.Id == id);
            Assert.Equal(replacement, row.Name);
            Assert.Equal(replacement, row.Description);
            Assert.Equal(createdDate, row.CreatedDate);
            Assert.NotEqual(forgedDate, row.ModifiedDate);
            Assert.NotNull(row.ModifiedDate);
            Assert.InRange(row.ModifiedDate.Value, updateStarted, DateTime.UtcNow.AddMinutes(1));
            Assert.True(row.ModifiedDate >= createdDate);
            Assert.Equal(2, await db.Levels.CountAsync());
            Assert.Equal(originalOffers, await OffersAsync());
        }
        using var reader = fixture.Client();
        using var detail = await reader.GetAsync(route + "/" + id);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using var detailJson = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        Assert.Equal(id, detailJson.RootElement.GetProperty("id").GetInt32());
        Assert.Equal(replacement, detailJson.RootElement.GetProperty(kind == "offer" ? "title" : "name").GetString());
        Assert.Equal(originalOffers, await OffersAsync(id, kind));
        var retainedLevel = await db.Levels.AsNoTracking().SingleAsync(item => item.Id == levelId);
        Assert.Equal(originalLevel.Name, retainedLevel.Name);
        Assert.Equal(originalLevel.Description, retainedLevel.Description);
        Assert.Equal(originalLevel.CreatedDate, retainedLevel.CreatedDate);
        Assert.Equal(originalLevel.ModifiedDate, retainedLevel.ModifiedDate);
        Assert.False(await db.Offers.AnyAsync(item => item.Id == forgedId));
        Assert.False(await db.Levels.AnyAsync(item => item.Id == forgedId));
    }

    [Theory]
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
        AssertLevelFields(level, name, description, direct: true);
        using var detail = await reader.GetAsync($"/jobs/levels/{levelId}/");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using var detailJson = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        AssertLevelFields(detailJson.RootElement, name, description, direct: true);
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
    [InlineData(null, "Valid description")]
    [InlineData("Valid name", null)]
    [InlineData(null, null)]
    public async Task SharedLevelUpdate_SourceRequiredFieldsFailPrivatelyWithoutChangingEitherOffer(string? name, string? description)
    {
        var id = await SeedAsync();
        var before = await SnapshotAsync();
        using var writer = fixture.Client("legacy-career.levels.update");
        using var response = await writer.PutAsJsonAsync($"/jobs/levels/{id}", new { Name = name, Description = description });
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal(500, json.RootElement.GetProperty("statusCode").GetInt32());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("details").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("traceId").GetString()));
        Assert.DoesNotContain("23502", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Npgsql", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Original level", body, StringComparison.Ordinal);
        Assert.Equal(before, await SnapshotAsync());
        using var reader = fixture.Client();
        using var listing = await reader.GetAsync("/jobs/");
        Assert.Equal(HttpStatusCode.OK, listing.StatusCode);
        using var listingJson = JsonDocument.Parse(await listing.Content.ReadAsStringAsync());
        foreach (var item in listingJson.RootElement.GetProperty("items").EnumerateArray())
            AssertLevelFields(item.GetProperty("level"), "Original level", "Original description");
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

    private async Task<string> OffersAsync(int? newId = null, string? kind = null)
    {
        await using var db = fixture.Context();
        return JsonSerializer.Serialize(await db.Offers.AsNoTracking().Where(row => kind != "offer" || row.Id != newId).OrderBy(row => row.Id)
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

    private static void AssertLevelFields(JsonElement level, string? name, string? description, bool direct = false)
    {
        if (name is null) Assert.False(level.TryGetProperty("name", out _));
        else Assert.Equal(name, level.GetProperty("name").GetString());
        if (description is null) Assert.False(level.TryGetProperty("description", out _));
        else Assert.Equal(description, level.GetProperty("description").GetString());
        Assert.False(level.TryGetProperty("Name", out _));
        if (direct)
        {
            var offers = level.GetProperty("offers");
            Assert.Equal(JsonValueKind.Array, offers.ValueKind);
            Assert.Equal(0, offers.GetArrayLength());
        }
        else Assert.False(level.TryGetProperty("offers", out _));
    }
}
