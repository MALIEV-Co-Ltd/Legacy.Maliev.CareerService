using Legacy.Maliev.CareerService.Api.Authorization;
using Legacy.Maliev.CareerService.Application.Interfaces;
using Legacy.Maliev.CareerService.Application.Models;
using Maliev.Aspire.ServiceDefaults.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Legacy.Maliev.CareerService.Api.Controllers;

/// <summary>Preserves the legacy jobs/levels HTTP contract during migration.</summary>
[ApiController]
[Route("jobs/[controller]")]
[Authorize]
public sealed class LevelsController(ICareerService careerService) : ControllerBase
{
    /// <summary>Creates a job level.</summary>
    /// <param name="request">The job level name and description.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <response code="201">The created job level, with its service-assigned identifier.</response>
    [HttpPost]
    [RequirePermission(JobOfferPermissions.LevelsCreate)]
    [ProducesResponseType<JobLevelResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult> CreateLevelAsync([FromBody] UpsertJobLevelRequest request, CancellationToken cancellationToken)
    {
        var created = await careerService.CreateLevelAsync(request, cancellationToken);
        return CreatedAtRoute("GetLevel", new { levelId = created.Id }, created);
    }

    /// <summary>Deletes a job level.</summary>
    /// <param name="levelId" example="42">The identifier of the job level to delete.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <response code="204">The job level was deleted.</response>
    /// <response code="404">The job level does not exist.</response>
    /// <response code="409">The career record changed during this request.</response>
    [HttpDelete("{levelId:int}")]
    [RequirePermission(JobOfferPermissions.LevelsDelete, RequireLiveCheck = true, IsCritical = true)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> DeleteLevelAsync(int levelId, CancellationToken cancellationToken)
    {
        try { return await careerService.DeleteLevelAsync(levelId, cancellationToken) ? NoContent() : NotFound(); }
        catch (CareerConcurrencyException) { return Conflict("The career record changed during this request."); }
    }

    /// <summary>Returns one job level.</summary>
    /// <param name="levelId" example="42">The identifier of the job level to retrieve.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet("{levelId:int}", Name = "GetLevel")]
    [AllowAnonymous]
    public async Task<ActionResult<JobLevelResponse>> GetLevelAsync(int levelId, CancellationToken cancellationToken)
    {
        var level = await careerService.GetLevelByIdAsync(levelId, cancellationToken);
        return level is null ? NotFound() : level;
    }

    /// <summary>Returns all job levels.</summary>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<JobLevelResponse>>> GetLevelsAsync(CancellationToken cancellationToken)
    {
        var levels = await careerService.GetLevelsAsync(cancellationToken);
        return levels.Count == 0 ? NotFound() : levels.ToArray();
    }

    /// <summary>Updates a job level.</summary>
    /// <param name="levelId" example="42">The identifier of the job level to update.</param>
    /// <param name="request">The replacement job level name and description.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <response code="204">The job level was updated.</response>
    /// <response code="404">The job level does not exist.</response>
    /// <response code="409">The career record changed during this request.</response>
    [HttpPut("{levelId:int}")]
    [RequirePermission(JobOfferPermissions.LevelsUpdate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> UpdateLevelAsync(int levelId, [FromBody] UpsertJobLevelRequest request, CancellationToken cancellationToken)
    {
        try { return await careerService.UpdateLevelAsync(levelId, request, cancellationToken) ? NoContent() : NotFound(); }
        catch (CareerConcurrencyException) { return Conflict("The career record changed during this request."); }
    }
}
