using System.Runtime.InteropServices;
using HdrImageViewer.Services;
using Xunit;

namespace HdrImageViewer.Tests;

/// <summary>
/// HDR color correctness gate for the in-process libjxl decoder: the RGBA16
/// pixels produced by <see cref="JxlNativeDecoder"/> must be byte-identical to
/// the djxl.exe --bits_per_sample 16 PPM16 path it replaces, for every PQ/HLG
/// JPEG XL fixture under the repo's local test/ directory. The test reports a skip when the local-only fixtures or bundled libjxl binaries are absent
/// (fresh checkouts / CI without the external/ toolchain).
/// </summary>
public sealed class JxlNativeDecoderParityTests
{
    public sealed class JxlFactAttribute : FactAttribute
    {
        public JxlFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("HDRVIEWER_REQUIRE_NATIVE_TESTS") != "1")
                Skip = MissingDependency();
        }
    }

    private static string? MissingDependency() =>
        !RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Requires Windows." :
        FindJxlFixtures().Count == 0 || FindTool("djxl.exe") is null || NativeToolLocator.FindFirstTool("libjxl.dll", "jxl.dll") is null
            ? "Requires HDR JXL fixtures, djxl.exe and a libjxl DLL." : null;

    [JxlFact]
    public async Task InProcessDecodeMatchesDjxlPpm16ForHdrFixtures()
    {
        Assert.Null(MissingDependency());
        var fixtures = FindJxlFixtures();
        var djxl = FindTool("djxl.exe")!;

        foreach (var fixture in fixtures)
        {
            var (width, height, expectedPixels) = await DecodeWithDjxlToRgba16Async(djxl, fixture);
            var actual = JxlNativeDecoder.Decode(
                fixture,
                DecodedBitmapTransfer.Pq,
                usesBt2020Primaries: true,
                colorDescription: "parity test",
                CancellationToken.None);

            Assert.Equal(width, actual.PixelWidth);
            Assert.Equal(height, actual.PixelHeight);
            Assert.Equal(DecodedBitmapPixelFormat.Rgba16Unorm, actual.PixelFormat);
            Assert.Equal(expectedPixels.Length, actual.RgbaPixels.Length);

            var mismatch = DescribeFirstMismatch(expectedPixels, actual.RgbaPixels, width);
            Assert.True(mismatch is null, $"{Path.GetFileName(fixture)}: {mismatch}");
        }
    }

    private static string? DescribeFirstMismatch(byte[] expected, byte[] actual, int width)
    {
        if (expected.AsSpan().SequenceEqual(actual))
        {
            return null;
        }

        var mismatchedSamples = 0;
        var maxDelta = 0;
        var firstIndex = -1;
        for (var i = 0; i + 1 < expected.Length; i += 2)
        {
            int e = expected[i] | (expected[i + 1] << 8);
            int a = actual[i] | (actual[i + 1] << 8);
            if (e != a)
            {
                mismatchedSamples++;
                maxDelta = Math.Max(maxDelta, Math.Abs(e - a));
                if (firstIndex < 0)
                {
                    firstIndex = i;
                }
            }
        }

        var pixel = firstIndex / 8;
        return $"pixels differ from the djxl PPM16 output: {mismatchedSamples} uint16 samples mismatch, max delta {maxDelta}, first at byte {firstIndex} (pixel {pixel % width},{pixel / width}).";
    }

    private static async Task<(int Width, int Height, byte[] Rgba16Pixels)> DecodeWithDjxlToRgba16Async(string djxl, string jxlPath)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "HdrImageViewerTests", "jxl-parity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var ppmPath = Path.Combine(tempDir, "decoded.ppm");
        try
        {
            using var process = NativeProcessRunner.Create(djxl);
            process.StartInfo.ArgumentList.Add(jxlPath);
            process.StartInfo.ArgumentList.Add(ppmPath);
            process.StartInfo.ArgumentList.Add("--quiet");
            process.StartInfo.ArgumentList.Add("--output_format");
            process.StartInfo.ArgumentList.Add("ppm");
            process.StartInfo.ArgumentList.Add("--bits_per_sample");
            process.StartInfo.ArgumentList.Add("16");
            await NativeProcessRunner.RunAsync(process, "libjxl djxl", CancellationToken.None);
            var bitmap = PortableImageReader.ReadPpmAsRgba16(
                ppmPath,
                decoderName: "djxl parity fixture",
                DecodedBitmapTransfer.Pq,
                usesBt2020Primaries: true,
                CancellationToken.None);
            return (bitmap.PixelWidth, bitmap.PixelHeight, bitmap.RgbaPixels);
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

    private static List<string> FindJxlFixtures()
    {
        var fixtures = new List<string>();
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var testDirectory = Path.Combine(directory.FullName, "test");
            if (Directory.Exists(testDirectory))
            {
                fixtures.AddRange(Directory.EnumerateFiles(testDirectory, "*.jxl", SearchOption.AllDirectories));
                if (fixtures.Count > 0)
                {
                    break;
                }
            }

            directory = directory.Parent;
        }

        return fixtures;
    }

    private static string? FindTool(string fileName)
    {
        return NativeToolLocator.FindTool(fileName);
    }
}
