using System.Collections.Concurrent;
using System.Data.Common;
using Legacy.Maliev.CareerService.Application.Models;
using Legacy.Maliev.CareerService.Data;
using Legacy.Maliev.CareerService.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Legacy.Maliev.CareerService.Tests.Data;

public sealed class CareerMigrationTests
{
    [Fact]
    public async Task Paginated_offer_query_filters_sorts_and_pages_in_postgres()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync();
        var commands = new RecordingCommandInterceptor();
        var options = new DbContextOptionsBuilder<CareerDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .AddInterceptors(commands)
            .Options;
        await using var context = new CareerDbContext(options);
        await context.Database.MigrateAsync();
        var level = new JobLevel { Name = "Engineering", Description = "Technical roles" };
        context.Levels.Add(level);
        await context.SaveChangesAsync();
        context.Offers.AddRange(Enumerable.Range(1, 30).Select(index => new JobOffer
        {
            LevelId = level.Id,
            Title = $"Engineer {index:D2}",
            Description = "Additive manufacturing engineer",
            CreatedDate = new DateTime(2026, 1, index, 0, 0, 0, DateTimeKind.Utc),
            IsFilled = false,
        }));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        commands.Clear();
        var repository = new CareerRepository(context);

        var page = await repository.GetPaginatedOffersAsync(
            JobSortType.JobId_Descending,
            "engineer",
            2,
            5,
            CancellationToken.None);

        Assert.Equal(30, page.TotalItems);
        Assert.Equal(6, page.TotalPages);
        Assert.Equal([25, 24, 23, 22, 21], page.Items.Select(item => item.Id));
        Assert.All(page.Items, item => Assert.Equal("Engineering", item.Level?.Name));
        Assert.Contains(commands.Commands, command => command.Contains("LIMIT", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(commands.Commands, command => command.Contains("OFFSET", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Single_reads_are_untracked_and_detached_updates_still_succeed()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<CareerDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;
        await using var context = new CareerDbContext(options);
        await context.Database.MigrateAsync();
        var repository = new CareerRepository(context);
        var level = new JobLevel { Name = "Fixture level", Description = "Fixture description" };
        await repository.AddLevelAsync(level, CancellationToken.None);
        var offer = new JobOffer { LevelId = level.Id, Title = "Fixture offer", IsFilled = false };
        await repository.AddOfferAsync(offer, CancellationToken.None);
        context.ChangeTracker.Clear();

        var loadedOffer = await repository.GetOfferByIdAsync(offer.Id, CancellationToken.None);
        var loadedLevel = await repository.GetLevelByIdAsync(level.Id, CancellationToken.None);

        Assert.NotNull(loadedOffer);
        Assert.NotNull(loadedLevel);
        Assert.Empty(context.ChangeTracker.Entries());
        var trackedOffer = await repository.GetOfferByIdForUpdateAsync(offer.Id, CancellationToken.None);
        Assert.NotNull(trackedOffer);
        trackedOffer.Title = "Updated fixture offer";
        await repository.UpdateOfferAsync(trackedOffer, CancellationToken.None);
        context.ChangeTracker.Clear();
        Assert.Equal(
            "Updated fixture offer",
            await context.Offers.AsNoTracking()
                .Where(value => value.Id == offer.Id)
                .Select(value => value.Title)
                .SingleAsync());
    }

    [Fact]
    public async Task Initial_migration_applies_twice_and_preserves_native_xmin()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await postgres.StartAsync();

        var options = new DbContextOptionsBuilder<CareerDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options;

        await using (var context = new CareerDbContext(options))
        {
            await context.Database.MigrateAsync();
            await context.Database.MigrateAsync();
        }

        await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
        await connection.OpenAsync();

        Assert.Equal(2L, await ExecuteScalarAsync(connection, """
            SELECT COUNT(*)
            FROM information_schema.tables
            WHERE table_schema = 'public' AND table_name IN ('Level', 'Offer');
            """));
        Assert.Equal(1L, await ExecuteScalarAsync(connection, """
            SELECT COUNT(*)
            FROM pg_indexes
            WHERE schemaname = 'public' AND tablename = 'Offer' AND indexname = 'IX_Offer_LevelID';
            """));
        Assert.Equal(0L, await ExecuteScalarAsync(connection, """
            SELECT COUNT(*)
            FROM pg_attribute attribute
            JOIN pg_class relation ON relation.oid = attribute.attrelid
            WHERE relation.relname IN ('Level', 'Offer')
              AND attribute.attname = 'xmin'
              AND attribute.attnum > 0;
            """));
    }

    private static async Task<long> ExecuteScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private sealed class RecordingCommandInterceptor : DbCommandInterceptor
    {
        private readonly ConcurrentQueue<string> commands = new();

        public IReadOnlyCollection<string> Commands => commands.ToArray();

        public void Clear() => commands.Clear();

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            commands.Enqueue(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            commands.Enqueue(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
