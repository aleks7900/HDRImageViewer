namespace HdrImageViewer.Presentation;

// Keep work shared by overlapping viewport windows. Cancellation is scoped to
// a path, while the semaphore limits all generations together. Callers own UI
// publication; this class never changes the captured synchronization context.
internal sealed class RetainedThumbnailLoads(CancellationToken lifetimeToken, int concurrency = 4) : IDisposable
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _slots = new(concurrency);
    private readonly Dictionary<string, Operation> _operations = new(StringComparer.OrdinalIgnoreCase);
    private int _activeCount;
    private bool _disposed;

    public Task WhenIdleAsync()
    {
        lock (_gate) return Task.WhenAll(_operations.Values.Select(operation => operation.Completion.Task));
    }

    public void Queue(string path, Func<CancellationToken, Task> load, Action? finished = null)
    {
        Operation operation;
        Task? predecessor;
        lock (_gate)
        {
            if (_disposed) return;
            _operations.TryGetValue(path, out var previous);
            if (previous is not null && !previous.Source.IsCancellationRequested) return;
            predecessor = previous?.Completion.Task;
            operation = new Operation(CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken));
            _operations[path] = operation;
            _activeCount++;
        }
        _ = RunAsync(path, operation, predecessor, load, finished);
    }

    public void KeepOnly(IReadOnlySet<string> keepPaths)
    {
        Operation[] removed;
        lock (_gate)
        {
            removed = _operations.Where(pair => !keepPaths.Contains(pair.Key)).Select(pair => pair.Value).ToArray();
        }
        foreach (var operation in removed) TryCancel(operation.Source);
    }

    public void Invalidate(string path)
    {
        Operation? operation;
        lock (_gate) _operations.TryGetValue(path, out operation);
        if (operation is not null) TryCancel(operation.Source);
    }

    public void Dispose()
    {
        Operation[] pending;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            pending = _operations.Values.ToArray();
            if (_activeCount == 0) _slots.Dispose();
        }
        foreach (var operation in pending) TryCancel(operation.Source);
    }

    private async Task RunAsync(string path, Operation operation, Task? predecessor, Func<CancellationToken, Task> load, Action? finished)
    {
        var acquired = false;
        try
        {
            // Do not cancel this wait: a canceled waiter must still form a
            // barrier for a later generation until the original native call
            // releases its file/decoder resources.
            if (predecessor is not null) await predecessor;
            var token = operation.Source.Token;
            token.ThrowIfCancellationRequested();
            await _slots.WaitAsync(token);
            acquired = true;
            token.ThrowIfCancellationRequested();
            await load(token);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // The presentation callback reports per-item errors. Always
            // observe a callback fault so a fire-and-forget queue stays safe.
        }
        finally
        {
            try { finished?.Invoke(); }
            catch { }
            if (acquired) _slots.Release();
            lock (_gate)
            {
                if (_operations.TryGetValue(path, out var current) && ReferenceEquals(current, operation))
                    _operations.Remove(path);
                _activeCount--;
                if (_disposed && _activeCount == 0) _slots.Dispose();
            }
            operation.Source.Dispose();
            operation.Completion.TrySetResult();
        }
    }

    private static void TryCancel(CancellationTokenSource source)
    {
        try { source.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private sealed class Operation(CancellationTokenSource source)
    {
        public CancellationTokenSource Source { get; } = source;
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
