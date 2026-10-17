using System.Net;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.CareerService.Domain;
using Legacy.Maliev.CareerService.Tests.Controllers;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.CareerService.Tests.Serialization;

[Collection("Career shared level")]
public sealed class CareerLevelInputHttpTests(CareerRouteFixture fixture) : IClassFixture<CareerRouteFixture>
{
    [Theory]
    [InlineData(false, """{"Name":"N","Name":null,"Description":true}""", "N", "true")]
    [InlineData(true, """{"Name":"N","Name":null,"Description":true}""", "N", "true")]
    [InlineData(false, """{"Name":1.2300e+02,"Description":"D","Description":null}""", "1.2300e+02", "D")]
    [InlineData(true, """{"Name":1.2300e+02,"Description":"D","Description":null}""", "1.2300e+02", "D")]
    public async Task NormalHttp_PersistsConsumedProjectionAndRefreshesPublicLevelReads(
        bool update, string body, string name, string description)
    {
        var originalId = await Seed();
        var offersBefore = await Offers();
        await using var beforeContext = fixture.Context();
        var original = await beforeContext.Levels.AsNoTracking().SingleAsync(row => row.Id == originalId);
        using var reader = fixture.Client();
        using var primed = await reader.GetAsync("/jobs/?index=1&size=10");
        Assert.Equal(HttpStatusCode.OK, primed.StatusCode);
        var started = DateTime.UtcNow.AddSeconds(-5);
        using var writer = fixture.Client(update ? "legacy-career.levels.update" : "legacy-career.levels.create");
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = update
            ? await writer.PutAsync($"/jobs/levels/{originalId}", content)
            : await writer.PostAsync("/jobs/levels", content);
        Assert.Equal(update ? HttpStatusCode.NoContent : HttpStatusCode.Created, response.StatusCode);
        int id;
        if (update)
        {
            id = originalId;
            Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
        }
        else
        {
            using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            id = created.RootElement.GetProperty("id").GetInt32();
            Assert.True(id > 0);
            Assert.NotEqual(originalId, id);
            Assert.EndsWith("/levels/" + id, response.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);
            AssertFields(created.RootElement, name, description);
        }
        await using var db = fixture.Context();
        var persisted = await db.Levels.AsNoTracking().SingleAsync(row => row.Id == id);
        Assert.Equal(name, persisted.Name);
        Assert.Equal(description, persisted.Description);
        Assert.NotNull(persisted.ModifiedDate);
        Assert.InRange(persisted.ModifiedDate.Value, started, DateTime.UtcNow.AddMinutes(1));
        if (update) Assert.Equal(original.CreatedDate, persisted.CreatedDate);
        else
        {
            Assert.NotNull(persisted.CreatedDate);
            Assert.InRange(persisted.CreatedDate.Value, started, DateTime.UtcNow.AddMinutes(1));
            var retained = await db.Levels.AsNoTracking().SingleAsync(row => row.Id == originalId);
            Assert.Equal(original.Name, retained.Name);
            Assert.Equal(original.Description, retained.Description);
            Assert.Equal(original.CreatedDate, retained.CreatedDate);
            Assert.Equal(original.ModifiedDate, retained.ModifiedDate);
        }
        Assert.Equal(update ? 1 : 2, await db.Levels.CountAsync());
        using var detail = await reader.GetAsync($"/jobs/levels/{id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using var json = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        AssertFields(json.RootElement, name, description);
        using var list = JsonDocument.Parse(await reader.GetStringAsync("/jobs/?index=1&size=10"));
        foreach (var offer in list.RootElement.GetProperty("items").EnumerateArray())
            AssertFields(offer.GetProperty("level"), update ? name : original.Name!, update ? description : original.Description!);
        Assert.Equal(offersBefore, await Offers());
    }

    [Theory]
    [InlineData("""{"Name":{"private-input-marker":1},"Description":"D"}""")]
    [InlineData("""{"Name":"N","Description":[]}""")]
    [InlineData("""{"Name":{},"Name":"N","Description":"D"}""")]
    [InlineData("""{"Name":"N","Description":[],"Description":"D"}""")]
    public async Task MalformedConsumedOccurrence_RejectsBeforeAnyGraphWrite(string body)
    {
        var id = await Seed();
        var before = await Snapshot();
        using var writer = fixture.Client("legacy-career.levels.update");
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await writer.PutAsync($"/jobs/levels/{id}", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("private-input-marker", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(before, await Snapshot());
    }

    private async Task<int> Seed()
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
        db.Offers.Add(new JobOffer { LevelId = level.Id, Title = "Untouched offer", IsFilled = false });
        await db.SaveChangesAsync();
        return level.Id;
    }

    private async Task<string> Offers()
    {
        await using var db = fixture.Context();
        return JsonSerializer.Serialize(await db.Offers.AsNoTracking().OrderBy(row => row.Id)
            .Select(row => new
            {
                row.Id,
                row.LevelId,
                row.Title,
                row.Description,
                row.Introduction,
                row.Prerequisites,
                row.WhatWeOffer,
                row.Location,
                row.IsFilled,
                row.CreatedDate,
                row.ModifiedDate
            }).ToArrayAsync());
    }

    private async Task<string> Snapshot()
    {
        await using var db = fixture.Context();
        return JsonSerializer.Serialize(new
        {
            Offers = await Offers(),
            Levels = await db.Levels.AsNoTracking().OrderBy(row => row.Id)
                .Select(row => new { row.Id, row.Name, row.Description, row.CreatedDate, row.ModifiedDate }).ToArrayAsync()
        });
    }

    private static void AssertFields(JsonElement value, string name, string description)
    {
        Assert.Equal(name, value.GetProperty("name").GetString());
        Assert.Equal(description, value.GetProperty("description").GetString());
    }
}
