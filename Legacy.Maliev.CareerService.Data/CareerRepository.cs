using Legacy.Maliev.CareerService.Application.Interfaces;
using Legacy.Maliev.CareerService.Application.Models;
using Legacy.Maliev.CareerService.Domain;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.CareerService.Data;

/// <summary>EF Core implementation of legacy career persistence.</summary>
public sealed class CareerRepository(CareerDbContext dbContext) : ICareerRepository
{
    /// <inheritdoc />
    public async Task<PaginatedJobOfferResponse> GetPaginatedOffersAsync(
        JobSortType? sort,
        string? search,
        int? index,
        int? size,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Offers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var isNumeric = int.TryParse(search, out var searchAsInteger);
            var pattern = $"%{EscapeLikePattern(search)}%";
            query = query.Where(offer =>
                (isNumeric && offer.Id == searchAsInteger) ||
                (offer.Title != null && EF.Functions.ILike(offer.Title, pattern, "\\")) ||
                (offer.Introduction != null && EF.Functions.ILike(offer.Introduction, pattern, "\\")) ||
                (offer.WhatWeOffer != null && EF.Functions.ILike(offer.WhatWeOffer, pattern, "\\")) ||
                (offer.Location != null && EF.Functions.ILike(offer.Location, pattern, "\\")) ||
                (offer.Description != null && EF.Functions.ILike(offer.Description, pattern, "\\")));
        }

        var totalItems = await query.CountAsync(cancellationToken);
        var pageIndex = Math.Max(index ?? 1, 1);
        var pageSize = Math.Max(size ?? totalItems, 1);
        var totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);
        query = sort switch
        {
            JobSortType.JobId_Descending => query.OrderByDescending(offer => offer.Id),
            JobSortType.JobCreatedDate_Ascending => query.OrderBy(offer => offer.CreatedDate),
            JobSortType.JobCreatedDate_Descending => query.OrderByDescending(offer => offer.CreatedDate),
            _ => query.OrderBy(offer => offer.Id),
        };
        var items = await query
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(offer => new JobOfferResponse(
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
                offer.Level == null
                    ? null
                    : new JobLevelResponse(
                        offer.Level.Id,
                        offer.Level.Name,
                        offer.Level.Description,
                        offer.Level.CreatedDate,
                        offer.Level.ModifiedDate)))
            .ToArrayAsync(cancellationToken);
        return new PaginatedJobOfferResponse(
            items,
            pageIndex,
            totalPages,
            totalItems,
            pageIndex > 1,
            pageIndex < totalPages);
    }

    /// <inheritdoc />
    public Task<JobOffer?> GetOfferByIdAsync(int id, CancellationToken cancellationToken) =>
        dbContext.Offers.Include(offer => offer.Level).AsNoTracking()
            .SingleOrDefaultAsync(offer => offer.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<JobOffer?> GetOfferByIdForUpdateAsync(int id, CancellationToken cancellationToken) =>
        dbContext.Offers.Include(offer => offer.Level)
            .SingleOrDefaultAsync(offer => offer.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<bool> HasOpenPositionsAsync(CancellationToken cancellationToken) =>
        dbContext.Offers.AnyAsync(offer => offer.IsFilled == false, cancellationToken);

    /// <inheritdoc />
    public async Task AddOfferAsync(JobOffer offer, CancellationToken cancellationToken)
    {
        await dbContext.Offers.AddAsync(offer, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateOfferAsync(JobOffer offer, CancellationToken cancellationToken)
    {
        dbContext.Offers.Update(offer);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteOfferAsync(JobOffer offer, CancellationToken cancellationToken)
    {
        dbContext.Offers.Remove(offer);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JobLevel>> GetLevelsAsync(CancellationToken cancellationToken) =>
        await dbContext.Levels.AsNoTracking().OrderBy(level => level.Id).ToArrayAsync(cancellationToken);

    /// <inheritdoc />
    public Task<JobLevel?> GetLevelByIdAsync(int id, CancellationToken cancellationToken) =>
        dbContext.Levels.AsNoTracking()
            .SingleOrDefaultAsync(level => level.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<JobLevel?> GetLevelByIdForUpdateAsync(int id, CancellationToken cancellationToken) =>
        dbContext.Levels.SingleOrDefaultAsync(level => level.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task AddLevelAsync(JobLevel level, CancellationToken cancellationToken)
    {
        await dbContext.Levels.AddAsync(level, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateLevelAsync(JobLevel level, CancellationToken cancellationToken)
    {
        dbContext.Levels.Update(level);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteLevelAsync(JobLevel level, CancellationToken cancellationToken)
    {
        dbContext.Levels.Remove(level);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string EscapeLikePattern(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
