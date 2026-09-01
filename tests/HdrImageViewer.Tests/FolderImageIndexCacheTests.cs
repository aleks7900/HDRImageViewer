using HdrImageViewer.Services;
using Xunit;

namespace HdrImageViewer.Tests;

public sealed class FolderImageIndexCacheTests
{
    [Fact]
    public void GetFolderImages_CachesSortedSupportedPathsUntilInvalidated()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var first = Path.Combine(directory, "a.jpg");
            var second = Path.Combine(directory, "B.png");
            File.WriteAllBytes(first, [1]);
            File.WriteAllBytes(second, [2]);
            File.WriteAllBytes(Path.Combine(directory, "ignored.txt"), [3]);

            using var cache = new FolderImageIndexCache();
            var initial = cache.GetFolderImages(second, CancellationToken.None);
            Assert.Equal([first, second], initial.Paths);
            Assert.Equal(1, initial.CurrentIndex);

            var added = Path.Combine(directory, "c.avif");
            File.WriteAllBytes(added, [4]);
            cache.InvalidatePath(added);
            var refreshed = cache.GetFolderImages(second, CancellationToken.None);

            Assert.Equal([first, second, added], refreshed.Paths);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void GetFolderImages_ObservesCancellationBeforeEnumeration()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var current = Path.Combine(directory, "image.jpg");
            File.WriteAllBytes(current, [1]);
            using var cache = new FolderImageIndexCache();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(
                () => cache.GetFolderImages(current, cancellation.Token));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "HdrImageViewer.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
