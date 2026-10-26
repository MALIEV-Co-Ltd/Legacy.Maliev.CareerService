using System.Text.Json;
using Docker.DotNet;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Testcontainers.PostgreSql;

namespace Legacy.Maliev.CareerService.Tests.Controllers;

internal static class CareerOwnedPostgresCase
{
    internal static async Task RunAsync(string family, Func<PostgreSqlContainer, CancellationToken, Task> body)
    {
        var lifetime = new CareerFixtureLifetime(family);
        var receipt = new Dictionary<string, object?>
        {
            ["owner"] = CareerFixtureContainerLimits.Owner,
            ["fixtureId"] = lifetime.FixtureId,
            ["family"] = family,
            ["sourceHead"] = Environment.GetEnvironmentVariable("GITHUB_SHA"),
            ["persistentData"] = false,
            ["engineAbsent"] = false,
        };
        Exception? primary = null;
        Exception? cleanupFailure = null;
        try
        {
            var postgres = lifetime.Create("postgres", 4,
                () => new PostgreSqlBuilder("postgres:18-alpine")
                    .WithLabel("com.maliev.career.fixture", lifetime.FixtureId)
                    .WithCreateParameterModifier(parameters => CareerFixtureContainerLimits.Postgres(parameters, lifetime.FixtureId)).Build(),
                async value =>
                {
                    var id = value.State == TestcontainersStates.Undefined ? null : value.Id;
                    receipt["containerId"] = id;
                    await CareerFixtureContainerLimits.ReleaseAsync(value, lifetime.FixtureId);
                    if (id is null) return;
                    using var docker = TestcontainersSettings.OS.DockerEndpointAuthConfig.GetDockerClientConfiguration(Guid.NewGuid()).CreateClient();
                    using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    try { await docker.Containers.InspectContainerAsync(id, budget.Token); }
                    catch (DockerContainerNotFoundException) { receipt["engineAbsent"] = true; return; }
                    throw new InvalidOperationException("The exact owned PostgreSQL container remains after cleanup.");
                }, value => CareerFixtureLifetime.ObserveContainer(value, "postgres:18-alpine"));
            await lifetime.RunAsync("case", async cancellation =>
            {
                await lifetime.RunAsync("postgres-start", token =>
                {
                    CareerFixtureContainerLimits.RequireFreshAdmission();
                    return postgres.StartAsync(token);
                }, TimeSpan.FromSeconds(120), cancellation);
                lifetime.ObserveOwned(postgres);
                using var docker = TestcontainersSettings.OS.DockerEndpointAuthConfig.GetDockerClientConfiguration(Guid.NewGuid()).CreateClient();
                using var inspection = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                inspection.CancelAfter(TimeSpan.FromSeconds(10));
                var actual = await docker.Containers.InspectContainerAsync(postgres.Id, inspection.Token);
                if (actual.ID != postgres.Id || actual.Created != postgres.CreatedTime || !actual.State.Running
                    || actual.HostConfig.Memory != 1073741824 || actual.HostConfig.MemorySwap != 1073741824
                    || actual.HostConfig.NanoCPUs != 750000000 || actual.HostConfig.PidsLimit != 128
                    || actual.HostConfig.Tmpfs.Count != 1 || !actual.HostConfig.Tmpfs.TryGetValue("/var/lib/postgresql", out var tmpfs)
                    || tmpfs != "rw,size=512m" || actual.Mounts.Any(mount => mount.Type != "tmpfs")
                    || actual.HostConfig.PortBindings.Values.SelectMany(bindings => bindings).Any(binding => binding.HostIP != "127.0.0.1")
                    || actual.Config.Labels["maliev.test.owner"] != CareerFixtureContainerLimits.Owner
                    || actual.Config.Labels["maliev.test.fixture"] != lifetime.FixtureId
                    || actual.Config.Labels["maliev.test.persistent-data"] != "false"
                    || !actual.Config.Entrypoint.SequenceEqual(new[] { "sh", "-c" }, StringComparer.Ordinal)
                    || !actual.Config.Cmd.SequenceEqual(new[] { "exec timeout 1200 docker-entrypoint.sh postgres" }, StringComparer.Ordinal)
                    || !DateTimeOffset.TryParseExact(actual.Config.Labels["maliev.test.expires-utc"], "O",
                        System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var expires)
                    || expires <= DateTimeOffset.UtcNow || expires > DateTimeOffset.UtcNow.AddMinutes(20.5))
                    throw new InvalidOperationException("Actual PostgreSQL limits or disposable ownership differ from the case contract.");
                receipt["containerId"] = actual.ID;
                receipt["createdUtc"] = actual.Created.ToUniversalTime().ToString("O");
                receipt["startedUtc"] = postgres.StartedTime.ToUniversalTime().ToString("O");
                receipt["declaredImage"] = "postgres:18-alpine";
                receipt["mappedPort"] = postgres.GetMappedPublicPort(5432);
                receipt["memoryBytes"] = actual.HostConfig.Memory;
                receipt["nanoCpus"] = actual.HostConfig.NanoCPUs;
                receipt["pids"] = actual.HostConfig.PidsLimit;
                receipt["tmpfs"] = tmpfs;
                receipt["expiresUtc"] = actual.Config.Labels["maliev.test.expires-utc"];
                await body(postgres, cancellation);
            }, TimeSpan.FromMinutes(4));
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            try { await lifetime.DisposeAsync(); }
            catch (Exception error)
            {
                cleanupFailure = error;
                receipt["cleanupFailureType"] = error.GetType().Name;
                if (primary is null) throw;
                primary.Data["CareerPostgresCleanupFailureType"] = error.GetType().Name;
            }
            finally
            {
                receipt["observedUtc"] = DateTimeOffset.UtcNow.ToString("O");
                receipt["failureType"] = primary?.GetType().Name;
                try
                {
                    var directory = Environment.GetEnvironmentVariable("VSTestResultsDirectory") ?? Path.Combine(Environment.CurrentDirectory, "runner-results");
                    Directory.CreateDirectory(directory);
                    await File.WriteAllTextAsync(Path.Combine(directory, "career-postgres-case-" + lifetime.FixtureId + ".json"), JsonSerializer.Serialize(receipt));
                }
                catch (Exception error) when (primary is not null || cleanupFailure is not null)
                {
                    (primary ?? cleanupFailure)!.Data["CareerPostgresEvidenceFailureType"] = error.GetType().Name;
                }
            }
        }
    }
}
