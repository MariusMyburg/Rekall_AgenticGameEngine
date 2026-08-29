namespace Rekall.Age.Studio;

internal sealed class RekallAgeStudioProjectTransitionCoordinator
{
    private readonly object _gate = new();
    private CancellationTokenSource? _cancellation;
    private TaskCompletionSource? _completion;

    public RekallAgeStudioProjectTransitionLease? TryBegin()
    {
        lock (_gate)
        {
            if (_cancellation is not null) return null;
            _cancellation = new CancellationTokenSource();
            _completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return new RekallAgeStudioProjectTransitionLease(this, _cancellation.Token);
        }
    }

    public async ValueTask CancelAndWaitAsync()
    {
        CancellationTokenSource? cancellation;
        Task? completion;
        lock (_gate)
        {
            cancellation = _cancellation;
            completion = _completion?.Task;
        }

        cancellation?.Cancel();
        if (completion is not null) await completion.ConfigureAwait(true);
    }

    private void Complete()
    {
        CancellationTokenSource? cancellation;
        TaskCompletionSource? completion;
        lock (_gate)
        {
            cancellation = _cancellation;
            completion = _completion;
            _cancellation = null;
            _completion = null;
        }
        cancellation?.Dispose();
        completion?.TrySetResult();
    }

    internal sealed class RekallAgeStudioProjectTransitionLease(
        RekallAgeStudioProjectTransitionCoordinator owner,
        CancellationToken cancellationToken) : IDisposable
    {
        private RekallAgeStudioProjectTransitionCoordinator? _owner = owner;

        public CancellationToken CancellationToken { get; } = cancellationToken;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Complete();
    }
}
