namespace Legacy.Maliev.CareerService.Domain;

/// <summary>Represents the legacy job level record.</summary>
public sealed class JobLevel
{
    /// <summary>Gets or sets the legacy integer identifier.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the level name.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets the level description.</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets the creation timestamp.</summary>
    public DateTime? CreatedDate { get; set; }

    /// <summary>Gets or sets the last modification timestamp.</summary>
    public DateTime? ModifiedDate { get; set; }

    /// <summary>Gets or sets the offers attached to the level.</summary>
    public ICollection<JobOffer>? Offers { get; set; }
}
