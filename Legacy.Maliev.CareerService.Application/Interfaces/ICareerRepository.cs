using Legacy.Maliev.CareerService.Domain;

namespace Legacy.Maliev.CareerService.Application.Interfaces;

/// <summary>Persists legacy career records.</summary>
public interface ICareerRepository
{
    /// <summary>Returns all job offers without tracking.</summary>
    Task<IReadOnlyList<JobOffer>> GetOffersAsync(CancellationToken cancellationToken);

    /// <summary>Returns one offer without tracking for read-only use.</summary>
    Task<JobOffer?> GetOfferByIdAsync(int id, CancellationToken cancellationToken);

    /// <summary>Returns one tracked offer for update or deletion.</summary>
    Task<JobOffer?> GetOfferByIdForUpdateAsync(int id, CancellationToken cancellationToken) =>
        GetOfferByIdAsync(id, cancellationToken);

    /// <summary>Returns whether at least one offer is open.</summary>
    Task<bool> HasOpenPositionsAsync(CancellationToken cancellationToken);

    /// <summary>Adds and saves an offer.</summary>
    Task AddOfferAsync(JobOffer offer, CancellationToken cancellationToken);

    /// <summary>Saves changes to an offer.</summary>
    Task UpdateOfferAsync(JobOffer offer, CancellationToken cancellationToken);

    /// <summary>Deletes and saves an offer.</summary>
    Task DeleteOfferAsync(JobOffer offer, CancellationToken cancellationToken);

    /// <summary>Returns all levels.</summary>
    Task<IReadOnlyList<JobLevel>> GetLevelsAsync(CancellationToken cancellationToken);

    /// <summary>Returns one level without tracking for read-only use.</summary>
    Task<JobLevel?> GetLevelByIdAsync(int id, CancellationToken cancellationToken);

    /// <summary>Returns one tracked level for update or deletion.</summary>
    Task<JobLevel?> GetLevelByIdForUpdateAsync(int id, CancellationToken cancellationToken) =>
        GetLevelByIdAsync(id, cancellationToken);

    /// <summary>Adds and saves a level.</summary>
    Task AddLevelAsync(JobLevel level, CancellationToken cancellationToken);

    /// <summary>Saves changes to a level.</summary>
    Task UpdateLevelAsync(JobLevel level, CancellationToken cancellationToken);

    /// <summary>Deletes and saves a level.</summary>
    Task DeleteLevelAsync(JobLevel level, CancellationToken cancellationToken);
}
