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
    [HttpPost]
    [RequirePermission(JobOfferPermissions.LevelsCreate)]
    public async Task<ActionResult> CreateLevelAsync([FromBody] UpsertJobLevelRequest request, CancellationToken cancellationToken)
    {
        var created = await careerService.CreateLevelAsync(request, cancellationToken);
        return CreatedAtRoute("GetLevel", new { levelId = created.Id }, created);
    }

    /// <summary>Deletes a job level.</summary>
    [HttpDelete("{levelId:int}")]
    [RequirePermission(JobOfferPermissions.LevelsDelete)]
    public async Task<ActionResult> DeleteLevelAsync(int levelId, CancellationToken cancellationToken) =>
        await careerService.DeleteLevelAsync(levelId, cancellationToken) ? NoContent() : NotFound();

    /// <summary>Returns one job level.</summary>
    [HttpGet("{levelId:int}", Name = "GetLevel")]
    [AllowAnonymous]
    public async Task<ActionResult<JobLevelResponse>> GetLevelAsync(int levelId, CancellationToken cancellationToken)
    {
        var level = await careerService.GetLevelByIdAsync(levelId, cancellationToken);
        return level is null ? NotFound() : level;
    }

    /// <summary>Returns all job levels.</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<JobLevelResponse>>> GetLevelsAsync(CancellationToken cancellationToken)
    {
        var levels = await careerService.GetLevelsAsync(cancellationToken);
        return levels.Count == 0 ? NotFound() : levels.ToArray();
    }

    /// <summary>Updates a job level.</summary>
    [HttpPut("{levelId:int}")]
    [RequirePermission(JobOfferPermissions.LevelsUpdate)]
    public async Task<ActionResult> UpdateLevelAsync(int levelId, [FromBody] UpsertJobLevelRequest request, CancellationToken cancellationToken) =>
        await careerService.UpdateLevelAsync(levelId, request, cancellationToken) ? NoContent() : NotFound();
}
