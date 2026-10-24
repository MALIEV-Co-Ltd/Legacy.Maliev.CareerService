using System.Net;
using Docker.DotNet;
using DotNet.Testcontainers.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.CareerService.Tests.Controllers;

/// <summary>Checks actual contained providers and exact engine absence on normal and failed startup.</summary>
[Collection("Career shared level")]
public sealed class CareerRouteFixtureNativeResourceTests
{
    /// <summary>Starts real providers and verifies their limits, database connectivity, and exact cleanup.</summary>
    /// <returns>A task completing native assertions and owned cleanup.</returns>
    [Fact]
    public async Task ActualAlpineProvidersAndPg18Tmpfs_StartMigrateAndDisappearAfterDisposal()
    {
        var fixture = new CareerRouteFixture();
        using var docker = TestcontainersSettings.OS.DockerEndpointAuthConfig.GetDockerClientConfiguration(Guid.NewGuid()).CreateClient();
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(180));
        string? postgresId = null;
        string? redisId = null;
        Exception? observedFailure = null;
        try
        {
            await fixture.InitializeAsync().WaitAsync(budget.Token);
            postgresId = fixture.PostgresResource.Id;
            redisId = fixture.RedisResource.Id;
            await AssertContainedAsync(docker, postgresId, 1073741824, 750000000, 128, "/var/lib/postgresql", budget.Token);
            await AssertContainedAsync(docker, redisId, 268435456, 500000000, 64, "/data", budget.Token);
            await using var db = fixture.Context();
            Assert.True(await db.Database.CanConnectAsync(budget.Token));
        }
        catch (Exception error)
        {
            observedFailure = error;
            throw;
        }
        finally
        {
            await DisposeRetainingPrimaryAsync(fixture, observedFailure);
        }

        await AssertAbsentAsync(docker, postgresId!, budget.Token);
        await AssertAbsentAsync(docker, redisId!, budget.Token);
    }

    /// <summary>Preserves a failure after both real providers start and proves neither remains.</summary>
    /// <returns>A task completing failure identity and native cleanup assertions.</returns>
    [Fact]
    public async Task ActualSecondProviderAcquiredThenStartupFails_BothExactIdsAreAbsent()
    {
        using var docker = TestcontainersSettings.OS.DockerEndpointAuthConfig.GetDockerClientConfiguration(Guid.NewGuid()).CreateClient();
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(180));
        var primary = new InvalidOperationException("Injected failure after actual second-provider acquisition.");
        string? postgresId = null;
        string? redisId = null;
        Exception? observedFailure = null;
        CareerRouteFixture fixture = null!;
        fixture = new CareerRouteFixture(async cancellation =>
        {
            postgresId = fixture.PostgresResource.Id;
            redisId = fixture.RedisResource.Id;
            await AssertContainedAsync(docker, postgresId, 1073741824, 750000000, 128, "/var/lib/postgresql", cancellation);
            await AssertContainedAsync(docker, redisId, 268435456, 500000000, 64, "/data", cancellation);
            throw primary;
        });
        try
        {
            var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.InitializeAsync().WaitAsync(budget.Token));
            Assert.Same(primary, actual);
            await AssertAbsentAsync(docker, postgresId!, budget.Token);
            await AssertAbsentAsync(docker, redisId!, budget.Token);
        }
        catch (Exception error)
        {
            observedFailure = error;
            throw;
        }
        finally
        {
            await DisposeRetainingPrimaryAsync(fixture, observedFailure);
        }
    }

    private static async Task DisposeRetainingPrimaryAsync(CareerRouteFixture fixture, Exception? primary)
    {
        try { await fixture.DisposeAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
        catch (Exception cleanup) when (primary is not null)
        {
            throw new AggregateException(primary, cleanup);
        }
    }

    private static async Task AssertContainedAsync(IDockerClient docker, string id, long memory, long cpu, long pids, string tmpfs, CancellationToken cancellation)
    {
        Assert.Matches("^[0-9a-f]{64}$", id);
        var actual = await docker.Containers.InspectContainerAsync(id, cancellation);
        Assert.Equal(id, actual.ID);
        Assert.True(actual.State.Running);
        Assert.Equal(memory, actual.HostConfig.Memory);
        Assert.Equal(memory, actual.HostConfig.MemorySwap);
        Assert.Equal(cpu, actual.HostConfig.NanoCPUs);
        Assert.Equal(pids, actual.HostConfig.PidsLimit);
        Assert.Single(actual.HostConfig.Tmpfs);
        Assert.Equal(tmpfs == "/data" ? "rw,size=64m" : "rw,size=512m", actual.HostConfig.Tmpfs[tmpfs]);
        foreach (var bindings in actual.HostConfig.PortBindings.Values)
            foreach (var binding in bindings) Assert.Equal("127.0.0.1", binding.HostIP);
        Assert.Equal(CareerFixtureContainerLimits.Owner, actual.Config.Labels["maliev.test.owner"]);
        Assert.Matches("^[0-9a-f]{32}$", actual.Config.Labels["maliev.test.fixture"]);
        Assert.Equal("false", actual.Config.Labels["maliev.test.persistent-data"]);
        var expiry = DateTimeOffset.Parse(actual.Config.Labels["maliev.test.expires-utc"], System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(expiry - DateTimeOffset.UtcNow, TimeSpan.Zero, TimeSpan.FromMinutes(20));
        Assert.Contains("timeout 1200", string.Join(" ", actual.Config.Cmd), StringComparison.Ordinal);
        Assert.DoesNotContain(actual.Mounts, mount => mount.Type != "tmpfs");
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
        {
            ProviderId = actual.ID,
            actual.Image,
            actual.Created,
            Owner = actual.Config.Labels["maliev.test.owner"],
            Fixture = actual.Config.Labels["maliev.test.fixture"],
            actual.HostConfig.Memory,
            actual.HostConfig.MemorySwap,
            actual.HostConfig.NanoCPUs,
            actual.HostConfig.PidsLimit,
            LoopbackOnly = true,
            Tmpfs = tmpfs
        }));
    }

    private static async Task AssertAbsentAsync(IDockerClient docker, string id, CancellationToken cancellation)
    {
        Assert.Matches("^[0-9a-f]{64}$", id);
        var actual = await Assert.ThrowsAsync<DockerContainerNotFoundException>(() => docker.Containers.InspectContainerAsync(id, cancellation));
        Assert.Equal(HttpStatusCode.NotFound, actual.StatusCode);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { ProviderId = id, VerifiedAbsent = true }));
    }
}
