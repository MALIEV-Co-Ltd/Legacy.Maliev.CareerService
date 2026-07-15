namespace Legacy.Maliev.CareerService.Application.Models;

/// <summary>Legacy-compatible job sort values.</summary>
public enum JobSortType
{
    /// <summary>Sort by identifier ascending.</summary>
    JobId_Ascending,

    /// <summary>Sort by identifier descending.</summary>
    JobId_Descending,

    /// <summary>Sort by created date ascending.</summary>
    JobCreatedDate_Ascending,

    /// <summary>Sort by created date descending.</summary>
    JobCreatedDate_Descending,
}

/// <summary>Legacy-compatible level response.</summary>
public sealed record JobLevelResponse(
    int Id,
    string? Name,
    string? Description,
    DateTime? CreatedDate,
    DateTime? ModifiedDate);

/// <summary>Legacy-compatible offer response.</summary>
public sealed record JobOfferResponse(
    int Id,
    int LevelId,
    string? Title,
    string? Introduction,
    string? Description,
    string? Prerequisites,
    string? WhatWeOffer,
    string? Location,
    bool? IsFilled,
    DateTime? CreatedDate,
    DateTime? ModifiedDate,
    JobLevelResponse? Level);

/// <summary>Legacy-compatible paginated Web API response shape.</summary>
public sealed record PaginatedJobOfferResponse(
    IReadOnlyList<JobOfferResponse> Items,
    int PageIndex,
    int TotalPages,
    int TotalItems,
    bool HasPreviousPage,
    bool HasNextPage);

/// <summary>Legacy-compatible job offer create/update payload.</summary>
public sealed record UpsertJobOfferRequest(
    int LevelId,
    string? Title,
    string? Introduction,
    string? Description,
    string? Prerequisites,
    string? WhatWeOffer,
    string? Location,
    bool? IsFilled);

/// <summary>Legacy-compatible level create/update payload.</summary>
public sealed record UpsertJobLevelRequest(
    string? Name,
    string? Description);
