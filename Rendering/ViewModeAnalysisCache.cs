namespace HdrImageViewer.Rendering;

/// <summary>Render-thread cache; view-only changes reuse each mode's analysis.</summary>
internal sealed class ViewModeAnalysisCache<T>
{
    private readonly Dictionary<GainmapViewMode, T> _entries = new();

    public T GetOrCreate(GainmapViewMode mode, Func<T> create)
    {
        if (_entries.TryGetValue(mode, out var value)) return value;
        value = create();
        _entries.Add(mode, value);
        return value;
    }

    public void Clear() => _entries.Clear();
}
