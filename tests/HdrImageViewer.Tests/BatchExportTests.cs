using HdrImageViewer.Services;
using Xunit;

namespace HdrImageViewer.Tests;

public sealed class BatchExportTests
{
    [Fact]
    public async Task FailureDoesNotStopFollowingItemsAndRetrySkipsSuccesses()
    {
        var queue = new BatchExportController();
        queue.Add(["a.png", "b.png", "b.png", "c.png"]);
        var calls = 0;
        await queue.RunAsync((item, _) =>
        {
            calls++;
            return item.FileName == "b.png" ? Task.FromException<string>(new IOException("bad image")) : Task.FromResult("done");
        }, CancellationToken.None);
        Assert.Equal(3, calls);
        Assert.Equal(BatchExportState.Failed, queue.Items[1].State);
        Assert.Equal(BatchExportState.Completed, queue.Items[2].State);
        queue.RetryUnfinished();
        await queue.RunAsync((_, _) => { calls++; return Task.FromResult("retry"); }, CancellationToken.None);
        Assert.Equal(4, calls);
        Assert.All(queue.Items, item => Assert.Equal(BatchExportState.Completed, item.State));
    }

    [Fact]
    public async Task CancellationStopsCurrentAndPendingItems()
    {
        var queue = new BatchExportController();
        queue.Add(["a.png", "b.png"]);
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        await queue.RunAsync((_, token) =>
        {
            calls++; cancellation.Cancel(); token.ThrowIfCancellationRequested(); return Task.FromResult("unreachable");
        }, cancellation.Token);
        Assert.Equal(1, calls);
        Assert.All(queue.Items, item => Assert.Equal(BatchExportState.Canceled, item.State));
        Assert.False(queue.IsRunning);
    }

    [Fact]
    public async Task NonOverwriteCommitProtectsFileCreatedDuringExport()
    {
        var directory = Path.Combine(Path.GetTempPath(), "HdrBatchTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var output = BatchExportController.ChooseOutputPath(directory, "photo.jpg", ".png");
            var source = Path.Combine(directory, "source.png");
            await File.WriteAllTextAsync(source, "new");
            await File.WriteAllTextAsync(output, "existing");
            await Assert.ThrowsAnyAsync<IOException>(() => ExportFileTransaction.CopyAsync(source, output, CancellationToken.None, false));
            Assert.Equal("existing", await File.ReadAllTextAsync(output));
            Assert.NotEqual(output, BatchExportController.ChooseOutputPath(directory, "photo.jpg", ".png"));
        }
        finally { Directory.Delete(directory, true); }
    }
}
