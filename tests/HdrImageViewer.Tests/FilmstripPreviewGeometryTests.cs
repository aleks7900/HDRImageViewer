using HdrImageViewer.Presentation;
using Xunit;

namespace HdrImageViewer.Tests;

public sealed class FilmstripPreviewGeometryTests
{
    [Theory]
    [InlineData(192, 144)]
    [InlineData(128, 192)]
    [InlineData(192, 128)]
    public void FinalPixelsCorrectAnIncorrectEarlyPreview(double width, double height)
    {
        var geometry = new FilmstripPreviewGeometry();
        geometry.SetDimensions(192, 108);
        Assert.True(geometry.SetDimensions(width, height, decodedPixels: true));
        // The image's 46-DIP height and computed content width have the same
        // aspect ratio, so Uniform adds no horizontal or vertical letterbox.
        Assert.Equal(width / height, (geometry.Width - 10) / 46, 10);
    }

    [Fact]
    public void RefinementRoundingAndInvalidationDoNotJitterTheFrame()
    {
        var geometry = new FilmstripPreviewGeometry();
        geometry.SetDimensions(4032, 3024);
        var width = geometry.Width;
        Assert.False(geometry.SetDimensions(192, 144, decodedPixels: true));
        Assert.False(geometry.SetDimensions(191, 144, decodedPixels: true));
        Assert.False(geometry.SetDimensions(double.NaN, 100, decodedPixels: true));
        geometry.Invalidate();
        Assert.Equal(width, geometry.Width);
        Assert.True(geometry.SetDimensions(3024, 4032));
    }
}
