using System.Numerics;
using HdrImageViewer.Rendering;
using Xunit;

namespace HdrImageViewer.Tests;

public sealed class PreviewAnalysisTests
{
    [Fact]
    public async Task AnalysisPublishesCurrentPreviewAndPreservesPixelStatistics()
    {
        using var controller = new PreviewAnalysisController();
        var result = await controller.AnalyzeAsync(controller.Version,
            token => CreateSnapshot(controller.Version, token), CancellationToken.None);

        Assert.Same(result, controller.Snapshot);
        Assert.NotNull(result);
        Assert.Equal(1, result.Count);
        Assert.Equal(80, result.AverageNits, 3);
        Assert.Equal(1, result.ChromaticityCount);
        Assert.Equal(1, result.RedHistogram[128]);
    }

    [Fact]
    public async Task ChangedPreviewDiscardsResultEvenWhenWorkerFinishesAfterCancellation()
    {
        using var controller = new PreviewAnalysisController();
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var version = controller.Version;
        var pending = controller.AnalyzeAsync(version, _ =>
        {
            started.SetResult();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10), CancellationToken.None));
            return CreateSnapshot(version, CancellationToken.None);
        }, CancellationToken.None);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            controller.Invalidate(clearSnapshot: true);
        }
        finally { release.Set(); }

        Assert.Null(await pending.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Null(controller.Snapshot);
    }

    [Fact]
    public async Task NewRequestWinsWhenOlderWorkerIgnoresCancellation()
    {
        using var controller = new PreviewAnalysisController();
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var version = controller.Version;
        var pending = controller.AnalyzeAsync(version, _ =>
        {
            started.SetResult();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10), CancellationToken.None));
            return CreateSnapshot(version, CancellationToken.None);
        }, CancellationToken.None);
        LuminanceSnapshot? latest;
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            latest = await controller.AnalyzeAsync(version,
                token => CreateSnapshot(version, token), CancellationToken.None);
        }
        finally { release.Set(); }

        Assert.Null(await pending.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.NotNull(latest);
        Assert.Same(latest, controller.Snapshot);
    }

    [Fact]
    public async Task CallerCancellationPropagatesWithoutPublishingPixels()
    {
        using var controller = new PreviewAnalysisController();
        using var source = new CancellationTokenSource();
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var version = controller.Version;
        var pending = controller.AnalyzeAsync(version, token =>
        {
            started.SetResult();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10), CancellationToken.None));
            return CreateSnapshot(version, token);
        }, source.Token);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            source.Cancel();
        }
        finally { release.Set(); }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
        Assert.Null(controller.Snapshot);
    }

    [Fact]
    public void LuminanceCalculationHonorsCancellation()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => CreateSnapshot(0, source.Token));
    }

    [Fact]
    public void AlternatingComparisonModesReusesAnalysisUntilImageOrSettingsChange()
    {
        var cache = new ViewModeAnalysisCache<object>();
        var calculations = 0;
        object Calculate() { calculations++; return new object(); }
        var hdr = cache.GetOrCreate(GainmapViewMode.Adaptive, Calculate);
        var sdr = cache.GetOrCreate(GainmapViewMode.Sdr, Calculate);

        for (var divider = 0; divider <= 100; divider++)
        {
            Assert.Same(hdr, cache.GetOrCreate(GainmapViewMode.Adaptive, Calculate));
            Assert.Same(sdr, cache.GetOrCreate(GainmapViewMode.Sdr, Calculate));
        }
        Assert.Equal(2, calculations);

        cache.Clear();
        Assert.NotSame(hdr, cache.GetOrCreate(GainmapViewMode.Adaptive, Calculate));
        Assert.NotSame(sdr, cache.GetOrCreate(GainmapViewMode.Sdr, Calculate));
        Assert.Equal(4, calculations);
    }

    private static LuminanceSnapshot CreateSnapshot(long version, CancellationToken cancellationToken = default)
    {
        var pixels = new byte[] { 0, 0x3c, 0, 0x3c, 0, 0x3c, 0, 0x3c };
        return new LuminanceSnapshot(1, 1, pixels, new Vector4(1, 1, 0, 0), version,
            cancellationToken: cancellationToken);
    }
}
