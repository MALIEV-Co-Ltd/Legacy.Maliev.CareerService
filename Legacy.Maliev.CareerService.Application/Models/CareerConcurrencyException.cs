namespace Legacy.Maliev.CareerService.Application.Models;

/// <summary>A genuine career persistence concurrency conflict, without provider details in its message.</summary>
public sealed class CareerConcurrencyException : Exception
{
    /// <summary>Preserves the internal cause while exposing only a fixed generic category.</summary>
    public CareerConcurrencyException(Exception innerException)
        : base("The career record changed during this request.", innerException)
    {
    }
}
