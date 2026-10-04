namespace HdrImageViewer.Services;

/// <summary>Maps upright image coordinates to the stored, unrotated pixel grid.</summary>
internal readonly record struct ExifOrientationTransform(int SourceWidth, int SourceHeight, int Orientation)
{
    public int Width => SwapsDimensions(Orientation) ? SourceHeight : SourceWidth;

    public int Height => SwapsDimensions(Orientation) ? SourceWidth : SourceHeight;

    public static bool SwapsDimensions(int orientation) => orientation is >= 5 and <= 8;

    public (int X, int Y) MapToSource(int x, int y) => Orientation switch
    {
        2 => (SourceWidth - 1 - x, y),
        3 => (SourceWidth - 1 - x, SourceHeight - 1 - y),
        4 => (x, SourceHeight - 1 - y),
        5 => (y, x),
        6 => (y, SourceHeight - 1 - x),
        7 => (SourceWidth - 1 - y, SourceHeight - 1 - x),
        8 => (SourceWidth - 1 - y, x),
        _ => (x, y),
    };
}
