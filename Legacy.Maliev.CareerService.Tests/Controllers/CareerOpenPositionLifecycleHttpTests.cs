using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.CareerService.Tests.Controllers;

[CollectionDefinition("Career open position lifecycle", DisableParallelization = true)]
public sealed class CareerOpenPositionLifecycleCollection;

[Collection("Career open position lifecycle")]
public sealed class CareerOpenPositionLifecycleHttpTests(CareerRouteFixture fixture) : IClassFixture<CareerRouteFixture>
{
    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Create_PreservesNullableFilledStateAndAnonymousConsumerBoolean(bool? filled)
    {
        await fixture.ResetAsync();
        var levelId = await fixture.SeedLevelAsync();
        using var client = fixture.Client("legacy-career.jobs.create");
        using var response = await client.PostAsJsonAsync("/Jobs", Payload(levelId, filled));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var id = json.RootElement.GetProperty("id").GetInt32();
        Assert.EndsWith($"/Jobs/{id}", response.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);
        await using var db = fixture.Context();
        var stored = await db.Offers.AsNoTracking().SingleAsync();
        Assert.Equal(id, stored.Id);
        Assert.Equal(filled, stored.IsFilled);
        Assert.Equal("  ช่าง Fixture  ", stored.Title);
        await AssertPublicStateAsync(id, filled);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(null, false)]
    [InlineData(false, null)]
    public async Task Update_PreservesNullableStateTransitionsCreationTimeAndPublicRead(bool? initial, bool? next)
    {
        await fixture.ResetAsync();
        var levelId = await fixture.SeedLevelAsync();
        await using var db = fixture.Context();
        db.Offers.Add(new Legacy.Maliev.CareerService.Domain.JobOffer
        {
            LevelId = levelId,
            Title = "Original",
            IsFilled = initial,
            CreatedDate = new DateTime(2020, 1, 1),
            ModifiedDate = new DateTime(2020, 1, 2)
        });
        await db.SaveChangesAsync();
        var before = await db.Offers.AsNoTracking().SingleAsync();
        await AssertPublicStateAsync(before.Id, initial);
        using var client = fixture.Client("legacy-career.jobs.update");
        using var response = await client.PutAsJsonAsync($"/Jobs/{before.Id}", Payload(levelId, next));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
        var after = await db.Offers.AsNoTracking().SingleAsync();
        Assert.Equal(next, after.IsFilled);
        Assert.Equal(before.CreatedDate, after.CreatedDate);
        Assert.Equal(before.LevelId, after.LevelId);
        Assert.True(after.ModifiedDate > before.ModifiedDate);
        Assert.Equal("  ช่าง Fixture  ", after.Title);
        await AssertPublicStateAsync(before.Id, next);
    }

    [Theory]
    [InlineData("anonymous", HttpStatusCode.Unauthorized)]
    [InlineData("expired", HttpStatusCode.Unauthorized)]
    [InlineData("wrong-signature", HttpStatusCode.Unauthorized)]
    [InlineData("missing-permission", HttpStatusCode.Forbidden)]
    public async Task InvalidUpdateAuthority_CannotChangeStoredOfferOrHomepageBoolean(string authority, HttpStatusCode expected)
    {
        await fixture.ResetAsync();
        var levelId = await fixture.SeedLevelAsync();
        await using var db = fixture.Context();
        db.Offers.Add(new Legacy.Maliev.CareerService.Domain.JobOffer { LevelId = levelId, Title = "Original", IsFilled = false });
        await db.SaveChangesAsync();
        var before = await db.Offers.AsNoTracking().SingleAsync();
        var snapshot = JsonSerializer.Serialize(before);
        using var client = fixture.Client("legacy-career.jobs.update", authority);
        using var response = await client.PutAsJsonAsync($"/Jobs/{before.Id}", Payload(levelId, true));
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(snapshot, JsonSerializer.Serialize(await db.Offers.AsNoTracking().SingleAsync()));
        await AssertPublicStateAsync(before.Id, false);
    }

    private static object Payload(int levelId, bool? filled) => new
    {
        LevelId = levelId,
        Title = "  ช่าง Fixture  ",
        Description = "Lifecycle fixture",
        Prerequisites = "Fixture prerequisites",
        IsFilled = filled
    };

    private async Task AssertPublicStateAsync(int id, bool? filled)
    {
        await using var db = fixture.Context();
        var before = JsonSerializer.Serialize(await db.Offers.AsNoTracking().SingleAsync());
        using var anonymous = fixture.Client();
        // The lower-case trailing-slash route is the original Web homepage call.
        foreach (var route in new[] { "/Jobs/job-opening-status", "/jobs/job-opening-status/" })
        {
            using var status = await anonymous.GetAsync(route);
            Assert.Equal(HttpStatusCode.OK, status.StatusCode);
            using var scalar = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
            Assert.Equal(filled == false, scalar.RootElement.GetBoolean());
        }
        using var detail = await anonymous.GetAsync($"/Jobs/{id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using var json = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        if (filled is null)
        {
            Assert.False(json.RootElement.TryGetProperty("isFilled", out _));
        }
        else
        {
            Assert.Equal(filled.Value, json.RootElement.GetProperty("isFilled").GetBoolean());
        }
        Assert.Equal(before, JsonSerializer.Serialize(await db.Offers.AsNoTracking().SingleAsync()));
    }
}
