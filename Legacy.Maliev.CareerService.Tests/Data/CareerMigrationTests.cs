using Legacy.Maliev.CareerService.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Legacy.Maliev.CareerService.Tests.Data;

public sealed class CareerMigrationTests
{
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
}
