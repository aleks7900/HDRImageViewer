using HdrImageViewer.Models;
using HdrImageViewer.Services;
using Xunit;

namespace HdrImageViewer.Tests;

public sealed class BitmapPreviewResamplerTests
{
    [Theory]
    [InlineData(400, 300, 128, 128, 96)]
    [InlineData(300, 400, 128, 96, 128)]
    [InlineData(400, 1, 128, 128, 1)]
    public void HdrPreview_RespectsLongestSideAndPreservesEncoding(
        int width, int height, int limit, int expectedWidth, int expectedHeight)
    {
        var source = new DecodedBitmap(width, height, new byte[width * height * 8], false,
            "HEIF", DecodedBitmapPixelFormat.Rgba16Unorm, DecodedBitmapTransfer.Pq,
            ColorGamut: GainMapColorGamut.DisplayP3);

        var result = BitmapPreviewResampler.Downscale(source, limit, CancellationToken.None);

        Assert.Equal(expectedWidth, result.PixelWidth);
        Assert.Equal(expectedHeight, result.PixelHeight);
        Assert.Equal(expectedWidth * expectedHeight * 8, result.RgbaPixels.Length);
        Assert.Equal(source.Transfer, result.Transfer);
        Assert.Equal(source.ColorGamut, result.ColorGamut);
        Assert.Equal(source.PixelFormat, result.PixelFormat);
    }

    [Fact]
    public void FullExport_WithoutLimitKeepsOriginalPixels()
    {
        var source = new DecodedBitmap(400, 300, new byte[400 * 300 * 8], false,
            PixelFormat: DecodedBitmapPixelFormat.Rgba16Unorm, Transfer: DecodedBitmapTransfer.Hlg);

        Assert.Same(source, BitmapPreviewResampler.Downscale(source, null, CancellationToken.None));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    public void Cancellation_PropagatesEvenWithoutResizing(int? limit)
    {
        var source = new DecodedBitmap(2, 2, new byte[16], false);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            BitmapPreviewResampler.Downscale(source, limit, cancellation.Token));
    }
}
