using System.Runtime.ExceptionServices;
using System.Text.Json;

using DotNet.Testcontainers.Containers;

namespace Legacy.Maliev.CareerService.Tests.Controllers;

internal sealed record CareerContainerObservation(string Id, string CreatedUtc, string StartedUtc,
    string DeclaredImage, string VolumeSettlement = "UNKNOWN", int? MappedPort = null);

internal sealed record CareerFixtureEvent(string FixtureId, string Family, string Stage, string Kind,
    string ResourceId, string? FailureType = null, CareerContainerObservation? Container = null,
    string NativeCustody = "UNQUALIFIED", string RunId = "UNQUALIFIED", string Phase = "UNKNOWN",
    string ObservedUtc = "");

/// <summary>Retains actual fixture work and independently attempts managed-resource release.</summary>
internal sealed class CareerFixtureLifetime
{
    private sealed class Pending(Guid id)
    {
        internal Guid Id { get; } = id;
        internal TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task? Actual { get; set; }
        internal TaskCompletionSource ActualAssigned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed record Resource(string Kind, int Order, string Id, Func<ValueTask> Release,
        Func<CareerContainerObservation>? Observe)
    {
        internal Task<Exception?>? ActualRelease { get; set; }
    }

    private readonly object gate = new();
    private readonly CancellationTokenSource stop = new();
    private readonly SemaphoreSlim reset = new(1, 1);
    private readonly AsyncLocal<Guid?> activeOperation = new();
    private readonly List<Pending> pending = [];
    private readonly HashSet<Guid> active = [];
    private readonly Dictionary<object, Resource> resources = new(ReferenceEqualityComparer.Instance);
    private readonly List<Exception> secondary = [];
    private readonly List<Exception> evidenceFailures = [];
    private readonly Action<CareerFixtureEvent> journal;
    private readonly string family;
    private bool closing;
    private Task? disposal;

    internal CareerFixtureLifetime(string family, Action<CareerFixtureEvent>? journal = null)
    {
        this.family = family;
        FixtureId = Guid.NewGuid().ToString("N");
        this.journal = journal ?? AppendJournal;
    }

    internal string FixtureId { get; }
    internal IReadOnlyList<Exception> SecondaryFailures { get { lock (gate) { return secondary.ToArray(); } } }

    private void AppendJournal(CareerFixtureEvent entry)
    {
        var directory = Environment.GetEnvironmentVariable("VSTestResultsDirectory")
            ?? Path.Combine(Environment.CurrentDirectory, "runner-results");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "career-fixture-" + FixtureId + ".jsonl");
        var configuredRun = Environment.GetEnvironmentVariable("CAREER_FIXTURE_RUN");
        var run = Guid.TryParse(configuredRun, out var runId) ? runId.ToString("N") : "UNQUALIFIED";
        var configuredPhase = Environment.GetEnvironmentVariable("CAREER_FIXTURE_PHASE");
        var phase = configuredPhase is "focus12" or "full" ? configuredPhase : "UNKNOWN";
        File.AppendAllText(path, JsonSerializer.Serialize(entry with { RunId = run, Phase = phase }) + "\n");
    }

    internal static CareerContainerObservation ObserveContainer(IContainer container, string image) =>
        new(container.Id, $"{container.CreatedTime:O}", $"{container.StartedTime:O}", image,
            MappedPort: container.GetMappedPublicPort(image.StartsWith("postgres:", StringComparison.Ordinal) ? 5432 : 6379));

    internal void ObserveOwned(object value)
    {
        Resource resource;
        lock (gate) { resource = resources[value]; }
        var observation = resource.Observe?.Invoke();
        Emit("creation-observed", resource.Kind, resource.Id, container: observation, required: true);
    }

    private void Emit(string stage, string kind, string id, Exception? failure = null,
        CareerContainerObservation? container = null, bool required = false)
    {
        try
        {
            lock (gate)
            {
                journal(new(FixtureId, family, stage, kind, id, failure?.GetType().Name, container,
                    ObservedUtc: DateTimeOffset.UtcNow.ToString("O")));
            }
        }
        catch (Exception error)
        {
            lock (gate) { secondary.Add(error); evidenceFailures.Add(error); }
            if (required) { throw; }
        }
    }

    private Pending Admit(string kind)
    {
        lock (gate)
        {
            if (closing && !(activeOperation.Value is Guid id && active.Contains(id)))
            {
                throw new InvalidOperationException("Fixture creation is closed.");
            }

            var entry = new Pending(Guid.NewGuid());
            pending.Add(entry); // Retained BEFORE journal or operation/resource creation.
            return entry;
        }
    }

    internal T Create<T>(string kind, int order, Func<T> create, Func<T, ValueTask> release,
        Func<T, CareerContainerObservation>? observe = null) where T : class
    {
        var entry = Admit(kind);
        entry.Actual = entry.Completion.Task;
        entry.ActualAssigned.TrySetResult();
        Exception? error = null;
        try
        {
            Emit("creation-intent", kind, entry.Id.ToString("N"), required: true);
            var value = create();
            lock (gate)
            {
                resources.Add(value, new(kind, order, entry.Id.ToString("N"), () => release(value),
                    observe is null ? null : () => observe(value)));
            }

            Emit("registered", kind, entry.Id.ToString("N"));
            return value;
        }
        catch (Exception failure)
        {
            error = failure;
            throw;
        }
        finally
        {
            if (error is null) { entry.Completion.TrySetResult(); }
            else { entry.Completion.TrySetException(error); }
        }
    }

    internal Task RunAsync(string kind, Func<CancellationToken, Task> body, TimeSpan timeout,
        CancellationToken caller = default, bool serialize = false)
    {
        var entry = Admit(kind);
        // ExecuteEntry catches all ordinary failures into the retained completion.
        entry.Actual = ExecuteEntry(entry, kind, body, timeout, caller, serialize);
        entry.ActualAssigned.TrySetResult();
        return caller.CanBeCanceled ? entry.Completion.Task.WaitAsync(caller) : entry.Completion.Task;
    }

    internal Task UseOwnedAsync<T>(Func<T> create, Func<T, CancellationToken, Task> operation,
        CancellationToken caller = default) where T : class =>
        UseOwnedAsync<T, bool>(create, async (value, token) => { await operation(value, token); return true; }, caller);

    internal async Task<TResult> UseOwnedAsync<T, TResult>(Func<T> create,
        Func<T, CancellationToken, Task<TResult>> operation, CancellationToken caller = default) where T : class
    {
        TResult result = default!;
        // No caller cancellation proxy here: observe operation AND context release.
        // Actual work remains in the shared registry even if an outer caller detaches.
        await RunAsync("owned-context-operation", async token =>
        {
            var value = create();
            Resource resource;
            lock (gate) { resource = resources[value]; }
            Exception? primary = null;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, caller);
            try { result = await operation(value, linked.Token); }
            catch (Exception error) { primary = error; }
            Exception? releaseError = null;
            try { releaseError = await ReleaseOnce(resource); }
            catch (Exception error) { releaseError = error; }
            if (releaseError is not null) { lock (gate) { secondary.Add(releaseError); } }
            if (primary is not null)
            {
                Emit("primary-preserved", resource.Kind, resource.Id, primary);
                ExceptionDispatchInfo.Capture(primary).Throw();
            }

            if (releaseError is not null) { ExceptionDispatchInfo.Capture(releaseError).Throw(); }
        }, TimeSpan.FromMinutes(4));
        return result;
    }

    private async Task ExecuteEntry(Pending entry, string kind, Func<CancellationToken, Task> body,
        TimeSpan timeout, CancellationToken caller, bool serialize)
    {
        Exception? error = null;
        var previous = activeOperation.Value;
        CancellationTokenSource? linked = null;
        var acquired = false;
        try
        {
            lock (gate) { active.Add(entry.Id); }
            activeOperation.Value = entry.Id;
            Emit("operation-intent", kind, entry.Id.ToString("N"), required: true);
            linked = CancellationTokenSource.CreateLinkedTokenSource(stop.Token, caller);
            linked.CancelAfter(timeout);
            if (serialize) { await reset.WaitAsync(linked.Token); acquired = true; }
            await body(linked.Token); // Retain actual completion, not just WaitAsync proxy.
        }
        catch (Exception failure)
        {
            error = failure;
            Emit("operation-failure", kind, entry.Id.ToString("N"), failure);
        }
        finally
        {
            if (acquired) { reset.Release(); }
            linked?.Dispose();
            activeOperation.Value = previous;
            lock (gate) { active.Remove(entry.Id); }
        }

        Emit("operation-terminal", kind, entry.Id.ToString("N"), error);
        if (error is null) { entry.Completion.TrySetResult(); }
        else { entry.Completion.TrySetException(error); }
    }

    internal Task DisposeAsync()
    {
        lock (gate)
        {
            if (disposal is not null) { return disposal; }
            closing = true;
            // Retain the actual cleanup task. A constructor-failure synchronous
            // join cannot trap disposal callbacks on the caller's synchronization context.
            disposal = Task.Run(DisposeCore);
            return disposal;
        }
    }

    internal async Task DisposePreservingAsync(Exception primary)
    {
        try { await DisposeAsync(); }
        catch (Exception failure) { lock (gate) { secondary.Add(failure); } }
        Emit("primary-preserved", "fixture", FixtureId, primary);
        ExceptionDispatchInfo.Capture(primary).Throw();
    }

    private async Task DisposeCore()
    {
        var cleanupFailures = new List<Exception>();
        try
        {
            Emit("creation-closed", "fixture", FixtureId);
            try { stop.Cancel(); }
            catch (Exception error) { cleanupFailures.Add(error); }
            // Admitted operations may register late resources. Drain to a fixed point.
            var joined = new HashSet<Guid>();
            while (true)
            {
                Pending[] batch;
                lock (gate) { batch = pending.Where(item => !joined.Contains(item.Id)).ToArray(); }
                if (batch.Length == 0) { break; }
                foreach (var entry in batch)
                {
                    try { await entry.Completion.Task; }
                    catch (Exception error) { lock (gate) { secondary.Add(error); } }
                    await entry.ActualAssigned.Task;
                    if (entry.Actual is not null && !ReferenceEquals(entry.Actual, entry.Completion.Task))
                    {
                        try { await entry.Actual; }
                        catch (Exception error) { lock (gate) { secondary.Add(error); } }
                    }
                    joined.Add(entry.Id);
                }
            }

            Resource[] releases;
            lock (gate) { releases = resources.Values.ToArray(); }
            foreach (var group in releases.GroupBy(item => item.Order).OrderBy(item => item.Key))
            {
                // Start all independent releases in a dependency tier before joining.
                var tasks = group.Select(ReleaseOnce).ToArray();
                foreach (var task in tasks)
                {
                    var error = await task;
                    if (error is not null) { cleanupFailures.Add(error); }
                }
            }

            Emit("managed-terminal", "fixture", FixtureId);
        }
        catch (Exception error) { cleanupFailures.Add(error); }
        finally
        {
            try { stop.Dispose(); }
            catch (Exception error) { cleanupFailures.Add(error); }
            try { reset.Dispose(); }
            catch (Exception error) { cleanupFailures.Add(error); }
        }

        lock (gate) { cleanupFailures.AddRange(evidenceFailures); secondary.AddRange(cleanupFailures); }
        if (cleanupFailures.Count != 0)
        {
            throw new AggregateException("Fixture managed cleanup failed.", cleanupFailures);
        }
    }

    private async Task<Exception?> ReleaseResource(Resource resource)
    {
        CareerContainerObservation? observation = null;
        if (resource.Observe is not null)
        {
            try { observation = resource.Observe(); }
            catch (Exception error) { Emit("observation-unknown", resource.Kind, resource.Id, error); }
        }

        Emit("release-intent", resource.Kind, resource.Id, container: observation);
        try
        {
            await resource.Release();
            Emit("managed-release-complete", resource.Kind, resource.Id, container: observation);
            // Object DisposeAsync success does NOT establish engine absence or volume settlement.
            return null;
        }
        catch (Exception error)
        {
            Emit("managed-release-failed", resource.Kind, resource.Id, error, observation);
            return error;
        }
    }

    private Task<Exception?> ReleaseOnce(Resource resource)
    {
        lock (gate)
        {
            return resource.ActualRelease ??= Task.Run(() => ReleaseResource(resource));
        }
    }
}
