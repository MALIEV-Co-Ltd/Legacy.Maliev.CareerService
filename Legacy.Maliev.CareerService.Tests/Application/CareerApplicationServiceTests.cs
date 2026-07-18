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

        public Task<PaginatedJobOfferResponse> GetPaginatedOffersAsync(
            JobSortType? sort,
            string? search,
            int? index,
            int? size,
            CancellationToken cancellationToken)
        {
            IEnumerable<JobOffer> query = offers;
            if (!string.IsNullOrWhiteSpace(search))
            {
                var isNumeric = int.TryParse(search, out var searchAsInteger);
                query = query.Where(offer =>
                    (isNumeric && offer.Id == searchAsInteger) ||
                    offer.Title?.Contains(search, StringComparison.OrdinalIgnoreCase) == true ||
                    offer.Introduction?.Contains(search, StringComparison.OrdinalIgnoreCase) == true ||
                    offer.WhatWeOffer?.Contains(search, StringComparison.OrdinalIgnoreCase) == true ||
                    offer.Location?.Contains(search, StringComparison.OrdinalIgnoreCase) == true ||
                    offer.Description?.Contains(search, StringComparison.OrdinalIgnoreCase) == true);
            }

            query = sort switch
            {
                JobSortType.JobId_Descending => query.OrderByDescending(offer => offer.Id),
                JobSortType.JobCreatedDate_Ascending => query.OrderBy(offer => offer.CreatedDate),
                JobSortType.JobCreatedDate_Descending => query.OrderByDescending(offer => offer.CreatedDate),
                _ => query.OrderBy(offer => offer.Id),
            };
            var filtered = query.ToArray();
            var pageIndex = Math.Max(index ?? 1, 1);
            var pageSize = Math.Max(size ?? filtered.Length, 1);
            var totalPages = filtered.Length == 0 ? 0 : (int)Math.Ceiling(filtered.Length / (double)pageSize);
            var page = filtered.Skip((pageIndex - 1) * pageSize).Take(pageSize).Select(ToResponse).ToArray();
            return Task.FromResult(new PaginatedJobOfferResponse(
                page, pageIndex, totalPages, filtered.Length, pageIndex > 1, pageIndex < totalPages));
        }

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

        private static JobOfferResponse ToResponse(JobOffer offer) => new(
            offer.Id,
            offer.LevelId,
            offer.Title,
            offer.Introduction,
            offer.Description,
            offer.Prerequisites,
            offer.WhatWeOffer,
            offer.Location,
            offer.IsFilled,
            offer.CreatedDate,
            offer.ModifiedDate,
            offer.Level is null
                ? null
                : new JobLevelResponse(
                    offer.Level.Id,
                    offer.Level.Name,
                    offer.Level.Description,
                    offer.Level.CreatedDate,
                    offer.Level.ModifiedDate));
    }
}
