namespace Legacy.Maliev.CareerService.Api.Authorization;

/// <summary>Permissions for mutating temporary legacy JobOffer data.</summary>
public static class JobOfferPermissions
{
    /// <summary>Read one protected JobOffer record.</summary>
    public const string JobsRead = "legacy-career.jobs.read";

    /// <summary>Create JobOffer records.</summary>
    public const string JobsCreate = "legacy-career.jobs.create";

    /// <summary>Update JobOffer records.</summary>
    public const string JobsUpdate = "legacy-career.jobs.update";

    /// <summary>Delete JobOffer records.</summary>
    public const string JobsDelete = "legacy-career.jobs.delete";

    /// <summary>Create job level records.</summary>
    public const string LevelsCreate = "legacy-career.levels.create";

    /// <summary>Update job level records.</summary>
    public const string LevelsUpdate = "legacy-career.levels.update";

    /// <summary>Delete job level records.</summary>
    public const string LevelsDelete = "legacy-career.levels.delete";
}
