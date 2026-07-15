using Legacy.Maliev.CareerService.Domain;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.CareerService.Data;

/// <summary>PostgreSQL context preserving the legacy JobOffers schema contract.</summary>
public sealed class CareerDbContext(DbContextOptions<CareerDbContext> options) : DbContext(options)
{
    /// <summary>Gets job offers.</summary>
    public DbSet<JobOffer> Offers => Set<JobOffer>();

    /// <summary>Gets job levels.</summary>
    public DbSet<JobLevel> Levels => Set<JobLevel>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var level = modelBuilder.Entity<JobLevel>();
        level.ToTable("Level");
        level.HasKey(entity => entity.Id);
        level.Property(entity => entity.Id).HasColumnName("ID").ValueGeneratedOnAdd();
        level.Property(entity => entity.Name).HasMaxLength(50).IsRequired();
        level.Property(entity => entity.Description).IsRequired();
        level.Property(entity => entity.CreatedDate).HasColumnType("timestamp with time zone");
        level.Property(entity => entity.ModifiedDate).HasColumnType("timestamp with time zone");
        level.Property<uint>("xmin").HasColumnType("xid").IsRowVersion();

        var offer = modelBuilder.Entity<JobOffer>();
        offer.ToTable("Offer");
        offer.HasKey(entity => entity.Id);
        offer.Property(entity => entity.Id).HasColumnName("ID").ValueGeneratedOnAdd();
        offer.Property(entity => entity.LevelId).HasColumnName("LevelID");
        offer.Property(entity => entity.Title).HasMaxLength(100);
        offer.Property(entity => entity.Introduction);
        offer.Property(entity => entity.Description);
        offer.Property(entity => entity.Prerequisites);
        offer.Property(entity => entity.WhatWeOffer);
        offer.Property(entity => entity.Location).HasMaxLength(100);
        offer.Property(entity => entity.IsFilled);
        offer.Property(entity => entity.CreatedDate).HasColumnType("timestamp with time zone");
        offer.Property(entity => entity.ModifiedDate).HasColumnType("timestamp with time zone");
        offer.Property<uint>("xmin").HasColumnType("xid").IsRowVersion();
        offer.HasOne(entity => entity.Level)
            .WithMany(levelEntity => levelEntity.Offers)
            .HasForeignKey(entity => entity.LevelId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_Offers_Level");
    }
}
