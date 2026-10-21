using System.Net;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.CareerService.Domain;
using Legacy.Maliev.CareerService.Tests.Controllers;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.CareerService.Tests.Serialization;

/// <summary>Checks original recognized scalar validation through normal offer and level HTTP writes.</summary>
/// <param name="fixture">The shared Career route fixture providing the application factory, request clients, and test database access.</param>
[Collection("Career shared level")]
public sealed class CareerScalarBodyInputHttpTests(CareerRouteFixture fixture) : IClassFixture<CareerRouteFixture>
{
    /// <summary>Provides accepted offer scalar representations and expected nullable filled states.</summary>
    /// <returns>Theory rows combining create/update mode, raw IsFilled member text including duplicate nulls, and the expected nullable Boolean projection.</returns>
    public static IEnumerable<object[]> ValidOfferScalars()
    {
        foreach (var update in new[] { false, true })
        {
            yield return [update, "42", true];
            yield return [update, "0", false];
            yield return [update, "\" true \"", true];
            yield return [update, "\"FALSE\"", false];
            yield return [update, "\"\"", null!];
            yield return [update, "null", null!];
            yield return [update, "true,\"IsFilled\":null", true];
            yield return [update, "false,\"IsFilled\":null", false];
        }
    }

    /// <summary>Checks persisted scalar values without writing ignored fields.</summary>
    /// <param name="update">Whether to PUT the seeded offer rather than POST a new offer.</param>
    /// <param name="token">The raw IsFilled JSON value, optionally followed by a duplicate IsFilled null member.</param>
    /// <param name="filled">The nullable Boolean expected after source-compatible scalar conversion and persistence.</param>
    /// <returns>A task that completes when offer scalar persistence, ignored-field preservation, response status, and public readback assertions finish.</returns>
    [Theory]
    [MemberData(nameof(ValidOfferScalars))]
    public async Task OfferScalars_PersistSourceValuesWithoutWritingIgnoredFields(bool update, string token, bool? filled)
    {
        var (levelId, selectedId, siblingId) = await Seed();
        var levelsBefore = await Levels();
        var selectedBefore = await Offer(selectedId);
        var siblingBefore = await Offer(siblingId);
        await using var beforeContext = fixture.Context();
        var original = await beforeContext.Offers.AsNoTracking().SingleAsync(row => row.Id == selectedId);
        using var reader = fixture.Client();
        using var primeDetail = await reader.GetAsync($"/Jobs/{selectedId}");
        using var primeList = await reader.GetAsync("/Jobs?index=1&size=10");
        Assert.Equal(HttpStatusCode.OK, primeDetail.StatusCode);
        Assert.Equal(HttpStatusCode.OK, primeList.StatusCode);
        Assert.Equal("false", await reader.GetStringAsync("/Jobs/job-opening-status"));
        var body = "{\"LevelId\":\" +" + levelId + " \",\"LevelId\":null,\"Title\":\"T\",\"Description\":\"D\",\"Prerequisites\":\"P\",\"IsFilled\":" + token +
            ",\"Introduction\":42,\"WhatWeOffer\":true,\"Location\":\"" + new string('L', 101) + "\"," + IgnoredValidFields + "}";
        var started = DateTime.UtcNow.AddSeconds(-5);
        using var writer = fixture.Client(update ? "legacy-career.jobs.update" : "legacy-career.jobs.create");
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = update ? await writer.PutAsync($"/Jobs/{selectedId}", content) : await writer.PostAsync("/Jobs", content);
        Assert.Equal(update ? HttpStatusCode.NoContent : HttpStatusCode.Created, response.StatusCode);
        var id = selectedId;
        if (update) Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
        else
        {
            using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            id = created.RootElement.GetProperty("id").GetInt32();
            Assert.True(id > 0);
            Assert.NotEqual(selectedId, id);
            Assert.NotEqual(siblingId, id);
            Assert.NotEqual(ForgedId, id);
            Assert.EndsWith("/Jobs/" + id, response.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);
            AssertOfferFields(created.RootElement, levelId, filled);
        }
        await using var db = fixture.Context();
        var row = await db.Offers.AsNoTracking().SingleAsync(item => item.Id == id);
        Assert.Equal(levelId, row.LevelId);
        Assert.Equal(filled, row.IsFilled);
        Assert.Equal("T", row.Title);
        Assert.Equal("D", row.Description);
        Assert.Equal("P", row.Prerequisites);
        Assert.Equal(update ? original.Introduction : null, row.Introduction);
        Assert.Equal(update ? original.WhatWeOffer : null, row.WhatWeOffer);
        Assert.Equal(update ? original.Location : null, row.Location);
        Assert.NotNull(row.ModifiedDate);
        Assert.InRange(row.ModifiedDate.Value, started, DateTime.UtcNow.AddMinutes(1));
        if (update) Assert.Equal(original.CreatedDate, row.CreatedDate);
        else
        {
            Assert.NotNull(row.CreatedDate);
            Assert.InRange(row.CreatedDate.Value, started, DateTime.UtcNow.AddMinutes(1));
            Assert.Equal(selectedBefore, await Offer(selectedId));
        }
        Assert.Equal(siblingBefore, await Offer(siblingId));
        Assert.Equal(levelsBefore, await Levels());
        Assert.Equal(update ? 2 : 3, await db.Offers.CountAsync());
        using var detail = await reader.GetAsync($"/Jobs/{id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using var detailJson = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        AssertOfferFields(detailJson.RootElement, levelId, filled);
        using var list = JsonDocument.Parse(await reader.GetStringAsync("/Jobs?index=1&size=10"));
        AssertOfferFields(list.RootElement.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("id").GetInt32() == id), levelId, filled);
        Assert.Equal(filled == false ? "true" : "false", await reader.GetStringAsync("/Jobs/job-opening-status"));
    }

    /// <summary>Checks recognized level scalars without replacing identity or server timestamps.</summary>
    /// <param name="update">Whether to PUT the existing level rather than POST a new level.</param>
    /// <returns>A task that completes when level writes retain server-controlled identity and timestamps while ignoring recognized scalar input fields.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LevelKnownIgnoredScalars_ValidateButNeverReplaceIdentityOrServerDates(bool update)
    {
        var (levelId, selectedId, siblingId) = await Seed();
        var offersBefore = await Offers();
        await using var beforeContext = fixture.Context();
        var before = await beforeContext.Levels.AsNoTracking().SingleAsync();
        var started = DateTime.UtcNow.AddSeconds(-5);
        using var writer = fixture.Client(update ? "legacy-career.levels.update" : "legacy-career.levels.create");
        using var content = new StringContent("{\"Name\":\"N\",\"Description\":\"D\"," + IgnoredValidFields + "}", Encoding.UTF8, "application/json");
        using var response = update ? await writer.PutAsync($"/jobs/levels/{levelId}", content) : await writer.PostAsync("/jobs/levels", content);
        Assert.Equal(update ? HttpStatusCode.NoContent : HttpStatusCode.Created, response.StatusCode);
        var id = levelId;
        if (update) Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
        else
        {
            using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            id = created.RootElement.GetProperty("id").GetInt32();
            Assert.True(id > 0);
            Assert.NotEqual(levelId, id);
            Assert.NotEqual(ForgedId, id);
            Assert.EndsWith("/levels/" + id, response.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        await using var db = fixture.Context();
        var row = await db.Levels.AsNoTracking().SingleAsync(item => item.Id == id);
        Assert.Equal("N", row.Name);
        Assert.Equal("D", row.Description);
        Assert.NotNull(row.ModifiedDate);
        Assert.InRange(row.ModifiedDate.Value, started, DateTime.UtcNow.AddMinutes(1));
        if (update) Assert.Equal(before.CreatedDate, row.CreatedDate);
        else
        {
            Assert.NotNull(row.CreatedDate);
            Assert.InRange(row.CreatedDate.Value, started, DateTime.UtcNow.AddMinutes(1));
            Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(await db.Levels.AsNoTracking().SingleAsync(item => item.Id == levelId)));
        }
        Assert.Equal(update ? 1 : 2, await db.Levels.CountAsync());
        Assert.Equal(offersBefore, await Offers());
        using var reader = fixture.Client();
        using var detail = JsonDocument.Parse(await reader.GetStringAsync($"/jobs/levels/{id}"));
        Assert.Equal("N", detail.RootElement.GetProperty("name").GetString());
        Assert.Equal("D", detail.RootElement.GetProperty("description").GetString());
    }

    /// <summary>Provides invalid recognized scalar field representations.</summary>
    /// <returns>Theory rows containing offer or level routes, create/update/missing mode, a recognized scalar property, and an invalid raw JSON value.</returns>
    public static IEnumerable<object[]> InvalidRecognizedFields()
    {
        foreach (var kind in new[] { "offer", "level" })
            foreach (var mode in new[] { "create", "update", "missing" })
            {
                yield return [kind, mode, "Id", "\"private-input-marker\""];
                yield return [kind, mode, "CreatedDate", "\"private-input-marker\""];
                yield return [kind, mode, "ModifiedDate", "{\"private-input-marker\":1}"];
                if (kind != "offer") continue;
                yield return [kind, mode, "LevelId", "{\"private-input-marker\":1}"];
                yield return [kind, mode, "IsFilled", "\"private-input-marker\""];
                foreach (var name in new[] { "Introduction", "WhatWeOffer", "Location" })
                    yield return [kind, mode, name, "{\"private-input-marker\":1}"];
            }
    }

    /// <summary>Checks that earlier invalid scalars fail before lookup or writes.</summary>
    /// <param name="kind">The resource receiving the invalid scalar: offer at /Jobs or level at /jobs/levels.</param>
    /// <param name="mode">The operation scenario: POST create, PUT the seeded record, or PUT a missing identifier.</param>
    /// <param name="field">The recognized scalar property whose first invalid occurrence precedes a later valid replacement.</param>
    /// <param name="token">The invalid raw JSON value inserted for that property, including malformed identity, date, Boolean, or text representations.</param>
    /// <returns>A task that completes when the earlier invalid scalar returns 400 before lookup or writes, without leaking input values.</returns>
    [Theory]
    [MemberData(nameof(InvalidRecognizedFields))]
    public async Task EarlierInvalidKnownScalar_RejectsBeforeExistingOrMissingLookupAndWrites(string kind, string mode, string field, string token)
    {
        var (levelId, selectedId, siblingId) = await Seed();
        var before = await Graph();
        var replacement = field switch
        {
            "Id" => "123",
            "LevelId" => levelId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "IsFilled" => "false",
            "CreatedDate" or "ModifiedDate" => "\"1980-01-01T00:00:00\"",
            _ => "\"valid\"",
        };
        var tail = kind == "offer"
            ? ",\"Title\":\"T\",\"Description\":\"D\",\"Prerequisites\":\"P\"" + (field == "LevelId" ? "" : ",\"LevelId\":" + levelId)
            : ",\"Name\":\"N\",\"Description\":\"D\"";
        var body = "{\"" + field + "\":" + token + ",\"" + field + "\":" + replacement + tail + "}";
        var route = kind == "offer" ? "/Jobs" : "/jobs/levels";
        var id = mode == "missing" ? int.MaxValue : kind == "offer" ? selectedId : levelId;
        using var writer = fixture.Client("legacy-career." + (kind == "offer" ? "jobs" : "levels") + (mode == "create" ? ".create" : ".update"));
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = mode == "create" ? await writer.PostAsync(route, content) : await writer.PutAsync(route + "/" + id, content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("private-input-marker", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(before, await Graph());
    }

    /// <summary>Checks that omitted zero level identifiers retain foreign-key failure or missing-record 404.</summary>
    /// <param name="mode">The offer write scenario: create, update the seeded offer, or update a missing offer.</param>
    /// <param name="fields">The optional appended LevelId JSON member, omitted entirely or represented by null or an empty string, to retain the default zero identifier.</param>
    /// <returns>A task that completes when default-zero level input produces the expected foreign-key 500 or missing-offer 404 without changing stored data.</returns>
    [Theory]
    [InlineData("create", "")]
    [InlineData("create", ",\"LevelId\":null")]
    [InlineData("create", ",\"LevelId\":\"\"")]
    [InlineData("update", "")]
    [InlineData("update", ",\"LevelId\":null")]
    [InlineData("update", ",\"LevelId\":\"\"")]
    [InlineData("missing", "")]
    [InlineData("missing", ",\"LevelId\":null")]
    [InlineData("missing", ",\"LevelId\":\"\"")]
    public async Task DefaultZeroLevelId_RetainsForeignKeyFailureOrMissing404(string mode, string fields)
    {
        var (levelId, selectedId, siblingId) = await Seed();
        var before = await Graph();
        using var writer = fixture.Client(mode == "create" ? "legacy-career.jobs.create" : "legacy-career.jobs.update");
        using var content = new StringContent("{\"Title\":\"T\"" + fields + "}", Encoding.UTF8, "application/json");
        using var response = mode == "create" ? await writer.PostAsync("/Jobs", content)
            : await writer.PutAsync("/Jobs/" + (mode == "missing" ? int.MaxValue : selectedId), content);
        Assert.Equal(mode == "missing" ? HttpStatusCode.NotFound : HttpStatusCode.InternalServerError, response.StatusCode);
        if (mode != "missing")
        {
            var body = await response.Content.ReadAsStringAsync();
            using var json = JsonDocument.Parse(body);
            Assert.Equal(500, json.RootElement.GetProperty("statusCode").GetInt32());
            Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("details").ValueKind);
            Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("traceId").GetString()));
            Assert.DoesNotContain("Npgsql", body, StringComparison.Ordinal);
            Assert.DoesNotContain("23503", body, StringComparison.Ordinal);
        }
        Assert.Equal(before, await Graph());
    }

    private const int ForgedId = 2147483000;
    private const string IgnoredValidFields = "\"Id\":\" +2147483000 \",\"Id\":null,\"CreatedDate\":\"1980-01-01T00:00:00\",\"CreatedDate\":null,\"ModifiedDate\":\"1980-01-01T00:00:00\",\"ModifiedDate\":null";

    private async Task<(int LevelId, int SelectedId, int SiblingId)> Seed()
    {
        await fixture.ResetAsync();
        await using var db = fixture.Context();
        var level = new JobLevel { Name = "Original level", Description = "Original description", CreatedDate = new DateTime(2020, 1, 1), ModifiedDate = new DateTime(2020, 1, 2) };
        db.Levels.Add(level);
        await db.SaveChangesAsync();
        var selected = new JobOffer
        {
            LevelId = level.Id,
            Title = "Original",
            Description = "Original description",
            Prerequisites = "Original prerequisites",
            Introduction = "Imported introduction",
            WhatWeOffer = "Imported benefits",
            Location = "Imported location",
            IsFilled = true,
            CreatedDate = new DateTime(2020, 1, 1),
            ModifiedDate = new DateTime(2020, 1, 2)
        };
        var sibling = new JobOffer { LevelId = level.Id, Title = "Sibling", IsFilled = true };
        db.Offers.AddRange(selected, sibling);
        await db.SaveChangesAsync();
        Assert.NotEqual(int.MaxValue, selected.Id);
        Assert.NotEqual(int.MaxValue, sibling.Id);
        Assert.NotEqual(int.MaxValue, level.Id);
        return (level.Id, selected.Id, sibling.Id);
    }

    private async Task<string> Offer(int id)
    {
        await using var db = fixture.Context();
        return JsonSerializer.Serialize(await db.Offers.AsNoTracking().Where(row => row.Id == id).Select(row => new
        {
            row.Id,
            row.LevelId,
            row.Title,
            row.Description,
            row.Prerequisites,
            row.Introduction,
            row.WhatWeOffer,
            row.Location,
            row.IsFilled,
            row.CreatedDate,
            row.ModifiedDate,
            Version = EF.Property<uint>(row, "Version")
        }).SingleAsync());
    }

    private async Task<string> Offers()
    {
        await using var db = fixture.Context();
        var ids = await db.Offers.AsNoTracking().OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync();
        var rows = new List<string>();
        foreach (var id in ids) rows.Add(await Offer(id));
        return JsonSerializer.Serialize(rows);
    }

    private async Task<string> Levels()
    {
        await using var db = fixture.Context();
        return JsonSerializer.Serialize(await db.Levels.AsNoTracking().OrderBy(row => row.Id).Select(row => new
        {
            row.Id,
            row.Name,
            row.Description,
            row.CreatedDate,
            row.ModifiedDate,
            Version = EF.Property<uint>(row, "Version")
        }).ToArrayAsync());
    }

    private async Task<string> Graph() => JsonSerializer.Serialize(new { Offers = await Offers(), Levels = await Levels() });

    private static void AssertOfferFields(JsonElement value, int levelId, bool? filled)
    {
        Assert.Equal(levelId, value.GetProperty("levelId").GetInt32());
        Assert.Equal("T", value.GetProperty("title").GetString());
        Assert.Equal("D", value.GetProperty("description").GetString());
        Assert.Equal("P", value.GetProperty("prerequisites").GetString());
        if (filled is null) Assert.False(value.TryGetProperty("isFilled", out _));
        else Assert.Equal(filled.Value, value.GetProperty("isFilled").GetBoolean());
    }
}
