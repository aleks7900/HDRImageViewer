using System.Numerics;
using HdrImageViewer.Rendering;
using HdrImageViewer.Services;
using Xunit;

namespace HdrImageViewer.Tests;

public sealed class ViewerToolsTests
{
    [Fact]
    public void HistogramExcludesLetterboxAndInvalidPixels()
    {
        var pixels = new byte[4 * 8];
        for (var x = 0; x < 4; x++)
        for (var c = 0; c < 3; c++)
        {
            var bits = BitConverter.HalfToUInt16Bits((Half)(x == 2 ? float.NaN : 1));
            pixels[x * 8 + c * 2] = (byte)bits;
            pixels[x * 8 + c * 2 + 1] = (byte)(bits >> 8);
        }
        var snapshot = new LuminanceSnapshot(4, 1, pixels, new Vector4(0.5f, 1, 0.25f, 0), 7);
        Assert.Equal(1, snapshot.Count);
        Assert.Equal(80, snapshot.AverageNits, 3);
        Assert.Equal(1, snapshot.Histogram.Sum());
        Assert.Equal(1, snapshot.ChromaticityCount);
        Assert.Equal(1, snapshot.ChromaticityBins.Sum());
        Assert.Null(snapshot.Sample(0, 0));
        Assert.Null(snapshot.Sample(2, 0));
        Assert.Equal(Vector3.One, snapshot.Sample(1, 0));
    }

    [Fact]
    public void LuminanceUsesScRgbWhiteAndExtendedRange()
    {
        Assert.Equal(80, LuminanceSnapshot.ToNits(Vector3.One), 3);
        Assert.Equal(1000, LuminanceSnapshot.ToNits(new Vector3(12.5f)), 2);
        Assert.Equal(0, LuminanceSnapshot.HistogramBin(-1));
        Assert.Equal(63, LuminanceSnapshot.HistogramBin(20000));
    }

    [Theory]
    [InlineData(80)]
    [InlineData(203)]
    [InlineData(320)]
    public void RgbHistogramBoundaryTracksSdrWhite(float white)
    {
        Assert.Equal(0.5, LuminanceSnapshot.ChannelPosition(white, white), 6);
        Assert.Equal(0.75, LuminanceSnapshot.ChannelPosition(white * 8, white), 6);
        Assert.Equal(1, LuminanceSnapshot.ChannelPosition(white * 64, white), 6);
        Assert.Equal(128, LuminanceSnapshot.ChannelBin(white, white));
        Assert.Equal(255, LuminanceSnapshot.ChannelBin(white * 128, white));
        Assert.Equal(0, LuminanceSnapshot.ChannelBin(-white, white));
        Assert.InRange(LuminanceSnapshot.ChannelPosition(white * 0.18f, white), 0.23, 0.24);
    }

    [Fact]
    public void RgbHistogramKeepsIndependentChannelsAndRejectsInvalidPixels()
    {
        var values = new[] { new Vector3(-0.5f, 1, 8), new Vector3(float.NaN, 2, 3) };
        var pixels = new byte[16];
        for (var i = 0; i < values.Length; i++)
        for (var c = 0; c < 3; c++)
        {
            var bits = BitConverter.HalfToUInt16Bits((Half)values[i][c]);
            pixels[i * 8 + c * 2] = (byte)bits;
            pixels[i * 8 + c * 2 + 1] = (byte)(bits >> 8);
        }
        var snapshot = new LuminanceSnapshot(2, 1, pixels, new Vector4(1, 1, 0, 0), 1);
        Assert.Equal(1, snapshot.Count);
        Assert.Equal(1, snapshot.RedHistogram[0]);
        Assert.Equal(1, snapshot.GreenHistogram[128]);
        Assert.Equal(1, snapshot.BlueHistogram[192]);
        Assert.Equal(1, snapshot.RedHistogram.Sum());
        Assert.Equal(1, snapshot.GreenHistogram.Sum());
        Assert.Equal(1, snapshot.BlueHistogram.Sum());
    }

    [Theory]
    [InlineData(1, 2, 3, "123456")]
    [InlineData(2, 2, 3, "214365")]
    [InlineData(3, 2, 3, "654321")]
    [InlineData(4, 2, 3, "563412")]
    [InlineData(5, 3, 2, "135246")]
    [InlineData(6, 3, 2, "531642")]
    [InlineData(7, 3, 2, "642531")]
    [InlineData(8, 3, 2, "246135")]
    public void BgraOrientationHandlesEveryExifTransform(int orientation, int width, int height, string expected)
    {
        var source = new byte[24];
        for (var i = 0; i < 6; i++) source[i * 4] = (byte)(i + 1);
        var result = BgraOrientation.Apply(source, 2, 3, orientation, CancellationToken.None);
        Assert.Equal(width, result.Width); Assert.Equal(height, result.Height);
        Assert.Equal(expected, string.Concat(Enumerable.Range(0, 6).Select(i => result.Pixels[i * 4].ToString())));
    }
}
