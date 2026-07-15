namespace Legacy.Maliev.CareerService.Domain;

/// <summary>Represents the legacy job offer record.</summary>
public sealed class JobOffer
{
    /// <summary>Gets or sets the legacy integer identifier.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the level identifier.</summary>
    public int LevelId { get; set; }

    /// <summary>Gets or sets the job title.</summary>
    public string? Title { get; set; }

    /// <summary>Gets or sets the introduction.</summary>
    public string? Introduction { get; set; }

    /// <summary>Gets or sets the description.</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets prerequisites.</summary>
    public string? Prerequisites { get; set; }

    /// <summary>Gets or sets what the company offers.</summary>
    public string? WhatWeOffer { get; set; }

    /// <summary>Gets or sets the work location.</summary>
    public string? Location { get; set; }

    /// <summary>Gets or sets whether the offer is filled.</summary>
    public bool? IsFilled { get; set; }

    /// <summary>Gets or sets the creation timestamp.</summary>
    public DateTime? CreatedDate { get; set; }

    /// <summary>Gets or sets the last modification timestamp.</summary>
    public DateTime? ModifiedDate { get; set; }

    /// <summary>Gets or sets the related level.</summary>
    public JobLevel? Level { get; set; }
}
