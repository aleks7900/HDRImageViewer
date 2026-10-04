namespace HdrImageViewer.Services;

internal static class BgraOrientation
{
    public static (byte[] Pixels, int Width, int Height) Apply(byte[] source, int width, int height, int orientation, CancellationToken cancellationToken)
    {
        if (source.Length != checked(width * height * 4)) throw new ArgumentException("Invalid BGRA dimensions.");
        if (orientation is < 2 or > 8) return (source, width, height);
        var transform = new ExifOrientationTransform(width, height, orientation);
        var outputWidth = transform.Width;
        var outputHeight = transform.Height;
        var output = new byte[source.Length];
        for (var y = 0; y < outputHeight; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < outputWidth; x++)
            {
                var (sx, sy) = transform.MapToSource(x, y);
                Buffer.BlockCopy(source, (sy * width + sx) * 4, output, (y * outputWidth + x) * 4, 4);
            }
        }
        return (output, outputWidth, outputHeight);
    }
}
