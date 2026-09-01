using System.Runtime.InteropServices;
using HdrImageViewer.Services;
using Xunit;

namespace HdrImageViewer.Tests;

/// <summary>
/// Integration gate for the EXR paths: encode a deterministic half-float
/// gradient with <see cref="NativeExrDecoder.Encode"/>, decode it with
/// <see cref="NativeExrDecoder.Decode"/>, then force the fallback by feeding the
/// same EXR through <c>oiiotool</c>/<c>magick</c> → PFM →
/// <see cref="PortableImageReader.ReadPfmAsLinearScRgb"/> and assert the two
/// produce identical pixels. Skips silently when the native EXR library or an
/// external EXR→PFM converter is not available (fresh checkouts / CI).
/// </summary>
public sealed class ExrNativeDecoderParityTests
{
    [Fact]
    public void EncodeThenNativeDecodeRoundTripsRgba16f()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || !NativeExrDecoder.IsAvailable)
        {
            return;
        }

        const int width = 64;
        const int height = 48;
        var pixels = CreateDeterministicRgba16f(width, height);
        var tempDir = Path.Combine(Path.GetTempPath(), "HdrImageViewerTests", "exr-roundtrip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var exrPath = Path.Combine(tempDir, "fixture.exr");
        try
        {
            NativeExrDecoder.Encode(exrPath, width, height, pixels);
            var decoded = NativeExrDecoder.Decode(exrPath);

            Assert.Equal(width, decoded.PixelWidth);
            Assert.Equal(height, decoded.PixelHeight);
            Assert.Equal(DecodedBitmapPixelFormat.Rgba16Float, decoded.PixelFormat);
            Assert.Equal(DecodedBitmapTransfer.LinearScRgb, decoded.Transfer);
            Assert.Equal(pixels, decoded.RgbaPixels);
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public async Task ExternalPfmFallbackMatchesNativeDecode()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || !NativeExrDecoder.IsAvailable)
        {
            return;
        }

        var converter = FindExrToPfmConverter();
        if (converter is null)
        {
            return;
        }

        const int width = 32;
        const int height = 24;
        var pixels = CreateDeterministicRgba16f(width, height);
        var tempDir = Path.Combine(Path.GetTempPath(), "HdrImageViewerTests", "exr-fallback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var exrPath = Path.Combine(tempDir, "fixture.exr");
        var pfmPath = Path.Combine(tempDir, "decoded.pfm");
        try
        {
            NativeExrDecoder.Encode(exrPath, width, height, pixels);
            var native = NativeExrDecoder.Decode(exrPath);

            using (var process = NativeProcessRunner.Create(converter))
            {
                process.StartInfo.ArgumentList.Add(exrPath);
                if (Path.GetFileNameWithoutExtension(converter).Equals("magick", StringComparison.OrdinalIgnoreCase))
                {
                    process.StartInfo.ArgumentList.Add("-colorspace");
                    process.StartInfo.ArgumentList.Add("RGB");
                    process.StartInfo.ArgumentList.Add("-define");
                    process.StartInfo.ArgumentList.Add("quantum:format=floating-point");
                    process.StartInfo.ArgumentList.Add("-depth");
                    process.StartInfo.ArgumentList.Add("32");
                }
                else
                {
                    process.StartInfo.ArgumentList.Add("-o");
                }

                process.StartInfo.ArgumentList.Add(pfmPath);
                await NativeProcessRunner.RunAsync(process, "EXR→PFM parity fixture", CancellationToken.None);
            }

            var fallback = PortableImageReader.ReadPfmAsLinearScRgb(pfmPath, "parity-test", CancellationToken.None);
            Assert.Equal(native.PixelWidth, fallback.PixelWidth);
            Assert.Equal(native.PixelHeight, fallback.PixelHeight);
            Assert.Equal(DecodedBitmapPixelFormat.Rgba16Float, fallback.PixelFormat);
            Assert.Equal(DecodedBitmapTransfer.LinearScRgb, fallback.Transfer);

            // Half-float PFM→EXR round trip is lossless for the values used
            // here, so the two pipelines must agree byte-for-byte.
            Assert.Equal(native.RgbaPixels, fallback.RgbaPixels);
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static string? FindExrToPfmConverter()
    {
        return NativeToolLocator.FindTool("oiiotool.exe")
            ?? NativeToolLocator.FindTool("magick.exe");
    }

    private static byte[] CreateDeterministicRgba16f(int width, int height)
    {
        var pixels = new byte[width * height * 8];
        var destination = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                WriteHalf(pixels, destination, (float)x / Math.Max(1, width - 1) * 4.0f);
                WriteHalf(pixels, destination + 2, (float)y / Math.Max(1, height - 1) * 3.0f);
                WriteHalf(pixels, destination + 4, ((x + y) & 1) == 0 ? 0.5f : 2.0f);
                WriteHalf(pixels, destination + 6, 1.0f);
                destination += 8;
            }
        }

        return pixels;
    }

    private static void WriteHalf(byte[] buffer, int offset, float value)
    {
        var bits = BitConverter.HalfToUInt16Bits((Half)value);
        buffer[offset] = (byte)(bits & 0xFF);
        buffer[offset + 1] = (byte)(bits >> 8);
    }
}
