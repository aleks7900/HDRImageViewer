using HdrImageViewer.Presentation;
using Xunit;

namespace HdrImageViewer.Tests;

public sealed class FilmstripThumbnailWindowTests
{
    [Theory]
    [InlineData(0, 37)]
    [InlineData(499, 73)]
    [InlineData(999, 37)]
    public void FolderEdges_LoadOnlyTheRetainedRadius(int focus, int expectedCount)
    {
        var indices = FilmstripThumbnailWindow.GetLoadOrder(1000, focus);
        Assert.Equal(expectedCount, indices.Count);
        Assert.Equal(focus, indices[0]);
        Assert.All(indices, index => Assert.InRange(Math.Abs(index - focus), 0, FilmstripThumbnailWindow.Radius));
        Assert.Equal(indices.Count, indices.Distinct().Count());
    }

    [Fact]
    public void ScrollingFarFromSelection_LoadsVisibleImagesAndRetainsCurrentThumbnail()
    {
        var indices = FilmstripThumbnailWindow.GetLoadOrder(10_000, 2, 900, 912);
        Assert.Equal(906, indices[0]);
        Assert.Contains(2, indices);
        Assert.All(Enumerable.Range(900, 13), index => Assert.Contains(index, indices));
        Assert.InRange(indices.Count, 1, 74);
    }

    [Fact]
    public void EmptyAndStaleViewportIndicesAreSafe()
    {
        Assert.Empty(FilmstripThumbnailWindow.GetLoadOrder(0, -1));
        Assert.Equal(0, Assert.Single(FilmstripThumbnailWindow.GetLoadOrder(1, -1, 99, 100)));
    }
}
