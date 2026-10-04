using HdrImageViewer.Presentation;
using Xunit;

namespace HdrImageViewer.Tests;

public sealed class ImageNavigationContextTests
{
    [Theory]
    [InlineData(0, 1, "bad.HDR,broken.png,b.jpg")]
    [InlineData(3, -1, "broken.png,bad.HDR,a.jpg")]
    public async Task NavigationSkipsConsecutiveFailuresInEitherDirection(int start, int direction, string expected)
    {
        string[] paths = ["a.jpg", "bad.HDR", "broken.png", "b.jpg"];
        var attempts = new List<string>();
        var failed = await ImageNavigationContext.NavigateAsync(paths, start, direction, path =>
        {
            attempts.Add(path);
            return Task.FromResult(path.EndsWith(".jpg", StringComparison.Ordinal)
                ? ImageLoadOutcome.Opened : ImageLoadOutcome.Failed);
        }, CancellationToken.None);
        Assert.Equal(expected.Split(','), attempts);
        Assert.Equal(2, failed.Count);
    }

    [Fact]
    public async Task AllFailuresStopAtBoundaryWithoutRetryingOrWrapping()
    {
        string[] paths = ["a.jpg", "bad.HDR", "bad2.HDR"];
        var attempts = new List<string>();
        var failed = await ImageNavigationContext.NavigateAsync(paths, 0, 1, path =>
        {
            attempts.Add(path);
            return Task.FromResult(ImageLoadOutcome.Failed);
        }, CancellationToken.None);
        Assert.Equal(paths.Skip(1), attempts);
        Assert.Equal(attempts, failed);
    }

    [Fact]
    public async Task SupersededOpenDoesNotContinueIntoAnotherFile()
    {
        var attempts = new List<string>();
        await ImageNavigationContext.NavigateAsync(["a.jpg", "b.jpg", "c.jpg"], 0, 1, path =>
        {
            attempts.Add(path);
            return Task.FromResult(ImageLoadOutcome.Canceled);
        }, CancellationToken.None);
        Assert.Equal(["b.jpg"], attempts);
    }

    [Fact]
    public void ConsecutiveNavigationKeepsCrossFolderSelectionAndOrder()
    {
        string[] paths = [@"C:\photos\c.jpg", @"D:\other\a.jpg", @"C:\photos\b.jpg"];
        IReadOnlyList<string> current = paths;
        foreach (var path in paths.Concat(paths.Reverse()))
        {
            current = ImageNavigationContext.ResolveExplicitPaths(path, null, current, true, true)!;
            Assert.Equal(paths, current);
        }
    }

    [Fact]
    public void OpeningANewFileResetsTheOldExplicitSelection()
    {
        string[] paths = [@"C:\photos\a.jpg", @"C:\photos\b.jpg"];
        Assert.Null(ImageNavigationContext.ResolveExplicitPaths(paths[0], null, paths, true, false));
        Assert.Null(ImageNavigationContext.ResolveExplicitPaths(@"D:\new.jpg", null, paths, true, true));
        Assert.Null(ImageNavigationContext.ResolveExplicitPaths(paths[0], null, paths, false, true));
    }
}
