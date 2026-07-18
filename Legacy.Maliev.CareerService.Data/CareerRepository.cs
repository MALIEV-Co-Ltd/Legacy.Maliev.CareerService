using Legacy.Maliev.CareerService.Application.Interfaces;
using Legacy.Maliev.CareerService.Domain;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.CareerService.Data;

/// <summary>EF Core implementation of legacy career persistence.</summary>
public sealed class CareerRepository(CareerDbContext dbContext) : ICareerRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<JobOffer>> GetOffersAsync(CancellationToken cancellationToken) =>
        await dbContext.Offers.Include(offer => offer.Level).AsNoTracking().OrderBy(offer => offer.Id).ToArrayAsync(cancellationToken);

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
}
