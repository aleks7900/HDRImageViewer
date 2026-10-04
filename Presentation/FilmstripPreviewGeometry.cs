namespace HdrImageViewer.Presentation;

internal sealed class FilmstripPreviewGeometry
{
    public double Width { get; private set; } = 80;
    public bool HasDimensions { get; private set; }

    public bool SetDimensions(double width, double height, bool decodedPixels = false)
    {
        if ((!decodedPixels && HasDimensions) || !double.IsFinite(width) || !double.IsFinite(height)
            || width <= 0 || height <= 0) return false;
        HasDimensions = true;
        var target = Math.Clamp(46.0 * width / height, 20.0, 184.0) + 10.0;
        // Ignore rounding differences in downsampled dimensions, but never
        // lock an incorrect shell/header aspect ratio over the actual pixels.
        if (Math.Abs(target - Width) < 0.5) return false;
        Width = target;
        return true;
    }

    public void Invalidate() => HasDimensions = false;
}
