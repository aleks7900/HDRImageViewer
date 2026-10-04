namespace HdrImageViewer.Presentation;

internal static class FilmstripThumbnailWindow
{
    public const int Radius = 36;

    // Loading and retention share this exact window, including at folder ends.
    // Scrolling the strip may move it away from the currently displayed image.
    public static IReadOnlyList<int> GetLoadOrder(int count, int currentIndex, int firstVisible = -1, int lastVisible = -1)
    {
        if (count <= 0) return [];
        var focus = firstVisible >= 0 && lastVisible >= firstVisible && firstVisible < count
            ? firstVisible + (Math.Min(lastVisible, count - 1) - firstVisible) / 2
            : Math.Clamp(currentIndex, 0, count - 1);
        var indices = new List<int>(Radius * 2 + 2) { focus };
        for (var offset = 1; offset <= Radius; offset++)
        {
            if (focus - offset >= 0) indices.Add(focus - offset);
            if (focus + offset < count) indices.Add(focus + offset);
        }
        if (currentIndex >= 0 && currentIndex < count && Math.Abs(currentIndex - focus) > Radius)
            indices.Add(currentIndex);
        return indices;
    }
}
