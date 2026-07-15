using Legacy.Maliev.CareerService.Api.Authorization;
using Legacy.Maliev.CareerService.Application.Interfaces;
using Legacy.Maliev.CareerService.Application.Models;
using Maliev.Aspire.ServiceDefaults.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Legacy.Maliev.CareerService.Api.Controllers;

/// <summary>Preserves the legacy Jobs HTTP contract during migration.</summary>
[ApiController]
[Route("Jobs")]
[Authorize]
public sealed class JobsController(ICareerService careerService) : ControllerBase
{
    /// <summary>Creates a job offer.</summary>
    [HttpPost]
    [RequirePermission(JobOfferPermissions.JobsCreate)]
    public async Task<ActionResult> CreateOfferAsync([FromBody] UpsertJobOfferRequest request, CancellationToken cancellationToken)
    {
        var created = await careerService.CreateOfferAsync(request, cancellationToken);
        return CreatedAtRoute("GetOffer", new { offerId = created.Id }, created);
    }

    /// <summary>Deletes a job offer.</summary>
    [HttpDelete("{offerId:int}")]
    [RequirePermission(JobOfferPermissions.JobsDelete)]
    public async Task<ActionResult> DeleteOfferAsync(int offerId, CancellationToken cancellationToken) =>
        await careerService.DeleteOfferAsync(offerId, cancellationToken) ? NoContent() : NotFound();

    /// <summary>Returns whether at least one open position exists.</summary>
    [HttpGet("job-opening-status")]
    [AllowAnonymous]
    public Task<bool> GetHasOpenPositionsAsync(CancellationToken cancellationToken) =>
        careerService.HasOpenPositionsAsync(cancellationToken);

    /// <summary>Returns one job offer.</summary>
    [HttpGet("{offerId:int}", Name = "GetOffer")]
    [AllowAnonymous]
    public async Task<ActionResult<JobOfferResponse>> GetOfferAsync(int offerId, CancellationToken cancellationToken)
    {
        var offer = await careerService.GetOfferByIdAsync(offerId, cancellationToken);
        return offer is null ? NotFound() : offer;
    }

    /// <summary>Returns paginated job offers.</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<PaginatedJobOfferResponse>> GetPaginatedAsync(
        [FromQuery] JobSortType? sort,
        [FromQuery] string? search,
        [FromQuery] int? index,
        [FromQuery] int? size,
        CancellationToken cancellationToken)
    {
        var jobs = await careerService.GetPaginatedAsync(sort, search, index, size, cancellationToken);
        return jobs.Items.Count == 0 ? NotFound() : jobs;
    }

    /// <summary>Updates a job offer.</summary>
    [HttpPut("{offerId:int}")]
    [RequirePermission(JobOfferPermissions.JobsUpdate)]
    public async Task<ActionResult> UpdateOfferAsync(int offerId, [FromBody] UpsertJobOfferRequest request, CancellationToken cancellationToken) =>
        await careerService.UpdateOfferAsync(offerId, request, cancellationToken) ? NoContent() : NotFound();
}
