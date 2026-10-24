using System.Net;
using System.Text;
using System.Text.Json;

using Legacy.Maliev.CareerService.Domain;

using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.CareerService.Tests.Controllers;

/// <summary>Retains original empty-record mutation and four-offer public status boundaries.</summary>
/// <param name="fixture">The owned PostgreSQL, Redis, and normal authorization fixture.</param>
[Collection("Career shared level")]
public sealed class CareerOriginalBoundaryRetentionHttpTests(CareerLifecycleFixture fixture)
    : IClassFixture<CareerLifecycleFixture>
{
    /// <summary>Checks original empty-object update lookup priority without creating records.</summary>
    /// <param name="kind">The original offer or level endpoint.</param>
    /// <returns>A task completing HTTP, authorization, and physical-state assertions.</returns>
    [Theory]
    [InlineData("offer")]
    [InlineData("level")]
    public async Task EmptyUpdateObject_MissingIntMaxReturns404WithoutPersistence(string kind)
    {
        await fixture.ResetAsync();
        var before = await PhysicalStateAsync();
        using var client = fixture.Client(Permission(kind, "update"));
        using var body = new StringContent("{}", Encoding.UTF8, "application/json");
        using var response = await client.PutAsync(Route(kind), body);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, await PhysicalStateAsync());
        await AssertEmptyDatabaseAsync();
    }

    /// <summary>Checks original missing deletion after a fresh live authorization decision.</summary>
    /// <param name="kind">The original offer or level endpoint.</param>
    /// <returns>A task completing the missing-row and exact live-decision assertions.</returns>
    [Theory]
    [InlineData("offer")]
    [InlineData("level")]
    public async Task Delete_OriginalEmptyDatabaseIntMaxReturns404AfterFreshLiveDecision(string kind)
    {
        await fixture.ResetAsync();
        using var client = fixture.Client(Permission(kind, "delete"));
        using var response = await client.DeleteAsync(Route(kind));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertEmptyDatabaseAsync();
        Assert.Equal(1, fixture.Authorities.Values.Single().LiveCalls);
    }

    /// <summary>Checks the original four-record true and false cohorts through anonymous HTTP.</summary>
    /// <param name="hasOpenPositions">Whether the first and fourth original records are unfilled.</param>
    /// <returns>A task completing public boolean and unchanged physical-row assertions.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OriginalFourOfferFilledCohort_PreservesPublicBoolean(bool hasOpenPositions)
    {
        await fixture.ResetAsync();
        await using (var db = fixture.Context())
        {
            var level = new JobLevel { Name = "Original cohort", Description = "Synthetic required principal" };
            db.Offers.AddRange(Enumerable.Range(0, 4).Select(index => new JobOffer
            {
                Level = level,
                IsFilled = !(hasOpenPositions && (index == 0 || index == 3))
            }));
            await db.SaveChangesAsync();
        }
        var before = await PhysicalStateAsync();
        using var client = fixture.Client();
        using var response = await client.GetAsync("/Jobs/job-opening-status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(hasOpenPositions, json.RootElement.GetBoolean());
        await using var readback = fixture.Context();
        Assert.Equal(4, await readback.Offers.CountAsync());
        Assert.Equal(hasOpenPositions ? 2 : 0, await readback.Offers.CountAsync(offer => offer.IsFilled == false));
        Assert.Equal(hasOpenPositions ? 2 : 4, await readback.Offers.CountAsync(offer => offer.IsFilled == true));
        Assert.Equal(before, await PhysicalStateAsync());
    }

    private static string Route(string kind) => kind == "offer" ? $"/Jobs/{int.MaxValue}" : $"/jobs/levels/{int.MaxValue}";

    private static string Permission(string kind, string action) => $"legacy-career.{(kind == "offer" ? "jobs" : "levels")}.{action}";

    private async Task AssertEmptyDatabaseAsync()
    {
        await using var db = fixture.Context();
        Assert.False(await db.Offers.AnyAsync());
        Assert.False(await db.Levels.AnyAsync());
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
}
