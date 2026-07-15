using Legacy.Maliev.CareerService.Application.Models;

namespace Legacy.Maliev.CareerService.Application.Interfaces;

/// <summary>Provides legacy career operations.</summary>
public interface ICareerService
{
    /// <summary>Returns paginated job offers using the legacy search and sort contract.</summary>
    Task<PaginatedJobOfferResponse> GetPaginatedAsync(
        JobSortType? sort,
        string? search,
        int? index,
        int? size,
        CancellationToken cancellationToken);

    /// <summary>Returns whether at least one job opening is not filled.</summary>
    Task<bool> HasOpenPositionsAsync(CancellationToken cancellationToken);

    /// <summary>Returns a job offer by legacy identifier.</summary>
    Task<JobOfferResponse?> GetOfferByIdAsync(int id, CancellationToken cancellationToken);

    /// <summary>Creates a job offer.</summary>
    Task<JobOfferResponse> CreateOfferAsync(UpsertJobOfferRequest request, CancellationToken cancellationToken);

    /// <summary>Updates a job offer when it exists.</summary>
    Task<bool> UpdateOfferAsync(int id, UpsertJobOfferRequest request, CancellationToken cancellationToken);

    /// <summary>Deletes a job offer when it exists.</summary>
    Task<bool> DeleteOfferAsync(int id, CancellationToken cancellationToken);

    /// <summary>Returns all job levels.</summary>
    Task<IReadOnlyList<JobLevelResponse>> GetLevelsAsync(CancellationToken cancellationToken);

    /// <summary>Returns a job level by legacy identifier.</summary>
    Task<JobLevelResponse?> GetLevelByIdAsync(int id, CancellationToken cancellationToken);

    /// <summary>Creates a job level.</summary>
    Task<JobLevelResponse> CreateLevelAsync(UpsertJobLevelRequest request, CancellationToken cancellationToken);

    /// <summary>Updates a job level when it exists.</summary>
    Task<bool> UpdateLevelAsync(int id, UpsertJobLevelRequest request, CancellationToken cancellationToken);

    /// <summary>Deletes a job level when it exists.</summary>
    Task<bool> DeleteLevelAsync(int id, CancellationToken cancellationToken);
}
