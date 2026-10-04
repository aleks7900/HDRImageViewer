using System.Numerics;
using HdrImageViewer.Rendering;
using HdrImageViewer.Services;
using Xunit;

namespace HdrImageViewer.Tests;

public sealed class HdrThumbnailAppearanceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void GainMapThumbnail_PreservesAuthoredSdrBaseRegardlessOfHdrBoost(int apple)
    {
        var primary = new DecodedBitmap(3, 1,
            [128, 128, 128, 255, 220, 100, 40, 255, 20, 180, 230, 255], false);
        var gain = new DecodedBitmap(3, 1,
            [255, 0, 128, 255, 0, 255, 64, 255, 128, 64, 255, 255], false);
        var constants = new GainMapShaderConstants
        {
            GainMapControl = new Vector4(1, apple, 0, 0),
            GainMapMax = new Vector4(8),
            Gamma = Vector4.One,
            Orientation = new Vector4(1, 0, 0, 0),
        };
        var actual = ThumbnailPixelConverter.ConvertGainMapBaseToBgra8(new(primary, gain, constants), CancellationToken.None);
        Assert.Equal(new byte[] { 128, 128, 128, 255, 40, 100, 220, 255, 230, 180, 20, 255 }, actual);
    }

    [Theory]
    [InlineData(1, "123456")]
    [InlineData(2, "321654")]
    [InlineData(3, "654321")]
    [InlineData(4, "456123")]
    [InlineData(5, "142536")]
    [InlineData(6, "415263")]
    [InlineData(7, "635241")]
    [InlineData(8, "362514")]
    public void GainMapSdrBase_RespectsExifOrientation(int orientation, string order)
    {
        var pixels = Enumerable.Range(1, 6).SelectMany(v => new byte[] { (byte)(v * 30), (byte)(v * 30), (byte)(v * 30), 255 }).ToArray();
        var primary = new DecodedBitmap(3, 2, pixels, false);
        var constants = new GainMapShaderConstants { Orientation = new Vector4(orientation, 0, 0, 0) };
        var actual = ThumbnailPixelConverter.ConvertGainMapBaseToBgra8(new(primary, primary, constants), CancellationToken.None);
        Assert.Equal(order.SelectMany(c => new byte[] { (byte)((c - '0') * 30), (byte)((c - '0') * 30), (byte)((c - '0') * 30), 255 }).ToArray(), actual);
    }

    [Fact]
    public void GainMapSdrBase_ConvertsTaggedDisplayP3ToSrgb()
    {
        var primary = new DecodedBitmap(1, 1, [153, 102, 51, 255], false);
        var constants = new GainMapShaderConstants { GainMapControl = new Vector4(1, 0, 1, 0) };
        Assert.Equal(new byte[] { 38, 99, 162, 255 },
            ThumbnailPixelConverter.ConvertGainMapBaseToBgra8(new(primary, primary, constants), CancellationToken.None));
    }

    [Fact]
    public void GainMapSdrBase_DoesNotConvertAlreadyColorManagedPixelsAgain()
    {
        var primary = new DecodedBitmap(1, 1, [153, 102, 51, 255], true);
        var constants = new GainMapShaderConstants
        {
            GainMapControl = new Vector4(1, 0, 1, 0),
            SourceEncoding = new Vector4(1, 0, 0, 0),
        };
        Assert.Equal(new byte[] { 51, 102, 153, 255 },
            ThumbnailPixelConverter.ConvertGainMapBaseToBgra8(new(primary, primary, constants), CancellationToken.None));
    }

    [Fact]
    public void AbsoluteHdrAndDisplayScRgb_UseTheirOwnWhiteReferences()
    {
        // 18% diffuse gray at the viewer's 203-nit HDR reference, versus
        // the same relative gray in display-referred scRGB (80-nit white).
        var hdr = FloatImage(DecodedBitmapTransfer.LinearSceneScRgb, new Vector4(0.18f * 203 / 80, 0.18f * 203 / 80, 0.18f * 203 / 80, 1));
        var scRgb = FloatImage(DecodedBitmapTransfer.LinearScRgb, new Vector4(0.18f, 0.18f, 0.18f, 1));
        var hdrPixels = ThumbnailPixelConverter.ConvertHdrPreviewToBgra8(hdr, CancellationToken.None);
        var scRgbPixels = ThumbnailPixelConverter.ConvertHdrPreviewToBgra8(scRgb, CancellationToken.None);
        Assert.Equal(new byte[] { 118, 118, 118, 255 }, hdrPixels);
        Assert.Equal(hdrPixels, scRgbPixels);
    }

    [Fact]
    public void ColoredHighlight_CompressesAllChannelsByTheSameFactor()
    {
        var source = FloatImage(DecodedBitmapTransfer.LinearSceneScRgb, new Vector4(4, 2, 1, 1) * new Vector4(203f / 80, 203f / 80, 203f / 80, 1));
        var actual = ThumbnailPixelConverter.ConvertHdrPreviewToBgra8(source, CancellationToken.None);
        var rgb = HdrColorMath.SrgbToLinear(new Vector3(actual[2], actual[1], actual[0]) / 255);
        Assert.Equal(255, actual[2]);
        Assert.InRange(rgb.Y / rgb.X, 0.49f, 0.51f);
        Assert.InRange(rgb.Z / rgb.X, 0.24f, 0.26f);
    }

    [Fact]
    public void TransparentAndInvalidPixels_DoNotBrightenOrPoisonPreview()
    {
        var source = FloatImage(DecodedBitmapTransfer.LinearScRgb,
            new Vector4(10000, 10000, 10000, 0), new Vector4(1, 1, 1, 0.5f),
            new Vector4(float.NaN, float.PositiveInfinity, -1, 1));
        var actual = ThumbnailPixelConverter.ConvertHdrPreviewToBgra8(source, CancellationToken.None);
        Assert.Equal(new byte[] { 0, 0, 0, 0, 128, 128, 128, 128, 0, 0, 0, 255 }, actual);
    }

    private static DecodedBitmap FloatImage(DecodedBitmapTransfer transfer, params Vector4[] values)
    {
        var bytes = new byte[values.Length * 8];
        for (var i = 0; i < values.Length; i++)
        {
            BitConverter.GetBytes((Half)values[i].X).CopyTo(bytes, i * 8);
            BitConverter.GetBytes((Half)values[i].Y).CopyTo(bytes, i * 8 + 2);
            BitConverter.GetBytes((Half)values[i].Z).CopyTo(bytes, i * 8 + 4);
            BitConverter.GetBytes((Half)values[i].W).CopyTo(bytes, i * 8 + 6);
        }
        return new DecodedBitmap(values.Length, 1, bytes, false,
            PixelFormat: DecodedBitmapPixelFormat.Rgba16Float, Transfer: transfer);
    }
}
