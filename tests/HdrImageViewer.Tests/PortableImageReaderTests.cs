using System.Buffers.Binary;
using System.Text;
using HdrImageViewer.Services;
using Xunit;

namespace HdrImageViewer.Tests;

public sealed class PortableImageReaderTests
{
    [Fact]
    public void ReadPpmAsRgba16_PreservesWhitespaceValuedFirstPixelByteAfterCrLf()
    {
        var header = Encoding.ASCII.GetBytes("P6\r\n2 1\r\n255\r\n");
        byte[] samples = [0x20, 0x0A, 0xFF, 0x01, 0x02, 0x03];
        using var stream = new MemoryStream([.. header, .. samples]);

        var bitmap = PortableImageReader.ReadPpmAsRgba16(
            stream,
            "test",
            DecodedBitmapTransfer.Pq,
            usesBt2020Primaries: true);

        Assert.Equal(2, bitmap.PixelWidth);
        Assert.Equal(1, bitmap.PixelHeight);
        Assert.Equal(0x2020, ReadUInt16(bitmap.RgbaPixels, 0));
        Assert.Equal(0x0A0A, ReadUInt16(bitmap.RgbaPixels, 2));
        Assert.Equal(0xFFFF, ReadUInt16(bitmap.RgbaPixels, 4));
        Assert.Equal(0x0101, ReadUInt16(bitmap.RgbaPixels, 8));
        Assert.Equal(0xFFFF, ReadUInt16(bitmap.RgbaPixels, 6));
    }

    [Fact]
    public void ReadPpmAsRgba16_ConvertsBigEndianSixteenBitSamples()
    {
        var header = Encoding.ASCII.GetBytes("P6\n1 1\n1023\n");
        byte[] samples = [0x03, 0xFF, 0x02, 0x00, 0x00, 0x00];
        using var stream = new MemoryStream([.. header, .. samples]);

        var bitmap = PortableImageReader.ReadPpmAsRgba16(
            stream,
            "test",
            DecodedBitmapTransfer.Hlg,
            usesBt2020Primaries: true);

        Assert.Equal(ushort.MaxValue, ReadUInt16(bitmap.RgbaPixels, 0));
        Assert.Equal(32800, ReadUInt16(bitmap.RgbaPixels, 2));
        Assert.Equal(0, ReadUInt16(bitmap.RgbaPixels, 4));
    }

    [Fact]
    public void ReadPfmAsLinearScRgb_FlipsBottomUpRowsAndReadsLittleEndian()
    {
        var bytes = new List<byte>(Encoding.ASCII.GetBytes("PF\n1 2\n-1.0\n"));
        AppendFloat(bytes, 0.25f);
        AppendFloat(bytes, 0.5f);
        AppendFloat(bytes, 0.75f);
        AppendFloat(bytes, 1.0f);
        AppendFloat(bytes, 2.0f);
        AppendFloat(bytes, 3.0f);
        using var stream = new MemoryStream(bytes.ToArray());

        var bitmap = PortableImageReader.ReadPfmAsLinearScRgb(stream, "test");

        Assert.Equal(1.0f, ReadHalf(bitmap.RgbaPixels, 0), 3);
        Assert.Equal(2.0f, ReadHalf(bitmap.RgbaPixels, 2), 3);
        Assert.Equal(3.0f, ReadHalf(bitmap.RgbaPixels, 4), 3);
        Assert.Equal(0.25f, ReadHalf(bitmap.RgbaPixels, 8), 3);
        Assert.Equal(0.5f, ReadHalf(bitmap.RgbaPixels, 10), 3);
        Assert.Equal(0.75f, ReadHalf(bitmap.RgbaPixels, 12), 3);
    }

    private static void AppendFloat(List<byte> bytes, float value)
    {
        Span<byte> encoded = stackalloc byte[sizeof(float)];
        BinaryPrimitives.WriteInt32LittleEndian(encoded, BitConverter.SingleToInt32Bits(value));
        bytes.AddRange(encoded.ToArray());
    }

    private static ushort ReadUInt16(byte[] bytes, int offset)
    {
        return BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, sizeof(ushort)));
    }

    private static float ReadHalf(byte[] bytes, int offset)
    {
        return (float)BitConverter.UInt16BitsToHalf(ReadUInt16(bytes, offset));
    }
}
