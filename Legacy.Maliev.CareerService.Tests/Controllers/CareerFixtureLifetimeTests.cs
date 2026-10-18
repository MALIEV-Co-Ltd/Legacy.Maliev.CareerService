using System.Collections.Concurrent;
using System.Text.Json;

namespace Legacy.Maliev.CareerService.Tests.Controllers;

public sealed class CareerFixtureLifetimeTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);
    private sealed class Dummy;

    [Fact]
    public async Task ContextOperationAndDisposalFailure_PreservesExactOperationPrimary()
    {
        var lifetime = new CareerFixtureLifetime("fake", _ => { });
        var primary = new InvalidOperationException("EF operation");
        var cleanup = new IOException("context disposal");
        var caught = await Assert.ThrowsAsync<InvalidOperationException>(() => lifetime.UseOwnedAsync<Dummy, int>(
            () => lifetime.Create("context", 1, () => new Dummy(), _ => ValueTask.FromException(cleanup)),
            (_, _) => Task.FromException<int>(primary)).WaitAsync(Bound));
        Assert.Same(primary, caught);
        Assert.Contains(cleanup, lifetime.SecondaryFailures);
        await Assert.ThrowsAsync<AggregateException>(() => lifetime.DisposeAsync().WaitAsync(Bound));
    }

    [Fact]
    public async Task ContextOperationSuccess_DisposalFailureIsPrimary()
    {
        var lifetime = new CareerFixtureLifetime("fake", _ => { });
        var cleanup = new IOException("context disposal");
        var caught = await Assert.ThrowsAsync<IOException>(() => lifetime.UseOwnedAsync<Dummy, int>(
            () => lifetime.Create("context", 1, () => new Dummy(), _ => ValueTask.FromException(cleanup)),
            (_, _) => Task.FromResult(7)).WaitAsync(Bound));
        Assert.Same(cleanup, caught);
        await Assert.ThrowsAsync<AggregateException>(() => lifetime.DisposeAsync().WaitAsync(Bound));
    }

    [Fact]
    public async Task ContextPrimaryAndEvidenceFailure_StillAttemptsDisposalAndPreservesPrimary()
    {
        var failEvidence = false;
        var released = false;
        var lifetime = new CareerFixtureLifetime("fake", _ => { if (failEvidence) { throw new IOException("evidence"); } });
        var primary = new InvalidOperationException("EF operation");
        var caught = await Assert.ThrowsAsync<InvalidOperationException>(() => lifetime.UseOwnedAsync<Dummy, int>(
            () => lifetime.Create("context", 1, () => new Dummy(), _ => { released = true; return ValueTask.CompletedTask; }),
            (_, _) => { failEvidence = true; return Task.FromException<int>(primary); }).WaitAsync(Bound));
        Assert.Same(primary, caught);
        Assert.True(released);
        await Assert.ThrowsAsync<AggregateException>(() => lifetime.DisposeAsync().WaitAsync(Bound));
    }

    [Fact]
    public async Task ExplicitContextRelease_IsNotRepeatedByFixtureCleanup()
    {
        var lifetime = new CareerFixtureLifetime("fake", _ => { });
        var releases = 0;
        var result = await lifetime.UseOwnedAsync<Dummy, int>(
            () => lifetime.Create("context", 1, () => new Dummy(), _ => { releases++; return ValueTask.CompletedTask; }),
            (_, _) => Task.FromResult(7)).WaitAsync(Bound);
        Assert.Equal(7, result);
        await lifetime.DisposeAsync().WaitAsync(Bound);
        Assert.Equal(1, releases);
    }

    [Fact]
    public async Task CreationIntent_PrecedesActualResourceCreation()
    {
        var trace = new List<string>();
        var lifetime = new CareerFixtureLifetime("fake", entry => trace.Add(entry.Stage));
        lifetime.Create("client", 0, () => { trace.Add("create"); return new Dummy(); }, _ => ValueTask.CompletedTask);
        await lifetime.DisposeAsync().WaitAsync(Bound);
        Assert.Equal("creation-intent", trace[0]);
        Assert.Equal("create", trace[1]);
    }

    [Fact]
    public async Task LateCreation_IsJoinedAndReleasedDuringDispose()
    {
        var lifetime = new CareerFixtureLifetime("fake", _ => { });
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposed = 0;
        var actual = lifetime.RunAsync("late-start", async _ =>
        {
            entered.TrySetResult();
            await release.Task;
            lifetime.Create("client", 0, () => new Dummy(), _ => { disposed++; return ValueTask.CompletedTask; });
        }, Bound);
        try
        {
            await entered.Task.WaitAsync(Bound);
            var cleanup = lifetime.DisposeAsync();
            Assert.False(cleanup.IsCompleted);
            release.TrySetResult();
            await Task.WhenAll(actual, cleanup).WaitAsync(Bound);
            Assert.Equal(1, disposed);
        }
        finally { release.TrySetResult(); await lifetime.DisposeAsync().WaitAsync(Bound); }
    }

    [Fact]
    public async Task CancellationProxy_DoesNotDetachActualLateWork()
    {
        var lifetime = new CareerFixtureLifetime("fake", _ => { });
        using var caller = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposed = 0;
        var proxy = lifetime.RunAsync("noncooperative", async _ =>
        {
            entered.TrySetResult();
            await release.Task;
            lifetime.Create("factory", 2, () => new Dummy(), _ => { disposed++; return ValueTask.CompletedTask; });
        }, Bound, caller.Token);
        try
        {
            await entered.Task.WaitAsync(Bound);
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => proxy);
            var cleanup = lifetime.DisposeAsync();
            Assert.False(cleanup.IsCompleted);
            release.TrySetResult();
            await cleanup.WaitAsync(Bound);
            Assert.Equal(1, disposed);
        }
        finally { release.TrySetResult(); await lifetime.DisposeAsync().WaitAsync(Bound); }
    }

    [Fact]
    public async Task FirstReleaseFailure_DoesNotSkipOtherTiers()
    {
        var lifetime = new CareerFixtureLifetime("fake", _ => { });
        var releases = new ConcurrentQueue<string>();
        lifetime.Create("factory", 2, () => new Dummy(), _ =>
        {
            releases.Enqueue("factory");
            return ValueTask.FromException(new InvalidOperationException("controlled"));
        });
        lifetime.Create("redis", 4, () => new Dummy(), _ => { releases.Enqueue("redis"); return ValueTask.CompletedTask; });
        lifetime.Create("postgres", 4, () => new Dummy(), _ => { releases.Enqueue("postgres"); return ValueTask.CompletedTask; });
        await Assert.ThrowsAsync<AggregateException>(() => lifetime.DisposeAsync().WaitAsync(Bound));
        Assert.Contains("factory", releases);
        Assert.Contains("redis", releases);
        Assert.Contains("postgres", releases);
    }

    [Fact]
    public async Task CooperativeTimeout_DoesNotClaimNoncooperativeWorkEnded()
    {
        var lifetime = new CareerFixtureLifetime("fake", _ => { });
        var timedOut = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasedResource = false;
        var actual = lifetime.RunAsync("timeout", async cancellation =>
        {
            using var registration = cancellation.Register(() => timedOut.TrySetResult());
            await release.Task; // Deliberately ignore cancellation while retaining actual work.
            lifetime.Create("context", 1, () => new Dummy(), _ => { releasedResource = true; return ValueTask.CompletedTask; });
        }, TimeSpan.FromMilliseconds(10));
        try
        {
            await timedOut.Task.WaitAsync(Bound);
            Assert.False(actual.IsCompleted);
            var cleanup = lifetime.DisposeAsync();
            Assert.False(cleanup.IsCompleted);
            release.TrySetResult();
            await Task.WhenAll(actual, cleanup).WaitAsync(Bound);
            Assert.True(releasedResource);
        }
        finally { release.TrySetResult(); await lifetime.DisposeAsync().WaitAsync(Bound); }
    }

    [Fact]
    public async Task IndependentSameTierReleases_StartBeforeFirstJoin()
    {
        var lifetime = new CareerFixtureLifetime("fake", _ => { });
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = false;
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lifetime.Create("first", 4, () => new Dummy(), _ => new ValueTask(release.Task));
        lifetime.Create("second", 4, () => new Dummy(), _ =>
        {
            secondStarted = true; secondEntered.TrySetResult(); return ValueTask.CompletedTask;
        });
        try
        {
            var cleanup = lifetime.DisposeAsync();
            await secondEntered.Task.WaitAsync(Bound);
            Assert.True(secondStarted);
            Assert.False(cleanup.IsCompleted);
            release.TrySetResult();
            await cleanup.WaitAsync(Bound);
        }
        finally { release.TrySetResult(); await lifetime.DisposeAsync().WaitAsync(Bound); }
    }

    [Fact]
    public async Task PrimaryFailure_IsSameObjectWhenCleanupFails()
    {
        var lifetime = new CareerFixtureLifetime("fake", _ => { });
        var primary = new InvalidOperationException("primary");
        lifetime.Create("factory", 2, () => new Dummy(), _ => ValueTask.FromException(new IOException("cleanup")));
        var caught = await Assert.ThrowsAsync<InvalidOperationException>(() => lifetime.DisposePreservingAsync(primary).WaitAsync(Bound));
        Assert.Same(primary, caught);
        Assert.NotEmpty(lifetime.SecondaryFailures);
    }

    [Fact]
    public async Task PrimaryFailure_IsSameObjectWhenEvidenceFails()
    {
        var released = false;
        var failEvidence = false;
        var lifetime = new CareerFixtureLifetime("fake", _ => { if (failEvidence) { throw new IOException("evidence"); } });
        lifetime.Create("client", 0, () => new Dummy(), _ => { released = true; return ValueTask.CompletedTask; });
        failEvidence = true;
        var primary = new InvalidOperationException("primary");
        var caught = await Assert.ThrowsAsync<InvalidOperationException>(() => lifetime.DisposePreservingAsync(primary).WaitAsync(Bound));
        Assert.Same(primary, caught);
        Assert.True(released);
        Assert.NotEmpty(lifetime.SecondaryFailures);
    }

    [Fact]
    public async Task AbsentContainer_DoesNotSettleUnknownVolumesOrNativeCustody()
    {
        var entries = new List<CareerFixtureEvent>();
        var lifetime = new CareerFixtureLifetime("fake", entries.Add);
        lifetime.Create("postgres", 4, () => new Dummy(), _ => ValueTask.CompletedTask,
            _ => new("UNKNOWN", "UNKNOWN", "UNKNOWN", "postgres:18-alpine"));
        await lifetime.DisposeAsync().WaitAsync(Bound);
        var terminal = Assert.Single(entries, entry => entry.Stage == "managed-release-complete");
        Assert.Equal("UNKNOWN", terminal.Container!.VolumeSettlement);
        Assert.Equal("UNQUALIFIED", terminal.NativeCustody);
    }

    [Fact]
    public async Task ClosedCreation_RejectsNewExternalResources()
    {
        var lifetime = new CareerFixtureLifetime("fake", _ => { });
        await lifetime.DisposeAsync().WaitAsync(Bound);
        var created = false;
        Assert.Throws<InvalidOperationException>(() => lifetime.Create("client", 0,
            () => { created = true; return new Dummy(); }, _ => ValueTask.CompletedTask));
        Assert.False(created);
    }

    [Fact]
    public async Task Disposal_IsIdempotentAndReleasesEachRegistrationOnce()
    {
        var lifetime = new CareerFixtureLifetime("fake", _ => { });
        var releases = 0;
        lifetime.Create("context", 1, () => new Dummy(), _ => { releases++; return ValueTask.CompletedTask; });
        var first = lifetime.DisposeAsync();
        var second = lifetime.DisposeAsync();
        Assert.Same(first, second);
        await first.WaitAsync(Bound);
        Assert.Equal(1, releases);
    }

    [Fact]
    public async Task ResetOperations_AreSerializedAndActuallyJoined()
    {
        var lifetime = new CareerFixtureLifetime("fake", _ => { });
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = 0;
        var first = lifetime.RunAsync("reset", async _ =>
        {
            entered++; firstEntered.TrySetResult(); await release.Task;
        }, Bound, serialize: true);
        await firstEntered.Task.WaitAsync(Bound);
        var second = lifetime.RunAsync("reset", _ => { entered++; return Task.CompletedTask; }, Bound, serialize: true);
        try
        {
            Assert.Equal(1, entered);
            release.TrySetResult();
            await Task.WhenAll(first, second).WaitAsync(Bound);
            Assert.Equal(2, entered);
        }
        finally { release.TrySetResult(); await lifetime.DisposeAsync().WaitAsync(Bound); }
    }

    [Fact]
    public async Task Journal_SerializesFailureTypeWithoutCredentialOrMessage()
    {
        var entries = new List<CareerFixtureEvent>();
        var lifetime = new CareerFixtureLifetime("fake", entries.Add);
        var secret = "never-serialize-this-private-message";
        var primary = new InvalidOperationException(secret);
        await Assert.ThrowsAsync<InvalidOperationException>(() => lifetime.DisposePreservingAsync(primary).WaitAsync(Bound));
        var json = JsonSerializer.Serialize(entries);
        Assert.DoesNotContain(secret, json, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", json, StringComparison.Ordinal);
    }
}
