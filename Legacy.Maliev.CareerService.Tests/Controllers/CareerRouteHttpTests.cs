using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Legacy.Maliev.CareerService.Data;
using Legacy.Maliev.CareerService.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;

namespace Legacy.Maliev.CareerService.Tests.Controllers;

public sealed class CareerRouteHttpTests(CareerRouteFixture fixture) : IClassFixture<CareerRouteFixture>
{
    [Fact]
    public async Task AnonymousRead_DefaultPageAndLevelProjection_PreserveLiteralJsonAndPersistedThai()
    {
        await fixture.ResetAsync();
        await using var db = fixture.Context();
        var level = new JobLevel { Name = "วิศวกร", Description = "Fixture level" };
        db.Levels.Add(level); await db.SaveChangesAsync();
        db.Offers.AddRange(new JobOffer { LevelId = level.Id, Title = "ช่าง Fixture", IsFilled = false },
            new JobOffer { LevelId = level.Id, Title = "Other fixture", IsFilled = true });
        await db.SaveChangesAsync();
        using var client = fixture.Client();
        using var response = await client.GetAsync("/Jobs");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var page = json.RootElement;
        Assert.Equal(2, page.GetProperty("totalItems").GetInt32());
        Assert.Equal(1, page.GetProperty("pageIndex").GetInt32());
        Assert.Equal(1, page.GetProperty("totalPages").GetInt32());
        Assert.False(page.GetProperty("hasNextPage").GetBoolean());
        var first = page.GetProperty("items")[0];
        Assert.Equal("ช่าง Fixture", first.GetProperty("title").GetString());
        Assert.False(first.TryGetProperty("introduction", out _));
        Assert.Equal("วิศวกร", first.GetProperty("level").GetProperty("name").GetString());
        Assert.False(first.GetProperty("level").TryGetProperty("offers", out _));
        Assert.Equal("true", await client.GetStringAsync("/Jobs/job-opening-status"));
        using var detail = await client.GetAsync($"/Jobs/{first.GetProperty("id").GetInt32()}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using var levels = await client.GetAsync("/jobs/levels");
        Assert.Equal(HttpStatusCode.OK, levels.StatusCode);
    }

    [Theory]
    [InlineData("/Jobs")]
    [InlineData("/Jobs/2147483647")]
    [InlineData("/jobs/levels")]
    [InlineData("/jobs/levels/2147483647")]
    public async Task AnonymousRead_AbsentData_Preserves404(string route)
    {
        await fixture.ResetAsync(); using var client = fixture.Client();
        using var response = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("Jobs", "legacy-career.jobs.create", "anonymous", 401)]
    [InlineData("Jobs", "legacy-career.jobs.create", "expired", 401)]
    [InlineData("Jobs", "legacy-career.jobs.create", "wrong-signature", 401)]
    [InlineData("Jobs", "legacy-career.jobs.create", "missing-permission", 403)]
    [InlineData("jobs/levels", "legacy-career.levels.create", "missing-permission", 403)]
    public async Task Create_NormalAuthFailure_PreservesZeroRows(string route, string permission, string authority, int status)
    {
        await fixture.ResetAsync(); using var client = fixture.Client(permission, authority);
        using var response = await client.PostAsJsonAsync("/" + route, new { Name = "Fixture", Description = "Fixture", LevelId = 1, Title = "Fixture" });
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        await using var db = fixture.Context();
        Assert.False(await db.Offers.AnyAsync()); Assert.False(await db.Levels.AnyAsync());
    }

    [Fact]
    public async Task LevelCreateUpdateAndRead_NormalRs256_PersistsSourceFieldsAndNamedLocation()
    {
        await fixture.ResetAsync();
        using var create = fixture.Client("legacy-career.levels.create");
        using var response = await create.PostAsJsonAsync("/jobs/levels", new { Name = "วิศวกร", Description = "Original fixture" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var id = json.RootElement.GetProperty("id").GetInt32();
        Assert.EndsWith($"/jobs/Levels/{id}", response.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);
        await using var db = fixture.Context(); var before = await db.Levels.AsNoTracking().SingleAsync();
        Assert.Equal("วิศวกร", before.Name); Assert.NotNull(before.CreatedDate);
        using var update = fixture.Client("legacy-career.levels.update");
        using var changed = await update.PutAsJsonAsync($"/jobs/levels/{id}", new { Name = "ช่าง", Description = "Updated fixture" });
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        var after = await db.Levels.AsNoTracking().SingleAsync();
        Assert.Equal(before.CreatedDate, after.CreatedDate); Assert.Equal("ช่าง", after.Name);
        using var read = fixture.Client(); using var detail = await read.GetAsync($"/jobs/levels/{id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
    }

    [Fact]
    public async Task OfferCreate_SourceMutationWhitelist_DoesNotInventWritesToReadOnlyRichFields()
    {
        await fixture.ResetAsync(); var id = await fixture.SeedLevelAsync();
        using var client = fixture.Client("legacy-career.jobs.create");
        using var response = await client.PostAsJsonAsync("/Jobs", Payload(id));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var db = fixture.Context(); var offer = await db.Offers.AsNoTracking().SingleAsync();
        Assert.Equal("วิศวกร Fixture", offer.Title); Assert.Equal("Updated description", offer.Description);
        Assert.Null(offer.Introduction); Assert.Null(offer.WhatWeOffer); Assert.Null(offer.Location);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(offer.Id, json.RootElement.GetProperty("id").GetInt32());
        Assert.EndsWith($"/Jobs/{offer.Id}", response.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.False(json.RootElement.TryGetProperty("introduction", out _));
        Assert.False(json.RootElement.TryGetProperty("whatWeOffer", out _));
        Assert.False(json.RootElement.TryGetProperty("location", out _));
    }

    [Fact]
    public async Task OfferUpdate_SourceMutationWhitelist_PreservesImportedRichFieldsAndCreatedDate()
    {
        await fixture.ResetAsync(); var levelId = await fixture.SeedLevelAsync();
        await using var db = fixture.Context();
        var offer = new JobOffer
        {
            LevelId = levelId,
            Title = "Original fixture",
            Description = "Before",
            Introduction = "Imported introduction",
            WhatWeOffer = "Imported benefits",
            Location = "Imported location",
            CreatedDate = new DateTime(2020, 1, 1),
            ModifiedDate = new DateTime(2020, 1, 2)
        };
        db.Offers.Add(offer); await db.SaveChangesAsync();
        using var client = fixture.Client("legacy-career.jobs.update");
        using var response = await client.PutAsJsonAsync($"/Jobs/{offer.Id}", Payload(levelId));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var stored = await db.Offers.AsNoTracking().SingleAsync();
        Assert.Equal("วิศวกร Fixture", stored.Title); Assert.Equal("Updated description", stored.Description);
        Assert.Equal(offer.CreatedDate, stored.CreatedDate);
        Assert.Equal("Imported introduction", stored.Introduction);
        Assert.Equal("Imported benefits", stored.WhatWeOffer);
        Assert.Equal("Imported location", stored.Location);
    }

    [Theory]
    [InlineData("Jobs", "legacy-career.jobs.update")]
    [InlineData("jobs/levels", "legacy-career.levels.update")]
    public async Task Update_MissingRecord_Preserves404AndZeroRows(string route, string permission)
    {
        await fixture.ResetAsync(); using var client = fixture.Client(permission);
        using var response = await client.PutAsJsonAsync($"/{route}/2147483647", new { Name = "Fixture", Description = "Fixture", LevelId = 1 });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await using var db = fixture.Context(); Assert.False(await db.Offers.AnyAsync()); Assert.False(await db.Levels.AnyAsync());
    }

    private static object Payload(int levelId) => new
    {
        LevelId = levelId,
        Title = "วิศวกร Fixture",
        Description = "Updated description",
        Prerequisites = "Fixture prerequisite",
        IsFilled = false,
        Introduction = "Untrusted new intro",
        WhatWeOffer = "Untrusted new benefits",
        Location = "Untrusted new location"
    };

    [Theory]
    [InlineData("%", "Literal%")]
    [InlineData("_", "Literal_")]
    [InlineData("\\", "Literal\\")]
    [InlineData("ช่าง", "ช่าง")]
    public async Task AnonymousSearch_PreservesLiteralSpecialCharactersAndThai(string search, string title)
    {
        await fixture.ResetAsync(); var id = await fixture.SeedLevelAsync(); await using var db = fixture.Context();
        db.Offers.AddRange(new JobOffer { LevelId = id, Title = title }, new JobOffer { LevelId = id, Title = "Unrelated" });
        await db.SaveChangesAsync(); using var client = fixture.Client();
        using var response = await client.GetAsync("/Jobs?search=" + Uri.EscapeDataString(search));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(1, json.RootElement.GetProperty("totalItems").GetInt32());
        Assert.Equal(title, Assert.Single(json.RootElement.GetProperty("items").EnumerateArray()).GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("JobId_Ascending", false)]
    [InlineData("JobId_Descending", true)]
    [InlineData("JobCreatedDate_Ascending", false)]
    [InlineData("JobCreatedDate_Descending", true)]
    public async Task AnonymousSortAndPaging_PreserveAllFourSourceSorts(string sort, bool descending)
    {
        await fixture.ResetAsync(); var levelId = await fixture.SeedLevelAsync(); await using var db = fixture.Context();
        var first = new JobOffer { LevelId = levelId, Title = "First", CreatedDate = new DateTime(2020, 1, 1) };
        var second = new JobOffer { LevelId = levelId, Title = "Second", CreatedDate = new DateTime(2020, 1, 2) };
        db.Offers.AddRange(first, second); await db.SaveChangesAsync(); using var client = fixture.Client();
        using var response = await client.GetAsync($"/Jobs?sort={sort}&index=2&size=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var page = json.RootElement; Assert.Equal(2, page.GetProperty("totalItems").GetInt32());
        Assert.Equal(2, page.GetProperty("totalPages").GetInt32()); Assert.True(page.GetProperty("hasPreviousPage").GetBoolean());
        Assert.False(page.GetProperty("hasNextPage").GetBoolean());
        Assert.Equal(descending ? first.Id : second.Id, Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("id").GetInt32());
        using var beyond = await client.GetAsync($"/Jobs?sort={sort}&index=3&size=1"); Assert.Equal(HttpStatusCode.NotFound, beyond.StatusCode);
    }

    [Theory]
    [InlineData("JobCreatedDate_Ascending", false)]
    [InlineData("JobCreatedDate_Descending", true)]
    public async Task AnonymousNullableCreatedDateSort_PreservesSourceNullPlacementAcrossPages(string sort, bool descending)
    {
        await fixture.ResetAsync();
        var levelId = await fixture.SeedLevelAsync();
        await using var db = fixture.Context();
        var undated = new JobOffer { LevelId = levelId, Title = "Undated fixture" };
        var earliest = new JobOffer { LevelId = levelId, Title = "Earliest fixture", CreatedDate = new DateTime(2020, 1, 1) };
        var middle = new JobOffer { LevelId = levelId, Title = "Middle fixture", CreatedDate = new DateTime(2020, 1, 2) };
        var latest = new JobOffer { LevelId = levelId, Title = "Latest fixture", CreatedDate = new DateTime(2020, 1, 3) };
        db.Offers.AddRange(middle, undated, latest, earliest);
        await db.SaveChangesAsync();
        await db.Offers.Where(offer => offer.Id == undated.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(offer => offer.CreatedDate, (DateTime?)null));
        Assert.Null(await db.Offers.AsNoTracking().Where(offer => offer.Id == undated.Id)
            .Select(offer => offer.CreatedDate).SingleAsync());
        var before = await db.Offers.AsNoTracking().OrderBy(offer => offer.Id).ToArrayAsync();
        var beforeLevel = await db.Levels.AsNoTracking().SingleAsync();
        var expected = descending
            ? new[] { latest.Id, middle.Id, earliest.Id, undated.Id }
            : new[] { undated.Id, earliest.Id, middle.Id, latest.Id };
        using var client = fixture.Client();
        using var response = await client.GetAsync($"/Jobs?sort={sort}&index=1&size=4");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(expected, items.Select(item => item.GetProperty("id").GetInt32()).ToArray());
        Assert.Equal(4, json.RootElement.GetProperty("totalItems").GetInt32());
        Assert.False(items.Single(item => item.GetProperty("id").GetInt32() == undated.Id).TryGetProperty("createdDate", out _));
        for (var index = 1; index <= expected.Length; index++)
        {
            using var pageResponse = await client.GetAsync($"/Jobs?sort={sort}&index={index}&size=1");
            Assert.Equal(HttpStatusCode.OK, pageResponse.StatusCode);
            using var pageJson = JsonDocument.Parse(await pageResponse.Content.ReadAsStringAsync());
            var page = pageJson.RootElement;
            Assert.Equal(index, page.GetProperty("pageIndex").GetInt32());
            Assert.Equal(4, page.GetProperty("totalItems").GetInt32());
            Assert.Equal(4, page.GetProperty("totalPages").GetInt32());
            Assert.Equal(index > 1, page.GetProperty("hasPreviousPage").GetBoolean());
            Assert.Equal(index < 4, page.GetProperty("hasNextPage").GetBoolean());
            Assert.Equal(expected[index - 1], Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("id").GetInt32());
        }
        using var beyond = await client.GetAsync($"/Jobs?sort={sort}&index=5&size=1");
        Assert.Equal(HttpStatusCode.NotFound, beyond.StatusCode);
        var after = await db.Offers.AsNoTracking().OrderBy(offer => offer.Id).ToArrayAsync();
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(after));
        Assert.Equal(JsonSerializer.Serialize(beforeLevel), JsonSerializer.Serialize(await db.Levels.AsNoTracking().SingleAsync()));
    }

    [Fact]
    public async Task NumericSearch_PreservesSourceIdOrTextRatherThanExclusiveIdRule()
    {
        await fixture.ResetAsync(); var levelId = await fixture.SeedLevelAsync(); await using var db = fixture.Context();
        var first = new JobOffer { LevelId = levelId, Title = "First" }; db.Offers.Add(first); await db.SaveChangesAsync();
        db.Offers.Add(new JobOffer { LevelId = levelId, Title = "Text includes " + first.Id }); await db.SaveChangesAsync();
        using var client = fixture.Client(); using var response = await client.GetAsync($"/Jobs?search={first.Id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(2, json.RootElement.GetProperty("totalItems").GetInt32());
    }

    [Theory]
    [InlineData("title")]
    [InlineData("introduction")]
    [InlineData("whatWeOffer")]
    [InlineData("location")]
    [InlineData("description")]
    public async Task AnonymousSearch_AllSourceTextFieldsPreserveLiteralCharactersAndSignificantSpaces(string field)
    {
        foreach (var search in new[] { "%", "_", "\\", "ช่าง", "  Padded  " })
        {
            await fixture.ResetAsync();
            var levelId = await fixture.SeedLevelAsync();
            await using var db = fixture.Context();
            var matched = new JobOffer { LevelId = levelId };
            var unrelated = new JobOffer { LevelId = levelId };
            SetSearchField(matched, field, "Prefix" + search + "Suffix");
            SetSearchField(unrelated, field, search == "  Padded  " ? "Padded" : "Unrelated");
            db.Offers.AddRange(matched, unrelated);
            await db.SaveChangesAsync();
            using var client = fixture.Client();
            using var response = await client.GetAsync("/Jobs?search=" + Uri.EscapeDataString(search));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(1, json.RootElement.GetProperty("totalItems").GetInt32());
            var item = Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal(matched.Id, item.GetProperty("id").GetInt32());
            Assert.Equal("Prefix" + search + "Suffix", item.GetProperty(field).GetString());
            Assert.Equal(2, await db.Offers.CountAsync());
        }
    }

    [Theory]
    [InlineData("prerequisites")]
    [InlineData("level")]
    public async Task AnonymousSearch_DoesNotExpandSourceFieldsToPrerequisitesOrLinkedLevel(string field)
    {
        await fixture.ResetAsync();
        await using var db = fixture.Context();
        var level = new JobLevel { Name = field == "level" ? "OnlyExcludedMarker" : "Unrelated", Description = "Unrelated" };
        db.Levels.Add(level);
        await db.SaveChangesAsync();
        db.Offers.Add(new JobOffer { LevelId = level.Id, Title = "Unrelated", Prerequisites = field == "prerequisites" ? "OnlyExcludedMarker" : null });
        await db.SaveChangesAsync();
        using var client = fixture.Client();
        using var response = await client.GetAsync("/Jobs?search=OnlyExcludedMarker");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(1, await db.Offers.CountAsync());
        Assert.Equal(1, await db.Levels.CountAsync());
    }

    [Fact]
    public async Task Update_ChangesExistingLevelAndRefreshesPublicProjectionWithoutChangingEitherPrincipalOrSurvivor()
    {
        await fixture.ResetAsync();
        JobLevel oldLevel;
        JobLevel nextLevel;
        JobOffer selected;
        JobOffer survivor;
        await using (var db = fixture.Context())
        {
            oldLevel = Level("  ระดับเดิม  ");
            nextLevel = Level("  ระดับใหม่  ");
            db.Levels.AddRange(oldLevel, nextLevel);
            await db.SaveChangesAsync();
            selected = Offer("Selected", oldLevel.Id);
            survivor = Offer("Survivor", oldLevel.Id);
            db.Offers.AddRange(selected, survivor);
            await db.SaveChangesAsync();
        }
        Assert.NotEqual(oldLevel.Id, nextLevel.Id);
        var beforeLevels = await LevelStateAsync();
        var beforeSurvivor = await OfferStateAsync(survivor.Id);
        uint beforeVersion;
        await using (var db = fixture.Context())
        {
            beforeVersion = await db.Offers.AsNoTracking().Where(offer => offer.Id == selected.Id)
                .Select(offer => EF.Property<uint>(offer, "Version")).SingleAsync();
        }
        using var anonymous = fixture.Client();
        using (var before = await anonymous.GetAsync($"/Jobs/{selected.Id}"))
        {
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);
            using var json = JsonDocument.Parse(await before.Content.ReadAsStringAsync());
            Assert.Equal(oldLevel.Id, json.RootElement.GetProperty("levelId").GetInt32());
            Assert.Equal(oldLevel.Id, json.RootElement.GetProperty("level").GetProperty("id").GetInt32());
        }
        using var writer = fixture.Client("legacy-career.jobs.update");
        using (var updated = await writer.PutAsJsonAsync($"/Jobs/{selected.Id}", new
        {
            LevelId = nextLevel.Id,
            Title = "  วิศวกรปรับระดับ  ",
            Description = "Updated description",
            Prerequisites = "Updated prerequisites",
            IsFilled = false,
            Id = survivor.Id,
            CreatedDate = new DateTime(1980, 1, 1),
            Introduction = "Forged introduction",
            WhatWeOffer = "Forged benefits",
            Location = "Forged location",
            Level = new { Id = oldLevel.Id, Name = "Forged principal", Description = "Forged description" }
        }))
        {
            Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
            Assert.Equal(string.Empty, await updated.Content.ReadAsStringAsync());
        }
        await using (var db = fixture.Context())
        {
            var actual = await db.Offers.AsNoTracking().SingleAsync(offer => offer.Id == selected.Id);
            Assert.Equal(nextLevel.Id, actual.LevelId);
            Assert.Equal("  วิศวกรปรับระดับ  ", actual.Title);
            Assert.Equal("Updated description", actual.Description);
            Assert.Equal("Updated prerequisites", actual.Prerequisites);
            Assert.False(actual.IsFilled);
            Assert.Equal(selected.CreatedDate, actual.CreatedDate);
            Assert.True(actual.ModifiedDate > selected.ModifiedDate);
            Assert.Equal(selected.Introduction, actual.Introduction);
            Assert.Equal(selected.WhatWeOffer, actual.WhatWeOffer);
            Assert.Equal(selected.Location, actual.Location);
            var version = await db.Offers.AsNoTracking().Where(offer => offer.Id == selected.Id)
                .Select(offer => EF.Property<uint>(offer, "Version")).SingleAsync();
            Assert.NotEqual(beforeVersion, version);
            Assert.Equal(2, await db.Offers.CountAsync());
            Assert.Equal(2, await db.Levels.CountAsync());
        }
        Assert.Equal(beforeLevels, await LevelStateAsync());
        Assert.Equal(beforeSurvivor, await OfferStateAsync(survivor.Id));
        var committed = await OfferStateAsync(selected.Id);
        foreach (var route in new[] { $"/Jobs/{selected.Id}", $"/jobs/{selected.Id}/" })
        {
            using var response = await anonymous.GetAsync(route);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            AssertSelected(json.RootElement, selected.Id, nextLevel);
        }
        using (var response = await anonymous.GetAsync("/jobs/"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(2, json.RootElement.GetProperty("totalItems").GetInt32());
            var items = json.RootElement.GetProperty("items").EnumerateArray().ToArray();
            Assert.Equal(new[] { selected.Id, survivor.Id }.OrderBy(id => id).ToArray(),
                items.Select(item => item.GetProperty("id").GetInt32()).ToArray());
            AssertSelected(Assert.Single(items, item => item.GetProperty("id").GetInt32() == selected.Id), selected.Id, nextLevel);
            var surviving = Assert.Single(items, item => item.GetProperty("id").GetInt32() == survivor.Id);
            Assert.Equal(oldLevel.Id, surviving.GetProperty("levelId").GetInt32());
            Assert.Equal(oldLevel.Name, surviving.GetProperty("level").GetProperty("name").GetString());
        }
        Assert.Equal("true", await anonymous.GetStringAsync("/jobs/job-opening-status/"));
        Assert.Equal(committed, await OfferStateAsync(selected.Id));
        Assert.Equal(beforeSurvivor, await OfferStateAsync(survivor.Id));
        Assert.Equal(beforeLevels, await LevelStateAsync());
    }

    private static JobLevel Level(string name) => new()
    {
        Name = name,
        Description = "Persisted principal",
        CreatedDate = new DateTime(2020, 1, 1),
        ModifiedDate = new DateTime(2020, 1, 2)
    };

    private static JobOffer Offer(string title, int levelId) => new()
    {
        LevelId = levelId,
        Title = title,
        Description = "Original description",
        Prerequisites = "Original prerequisites",
        Introduction = "Imported introduction",
        WhatWeOffer = "Imported benefits",
        Location = "Imported location",
        IsFilled = true,
        CreatedDate = new DateTime(2020, 1, 1),
        ModifiedDate = new DateTime(2020, 1, 2)
    };

    private static void AssertSelected(JsonElement item, int id, JobLevel level)
    {
        Assert.Equal(id, item.GetProperty("id").GetInt32());
        Assert.Equal(level.Id, item.GetProperty("levelId").GetInt32());
        Assert.Equal("  วิศวกรปรับระดับ  ", item.GetProperty("title").GetString());
        Assert.Equal(level.Id, item.GetProperty("level").GetProperty("id").GetInt32());
        Assert.Equal(level.Name, item.GetProperty("level").GetProperty("name").GetString());
        Assert.False(item.GetProperty("level").TryGetProperty("offers", out _));
    }

    private async Task<string> OfferStateAsync(int id)
    {
        await using var db = fixture.Context();
        return JsonSerializer.Serialize(await db.Offers.AsNoTracking().Where(offer => offer.Id == id)
            .Select(offer => new { Row = offer, Version = EF.Property<uint>(offer, "Version") }).SingleAsync());
    }

    private async Task<string> LevelStateAsync()
    {
        await using var db = fixture.Context();
        return JsonSerializer.Serialize(await db.Levels.AsNoTracking().OrderBy(level => level.Id)
            .Select(level => new { Row = level, Version = EF.Property<uint>(level, "Version") }).ToArrayAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NullUpdateBody_IsBadRequestBeforeExistingOrMissingLookupAndPreservesPhysicalGraph(bool missing)
    {
        await fixture.ResetAsync();
        int id;
        await using (var db = fixture.Context())
        {
            var selected = new JobOffer
            {
                Level = new JobLevel { Name = "  ระดับเดิม  ", Description = "Persisted principal" },
                Title = "  วิศวกร  ",
                Description = "Persisted description",
                Prerequisites = "Persisted prerequisites",
                Introduction = "Imported introduction",
                WhatWeOffer = "Imported benefits",
                Location = "Imported location",
                IsFilled = false,
                CreatedDate = new DateTime(2020, 1, 1),
                ModifiedDate = new DateTime(2020, 1, 2)
            };
            var survivor = new JobOffer
            {
                Level = new JobLevel { Name = "Survivor level", Description = "Unrelated principal" },
                Title = "Survivor",
                IsFilled = true
            };
            db.Offers.AddRange(selected, survivor);
            await db.SaveChangesAsync();
            id = missing ? int.MaxValue : selected.Id;
            Assert.NotEqual(int.MaxValue, selected.Id);
            Assert.NotEqual(selected.Id, survivor.Id);
            Assert.NotEqual(selected.LevelId, survivor.LevelId);
            Assert.Equal(2, await db.Offers.CountAsync());
            Assert.Equal(2, await db.Levels.CountAsync());
        }
        var before = await PhysicalStateAsync();
        using var writer = fixture.Client("legacy-career.jobs.update");
        foreach (var route in new[] { $"/Jobs/{id}", $"/jobs/{id}/" })
        {
            using var response = await writer.PutAsync(route, new StringContent("null", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(before, await PhysicalStateAsync());
        }
        using var anonymous = fixture.Client();
        Assert.Equal("true", await anonymous.GetStringAsync("/jobs/job-opening-status/"));
        Assert.Equal(before, await PhysicalStateAsync());
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LevelNullUpdateBody_PreservesAutomaticValidationAndPhysicalOfferLevelGraph(bool missing)
    {
        await fixture.ResetAsync();
        int levelId;
        await using (var db = fixture.Context())
        {
            var selected = new JobLevel { Name = "Synthetic selected", Description = "Retained selected" };
            var survivor = new JobLevel { Name = "Synthetic survivor", Description = "Retained survivor" };
            db.Levels.AddRange(selected, survivor);
            db.Offers.Add(new JobOffer { Level = selected, Title = "Synthetic linked offer", IsFilled = false });
            await db.SaveChangesAsync();
            levelId = missing ? int.MaxValue : selected.Id;
            Assert.NotEqual(int.MaxValue, selected.Id);
        }

        var before = await PhysicalStateAsync();
        using var writer = fixture.Client("legacy-career.levels.update");
        foreach (var route in new[] { $"/jobs/Levels/{levelId}", $"/jobs/levels/{levelId}/" })
        {
            using var response = await writer.PutAsync(route, new StringContent("null", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(400, json.RootElement.GetProperty("status").GetInt32());
            Assert.Equal(JsonValueKind.Object, json.RootElement.GetProperty("errors").ValueKind);
            Assert.Equal(before, await PhysicalStateAsync());
        }
    }

    [Theory]
    [InlineData("JobCreatedDate_Ascending", false, 2000, 3000)]
    [InlineData("JobCreatedDate_Descending", true, 3000, 2000)]
    public async Task OriginalThousandAndOneDateCorpus_PreservesCompleteYearOrderAndPhysicalGraph(
        string sort, bool descending, int firstYear, int lastYear)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var cancellation = lifetime.Token;
        await fixture.ResetAsync();
        await using (var db = fixture.Context())
        {
            db.Offers.AddRange(Enumerable.Range(0, 1001).Select(index => new JobOffer
            {
                CreatedDate = new DateTime(2000 + index, 1, 1),
                Level = new JobLevel
                {
                    Name = "test name",
                    Description = "test description",
                },
            }));
            await db.SaveChangesAsync(cancellation);
            Assert.Equal(1001, await db.Offers.CountAsync(cancellation));
            Assert.Equal(1001, await db.Levels.CountAsync(cancellation));
        }

        var before = await OriginalSortCorpusPhysicalSnapshotAsync(cancellation);
        var expectedYears = Enumerable.Range(2000, 1001).ToArray();
        if (descending)
        {
            Array.Reverse(expectedYears);
        }

        using var client = fixture.Client();
        Assert.Null(client.DefaultRequestHeaders.Authorization);
        foreach (var route in new[] { "/Jobs", "/jobs/" })
        {
            using var response = await client.GetAsync($"{route}?sort={sort}", cancellation);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
            var page = json.RootElement;
            Assert.Equal(1001, page.GetProperty("totalItems").GetInt32());
            Assert.Equal(1, page.GetProperty("pageIndex").GetInt32());
            Assert.Equal(1, page.GetProperty("totalPages").GetInt32());
            Assert.False(page.GetProperty("hasPreviousPage").GetBoolean());
            Assert.False(page.GetProperty("hasNextPage").GetBoolean());
            var items = page.GetProperty("items").EnumerateArray().ToArray();
            Assert.Equal(1001, items.Length);
            Assert.Equal(1001, items.Select(item => item.GetProperty("id").GetInt32()).Distinct().Count());
            var years = items.Select(item => item.GetProperty("createdDate").GetDateTime().Year).ToArray();
            Assert.Equal(firstYear, years[0]);
            Assert.Equal(lastYear, years[^1]);
            Assert.Equal(expectedYears, years);
        }

        Assert.Equal(before, await OriginalSortCorpusPhysicalSnapshotAsync(cancellation));
    }

    [Theory]
    [InlineData("JobId_Ascending", false, 1, 1000)]
    [InlineData("JobId_Descending", true, 1000, 1)]
    public async Task OriginalThousandExplicitIdCorpus_PreservesCompleteIdOrderAndPhysicalGraph(
        string sort, bool descending, int firstId, int lastId)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var cancellation = lifetime.Token;
        await fixture.ResetAsync();
        await using (var db = fixture.Context())
        {
            db.Offers.AddRange(Enumerable.Range(1, 1000).Select(id => new JobOffer
            {
                Id = id,
                Level = new JobLevel
                {
                    Name = "test name",
                    Description = "test description",
                },
            }));
            await db.SaveChangesAsync(cancellation);
            Assert.Equal(1000, await db.Offers.CountAsync(cancellation));
            Assert.Equal(1000, await db.Levels.CountAsync(cancellation));
        }

        var before = await OriginalSortCorpusPhysicalSnapshotAsync(cancellation);
        var expectedIds = Enumerable.Range(1, 1000).ToArray();
        if (descending)
        {
            Array.Reverse(expectedIds);
        }

        using var client = fixture.Client();
        Assert.Null(client.DefaultRequestHeaders.Authorization);
        foreach (var route in new[] { "/Jobs", "/jobs/" })
        {
            using var response = await client.GetAsync($"{route}?sort={sort}", cancellation);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
            var page = json.RootElement;
            Assert.Equal(1000, page.GetProperty("totalItems").GetInt32());
            Assert.Equal(1, page.GetProperty("pageIndex").GetInt32());
            Assert.Equal(1, page.GetProperty("totalPages").GetInt32());
            Assert.False(page.GetProperty("hasPreviousPage").GetBoolean());
            Assert.False(page.GetProperty("hasNextPage").GetBoolean());
            var ids = page.GetProperty("items").EnumerateArray()
                .Select(item => item.GetProperty("id").GetInt32()).ToArray();
            Assert.Equal(1000, ids.Length);
            Assert.Equal(firstId, ids[0]);
            Assert.Equal(lastId, ids[^1]);
            Assert.Equal(expectedIds, ids);
        }

        Assert.Equal(before, await OriginalSortCorpusPhysicalSnapshotAsync(cancellation));
    }

    private async Task<string> OriginalSortCorpusPhysicalSnapshotAsync(CancellationToken cancellation)
    {
        await using var db = fixture.Context();
        return JsonSerializer.Serialize(new
        {
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
            Levels = await db.Levels.AsNoTracking().OrderBy(row => row.Id).Select(row => new
            {
                row.Id,
                row.Name,
                row.Description,
                row.CreatedDate,
                row.ModifiedDate,
                Version = EF.Property<uint>(row, "Version"),
            }).ToArrayAsync(cancellation),
        });
    }

    private static void SetSearchField(JobOffer offer, string field, string value)
    {
        switch (field)
        {
            case "title": offer.Title = value; break;
            case "introduction": offer.Introduction = value; break;
            case "whatWeOffer": offer.WhatWeOffer = value; break;
            case "location": offer.Location = value; break;
            case "description": offer.Description = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(field));
        }
    }

    [Theory]
    [InlineData("Jobs", "legacy-career.jobs.create")]
    [InlineData("jobs/levels", "legacy-career.levels.create")]
    public async Task NullCreateBody_Is400WithoutPersistence(string route, string permission)
    {
        await fixture.ResetAsync(); using var client = fixture.Client(permission);
        using var response = await client.PostAsync("/" + route, new StringContent("null", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); await using var db = fixture.Context();
        Assert.False(await db.Offers.AnyAsync()); Assert.False(await db.Levels.AnyAsync());
    }

    [Fact]
    public async Task InvalidOfferForeignKey_ActualPostgresFailureDoesNotPersistRootOrExposeProvider()
    {
        await fixture.ResetAsync(); using var client = fixture.Client("legacy-career.jobs.create");
        using var response = await client.PostAsJsonAsync("/Jobs", Payload(int.MaxValue));
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("Npgsql", await response.Content.ReadAsStringAsync());
        await using var db = fixture.Context(); Assert.False(await db.Offers.AnyAsync()); Assert.False(await db.Levels.AnyAsync());
    }
}

public sealed class CareerRouteFixture : IAsyncLifetime
{
    private const string Issuer = "https://career-component.example.invalid";
    private const string Audience = "career-component";
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private readonly IContainer redis = new ContainerBuilder("redis:7.4-alpine").WithPortBinding(6379, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(6379)).Build();
    private readonly RSA key = RSA.Create(2048);
    private readonly RSA wrongKey = RSA.Create(2048);
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await postgres.StartAsync(); await redis.StartAsync();
        await using var db = Context(); await db.Database.MigrateAsync();
        Factory = new CareerFactory(this);
    }

    public CareerDbContext Context() => new(new DbContextOptionsBuilder<CareerDbContext>().UseNpgsql(postgres.GetConnectionString()).Options);
    public async Task ResetAsync()
    {
        await using var db = Context();
        await db.Offers.ExecuteDeleteAsync(); await db.Levels.ExecuteDeleteAsync();
    }
    public async Task<int> SeedLevelAsync()
    {
        await using var db = Context(); var level = new JobLevel { Name = "Fixture level", Description = "Fixture description" };
        db.Levels.Add(level); await db.SaveChangesAsync(); return level.Id;
    }
    public HttpClient Client(string? permission = null, string authority = "valid")
    {
        var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (permission is null || authority == "anonymous") return client;
        var claims = new List<Claim> { new(JwtRegisteredClaimNames.Sub, "service:career-fixture-" + Guid.NewGuid().ToString("N")) };
        if (authority != "missing-permission") claims.Add(new("permissions", permission));
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(Issuer, Audience, claims, now.AddHours(-1),
            authority == "expired" ? now.AddMinutes(-10) : now.AddMinutes(5),
            new SigningCredentials(new RsaSecurityKey(authority == "wrong-signature" ? wrongKey : key), SecurityAlgorithms.RsaSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        return client;
    }
    public async Task DisposeAsync()
    {
        if (Factory is not null) await Factory.DisposeAsync();
        key.Dispose(); wrongKey.Dispose(); await redis.DisposeAsync(); await postgres.DisposeAsync();
    }
    private sealed class CareerFactory(CareerRouteFixture fixture) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Production");
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CareerDbContext"] = fixture.postgres.GetConnectionString(),
                ["ConnectionStrings:redis"] = $"{fixture.redis.Hostname}:{fixture.redis.GetMappedPublicPort(6379)}",
                ["Cache:RedisEnabled"] = "true",
                ["Jwt:Issuer"] = Issuer,
                ["Jwt:Audience"] = Audience,
                ["Jwt:PublicKey"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(fixture.key.ExportSubjectPublicKeyInfoPem())),
                ["Logging:LogLevel:Default"] = "Warning",
            }));
            return base.CreateHost(builder);
        }
    }
}
