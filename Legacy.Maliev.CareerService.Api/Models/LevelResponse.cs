using Legacy.Maliev.CareerService.Application.Models;

namespace Legacy.Maliev.CareerService.Api.Models;

/// <summary>Preserves the original direct level endpoint response.</summary>
public sealed record LevelResponse(
    int Id,
    string? Name,
    string? Description,
    DateTime? CreatedDate,
    DateTime? ModifiedDate)
{
    /// <summary>Gets the original initialized collection from an unloaded level.</summary>
    public IReadOnlyList<JobOfferResponse> Offers { get; } = [];
}
