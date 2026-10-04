using HdrImageViewer.Models;
using HdrImageViewer.Services;
using Xunit;

namespace HdrImageViewer.Tests;

public sealed class ThumbnailAlphaTests
{
    [Theory]
    [InlineData(DecodedBitmapPixelFormat.Rgba8Unorm)]
    [InlineData(DecodedBitmapPixelFormat.Rgba16Unorm)]
    [InlineData(DecodedBitmapPixelFormat.Rgba16Float)]
    public void ThumbnailPixelsPreserveAndPremultiplyTransparency(DecodedBitmapPixelFormat format)
    {
        foreach (var alpha in new[] { 0f, 0.5f, 1f })
        {
            var bitmap = CreateWhitePixel(format, alpha);
            var expectedAlpha = format == DecodedBitmapPixelFormat.Rgba8Unorm && alpha == 0.5f ? (byte)128 : (byte)MathF.Round(alpha * 255);
            var pixels = ThumbnailPixelConverter.ConvertSdrToBgra8(bitmap, CancellationToken.None, premultiplyAlpha: true);
            Assert.Equal(new[] { expectedAlpha, expectedAlpha, expectedAlpha, expectedAlpha }, pixels);
            Assert.Equal(new byte[] { 255, 255, 255, 255 },
                ThumbnailPixelConverter.ConvertSdrToBgra8(bitmap, CancellationToken.None));
        }
    }

    [Fact]
    public void HdrThumbnailPremultipliesAfterToneMapping()
    {
        var bitmap = CreateWhitePixel(DecodedBitmapPixelFormat.Rgba16Float, 0.5f) with { Transfer = DecodedBitmapTransfer.LinearScRgb };
        var opaque = ThumbnailPixelConverter.ConvertHdrToBgra8(bitmap, CancellationToken.None);
        var transparent = ThumbnailPixelConverter.ConvertHdrToBgra8(bitmap, CancellationToken.None, premultiplyAlpha: true);
        Assert.Equal(128, transparent[3]);
        for (var channel = 0; channel < 3; channel++)
            Assert.InRange(Math.Abs(transparent[channel] - opaque[channel] * 0.5), 0, 1);
        Assert.Equal(255, opaque[3]);
    }

    [Fact]
    public void SdrJxlRequiresTheNativeThumbnailDecodeRoute()
    {
        var probe = new JxlProbeResult(true, 384, 96, 8, "sRGB", "sRGB", null, null, "SDR JPEG XL");
        var document = new HdrImageDocument("photo.jxl", "photo.jxl", DecoderCatalog.Describe("photo.jxl", jxlProbe: probe), JxlProbe: probe);
        Assert.Equal(HdrImageKind.StandardDynamicRange, document.Format.Kind);
        Assert.True(ThumbnailDecodePolicy.RequiresPixelDecode(document));
    }

    [Fact]
    public void OrdinarySdrPngKeepsTheCachedShellRoute()
    {
        var document = new HdrImageDocument("photo.png", "photo.png", DecoderCatalog.Describe("photo.png"));
        Assert.False(ThumbnailDecodePolicy.RequiresPixelDecode(document));
    }

    private static DecodedBitmap CreateWhitePixel(DecodedBitmapPixelFormat format, float alpha)
    {
        byte[] pixels;
        if (format == DecodedBitmapPixelFormat.Rgba8Unorm)
            pixels = [255, 255, 255, (byte)MathF.Round(alpha * 255)];
        else
        {
            pixels = new byte[8];
            for (var channel = 0; channel < 4; channel++)
            {
                var value = channel == 3 ? alpha : 1f;
                var encoded = format == DecodedBitmapPixelFormat.Rgba16Float
                    ? BitConverter.HalfToUInt16Bits((Half)value)
                    : (ushort)MathF.Round(value * 65535);
                BitConverter.GetBytes(encoded).CopyTo(pixels, channel * 2);
            }
        }
        return new DecodedBitmap(1, 1, pixels, true, PixelFormat: format);
    }
}
