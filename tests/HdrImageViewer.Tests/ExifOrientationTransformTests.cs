using HdrImageViewer.Presentation;
using HdrImageViewer.Services;
using Xunit;

namespace HdrImageViewer.Tests;

public sealed class ExifOrientationTransformTests
{
    [Theory]
    [InlineData(1, 2, 3, "123456")]
    [InlineData(2, 2, 3, "214365")]
    [InlineData(3, 2, 3, "654321")]
    [InlineData(4, 2, 3, "563412")]
    [InlineData(5, 3, 2, "135246")]
    [InlineData(6, 3, 2, "531642")]
    [InlineData(7, 3, 2, "642531")]
    [InlineData(8, 3, 2, "246135")]
    [InlineData(0, 2, 3, "123456")]
    [InlineData(9, 2, 3, "123456")]
    public void UprightPixelGridMatchesExifOrientation(int orientation, int width, int height, string expected)
    {
        var transform = new ExifOrientationTransform(2, 3, orientation);
        Assert.Equal(width, transform.Width);
        Assert.Equal(height, transform.Height);
        var actual = new List<int>();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var source = transform.MapToSource(x, y);
                Assert.InRange(source.X, 0, 1);
                Assert.InRange(source.Y, 0, 2);
                actual.Add(source.Y * 2 + source.X + 1);
            }
        }
        Assert.Equal(expected, string.Concat(actual));
    }

    [Theory]
    [InlineData(2, "24")]
    [InlineData(3, "64")]
    [InlineData(4, "53")]
    [InlineData(5, "13")]
    [InlineData(6, "53")]
    [InlineData(7, "64")]
    [InlineData(8, "24")]
    public void AsymmetricDisplayedCropSelectsTheExpectedStoredPixels(int orientation, string expected)
    {
        // The same top-left strip on screen must select mirrored/rotated source
        // pixels. This catches applying crop coordinates before orientation.
        var transform = new ExifOrientationTransform(2, 3, orientation);
        var crop = transform.Width == 3
            ? ViewerViewportMath.CalculateCropPixels(0, 0, 2.0 / 3.0, 0.5, (uint)transform.Width, (uint)transform.Height)
            : ViewerViewportMath.CalculateCropPixels(0, 0, 0.5, 2.0 / 3.0, (uint)transform.Width, (uint)transform.Height);
        var actual = new List<int>();
        for (var y = crop.Y; y < crop.Y + crop.Height; y++)
        {
            for (var x = crop.X; x < crop.X + crop.Width; x++)
            {
                var source = transform.MapToSource((int)x, (int)y);
                actual.Add(source.Y * 2 + source.X + 1);
            }
        }
        Assert.Equal(expected, string.Concat(actual));
    }
}
