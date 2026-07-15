using Legacy.Maliev.CareerService.Application.Interfaces;
using Legacy.Maliev.CareerService.Application.Models;
using Legacy.Maliev.CareerService.Domain;

namespace Legacy.Maliev.CareerService.Application.Services;

/// <summary>Implements legacy-compatible career behavior.</summary>
public sealed class CareerApplicationService(ICareerRepository repository, TimeProvider timeProvider) : ICareerService
{
    /// <inheritdoc />
    public async Task<PaginatedJobOfferResponse> GetPaginatedAsync(JobSortType? sort, string? search, int? index, int? size, CancellationToken cancellationToken)
    {
        var query = ApplySearch(await repository.GetOffersAsync(cancellationToken), search);
        query = ApplySort(query, sort);
        var totalItems = query.Count;
        var pageIndex = Math.Max(index ?? 1, 1);
        var pageSize = Math.Max(size ?? totalItems, 1);
        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);
        var items = query.Skip((pageIndex - 1) * pageSize).Take(pageSize).Select(ToResponse).ToArray();
        return new PaginatedJobOfferResponse(items, pageIndex, totalPages, totalItems, pageIndex > 1, pageIndex < totalPages);
    }

    /// <inheritdoc />
    public Task<bool> HasOpenPositionsAsync(CancellationToken cancellationToken) => repository.HasOpenPositionsAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<JobOfferResponse?> GetOfferByIdAsync(int id, CancellationToken cancellationToken) =>
        (await repository.GetOfferByIdAsync(id, cancellationToken)) is { } offer ? ToResponse(offer) : null;

    /// <inheritdoc />
    public async Task<JobOfferResponse> CreateOfferAsync(UpsertJobOfferRequest request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var offer = new JobOffer
        {
            LevelId = request.LevelId,
            Title = request.Title,
            Introduction = request.Introduction,
            Description = request.Description,
            Prerequisites = request.Prerequisites,
            WhatWeOffer = request.WhatWeOffer,
            Location = request.Location,
            IsFilled = request.IsFilled,
            CreatedDate = now,
            ModifiedDate = now,
        };
        await repository.AddOfferAsync(offer, cancellationToken);
        return ToResponse(offer);
    }

    /// <inheritdoc />
    public async Task<bool> UpdateOfferAsync(int id, UpsertJobOfferRequest request, CancellationToken cancellationToken)
    {
        var offer = await repository.GetOfferByIdAsync(id, cancellationToken);
        if (offer is null)
        {
            return false;
        }

        offer.LevelId = request.LevelId;
        offer.Title = request.Title;
        offer.Introduction = request.Introduction;
        offer.Description = request.Description;
        offer.Prerequisites = request.Prerequisites;
        offer.WhatWeOffer = request.WhatWeOffer;
        offer.Location = request.Location;
        offer.IsFilled = request.IsFilled;
        offer.ModifiedDate = timeProvider.GetUtcNow().UtcDateTime;
        await repository.UpdateOfferAsync(offer, cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteOfferAsync(int id, CancellationToken cancellationToken)
    {
        var offer = await repository.GetOfferByIdAsync(id, cancellationToken);
        if (offer is null)
        {
            return false;
        }

        await repository.DeleteOfferAsync(offer, cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JobLevelResponse>> GetLevelsAsync(CancellationToken cancellationToken) =>
        (await repository.GetLevelsAsync(cancellationToken)).Select(ToResponse).ToArray();

    /// <inheritdoc />
    public async Task<JobLevelResponse?> GetLevelByIdAsync(int id, CancellationToken cancellationToken) =>
        (await repository.GetLevelByIdAsync(id, cancellationToken)) is { } level ? ToResponse(level) : null;

    /// <inheritdoc />
    public async Task<JobLevelResponse> CreateLevelAsync(UpsertJobLevelRequest request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var level = new JobLevel { Name = request.Name, Description = request.Description, CreatedDate = now, ModifiedDate = now };
        await repository.AddLevelAsync(level, cancellationToken);
        return ToResponse(level);
    }

    /// <inheritdoc />
    public async Task<bool> UpdateLevelAsync(int id, UpsertJobLevelRequest request, CancellationToken cancellationToken)
    {
        var level = await repository.GetLevelByIdAsync(id, cancellationToken);
        if (level is null)
        {
            return false;
        }

        level.Name = request.Name;
        level.Description = request.Description;
        level.ModifiedDate = timeProvider.GetUtcNow().UtcDateTime;
        await repository.UpdateLevelAsync(level, cancellationToken);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteLevelAsync(int id, CancellationToken cancellationToken)
    {
        var level = await repository.GetLevelByIdAsync(id, cancellationToken);
        if (level is null)
        {
            return false;
        }

        await repository.DeleteLevelAsync(level, cancellationToken);
        return true;
    }

    private static IReadOnlyList<JobOffer> ApplySearch(IReadOnlyList<JobOffer> offers, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return offers;
        }

        var normalized = search.ToLowerInvariant();
        var isNumeric = int.TryParse(normalized, out var searchAsInteger);
        return offers.Where(offer =>
            (isNumeric && offer.Id == searchAsInteger) ||
            Contains(offer.Title, normalized) ||
            Contains(offer.Introduction, normalized) ||
            Contains(offer.WhatWeOffer, normalized) ||
            Contains(offer.Location, normalized) ||
            Contains(offer.Description, normalized)).ToArray();
    }

    private static IReadOnlyList<JobOffer> ApplySort(IReadOnlyList<JobOffer> offers, JobSortType? sort) =>
        (sort switch
        {
            JobSortType.JobId_Descending => offers.OrderByDescending(offer => offer.Id),
            JobSortType.JobCreatedDate_Ascending => offers.OrderBy(offer => offer.CreatedDate),
            JobSortType.JobCreatedDate_Descending => offers.OrderByDescending(offer => offer.CreatedDate),
            _ => offers.OrderBy(offer => offer.Id),
        }).ToArray();

    private static bool Contains(string? value, string search) =>
        value?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;

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
        offer.Level is null ? null : ToResponse(offer.Level));

    private static JobLevelResponse ToResponse(JobLevel level) => new(
        level.Id,
        level.Name,
        level.Description,
        level.CreatedDate,
        level.ModifiedDate);
}
