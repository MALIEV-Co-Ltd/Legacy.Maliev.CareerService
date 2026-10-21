using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Legacy.Maliev.CareerService.Domain;
using Legacy.Maliev.CareerService.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.CareerService.Tests.Controllers;

/// <summary>Checks authorization, concurrency, cancellation, and documentation through real Career hosts.</summary>
/// <param name="fixture">The shared PostgreSQL, Redis, production host, and controlled IAM fixture used for the HTTP assertions.</param>
[Collection("Career scaffold runtime")]
public sealed class CareerLifecycleAcceptanceTests(CareerLifecycleFixture fixture) : IClassFixture<CareerLifecycleFixture>
{
    /// <summary>Checks generated EF preview code and queries against the migrated owned graph.</summary>
    /// <returns>A task that completes after checking generated EF preview code and queries against the migrated owned graph.</returns>
    [Fact]
    public async Task Scaffold_ActualEfPreviewBuildAndGeneratedQueriesPreserveMigratedOwnedGraph()
    {
        await fixture.ResetAsync();
        var (level, offer) = await SeedAsync();
        await CareerScaffoldRuntimeProof.RunAsync(fixture, level.Id, offer.Id);
    }

    /// <summary>Checks fresh IAM authorization and deletion of only the selected PostgreSQL row.</summary>
    /// <param name="kind">The resource to mutate: offer selects the Jobs route and jobs permission; level selects the levels route and levels permission.</param>
    /// <returns>A task that completes after checking fresh IAM authorization and deletion of only the selected PostgreSQL row.</returns>
    [Theory]
    [InlineData("offer")]
    [InlineData("level")]
    public async Task Delete_ActualIamClientNormalRs256AndPostgres_RequiresFreshLiveDecisionAndDeletesOnlySelectedRow(string kind)
    {
        await fixture.ResetAsync(); var (level, offer) = await SeedAsync();
        var path = kind == "offer" ? $"/Jobs/{offer.Id}" : $"/jobs/levels/{level.Id}";
        if (kind == "level")
        {
            await using var db = fixture.Context(); await db.Offers.ExecuteDeleteAsync();
        }
        using var client = fixture.Client(Permission(kind, "delete"));
        using var first = await client.DeleteAsync(path); Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        using var second = await client.DeleteAsync(path); Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
        await using var actual = fixture.Context();
        Assert.Equal(kind == "offer" ? 1 : 0, await actual.Levels.CountAsync());
        Assert.False(await actual.Offers.AnyAsync());
        Assert.Equal(2, fixture.Authorities.Values.Single().LiveCalls);
    }

    /// <summary>Checks that live denial, malformed decisions, and unavailable IAM cannot reuse signed permissions.</summary>
    /// <param name="kind">The resource to delete: offer selects the Jobs route; level selects the unlinked job-level route.</param>
    /// <param name="decision">The controlled live IAM outcome: deny, unavailable, malformed, or no-client to omit the IAM registration.</param>
    /// <returns>A task that completes after checking that live denial, malformed decisions, and unavailable IAM cannot reuse signed permissions.</returns>
    [Theory]
    [InlineData("offer", "deny")]
    [InlineData("offer", "unavailable")]
    [InlineData("offer", "malformed")]
    [InlineData("offer", "no-client")]
    [InlineData("level", "deny")]
    [InlineData("level", "unavailable")]
    [InlineData("level", "malformed")]
    [InlineData("level", "no-client")]
    public async Task Delete_LiveDenialFaultOrMissingIamCannotFallBackToSignedPermission(string kind, string decision)
    {
        await fixture.ResetAsync(); var (level, offer) = await SeedAsync();
        if (kind == "level")
        {
            await using var initial = fixture.Context(); await initial.Offers.ExecuteDeleteAsync();
        }
        using var factory = decision == "no-client" ? fixture.NewFactory(iam: false) : null;
        using var client = fixture.Client(Permission(kind, "delete"), decision, factory);
        using var response = await client.DeleteAsync(kind == "offer" ? $"/Jobs/{offer.Id}" : $"/jobs/levels/{level.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await using var db = fixture.Context();
        Assert.Equal("Original level", (await db.Levels.SingleAsync()).Name);
        Assert.Equal(kind == "offer" ? 1 : 0, await db.Offers.CountAsync());
    }

    /// <summary>Checks that foreign-key refusal preserves linked rows and hides provider details.</summary>
    /// <returns>A task that completes after checking that foreign-key refusal preserves linked rows and hides provider details.</returns>
    [Fact]
    public async Task Delete_LinkedLevel_RealForeignKeyRefusalPreservesOfferAndLevelWithoutProviderDisclosure()
    {
        await fixture.ResetAsync(); var (level, offer) = await SeedAsync();
        using var client = fixture.Client("legacy-career.levels.delete");
        using var response = await client.DeleteAsync($"/jobs/levels/{level.Id}");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("Npgsql", await response.Content.ReadAsStringAsync());
        await using var db = fixture.Context();
        Assert.Equal(level.Id, (await db.Levels.SingleAsync()).Id);
        Assert.Equal(level.Id, (await db.Offers.SingleAsync()).LevelId);
        Assert.Equal(offer.Id, (await db.Offers.SingleAsync()).Id);
    }

    /// <summary>Checks that a stale update returns conflict and preserves the committed winner.</summary>
    /// <param name="kind">The resource to mutate: offer selects the Jobs route and jobs permission; level selects the levels route and levels permission.</param>
    /// <returns>A task that completes after checking that a stale update returns conflict and preserves the committed winner.</returns>
    [Theory]
    [InlineData("offer")]
    [InlineData("level")]
    public async Task Update_ConcurrentCommittedWinner_StaleXminReturnsConflictAndPreservesWinner(string kind)
    {
        await fixture.ResetAsync(); var (level, offer) = await SeedAsync(); var schedule = new SaveSchedule();
        using var factory = fixture.NewFactory(schedule: schedule);
        using var stale = fixture.Client(Permission(kind, "update"), factory: factory);
        var path = kind == "offer" ? $"/Jobs/{offer.Id}" : $"/jobs/levels/{level.Id}";
        var pending = stale.PutAsJsonAsync(path, Payload(kind, level.Id, "Loser"));
        await schedule.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        try
        {
            using var winner = fixture.Client(Permission(kind, "update"));
            using var committed = await winner.PutAsJsonAsync(path, Payload(kind, level.Id, "Winner"));
            Assert.Equal(HttpStatusCode.NoContent, committed.StatusCode);
        }
        finally { schedule.Release.TrySetResult(); }
        using var conflict = await pending.WaitAsync(TimeSpan.FromSeconds(15));
        await using var db = fixture.Context();
        Assert.Equal("Winner", kind == "offer" ? (await db.Offers.SingleAsync()).Title : (await db.Levels.SingleAsync()).Name);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.DoesNotContain("DbUpdateConcurrencyException", await conflict.Content.ReadAsStringAsync());
    }

    /// <summary>Checks that a stale deletion returns conflict without reviving a deleted row.</summary>
    /// <param name="kind">The resource to mutate: offer selects the Jobs route and jobs permission; level selects the levels route and levels permission.</param>
    /// <returns>A task that completes after checking that a stale deletion returns conflict without reviving a deleted row.</returns>
    [Theory]
    [InlineData("offer")]
    [InlineData("level")]
    public async Task Delete_ConcurrentDeletionWinner_StaleXminReturnsConflictWithoutRevivingRows(string kind)
    {
        await fixture.ResetAsync(); var (level, offer) = await SeedAsync();
        if (kind == "level")
        {
            await using var initial = fixture.Context(); await initial.Offers.ExecuteDeleteAsync();
        }
        var schedule = new SaveSchedule();
        using var factory = fixture.NewFactory(schedule: schedule);
        using var stale = fixture.Client(Permission(kind, "delete"), factory: factory);
        var path = kind == "offer" ? $"/Jobs/{offer.Id}" : $"/jobs/levels/{level.Id}";
        var pending = stale.DeleteAsync(path);
        await schedule.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        try
        {
            using var winner = fixture.Client(Permission(kind, "delete"));
            using var deleted = await winner.DeleteAsync(path); Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }
        finally { schedule.Release.TrySetResult(); }
        using var conflict = await pending.WaitAsync(TimeSpan.FromSeconds(15));
        await using var db = fixture.Context();
        Assert.False(await db.Offers.AnyAsync());
        Assert.Equal(kind == "offer" ? 1 : 0, await db.Levels.CountAsync());
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }

    /// <summary>Checks that cancellation before a database write leaves persisted rows unchanged.</summary>
    /// <param name="kind">The resource to mutate: offer selects the Jobs route and jobs permission; level selects the levels route and levels permission.</param>
    /// <returns>A task that completes after checking that cancellation before a database write leaves persisted rows unchanged.</returns>
    [Theory]
    [InlineData("offer")]
    [InlineData("level")]
    public async Task Update_CallerAbortBeforeDatabaseWrite_PropagatesCancellationAndLeavesRowsUnchanged(string kind)
    {
        await fixture.ResetAsync(); var (level, offer) = await SeedAsync(); var schedule = new SaveSchedule();
        using var factory = fixture.NewFactory(schedule: schedule);
        using var client = fixture.Client(Permission(kind, "update"), factory: factory);
        using var cancellation = new CancellationTokenSource();
        var pending = client.PutAsJsonAsync(kind == "offer" ? $"/Jobs/{offer.Id}" : $"/jobs/levels/{level.Id}", Payload(kind, level.Id, "Aborted"), cancellation.Token);
        await schedule.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        cancellation.Cancel();
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(15)));
            await schedule.Exited.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.True(schedule.SawCallerCancellation);
        }
        finally { schedule.Release.TrySetResult(); }
        await using var db = fixture.Context();
        Assert.Equal("Original offer", (await db.Offers.SingleAsync()).Title);
        Assert.Equal("Original level", (await db.Levels.SingleAsync()).Name);
    }

    /// <summary>Checks nonproduction documentation casing and operations while production documentation stays hidden.</summary>
    /// <returns>A task that completes after checking nonproduction documentation casing and operations while production documentation stays hidden.</returns>
    [Fact]
    public async Task Documentation_ActualNonProductionHttpPreservesCamelCaseAndSourceActions_ProductionIsHidden()
    {
        using var production = fixture.Client();
        using var hidden = await production.GetAsync("/Jobs/openapi/v1.json"); Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        using var docsFactory = fixture.NewFactory(iam: false, environment: "Testing");
        using var client = fixture.Client(factory: docsFactory);
        using var response = await client.GetAsync("/Jobs/openapi/v1.json"); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths").EnumerateObject().ToDictionary(item => item.Name, item => item.Value, StringComparer.OrdinalIgnoreCase);
        Assert.True(paths["/Jobs/job-opening-status"].TryGetProperty("get", out _));
        Assert.True(paths["/Jobs/{offerId}"].TryGetProperty("delete", out _));
        Assert.True(paths["/jobs/Levels/{levelId}"].TryGetProperty("delete", out _));
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var expected = new Dictionary<string, string[]>
        {
            ["UpsertJobOfferRequest"] = ["levelId", "title", "introduction", "description", "prerequisites", "whatWeOffer", "location", "isFilled"],
            ["UpsertJobLevelRequest"] = ["name", "description"],
            ["JobLevelResponse"] = ["id", "name", "description", "createdDate", "modifiedDate"],
            ["JobOfferResponse"] = ["id", "levelId", "title", "introduction", "description", "prerequisites", "whatWeOffer", "location", "isFilled", "createdDate", "modifiedDate", "level"],
            ["PaginatedJobOfferResponse"] = ["items", "pageIndex", "totalPages", "totalItems", "hasPreviousPage", "hasNextPage"],
        };
        foreach (var (name, properties) in expected)
            Assert.Equal(properties.Order(StringComparer.Ordinal).ToArray(), schemas.GetProperty(name).GetProperty("properties").EnumerateObject().Select(item => item.Name).Order(StringComparer.Ordinal).ToArray());
    }

    /// <summary>Checks that arbitrary save failures preserve generic classification without committing changes.</summary>
    /// <param name="kind">The resource to update: offer selects a job offer; level selects its job level.</param>
    /// <param name="invalidOperation">Whether the save interceptor throws InvalidOperationException rather than IOException.</param>
    /// <param name="status">The expected HTTP error status for the injected save failure: 400 for InvalidOperationException or 500 for IOException.</param>
    /// <returns>A task that completes after checking that arbitrary save failures preserve generic classification without committing changes.</returns>
    [Theory]
    [InlineData("offer", false, 500)]
    [InlineData("level", false, 500)]
    [InlineData("offer", true, 400)]
    [InlineData("level", true, 400)]
    public async Task Update_ArbitrarySaveFailureRetainsExistingGenericClassificationAndDoesNotCommit(string kind, bool invalidOperation, int status)
    {
        await fixture.ResetAsync(); var (level, offer) = await SeedAsync();
        var schedule = new SaveSchedule { Failure = invalidOperation ? new InvalidOperationException("Controlled arbitrary save failure") : new IOException("Controlled arbitrary save failure") };
        schedule.Release.TrySetResult();
        using var factory = fixture.NewFactory(schedule: schedule);
        using var client = fixture.Client(Permission(kind, "update"), factory: factory);
        using var response = await client.PutAsJsonAsync(kind == "offer" ? $"/Jobs/{offer.Id}" : $"/jobs/levels/{level.Id}", Payload(kind, level.Id, "Must not persist"));
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        Assert.DoesNotContain("Controlled arbitrary save failure", await response.Content.ReadAsStringAsync());
        await using var db = fixture.Context();
        Assert.Equal("Original offer", (await db.Offers.SingleAsync()).Title);
        Assert.Equal("Original level", (await db.Levels.SingleAsync()).Name);
    }

    /// <summary>Checks that caller cancellation during an xmin failure is not converted into conflict.</summary>
    /// <param name="kind">The stale entity used for the real xmin conflict: offer selects the offer repository methods; level selects the level methods.</param>
    /// <param name="delete">Whether to attempt deletion rather than an update of the stale entity after the winning update commits.</param>
    /// <returns>A task that completes after checking that caller cancellation during an xmin failure is not converted into conflict.</returns>
    [Theory]
    [InlineData("offer", false)]
    [InlineData("offer", true)]
    [InlineData("level", false)]
    [InlineData("level", true)]
    public async Task Repository_RealXminFailureWithCallerCancellationCannotBecomeConflict(string kind, bool delete)
    {
        await fixture.ResetAsync(); var (level, offer) = await SeedAsync();
        using var cancellation = new CancellationTokenSource();
        var schedule = new SaveSchedule { OnConcurrencyFailure = cancellation.Cancel };
        schedule.Release.TrySetResult();
        using var factory = fixture.NewFactory(schedule: schedule);
        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ICareerRepository>();
        // Actual production registrations/options; direct boundary is intentionally
        // not positive HTTP authentication proof. Cancellation occurs on a genuine
        // failed xmin save, not a fabricated concurrency exception.
        var staleOffer = kind == "offer" ? await repository.GetOfferByIdForUpdateAsync(offer.Id, CancellationToken.None) : null;
        var staleLevel = kind == "level" ? await repository.GetLevelByIdForUpdateAsync(level.Id, CancellationToken.None) : null;
        await using (var winner = fixture.Context())
        {
            if (kind == "offer") (await winner.Offers.SingleAsync()).Title = "Winner";
            else (await winner.Levels.SingleAsync()).Name = "Winner";
            await winner.SaveChangesAsync();
        }
        var failure = await Record.ExceptionAsync(() => kind == "offer"
            ? delete ? repository.DeleteOfferAsync(staleOffer!, cancellation.Token) : repository.UpdateOfferAsync(staleOffer!, cancellation.Token)
            : delete ? repository.DeleteLevelAsync(staleLevel!, cancellation.Token) : repository.UpdateLevelAsync(staleLevel!, cancellation.Token));
        Assert.True(cancellation.IsCancellationRequested);
        Assert.IsAssignableFrom<OperationCanceledException>(failure);
        await using var actual = fixture.Context();
        Assert.Equal("Winner", kind == "offer" ? (await actual.Offers.SingleAsync()).Title : (await actual.Levels.SingleAsync()).Name);
    }

    private async Task<(JobLevel Level, JobOffer Offer)> SeedAsync()
    {
        await using var db = fixture.Context();
        var level = new JobLevel { Name = "Original level", Description = "Synthetic fixture" };
        db.Levels.Add(level); await db.SaveChangesAsync();
        var offer = new JobOffer { LevelId = level.Id, Title = "Original offer", Description = "Synthetic fixture", IsFilled = false };
        db.Offers.Add(offer); await db.SaveChangesAsync(); return (level, offer);
    }
    private static string Permission(string kind, string action) => $"legacy-career.{(kind == "offer" ? "jobs" : "levels")}.{action}";
    private static object Payload(string kind, int levelId, string name) => kind == "offer"
        ? new { LevelId = levelId, Title = name, Description = "Synthetic fixture" }
        : new { Name = name, Description = "Synthetic fixture" };
}
