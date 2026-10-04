namespace HdrImageViewer.Presentation;

internal enum ImageLoadOutcome { Opened, Failed, Canceled }

internal static class ImageNavigationContext
{
    public static async Task<IReadOnlyList<string>> NavigateAsync(
        IReadOnlyList<string> paths, int currentIndex, int direction,
        Func<string, Task<ImageLoadOutcome>> open, CancellationToken cancellationToken)
    {
        if (direction is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direction));
        var failures = new List<string>();
        for (var index = currentIndex + direction; index >= 0 && index < paths.Count; index += direction)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await open(paths[index]);
            // Superseded loads must not navigate over the user's newer open.
            if (result != ImageLoadOutcome.Failed) break;
            failures.Add(paths[index]);
        }
        return failures;
    }

    public static IReadOnlyList<string>? ResolveExplicitPaths(
        string path,
        IReadOnlyList<string>? suppliedPaths,
        IReadOnlyList<string> currentPaths,
        bool currentIsExplicit,
        bool preserveCurrent)
    {
        if (suppliedPaths is not null) return suppliedPaths.ToArray();
        return preserveCurrent && currentIsExplicit && currentPaths.Contains(path, StringComparer.OrdinalIgnoreCase)
            ? currentPaths.ToArray()
            : null;
    }
}
