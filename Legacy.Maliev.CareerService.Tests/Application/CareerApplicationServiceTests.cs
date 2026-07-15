using Legacy.Maliev.CareerService.Application.Interfaces;
using Legacy.Maliev.CareerService.Application.Models;
using Legacy.Maliev.CareerService.Application.Services;
using Legacy.Maliev.CareerService.Domain;
using Microsoft.Extensions.Time.Testing;

namespace Legacy.Maliev.CareerService.Tests.Application;

public sealed class CareerApplicationServiceTests
{
    [Fact]
    public async Task HasOpenPositionsAsync_preserves_legacy_unfilled_offer_rule()
    {
        var service = new CareerApplicationService(
            new InMemoryCareerRepository(new JobOffer { Id = 1, IsFilled = false }),
            TimeProvider.System);

        Assert.True(await service.HasOpenPositionsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetPaginatedAsync_preserves_search_sort_and_level_projection()
    {
        var repository = new InMemoryCareerRepository(
            new JobOffer { Id = 2, Title = "Designer", CreatedDate = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), Level = new JobLevel { Id = 1, Name = "Senior" } },
            new JobOffer { Id = 1, Title = "Engineer", Description = "Build 3D tools", CreatedDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), Level = new JobLevel { Id = 2, Name = "Lead" } });
        var service = new CareerApplicationService(repository, TimeProvider.System);

        var page = await service.GetPaginatedAsync(JobSortType.JobId_Descending, "3D", 1, 10, CancellationToken.None);

        Assert.Single(page.Items);
        Assert.Equal(1, page.Items[0].Id);
        Assert.Equal("Lead", page.Items[0].Level?.Name);
        Assert.False(page.HasPreviousPage);
        Assert.False(page.HasNextPage);
    }

    [Fact]
    public async Task CreateOfferAsync_preserves_legacy_offer_fields_and_timestamps()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 7, 15, 1, 2, 3, TimeSpan.Zero));
        var repository = new InMemoryCareerRepository();
        var service = new CareerApplicationService(repository, timeProvider);

        var created = await service.CreateOfferAsync(
            new UpsertJobOfferRequest(3, "Engineer", "Intro", "Description", "Prereq", "Offer", "Bangkok", false),
            CancellationToken.None);

        Assert.Equal(3, created.LevelId);
        Assert.Equal("Engineer", created.Title);
        Assert.False(created.IsFilled);
        Assert.Equal(new DateTime(2026, 7, 15, 1, 2, 3, DateTimeKind.Utc), created.CreatedDate);
    }

    private sealed class InMemoryCareerRepository(params JobOffer[] seed) : ICareerRepository
    {
        private readonly List<JobOffer> offers = seed.ToList();
        private readonly List<JobLevel> levels = [];

        public Task<IReadOnlyList<JobOffer>> GetOffersAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<JobOffer>>(offers.ToArray());

        public Task<JobOffer?> GetOfferByIdAsync(int id, CancellationToken cancellationToken) =>
            Task.FromResult(offers.SingleOrDefault(offer => offer.Id == id));

        public Task<bool> HasOpenPositionsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(offers.Any(offer => offer.IsFilled == false));

        public Task AddOfferAsync(JobOffer offer, CancellationToken cancellationToken)
        {
            offer.Id = offers.Count == 0 ? 1 : offers.Max(item => item.Id) + 1;
            offers.Add(offer);
            return Task.CompletedTask;
        }

        public Task UpdateOfferAsync(JobOffer offer, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DeleteOfferAsync(JobOffer offer, CancellationToken cancellationToken)
        {
            offers.Remove(offer);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<JobLevel>> GetLevelsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<JobLevel>>(levels.ToArray());

        public Task<JobLevel?> GetLevelByIdAsync(int id, CancellationToken cancellationToken) =>
            Task.FromResult(levels.SingleOrDefault(level => level.Id == id));

        public Task AddLevelAsync(JobLevel level, CancellationToken cancellationToken)
        {
            level.Id = levels.Count == 0 ? 1 : levels.Max(item => item.Id) + 1;
            levels.Add(level);
            return Task.CompletedTask;
        }

        public Task UpdateLevelAsync(JobLevel level, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DeleteLevelAsync(JobLevel level, CancellationToken cancellationToken)
        {
            levels.Remove(level);
            return Task.CompletedTask;
        }
    }
}
