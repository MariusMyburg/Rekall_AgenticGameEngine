namespace Rekall.Age.Studio;

internal sealed record RekallAgeStudioShutdownResult(
    bool TerminalCleanupComplete,
    int Attempts,
    Exception? Failure);

/// <summary>
/// Bounds renderer-shutdown retries without blocking the WPF dispatcher. A later close request
/// may start another bounded attempt group if native cleanup remains incomplete.
/// </summary>
internal sealed class RekallAgeStudioShutdownCoordinator
{
    private const string IncompleteCode = "REKALL_STUDIO_VULKAN_SHUTDOWN_INCOMPLETE";
    private readonly int _maximumAttempts;
    private readonly TimeSpan _retryDelay;
    private readonly Func<TimeSpan, CancellationToken, ValueTask> _delay;
    private int _attemptInProgress;

    internal RekallAgeStudioShutdownCoordinator(
        int maximumAttempts,
        TimeSpan retryDelay,
        Func<TimeSpan, CancellationToken, ValueTask> delay)
    {
        if (maximumAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maximumAttempts));
        if (retryDelay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(retryDelay));
        _maximumAttempts = maximumAttempts;
        _retryDelay = retryDelay;
        _delay = delay ?? throw new ArgumentNullException(nameof(delay));
    }

    internal bool IsDisposalComplete { get; private set; }

    internal bool IsAttemptInProgress => Volatile.Read(ref _attemptInProgress) != 0;

    internal async ValueTask<RekallAgeStudioShutdownResult> TryShutdownAsync(
        RekallAgeStudioViewModel viewModel,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        if (IsDisposalComplete)
        {
            return new RekallAgeStudioShutdownResult(true, 0, null);
        }
        if (Interlocked.Exchange(ref _attemptInProgress, 1) != 0)
        {
            return new RekallAgeStudioShutdownResult(
                false,
                0,
                new InvalidOperationException("Studio shutdown is already in progress."));
        }

        var failures = new List<Exception>();
        try
        {
            for (var attempt = 1; attempt <= _maximumAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await viewModel.DisposeAsync();
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }

                if (viewModel.IsDisposalComplete)
                {
                    IsDisposalComplete = true;
                    return new RekallAgeStudioShutdownResult(
                        true,
                        attempt,
                        failures.Count == 0
                            ? null
                            : new AggregateException(
                                "Studio shutdown completed with cleanup diagnostics.",
                                failures));
                }

                if (attempt < _maximumAttempts)
                {
                    await _delay(_retryDelay, cancellationToken);
                }
            }

            if (failures.Count == 0)
            {
                failures.Add(new InvalidOperationException(
                    "The preview session did not prove terminal renderer cleanup."));
            }
            return new RekallAgeStudioShutdownResult(
                false,
                _maximumAttempts,
                new AggregateException(
                    $"{IncompleteCode}: Studio kept the Vulkan child HWND alive because renderer cleanup "
                    + $"remained incomplete after {_maximumAttempts} attempts.",
                    failures));
        }
        finally
        {
            Volatile.Write(ref _attemptInProgress, 0);
        }
    }
}
