using System.Runtime.ExceptionServices;
using Docker.DotNet;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;

namespace Legacy.Maliev.CareerService.Tests.Controllers;

internal static class CareerFixtureContainerLimits
{
    internal static string Owner { get; } = Environment.GetEnvironmentVariable("MALIEV_TEST_RESOURCE_OWNER") ?? "";

    internal static void RequireFreshAdmission()
    {
        if (!OperatingSystem.IsLinux() || string.IsNullOrWhiteSpace(Owner)
            || Environment.GetEnvironmentVariable("MALIEV_TEST_RESOURCE_OWNER") != Owner)
            throw new InvalidOperationException("Actual Linux Career resource owner admission is required.");
        var line = File.ReadLines("/proc/meminfo").Single(value => value.StartsWith("MemFree:", StringComparison.Ordinal));
        var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 || parts[2] != "kB"
            || !long.TryParse(parts[1], System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var physicalKiB) || physicalKiB < 4194304)
            throw new InvalidOperationException("Fresh 4194304-KiB free physical memory is required.");
    }

    internal static async ValueTask ReleaseAsync(IContainer container, string fixtureId)
    {
        // Undefined objects may never have reached engine creation. Do not invent an ID
        // or call their managed disposal engine-absence proof; the lifetime journal retains that distinction.
        if (container.State != TestcontainersStates.Undefined)
        {
            using var docker = TestcontainersSettings.OS.DockerEndpointAuthConfig.GetDockerClientConfiguration(Guid.NewGuid()).CreateClient();
            using var observationBudget = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            ContainerInspectResponse actual;
            try { actual = await docker.Containers.InspectContainerAsync(container.Id, observationBudget.Token); }
            catch (DockerContainerNotFoundException)
            {
                await container.DisposeAsync();
                return;
            }
            if (actual.ID != container.Id || actual.Created != container.CreatedTime
                || !actual.Config.Labels.TryGetValue("maliev.test.owner", out var owner) || owner != Owner
                || !actual.Config.Labels.TryGetValue("maliev.test.fixture", out var fixture) || fixture != fixtureId
                || !actual.Config.Labels.TryGetValue("maliev.test.persistent-data", out var persistent) || persistent != "false"
                || actual.Mounts.Any(mount => mount.Type != "tmpfs"))
                throw new InvalidOperationException("Exact disposable Career container custody could not be verified.");
        }
        Exception? stopFailure = null;
        using var stopBudget = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try { await container.StopAsync(stopBudget.Token); }
        catch (Exception error) { stopFailure = error; }
        try { await container.DisposeAsync(); }
        catch (Exception cleanup) when (stopFailure is not null)
        {
            throw new AggregateException(stopFailure, cleanup);
        }
        if (stopFailure is not null) ExceptionDispatchInfo.Capture(stopFailure).Throw();
    }

    internal static void Postgres(CreateContainerParameters parameters, string fixtureId)
    {
        Configure(parameters, fixtureId);
        parameters.Entrypoint = ["sh", "-c"];
        parameters.Cmd = ["exec timeout 1200 docker-entrypoint.sh postgres"];
        parameters.HostConfig.Memory = 1024L * 1024 * 1024;
        parameters.HostConfig.MemorySwap = 1024L * 1024 * 1024;
        parameters.HostConfig.NanoCPUs = 750000000;
        parameters.HostConfig.PidsLimit = 128;
        parameters.HostConfig.Tmpfs = new Dictionary<string, string> { ["/var/lib/postgresql"] = "rw,size=512m" };
    }

    internal static void Redis(CreateContainerParameters parameters, string fixtureId)
    {
        Configure(parameters, fixtureId);
        parameters.Cmd = ["sh", "-c", "exec timeout 1200 redis-server --save '' --appendonly no"];
        parameters.HostConfig.Memory = 256L * 1024 * 1024;
        parameters.HostConfig.MemorySwap = 256L * 1024 * 1024;
        parameters.HostConfig.NanoCPUs = 500000000;
        parameters.HostConfig.PidsLimit = 64;
        parameters.HostConfig.Tmpfs = new Dictionary<string, string> { ["/data"] = "rw,size=64m" };
    }

    private static void Configure(CreateContainerParameters parameters, string fixtureId)
    {
        parameters.Labels ??= new Dictionary<string, string>();
        parameters.Labels["maliev.test.owner"] = Owner;
        parameters.Labels["maliev.test.fixture"] = fixtureId;
        parameters.Labels["maliev.test.expires-utc"] = DateTimeOffset.UtcNow.AddMinutes(20).ToString("O");
        parameters.Labels["maliev.test.persistent-data"] = "false";
        foreach (var bindings in parameters.HostConfig.PortBindings.Values)
            foreach (var binding in bindings) binding.HostIP = "127.0.0.1";
    }
}
