namespace Rekall.Age.Studio;

internal readonly record struct RekallAgeStudioViewportVisualState(
    bool PresentationSurfaceVisible,
    bool PlaceholderVisible,
    bool NativeAirspaceVisible);

internal enum RekallAgeStudioViewportTickAction
{
    None,
    RecoverPresentation,
    RefreshEditDependencies,
    AdvanceSimulation
}

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
        DateTimeOffset now) =>
        Synchronize(hasProject, viewportAvailable, hasPresentableMetrics: true, now);

    internal RekallAgeStudioViewportVisualState Synchronize(
        bool hasProject,
        bool viewportAvailable,
        bool hasPresentableMetrics,
        DateTimeOffset now)
    {
        if (!hasProject)
        {
            _retryPending = false;
            _nextRetryAt = default;
            return new RekallAgeStudioViewportVisualState(
                PresentationSurfaceVisible: false,
                PlaceholderVisible: false,
                NativeAirspaceVisible: false);
        }

        if (viewportAvailable)
        {
            _retryPending = false;
            _nextRetryAt = default;
            return new RekallAgeStudioViewportVisualState(
                PresentationSurfaceVisible: true,
                PlaceholderVisible: false,
                NativeAirspaceVisible: true);
        }

        // A newly opened project cannot obtain Vulkan surface metrics while the HwndHost
        // itself is hidden. Give the host one layout pass, but keep its child presentation
        // hidden. Once metrics exist, either Vulkan succeeds or the ordinary unavailable
        // placeholder can safely replace the native airspace.
        if (!hasPresentableMetrics)
        {
            return new RekallAgeStudioViewportVisualState(
                PresentationSurfaceVisible: false,
                PlaceholderVisible: false,
                NativeAirspaceVisible: true);
        }

        if (!_retryPending)
        {
            _retryPending = true;
            _nextRetryAt = now;
        }

        return new RekallAgeStudioViewportVisualState(
            PresentationSurfaceVisible: false,
            PlaceholderVisible: true,
            NativeAirspaceVisible: false);
    }

    internal bool TryBeginAutomaticRetry(DateTimeOffset now)
    {
        if (!_retryPending || now < _nextRetryAt) return false;
        _nextRetryAt = now + _retryInterval;
        return true;
    }

    internal RekallAgeStudioViewportTickAction SelectTickAction(
        bool hasProject,
        bool viewportAvailable,
        bool isSimulating,
        DateTimeOffset now)
    {
        if (!hasProject) return RekallAgeStudioViewportTickAction.None;
        if (!viewportAvailable)
        {
            return TryBeginAutomaticRetry(now)
                ? RekallAgeStudioViewportTickAction.RecoverPresentation
                : RekallAgeStudioViewportTickAction.None;
        }

        return isSimulating
            ? RekallAgeStudioViewportTickAction.AdvanceSimulation
            : RekallAgeStudioViewportTickAction.RefreshEditDependencies;
    }
}
