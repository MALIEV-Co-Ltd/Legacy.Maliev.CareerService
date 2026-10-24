using Microsoft.Extensions.Logging.Console;

namespace Legacy.Maliev.CareerService.Api.Logging;

/// <summary>Registers Career's private console diagnostics after the shared service defaults.</summary>
public static class CareerDiagnosticLogging
{
    /// <summary>Selects the application-owned formatter and preserves the original console warning filter.</summary>
    /// <param name="builder">The application logging builder.</param>
    /// <returns>The same logging builder.</returns>
    public static ILoggingBuilder AddCareerPrivateDiagnostics(this ILoggingBuilder builder)
    {
        builder.AddConsoleFormatter<CareerDiagnosticConsoleFormatter, ConsoleFormatterOptions>();
        builder.AddConsole(options => options.FormatterName = CareerDiagnosticConsoleFormatter.FormatterName);
        builder.AddFilter<ConsoleLoggerProvider>(null, LogLevel.Warning);
        return builder;
    }
}
