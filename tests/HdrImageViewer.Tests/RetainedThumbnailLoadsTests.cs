using HdrImageViewer.Presentation;
using Xunit;

namespace HdrImageViewer.Tests;

public sealed class RetainedThumbnailLoadsTests
{
    [Fact]
    public async Task OverlappingViewport_ReusesPendingLoadWithoutCancellation()
    {
        using var loads = new RetainedThumbnailLoads(CancellationToken.None);
        var finish = Signal();
        var count = 0;
        CancellationToken activeToken = default;
        Task Load(CancellationToken token)
        {
            count++;
            activeToken = token;
            return finish.Task;
        }

        loads.Queue("a.heic", Load);
        loads.KeepOnly(Paths("a.heic", "b.heic"));
        loads.Queue("A.HEIC", Load);

        Assert.Equal(1, count);
        Assert.False(activeToken.IsCancellationRequested);
        finish.SetResult();
        await loads.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ReenteringCanceledPath_WaitsForEveryPredecessorBeforeRestart()
    {
        using var loads = new RetainedThumbnailLoads(CancellationToken.None);
        var finishOriginal = Signal();
        var replacementStarted = Signal();
        var canceledWaiterStarted = false;
        loads.Queue("a", _ => finishOriginal.Task); // Models a non-cancelable native decode.
        loads.KeepOnly(Paths());
        loads.Queue("a", _ => { canceledWaiterStarted = true; return Task.CompletedTask; });
        loads.KeepOnly(Paths());
        loads.Queue("a", _ => { replacementStarted.SetResult(); return Task.CompletedTask; });

        Assert.False(replacementStarted.Task.IsCompleted);
        finishOriginal.SetResult();
        await replacementStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await loads.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(canceledWaiterStarted);
    }

    [Fact]
    public async Task ConcurrencyLimit_AppliesAcrossViewportGenerations()
    {
        using var loads = new RetainedThumbnailLoads(CancellationToken.None, concurrency: 2);
        var finishOld = Signal();
        var nextStarted = Signal();
        var oldActive = 0;
        Task Old(CancellationToken _)
        {
            oldActive++;
            return finishOld.Task;
        }
        loads.Queue("a", Old);
        loads.Queue("b", Old);
        Assert.Equal(2, oldActive);
        loads.KeepOnly(Paths("c"));
        loads.Queue("c", _ => { nextStarted.SetResult(); return Task.CompletedTask; });

        Assert.False(nextStarted.Task.IsCompleted);
        finishOld.SetResult();
        await nextStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await loads.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Dispose_CancelsActiveWorkAndWaitsToReleaseSharedResources()
    {
        var loads = new RetainedThumbnailLoads(CancellationToken.None, concurrency: 1);
        var finish = Signal();
        CancellationToken activeToken = default;
        var queuedStarted = false;
        loads.Queue("a", token => { activeToken = token; return finish.Task; });
        loads.Queue("b", _ => { queuedStarted = true; return Task.CompletedTask; });
        loads.Dispose();

        Assert.True(activeToken.IsCancellationRequested);
        finish.SetResult();
        await loads.WhenIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        loads.Dispose();
        Assert.False(queuedStarted);
    }

    private static HashSet<string> Paths(params string[] paths) => new(paths, StringComparer.OrdinalIgnoreCase);
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
