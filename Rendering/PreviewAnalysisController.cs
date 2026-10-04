namespace HdrImageViewer.Rendering;

/// <summary>
/// Runs pixel-only work away from the render thread and publishes results only
/// while their preview version is current. No graphics resources enter this class.
/// </summary>
internal sealed class PreviewAnalysisController : IDisposable
{
    private readonly object _sync = new();
    private CancellationTokenSource? _pending;
    private long _version;
    private LuminanceSnapshot? _snapshot;
    private bool _disposed;

    public long Version { get { lock (_sync) return _version; } }
    public LuminanceSnapshot? Snapshot { get { lock (_sync) return _snapshot; } }

    public void Invalidate(bool clearSnapshot = false)
    {
        lock (_sync)
        {
            _version++;
            _pending?.Cancel();
            if (clearSnapshot) _snapshot = null;
        }
    }

    public async Task<LuminanceSnapshot?> AnalyzeAsync(
        long version,
        Func<CancellationToken, LuminanceSnapshot> analyze,
        CancellationToken cancellationToken)
    {
        CancellationTokenSource source;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (version != _version) return null;
            _pending?.Cancel();
            source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _pending = source;
            _snapshot = null;
        }

        try
        {
            var snapshot = await Task.Run(() => analyze(source.Token), source.Token);
            lock (_sync)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_disposed || source.IsCancellationRequested
                    || !ReferenceEquals(_pending, source) || version != _version)
                    return null;
                _snapshot = snapshot;
                return snapshot;
            }
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_pending, source)) _pending = null;
                source.Dispose();
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            _pending?.Cancel();
            _snapshot = null;
        }
    }
}
