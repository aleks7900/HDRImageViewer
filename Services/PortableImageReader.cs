using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using HdrImageViewer.Models;

namespace HdrImageViewer.Services;

internal static class PortableImageReader
{
    public static DecodedBitmap ReadPpmAsRgba16(
        string path,
        string decoderName,
        DecodedBitmapTransfer transfer,
        bool usesBt2020Primaries,
        CancellationToken cancellationToken = default)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.SequentialScan);
        return ReadPpmAsRgba16(
            stream,
            decoderName,
            transfer,
            usesBt2020Primaries,
            cancellationToken);
    }

    internal static DecodedBitmap ReadPpmAsRgba16(
        Stream stream,
        string decoderName,
        DecodedBitmapTransfer transfer,
        bool usesBt2020Primaries,
        CancellationToken cancellationToken = default)
    {
        var magic = ReadHeaderToken(stream, "PPM header is incomplete.", out _);
        if (!string.Equals(magic, "P6", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("libjxl djxl PPM output was not a binary P6 image.");
        }

        var width = int.Parse(
            ReadHeaderToken(stream, "PPM header is incomplete.", out _),
            CultureInfo.InvariantCulture);
        var height = int.Parse(
            ReadHeaderToken(stream, "PPM header is incomplete.", out _),
            CultureInfo.InvariantCulture);
        var maxValue = int.Parse(
            ReadHeaderToken(stream, "PPM header is incomplete.", out var finalDelimiter),
            CultureInfo.InvariantCulture);
        ConsumeOptionalLineFeed(stream, finalDelimiter);
        if (width <= 0 || height <= 0 || maxValue <= 0 || maxValue > ushort.MaxValue)
        {
            throw new InvalidOperationException(
                $"libjxl djxl PPM output has invalid metadata: {width}x{height}, max {maxValue}.");
        }

        var bytesPerSample = maxValue > byte.MaxValue ? 2 : 1;
        var sourceRowLength = checked(width * 3 * bytesPerSample);
        var pixels = new byte[checked(width * height * 8)];
        var sourceRow = ArrayPool<byte>.Shared.Rent(sourceRowLength);
        try
        {
            for (var y = 0; y < height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReadExactly(
                    stream,
                    sourceRow.AsSpan(0, sourceRowLength),
                    "libjxl djxl PPM output is truncated.");
                var source = 0;
                var destination = checked(y * width * 8);
                for (var x = 0; x < width; x++)
                {
                    if (bytesPerSample == 2)
                    {
                        WritePpmUInt16AsLittleEndian(
                            sourceRow,
                            source,
                            maxValue,
                            pixels,
                            destination);
                        WritePpmUInt16AsLittleEndian(
                            sourceRow,
                            source + 2,
                            maxValue,
                            pixels,
                            destination + 2);
                        WritePpmUInt16AsLittleEndian(
                            sourceRow,
                            source + 4,
                            maxValue,
                            pixels,
                            destination + 4);
                        source += 6;
                    }
                    else
                    {
                        WritePpmByteAsUInt16(
                            sourceRow[source],
                            maxValue,
                            pixels,
                            destination);
                        WritePpmByteAsUInt16(
                            sourceRow[source + 1],
                            maxValue,
                            pixels,
                            destination + 2);
                        WritePpmByteAsUInt16(
                            sourceRow[source + 2],
                            maxValue,
                            pixels,
                            destination + 4);
                        source += 3;
                    }

                    pixels[destination + 6] = 0xFF;
                    pixels[destination + 7] = 0xFF;
                    destination += 8;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(sourceRow);
        }

        return new DecodedBitmap(
            width,
            height,
            pixels,
            ColorManagedToSrgb: false,
            decoderName,
            DecodedBitmapPixelFormat.Rgba16Unorm,
            transfer,
            usesBt2020Primaries,
            usesBt2020Primaries
                ? GainMapColorGamut.Bt2100
                : GainMapColorGamut.Bt709);
    }

    public static DecodedBitmap ReadPfmAsLinearScRgb(
        string path,
        string backendName,
        CancellationToken cancellationToken = default)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.SequentialScan);
        return ReadPfmAsLinearScRgb(stream, backendName, cancellationToken);
    }

    internal static DecodedBitmap ReadPfmAsLinearScRgb(
        Stream stream,
        string backendName,
        CancellationToken cancellationToken = default)
    {
        const string IncompleteHeader = "EXR 中间 PFM 输出无效：文件头不完整。";
        var magic = ReadHeaderToken(stream, IncompleteHeader, out _);
        var channels = magic switch
        {
            "PF" => 3,
            "Pf" => 1,
            _ => throw new InvalidOperationException(
                "EXR 中间 PFM 输出无效：缺少 PF/Pf 文件头。"),
        };
        var width = int.Parse(
            ReadHeaderToken(stream, IncompleteHeader, out _),
            CultureInfo.InvariantCulture);
        var height = int.Parse(
            ReadHeaderToken(stream, IncompleteHeader, out _),
            CultureInfo.InvariantCulture);
        var scale = float.Parse(
            ReadHeaderToken(stream, IncompleteHeader, out var finalDelimiter),
            CultureInfo.InvariantCulture);
        ConsumeOptionalLineFeed(stream, finalDelimiter);
        if (width <= 0 || height <= 0 || !float.IsFinite(scale) || scale == 0.0f)
        {
            throw new InvalidOperationException(
                $"EXR 中间 PFM 输出尺寸或 scale 无效：{width}x{height}, scale {scale}。");
        }

        var littleEndian = scale < 0.0f;
        var sourceRowLength = checked(width * channels * sizeof(float));
        var pixels = new byte[checked(width * height * 8)];
        var sourceRow = ArrayPool<byte>.Shared.Rent(sourceRowLength);
        try
        {
            for (var sourceY = 0; sourceY < height; sourceY++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReadExactly(
                    stream,
                    sourceRow.AsSpan(0, sourceRowLength),
                    "EXR 中间 PFM 输出不完整。");
                var destinationY = height - 1 - sourceY;
                var source = 0;
                var destination = checked(destinationY * width * 8);
                for (var x = 0; x < width; x++)
                {
                    var red = ReadPfmFloat(sourceRow, source, littleEndian);
                    var green = channels == 1
                        ? red
                        : ReadPfmFloat(sourceRow, source + sizeof(float), littleEndian);
                    var blue = channels == 1
                        ? red
                        : ReadPfmFloat(sourceRow, source + (2 * sizeof(float)), littleEndian);
                    WriteHalfLittleEndian(pixels, destination, red);
                    WriteHalfLittleEndian(pixels, destination + 2, green);
                    WriteHalfLittleEndian(pixels, destination + 4, blue);
                    WriteHalfLittleEndian(pixels, destination + 6, 1.0f);
                    source += channels * sizeof(float);
                    destination += 8;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(sourceRow);
        }

        return new DecodedBitmap(
            width,
            height,
            pixels,
            ColorManagedToSrgb: false,
            $"OpenEXR scene-linear via {backendName}",
            DecodedBitmapPixelFormat.Rgba16Float,
            DecodedBitmapTransfer.LinearScRgb,
            UsesBt2020Primaries: false);
    }

    private static string ReadHeaderToken(
        Stream stream,
        string incompleteMessage,
        out int delimiter)
    {
        int value;
        while (true)
        {
            value = stream.ReadByte();
            if (value < 0)
            {
                throw new InvalidOperationException(incompleteMessage);
            }

            if (value == '#')
            {
                SkipComment(stream);
                continue;
            }

            if (!IsAsciiWhitespace(value))
            {
                break;
            }
        }

        var token = new StringBuilder(16);
        do
        {
            token.Append((char)value);
            value = stream.ReadByte();
        }
        while (value >= 0 && !IsAsciiWhitespace(value));

        delimiter = value;
        if (token.Length == 0 || delimiter < 0)
        {
            throw new InvalidOperationException(incompleteMessage);
        }

        return token.ToString();
    }

    private static void SkipComment(Stream stream)
    {
        int value;
        do
        {
            value = stream.ReadByte();
        }
        while (value >= 0 && value != '\n');
    }

    private static void ConsumeOptionalLineFeed(Stream stream, int delimiter)
    {
        if (delimiter != '\r')
        {
            return;
        }

        var value = stream.ReadByte();
        if (value >= 0 && value != '\n')
        {
            if (!stream.CanSeek)
            {
                throw new InvalidOperationException(
                    "Portable image stream must be seekable when a CR header delimiter is not followed by LF.");
            }

            stream.Seek(-1, SeekOrigin.Current);
        }
    }

    private static bool IsAsciiWhitespace(int value)
    {
        return value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' or (byte)'\v' or (byte)'\f';
    }

    private static void ReadExactly(
        Stream stream,
        Span<byte> destination,
        string truncatedMessage)
    {
        var totalRead = 0;
        while (totalRead < destination.Length)
        {
            var read = stream.Read(destination[totalRead..]);
            if (read == 0)
            {
                throw new InvalidOperationException(truncatedMessage);
            }

            totalRead += read;
        }
    }

    private static void WritePpmUInt16AsLittleEndian(
        byte[] source,
        int sourceOffset,
        int maxValue,
        byte[] destination,
        int destinationOffset)
    {
        var value = BinaryPrimitives.ReadUInt16BigEndian(
            source.AsSpan(sourceOffset, sizeof(ushort)));
        var scaled = maxValue == ushort.MaxValue
            ? value
            : (int)Math.Round(value * (ushort.MaxValue / (double)maxValue));
        BinaryPrimitives.WriteUInt16LittleEndian(
            destination.AsSpan(destinationOffset, sizeof(ushort)),
            checked((ushort)scaled));
    }

    private static void WritePpmByteAsUInt16(
        byte value,
        int maxValue,
        byte[] destination,
        int destinationOffset)
    {
        var scaled = maxValue == byte.MaxValue
            ? (value << 8) | value
            : (int)Math.Round(value * (ushort.MaxValue / (double)maxValue));
        BinaryPrimitives.WriteUInt16LittleEndian(
            destination.AsSpan(destinationOffset, sizeof(ushort)),
            checked((ushort)scaled));
    }

    private static float ReadPfmFloat(
        byte[] data,
        int offset,
        bool littleEndian)
    {
        var bits = littleEndian
            ? BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, sizeof(float)))
            : BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset, sizeof(float)));
        return BitConverter.Int32BitsToSingle(bits);
    }

    private static void WriteHalfLittleEndian(
        byte[] data,
        int offset,
        float value)
    {
        if (!float.IsFinite(value))
        {
            value = 0.0f;
        }

        var bits = BitConverter.HalfToUInt16Bits(
            (Half)Math.Clamp(value, -65504.0f, 65504.0f));
        BinaryPrimitives.WriteUInt16LittleEndian(
            data.AsSpan(offset, sizeof(ushort)),
            bits);
    }
}
