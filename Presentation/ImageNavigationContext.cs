namespace HdrImageViewer.Presentation;

internal static class ImageNavigationContext
{
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
