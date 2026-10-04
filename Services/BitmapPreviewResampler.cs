using System.Diagnostics;

namespace HdrImageViewer.Services;

internal static class BitmapPreviewResampler
{
    internal static DecodedBitmap Downscale(
        DecodedBitmap bitmap,
        int? maxPixelSize,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryCalculateScaledSize(bitmap.PixelWidth, bitmap.PixelHeight, maxPixelSize, out var scaledWidth, out var scaledHeight))
        {
            return bitmap;
        }

        var timer = Stopwatch.StartNew();
        var destinationWidth = checked((int)scaledWidth);
        var destinationHeight = checked((int)scaledHeight);
        var bytesPerPixel = bitmap.BytesPerPixel;
        var destination = new byte[checked(destinationWidth * destinationHeight * bytesPerPixel)];
        System.Threading.Tasks.Parallel.For(0, destinationHeight, new ParallelOptions { CancellationToken = cancellationToken }, y =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceY = Math.Min(bitmap.PixelHeight - 1, (int)((long)y * bitmap.PixelHeight / destinationHeight));
            var sourceRow = checked(sourceY * bitmap.PixelWidth * bytesPerPixel);
            var destinationRow = checked(y * destinationWidth * bytesPerPixel);
            for (var x = 0; x < destinationWidth; x++)
            {
                var sourceX = Math.Min(bitmap.PixelWidth - 1, (int)((long)x * bitmap.PixelWidth / destinationWidth));
                System.Buffer.BlockCopy(
                    bitmap.RgbaPixels,
                    checked(sourceRow + (sourceX * bytesPerPixel)),
                    destination,
                    checked(destinationRow + (x * bytesPerPixel)),
                    bytesPerPixel);
            }
        });
        var scaleMs = timer.ElapsedMilliseconds;

        return bitmap with
        {
            PixelWidth = destinationWidth,
            PixelHeight = destinationHeight,
            RgbaPixels = destination,
            DecoderName = $"{bitmap.DecoderName} [preview downscale {bitmap.PixelWidth}x{bitmap.PixelHeight}->{destinationWidth}x{destinationHeight} {scaleMs}ms]",
        };
    }

    internal static bool TryCalculateScaledSize(
        int width,
        int height,
        int? maxPixelSize,
        out uint scaledWidth,
        out uint scaledHeight)
    {
        scaledWidth = 0;
        scaledHeight = 0;
        if (maxPixelSize is null || maxPixelSize <= 0 || width <= 0 || height <= 0)
        {
            return false;
        }

        var largerSide = Math.Max(width, height);
        if (largerSide <= maxPixelSize.Value)
        {
            return false;
        }

        var scale = maxPixelSize.Value / (double)largerSide;
        scaledWidth = Math.Max(1u, (uint)Math.Round(width * scale));
        scaledHeight = Math.Max(1u, (uint)Math.Round(height * scale));
        return true;
    }

}
