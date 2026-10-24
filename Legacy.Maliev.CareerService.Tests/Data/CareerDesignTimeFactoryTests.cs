using System.Data;
using Legacy.Maliev.CareerService.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Legacy.Maliev.CareerService.Tests.Data;

/// <summary>Serializes tests that change design-time environment settings.</summary>
[CollectionDefinition("Career design-time environment", DisableParallelization = true)]
public sealed class CareerDesignTimeEnvironmentCollection;

/// <summary>Checks that design-time contexts require the explicit Career connection setting.</summary>
[Collection("Career design-time environment")]
public sealed class CareerDesignTimeFactoryTests
{
    private const string ConnectionKey = "ConnectionStrings__CareerDbContext";
    private const string SyntheticConnection = "Host=127.0.0.1;Port=1;Database=career_factory_fixture;Username=fixture";

    /// <summary>Checks that missing or blank explicit connection values refuse context creation.</summary>
    /// <param name="value">The null, empty, or whitespace value assigned to ConnectionStrings__CareerDbContext to verify that design-time creation refuses a missing explicit connection.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    public void MissingOrBlankExplicitConnection_RefusesDesignTimeCreation(string? value)
    {
        using var environment = new IsolatedEnvironment(value);
        var exception = Assert.Throws<InvalidOperationException>(() => new CareerDbContextFactory().CreateDbContext([]));
        Assert.Contains(ConnectionKey, exception.Message, StringComparison.Ordinal);
        Assert.Contains("required", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("127.0.0.1", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>Checks explicit Npgsql selection without opening a connection.</summary>
    [Fact]
    public void ExplicitConnection_SelectsNpgsqlWithoutOpeningAnyConnection()
    {
        using var environment = new IsolatedEnvironment(SyntheticConnection);
        using var context = new CareerDbContextFactory().CreateDbContext([]);

        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", context.Database.ProviderName);
        var connection = Assert.IsType<NpgsqlConnection>(context.Database.GetDbConnection());
        Assert.Equal(ConnectionState.Closed, connection.State);
        var settings = new NpgsqlConnectionStringBuilder(connection.ConnectionString);
        Assert.Equal("127.0.0.1", settings.Host);
        Assert.Equal(1, settings.Port);
        Assert.Equal("career_factory_fixture", settings.Database);
        Assert.Equal("fixture", settings.Username);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    /// <summary>Checks that each context creation reads the current explicit setting.</summary>
    [Fact]
    public void FactoryReadsExplicitValueForEachCreation_AndDoesNotReuseEarlierConnection()
    {
        using var environment = new IsolatedEnvironment(SyntheticConnection);
        var factory = new CareerDbContextFactory();
        using var first = factory.CreateDbContext([]);
        environment.Set("Host=127.0.0.1;Port=1;Database=career_factory_second;Username=fixture");
        using var second = factory.CreateDbContext([]);

        Assert.Equal("career_factory_fixture", new NpgsqlConnectionStringBuilder(first.Database.GetDbConnection().ConnectionString).Database);
        Assert.Equal("career_factory_second", new NpgsqlConnectionStringBuilder(second.Database.GetDbConnection().ConnectionString).Database);
        Assert.Equal(ConnectionState.Closed, first.Database.GetDbConnection().State);
        Assert.Equal(ConnectionState.Closed, second.Database.GetDbConnection().State);
    }

    /// <summary>Checks that other connection variables and arguments cannot replace the required setting.</summary>
    [Fact]
    public void OtherConnectionVariablesAndArguments_CannotReplaceTheRequiredExplicitValue()
    {
        using var environment = new IsolatedEnvironment(null);
        Environment.SetEnvironmentVariable("ConnectionStrings__JobOffersContext", SyntheticConnection, EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", SyntheticConnection, EnvironmentVariableTarget.Process);
        Assert.Throws<InvalidOperationException>(() => new CareerDbContextFactory().CreateDbContext(
            ["--connection", SyntheticConnection, "--ConnectionStrings:CareerDbContext=" + SyntheticConnection]));
    }

    /// <summary>Checks that the original Jobs setting cannot substitute for the Career factory setting.</summary>
    [Fact]
    public void SourceJobsConnectionVariable_CannotSubstituteForTheExplicitCareerFactorySetting()
    {
        using var environment = new IsolatedEnvironment(null);
        Environment.SetEnvironmentVariable("ConnectionStrings__JobsDbContext", SyntheticConnection, EnvironmentVariableTarget.Process);

        var exception = Assert.Throws<InvalidOperationException>(() => new CareerDbContextFactory().CreateDbContext([]));
        Assert.Contains(ConnectionKey, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("127.0.0.1", exception.Message, StringComparison.Ordinal);
    }

    private sealed class IsolatedEnvironment : IDisposable
    {
        private static readonly string[] Keys =
            [ConnectionKey, "ConnectionStrings__JobOffersContext", "ConnectionStrings__JobsDbContext", "ConnectionStrings__DefaultConnection"];
        private readonly Dictionary<string, string?> previous = Keys.ToDictionary(key => key,
            key => Environment.GetEnvironmentVariable(key, EnvironmentVariableTarget.Process));

        internal IsolatedEnvironment(string? value)
        {
            foreach (var key in Keys) Environment.SetEnvironmentVariable(key, null, EnvironmentVariableTarget.Process);
            Set(value);
        }

        internal void Set(string? value) =>
            Environment.SetEnvironmentVariable(ConnectionKey, value, EnvironmentVariableTarget.Process);

        public void Dispose()
        {
            foreach (var (key, value) in previous)
                Environment.SetEnvironmentVariable(key, value, EnvironmentVariableTarget.Process);
        }
    }
}
