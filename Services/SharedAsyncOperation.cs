namespace HdrImageViewer.Services;

internal sealed class SharedAsyncOperation<T> : IDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _source = new();
    private readonly Lazy<Task<T>> _completion;
    private int _waiterCount;
    private bool _abandoned;
    private bool _disposed;

    public SharedAsyncOperation(Func<CancellationToken, Task<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        _completion = new Lazy<Task<T>>(
            async () => await operation(_source.Token).ConfigureAwait(false),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public Task<T> Completion => _completion.Value;

    public bool IsAbandoned
    {
        get
        {
            lock (_gate)
            {
                return _abandoned;
            }
        }
    }

    public async Task<T> WaitAsync(CancellationToken cancellationToken = default)
    {
        Task<T> completion;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_abandoned)
            {
                throw new OperationCanceledException("The shared operation no longer has an active owner.");
            }

            _waiterCount++;
            completion = _completion.Value;
        }

        try
        {
            return await completion.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            var cancelSharedOperation = false;
            lock (_gate)
            {
                _waiterCount--;
                if (_waiterCount == 0 && !completion.IsCompleted)
                {
                    _abandoned = true;
                    cancelSharedOperation = true;
                }
            }

            if (cancelSharedOperation)
            {
                try
                {
                    _source.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _source.Dispose();
    }
}
