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
    /// <param name="request">The job offer details and existing level identifier.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <response code="201">The created job offer, with its service-assigned identifier.</response>
    [HttpPost]
    [RequirePermission(JobOfferPermissions.JobsCreate)]
    [ProducesResponseType<JobOfferResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult> CreateOfferAsync([FromBody] UpsertJobOfferRequest request, CancellationToken cancellationToken)
    {
        var created = await careerService.CreateOfferAsync(request, cancellationToken);
        return CreatedAtRoute("GetOffer", new { offerId = created.Id }, created);
    }

    /// <summary>Deletes a job offer.</summary>
    /// <param name="offerId" example="42">The identifier of the job offer to delete.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <response code="204">The job offer was deleted.</response>
    /// <response code="404">The job offer does not exist.</response>
    /// <response code="409">The career record changed during this request.</response>
    [HttpDelete("{offerId:int}")]
    [RequirePermission(JobOfferPermissions.JobsDelete, RequireLiveCheck = true, IsCritical = true)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> DeleteOfferAsync(int offerId, CancellationToken cancellationToken)
    {
        try { return await careerService.DeleteOfferAsync(offerId, cancellationToken) ? NoContent() : NotFound(); }
        catch (CareerConcurrencyException) { return Conflict("The career record changed during this request."); }
    }

    /// <summary>Returns whether at least one open position exists.</summary>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet("job-opening-status")]
    [AllowAnonymous]
    public Task<bool> GetHasOpenPositionsAsync(CancellationToken cancellationToken) =>
        careerService.HasOpenPositionsAsync(cancellationToken);

    /// <summary>Returns one job offer.</summary>
    /// <param name="offerId" example="42">The identifier of the job offer to retrieve.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet("{offerId:int}", Name = "GetOffer")]
    [AllowAnonymous]
    public async Task<ActionResult<JobOfferResponse>> GetOfferAsync(int offerId, CancellationToken cancellationToken)
    {
        var offer = await careerService.GetOfferByIdAsync(offerId, cancellationToken);
        return offer is null ? NotFound() : offer;
    }

    /// <summary>Returns paginated job offers.</summary>
    /// <param name="sort" example="0">The legacy job-offer sort value.</param>
    /// <param name="search" example="engineer">Text to search in the job offer fields.</param>
    /// <param name="index" example="1">The one-based page index; omitted values use the existing default.</param>
    /// <param name="size" example="10">The page size; omitted values use the existing default.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
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
    /// <param name="offerId" example="42">The identifier of the job offer to update.</param>
    /// <param name="request">The replacement job offer details.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <response code="204">The job offer was updated.</response>
    /// <response code="404">The job offer does not exist.</response>
    /// <response code="409">The career record changed during this request.</response>
    [HttpPut("{offerId:int}")]
    [RequirePermission(JobOfferPermissions.JobsUpdate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> UpdateOfferAsync(int offerId, [FromBody] UpsertJobOfferRequest request, CancellationToken cancellationToken)
    {
        try { return await careerService.UpdateOfferAsync(offerId, request, cancellationToken) ? NoContent() : NotFound(); }
        catch (CareerConcurrencyException) { return Conflict("The career record changed during this request."); }
    }
}
