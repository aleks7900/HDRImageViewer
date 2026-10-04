using HdrImageViewer.Presentation;
using Xunit;

namespace HdrImageViewer.Tests;

public sealed class ImageNavigationContextTests
{
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
