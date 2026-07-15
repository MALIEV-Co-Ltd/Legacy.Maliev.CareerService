using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Legacy.Maliev.CareerService.Data;

/// <summary>Creates the context for explicit design-time migration commands.</summary>
public sealed class CareerDbContextFactory : IDesignTimeDbContextFactory<CareerDbContext>
{
    /// <inheritdoc />
    public CareerDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__CareerDbContext");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings__CareerDbContext is required for design-time migration commands.");
        }

        var options = new DbContextOptionsBuilder<CareerDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new CareerDbContext(options);
    }
}
