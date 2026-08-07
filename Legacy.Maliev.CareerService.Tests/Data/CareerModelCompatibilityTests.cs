using System.Runtime.CompilerServices;
using Legacy.Maliev.CareerService.Data;
using Legacy.Maliev.CareerService.Domain;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.CareerService.Tests.Data;

public sealed class CareerModelCompatibilityTests
{
    [Fact]
    public void Model_maps_to_legacy_Level_and_Offer_tables()
    {
        var options = new DbContextOptionsBuilder<CareerDbContext>()
            .UseNpgsql("Host=localhost;Database=unused")
            .Options;
        using var context = new CareerDbContext(options);

        var offer = context.Model.FindEntityType(typeof(JobOffer));
        var level = context.Model.FindEntityType(typeof(JobLevel));

        Assert.Equal("Offer", offer?.GetTableName());
        Assert.Equal("Level", level?.GetTableName());
        Assert.Equal("ID", offer?.FindProperty(nameof(JobOffer.Id))?.GetColumnName());
        Assert.Equal("LevelID", offer?.FindProperty(nameof(JobOffer.LevelId))?.GetColumnName());
        Assert.Equal(100, offer?.FindProperty(nameof(JobOffer.Title))?.GetMaxLength());
    }

    [Fact]
    public void Model_UsesUtcWallClockTimestampContractForAllDateColumns()
    {
        var options = new DbContextOptionsBuilder<CareerDbContext>()
            .UseNpgsql("Host=localhost;Database=model")
            .Options;
        using var context = new CareerDbContext(options);

        foreach (var entityType in new[] { typeof(JobLevel), typeof(JobOffer) })
        {
            var entity = context.Model.FindEntityType(entityType)!;
            foreach (var propertyName in new[] { nameof(JobLevel.CreatedDate), nameof(JobLevel.ModifiedDate) })
            {
                var property = entity.FindProperty(propertyName)!;
                Assert.Equal("timestamp without time zone", property.GetColumnType());
                Assert.Equal("CURRENT_TIMESTAMP AT TIME ZONE 'UTC'", property.GetDefaultValueSql());
            }
        }
    }

    [Fact]
    public void TimestampMigration_ConvertsExistingValuesExplicitlyAsUtc()
    {
        var migration = File.ReadAllText(FindRepositoryFile(
            "Legacy.Maliev.CareerService.Data/Migrations/20260807143000_AlignUtcTimestampColumns.cs"));

        Assert.Contains("DROP DEFAULT", migration, StringComparison.Ordinal);
        Assert.Contains("timestamp without time zone", migration, StringComparison.Ordinal);
        Assert.Contains("USING \"{column}\" AT TIME ZONE 'UTC'", migration, StringComparison.Ordinal);
    }

    private static string FindRepositoryFile(string relativePath, [CallerFilePath] string sourceFile = "")
    {
        for (var directory = new DirectoryInfo(Path.GetDirectoryName(sourceFile)!);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Could not find migration source '{relativePath}'.");
    }
}
