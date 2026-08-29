namespace Rekall.Age.Studio;

internal readonly record struct RekallAgeStudioViewportVisualState(
    bool PresentationSurfaceVisible,
    bool PlaceholderVisible);

/// <summary>
/// Owns the World viewport's unavailable/retry policy independently of WPF property-change
/// notifications. In particular, an initial false value is still an explicit visual state.
/// </summary>
internal sealed class RekallAgeStudioViewportRecoveryState
{
    private readonly TimeSpan _retryInterval;
    private bool _retryPending;
    private DateTimeOffset _nextRetryAt;

    internal RekallAgeStudioViewportRecoveryState(TimeSpan retryInterval)
    {
        if (retryInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(retryInterval));
        _retryInterval = retryInterval;
    }

    internal RekallAgeStudioViewportVisualState Synchronize(
        bool hasProject,
        bool viewportAvailable,
        DateTimeOffset now)
    {
        if (!hasProject || viewportAvailable)
        {
            _retryPending = false;
            _nextRetryAt = default;
            return new RekallAgeStudioViewportVisualState(
                PresentationSurfaceVisible: hasProject && viewportAvailable,
                PlaceholderVisible: !viewportAvailable);
        }

        if (!_retryPending)
        {
            _retryPending = true;
            _nextRetryAt = now;
        }

        return new RekallAgeStudioViewportVisualState(
            PresentationSurfaceVisible: false,
            PlaceholderVisible: true);
    }

    internal bool TryBeginAutomaticRetry(DateTimeOffset now)
    {
        if (!_retryPending || now < _nextRetryAt) return false;
        _nextRetryAt = now + _retryInterval;
        return true;
    }
}
