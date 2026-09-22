namespace HdrImageViewer.Services;

internal static class BgraOrientation
{
    public static (byte[] Pixels, int Width, int Height) Apply(byte[] source, int width, int height, int orientation, CancellationToken cancellationToken)
    {
        if (source.Length != checked(width * height * 4)) throw new ArgumentException("Invalid BGRA dimensions.");
        if (orientation is < 2 or > 8) return (source, width, height);
        var outputWidth = orientation >= 5 ? height : width;
        var outputHeight = orientation >= 5 ? width : height;
        var output = new byte[source.Length];
        for (var y = 0; y < outputHeight; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var x = 0; x < outputWidth; x++)
            {
                var (sx, sy) = orientation switch
                {
                    2 => (width - 1 - x, y), 3 => (width - 1 - x, height - 1 - y),
                    4 => (x, height - 1 - y), 5 => (y, x), 6 => (y, height - 1 - x),
                    7 => (width - 1 - y, height - 1 - x), _ => (width - 1 - y, x)
                };
                Buffer.BlockCopy(source, (sy * width + sx) * 4, output, (y * outputWidth + x) * 4, 4);
            }
        }
        return (output, outputWidth, outputHeight);
    }
}
