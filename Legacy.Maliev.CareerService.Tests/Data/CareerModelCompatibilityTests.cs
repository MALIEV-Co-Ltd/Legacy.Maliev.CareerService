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
}
