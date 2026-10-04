using System.Data;
using Legacy.Maliev.CareerService.Data;
using Legacy.Maliev.CareerService.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Legacy.Maliev.CareerService.Tests.Data;

public sealed class CareerContextConfigurationContractTests
{
    [Fact]
    public void ExplicitNpgsqlConnection_IsPreservedWhileBuildingTheLegacyModelWithoutOpeningIt()
    {
        using var connection = new NpgsqlConnection("Host=127.0.0.1;Port=1;Database=career_context_fixture;Username=fixture");
        var options = new DbContextOptionsBuilder<CareerDbContext>().UseNpgsql(connection).Options;
        using var context = new CareerDbContext(options);

        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", context.Database.ProviderName);
        Assert.Same(connection, context.Database.GetDbConnection());
        var offer = context.Model.FindEntityType(typeof(JobOffer));
        var level = context.Model.FindEntityType(typeof(JobLevel));
        Assert.NotNull(offer);
        Assert.NotNull(level);
        Assert.Equal("Offer", offer.GetTableName());
        Assert.Equal("Level", level.GetTableName());
        Assert.Equal(100, offer.FindProperty(nameof(JobOffer.Title))?.GetMaxLength());
        Assert.Equal(100, offer.FindProperty(nameof(JobOffer.Location))?.GetMaxLength());
        Assert.Equal(50, level.FindProperty(nameof(JobLevel.Name))?.GetMaxLength());
        var relationship = Assert.Single(offer.GetForeignKeys());
        Assert.Equal(level, relationship.PrincipalEntityType);
        Assert.Equal(nameof(JobOffer.LevelId), Assert.Single(relationship.Properties).Name);
        Assert.Equal(DeleteBehavior.ClientSetNull, relationship.DeleteBehavior);
        Assert.Equal(ConnectionState.Closed, connection.State);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public void MissingProvider_CannotBuildAModelUsingAnImplicitLegacyConnection()
    {
        using var context = new CareerDbContext(new DbContextOptionsBuilder<CareerDbContext>().Options);

        Assert.Throws<InvalidOperationException>(() => _ = context.Model);
    }
}
