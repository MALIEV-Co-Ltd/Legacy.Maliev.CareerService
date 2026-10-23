using System.Net;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.CareerService.Domain;
using Legacy.Maliev.CareerService.Tests.Controllers;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.CareerService.Tests.Serialization;

/// <summary>Checks source offer-field projection through the normal HTTP input pipeline.</summary>
/// <param name="fixture">The shared route fixture providing Career database reset, entity contexts, and permission-bearing HTTP clients for offer input tests.</param>
[Collection("Career shared level")]
public sealed class CareerOfferInputHttpTests(CareerRouteFixture fixture) : IClassFixture<CareerRouteFixture>
{
    /// <summary>Checks that only consumed offer fields are persisted.</summary>
    /// <param name="update">Whether to update the selected seeded offer with PUT instead of creating an offer with POST.</param>
    /// <param name="fields">The JSON object containing consumed offer fields and repeated scalar or null occurrences, extended with the level identifier and ignored fields before submission.</param>
    /// <param name="title">The expected source-compatible title text in the persisted offer and HTTP responses.</param>
    /// <param name="description">The expected source-compatible description text in the persisted offer and HTTP responses.</param>
    /// <param name="prerequisites">The expected source-compatible prerequisites text in the persisted offer and HTTP responses.</param>
    /// <returns>A task that completes after asserting consumed offer projections, timestamp behavior, refreshed public reads, and preservation of ignored fields and unaffected graph rows.</returns>
    [Theory]
    [InlineData(false, """{"Title":1.2300e+02,"Description":true,"Prerequisites":"P","Prerequisites":null}""", "1.2300e+02", "true", "P")]
    [InlineData(true, """{"Title":1.2300e+02,"Description":true,"Prerequisites":"P","Prerequisites":null}""", "1.2300e+02", "true", "P")]
    [InlineData(false, """{"Title":"T","Title":null,"Description":-0,"Description":null,"Prerequisites":false,"Prerequisites":null}""", "T", "-0", "false")]
    [InlineData(true, """{"Title":"T","Title":null,"Description":-0,"Description":null,"Prerequisites":false,"Prerequisites":null}""", "T", "-0", "false")]
    public async Task NormalHttp_PersistsOnlyConsumedProjection(
        bool update, string fields, string title, string description, string prerequisites)
    {
        var (levelId, selectedId, siblingId) = await Seed();
        var levelsBefore = await Levels();
        await using var beforeContext = fixture.Context();
        var original = await beforeContext.Offers.AsNoTracking().SingleAsync(row => row.Id == selectedId);
        var sibling = await OfferSnapshot(siblingId);
        var selectedBefore = await OfferSnapshot(selectedId);
        using var reader = fixture.Client();
        using var prime = await reader.GetAsync("/Jobs?index=1&size=10");
        Assert.Equal(HttpStatusCode.OK, prime.StatusCode);
        var started = DateTime.UtcNow.AddSeconds(-5);
        var body = fields[..^1] + ",\"LevelId\":" + levelId +
            ",\"IsFilled\":true,\"Introduction\":\"ignored introduction\",\"WhatWeOffer\":\"ignored offer\",\"Location\":\"ignored location\"}";
        using var writer = fixture.Client(update ? "legacy-career.jobs.update" : "legacy-career.jobs.create");
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = update
            ? await writer.PutAsync($"/Jobs/{selectedId}", content)
            : await writer.PostAsync("/Jobs", content);
        Assert.Equal(update ? HttpStatusCode.NoContent : HttpStatusCode.Created, response.StatusCode);
        int id;
        if (update)
        {
            id = selectedId;
            Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
        }
        else
        {
            using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            id = created.RootElement.GetProperty("id").GetInt32();
            Assert.True(id > 0);
            Assert.NotEqual(selectedId, id);
            Assert.EndsWith("/Jobs/" + id, response.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);
            AssertFields(created.RootElement, title, description, prerequisites);
        }
        await using var db = fixture.Context();
        var persisted = await db.Offers.AsNoTracking().SingleAsync(row => row.Id == id);
        Assert.Equal(levelId, persisted.LevelId);
        Assert.True(persisted.IsFilled);
        Assert.Equal(title, persisted.Title);
        Assert.Equal(description, persisted.Description);
        Assert.Equal(prerequisites, persisted.Prerequisites);
        Assert.Equal(update ? original.Introduction : null, persisted.Introduction);
        Assert.Equal(update ? original.WhatWeOffer : null, persisted.WhatWeOffer);
        Assert.Equal(update ? original.Location : null, persisted.Location);
        Assert.NotNull(persisted.ModifiedDate);
        Assert.InRange(persisted.ModifiedDate.Value, started, DateTime.UtcNow.AddMinutes(1));
        if (update) Assert.Equal(original.CreatedDate, persisted.CreatedDate);
        else
        {
            Assert.NotNull(persisted.CreatedDate);
            Assert.InRange(persisted.CreatedDate.Value, started, DateTime.UtcNow.AddMinutes(1));
            Assert.Equal(selectedBefore, await OfferSnapshot(selectedId));
        }
        Assert.Equal(update ? 2 : 3, await db.Offers.CountAsync());
        Assert.Equal(sibling, await OfferSnapshot(siblingId));
        Assert.Equal(levelsBefore, await Levels());
        using var detail = await reader.GetAsync($"/Jobs/{id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using var detailJson = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        AssertFields(detailJson.RootElement, title, description, prerequisites);
        using var listJson = JsonDocument.Parse(await reader.GetStringAsync("/Jobs?index=1&size=10"));
        var listed = listJson.RootElement.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("id").GetInt32() == id);
        AssertFields(listed, title, description, prerequisites);
    }

    /// <summary>Checks that an earlier invalid consumed container prevents every graph write.</summary>
    /// <param name="update">Whether to exercise the offer update endpoint with PUT instead of the creation endpoint with POST.</param>
    /// <param name="field">The consumed offer field, Title, Description, or Prerequisites, given an earlier object value followed by valid text.</param>
    /// <returns>A task that completes after asserting a sanitized bad-request response and an unchanged offer-and-level database graph.</returns>
    [Theory]
    [InlineData(false, "Title")]
    [InlineData(true, "Title")]
    [InlineData(false, "Description")]
    [InlineData(true, "Description")]
    [InlineData(false, "Prerequisites")]
    [InlineData(true, "Prerequisites")]
    public async Task EarlierConsumedContainer_RejectsBeforeAnyGraphWrite(bool update, string field)
    {
        var (levelId, selectedId, siblingId) = await Seed();
        var before = await Graph();
        var body = "{\"" + field + "\":{\"private-input-marker\":1},\"" + field +
            "\":\"valid\",\"LevelId\":" + levelId + "}";
        using var writer = fixture.Client(update ? "legacy-career.jobs.update" : "legacy-career.jobs.create");
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = update
            ? await writer.PutAsync($"/Jobs/{selectedId}", content)
            : await writer.PostAsync("/Jobs", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("private-input-marker", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(before, await Graph());
    }

    private async Task<(int LevelId, int SelectedId, int SiblingId)> Seed()
    {
        await fixture.ResetAsync();
        await using var db = fixture.Context();
        var level = new JobLevel { Name = "Existing level", Description = "Existing description" };
        db.Levels.Add(level);
        await db.SaveChangesAsync();
        var offer = new JobOffer
        {
            LevelId = level.Id,
            Title = "Original",
            Description = "Original description",
            Prerequisites = "Original prerequisites",
            Introduction = "Imported introduction",
            WhatWeOffer = "Imported offer",
            Location = "Imported location",
            IsFilled = false,
            CreatedDate = new DateTime(2020, 1, 1),
            ModifiedDate = new DateTime(2020, 1, 2)
        };
        db.Offers.Add(offer);
        await db.SaveChangesAsync();
        var sibling = new JobOffer { LevelId = level.Id, Title = "Sibling", IsFilled = false };
        db.Offers.Add(sibling);
        await db.SaveChangesAsync();
        return (level.Id, offer.Id, sibling.Id);
    }

    private async Task<string> OfferSnapshot(int id)
    {
        await using var db = fixture.Context();
        return JsonSerializer.Serialize(await db.Offers.AsNoTracking().Where(row => row.Id == id)
            .Select(row => new
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
                row.ModifiedDate
            }).SingleAsync());
    }

    private async Task<string> Levels()
    {
        await using var db = fixture.Context();
        return JsonSerializer.Serialize(await db.Levels.AsNoTracking().OrderBy(row => row.Id)
            .Select(row => new { row.Id, row.Name, row.Description, row.CreatedDate, row.ModifiedDate }).ToArrayAsync());
    }

    private async Task<string> Graph()
    {
        await using var db = fixture.Context();
        var ids = await db.Offers.AsNoTracking().OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync();
        var rows = new List<string>();
        foreach (var id in ids) rows.Add(await OfferSnapshot(id));
        return JsonSerializer.Serialize(new { Offers = rows, Levels = await Levels() });
    }

    private static void AssertFields(JsonElement value, string title, string description, string prerequisites)
    {
        Assert.Equal(title, value.GetProperty("title").GetString());
        Assert.Equal(description, value.GetProperty("description").GetString());
        Assert.Equal(prerequisites, value.GetProperty("prerequisites").GetString());
    }
}
