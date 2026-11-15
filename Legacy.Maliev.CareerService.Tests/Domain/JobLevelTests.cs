using Legacy.Maliev.CareerService.Domain;

namespace Legacy.Maliev.CareerService.Tests.Domain;

/// <summary>Checks the original level's independently initialized offer collection.</summary>
public sealed class JobLevelTests
{
    /// <summary>New levels have separate, mutable sets of offers.</summary>
    [Fact]
    public void NewLevels_HaveIndependentEmptyOfferSets()
    {
        var first = new JobLevel();
        var second = new JobLevel();
        var offer = new JobOffer();

        Assert.NotNull(first.Offers);
        Assert.NotNull(second.Offers);
        Assert.Empty(first.Offers);
        Assert.Empty(second.Offers);
        first.Offers.Add(offer);
        first.Offers.Add(offer);
        Assert.Single(first.Offers);
        Assert.Empty(second.Offers);
    }

    /// <summary>Callers can still explicitly clear the navigation collection.</summary>
    [Fact]
    public void Offers_CanStillBeSetToNull()
    {
        var level = new JobLevel { Offers = null };

        Assert.Null(level.Offers);
    }
}
