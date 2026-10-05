using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Legacy.Maliev.CareerService.Domain;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.CareerService.Tests.Controllers;

[CollectionDefinition("Career persisted constraints", DisableParallelization = true)]
public sealed class CareerPersistedConstraintCollection;

[Collection("Career persisted constraints")]
public sealed class CareerPersistedConstraintHttpTests(CareerRouteFixture fixture) : IClassFixture<CareerRouteFixture>
{
    [Theory]
    [InlineData("level", 50)]
    [InlineData("offer", 100)]
    public async Task SourceBoundedText_AcceptsExactLimitThenRollsBackOverlongUpdatePrivately(string kind, int limit)
    {
        var (levelId, offerId) = await SeedAsync();
        var route = kind == "level" ? $"/jobs/levels/{levelId}" : $"/jobs/{offerId}";
        using var client = fixture.Client(kind == "level" ? "legacy-career.levels.update" : "legacy-career.jobs.update");
        var text = new string('ก', limit);
        using var valid = await client.PutAsJsonAsync(route, Payload(kind, levelId, text));
        Assert.Equal(HttpStatusCode.NoContent, valid.StatusCode);
        using var reader = fixture.Client();
        using var detail = await reader.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using var json = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        Assert.Equal(text, json.RootElement.GetProperty(kind == "level" ? "name" : "title").GetString());
        await using (var db = fixture.Context())
        {
            Assert.Equal(text, kind == "level"
                ? (await db.Levels.AsNoTracking().SingleAsync()).Name
                : (await db.Offers.AsNoTracking().SingleAsync()).Title);
        }
        var before = await SnapshotAsync();
        using var refused = await client.PutAsJsonAsync(route, Payload(kind, levelId, text + "ก"));
        await AssertPrivateFailureAsync(refused);
        Assert.Equal(before, await SnapshotAsync());
        using var after = await reader.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        using var afterJson = JsonDocument.Parse(await after.Content.ReadAsStringAsync());
        Assert.Equal(text, afterJson.RootElement.GetProperty(kind == "level" ? "name" : "title").GetString());
    }

    [Fact]
    public async Task OfferUpdate_MissingLevelRollsBackEveryChangedFieldAndPreservesImportedData()
    {
        var (_, offerId) = await SeedAsync();
        var before = await SnapshotAsync();
        using var client = fixture.Client("legacy-career.jobs.update");
        using var refused = await client.PutAsJsonAsync($"/jobs/{offerId}", new
        {
            LevelId = int.MaxValue,
            Title = "Refused title",
            Description = "Refused description",
            Prerequisites = "Refused prerequisites",
            IsFilled = true,
            Introduction = "Do not write",
            WhatWeOffer = "Do not write",
            Location = "Do not write"
        });
        await AssertPrivateFailureAsync(refused);
        Assert.Equal(before, await SnapshotAsync());
        using var reader = fixture.Client();
        using var detail = await reader.GetAsync($"/jobs/{offerId}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using var json = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        Assert.Equal("Original title", json.RootElement.GetProperty("title").GetString());
        Assert.Equal("Imported introduction", json.RootElement.GetProperty("introduction").GetString());
        Assert.Equal("Imported benefits", json.RootElement.GetProperty("whatWeOffer").GetString());
        Assert.Equal("Bangkok fixture", json.RootElement.GetProperty("location").GetString());
        Assert.Equal("true", await reader.GetStringAsync("/jobs/job-opening-status/"));
    }

    [Theory]
    [InlineData(null, "Description")]
    [InlineData("Name", null)]
    [InlineData(null, null)]
    public async Task LevelCreate_SourceRequiredFieldsFailPrivatelyWithoutCreatingRowsOrChangingExistingOffer(string? name, string? description)
    {
        await SeedAsync();
        var before = await SnapshotAsync();
        using var client = fixture.Client("legacy-career.levels.create");
        using var refused = await client.PostAsJsonAsync("/jobs/levels", new { Name = name, Description = description });
        await AssertPrivateFailureAsync(refused);
        Assert.Equal(before, await SnapshotAsync());
    }

    private static object Payload(string kind, int levelId, string text) => kind == "level"
        ? new { Name = text, Description = "Updated description" }
        : (object)new { LevelId = levelId, Title = text, Description = "Updated description", Prerequisites = "Updated prerequisites", IsFilled = false };

    private async Task<(int LevelId, int OfferId)> SeedAsync()
    {
        await fixture.ResetAsync();
        await using var db = fixture.Context();
        var level = new JobLevel { Name = "Original level", Description = "Original description", CreatedDate = new DateTime(2020, 1, 1), ModifiedDate = new DateTime(2020, 1, 2) };
        db.Levels.Add(level);
        await db.SaveChangesAsync();
        var offer = new JobOffer
        {
            LevelId = level.Id,
            Title = "Original title",
            Description = "Original description",
            Prerequisites = "Original prerequisites",
            Introduction = "Imported introduction",
            WhatWeOffer = "Imported benefits",
            Location = "Bangkok fixture",
            IsFilled = false,
            CreatedDate = new DateTime(2020, 1, 1),
            ModifiedDate = new DateTime(2020, 1, 2)
        };
        db.Offers.Add(offer);
        await db.SaveChangesAsync();
        return (level.Id, offer.Id);
    }

    private async Task<string> SnapshotAsync()
    {
        await using var db = fixture.Context();
        return JsonSerializer.Serialize(new
        {
            Levels = await db.Levels.AsNoTracking().OrderBy(row => row.Id).Select(row => new { row.Id, row.Name, row.Description, row.CreatedDate, row.ModifiedDate }).ToArrayAsync(),
            Offers = await db.Offers.AsNoTracking().OrderBy(row => row.Id).Select(row => new { row.Id, row.LevelId, row.Title, row.Description, row.Prerequisites, row.Introduction, row.WhatWeOffer, row.Location, row.IsFilled, row.CreatedDate, row.ModifiedDate }).ToArrayAsync()
        });
    }

    private static async Task AssertPrivateFailureAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal(500, json.RootElement.GetProperty("statusCode").GetInt32());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("details").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("traceId").GetString()));
        foreach (var privateText in new[] { "Npgsql", "23502", "23503", "22001", "Original title", "Refused title" })
            Assert.DoesNotContain(privateText, body, StringComparison.Ordinal);
    }
}
