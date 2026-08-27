using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Player.Windows;

/// <summary>
/// Accumulates raw keyboard/mouse device-fact events between successive
/// <see cref="ConsumeRuntimeInput"/> calls and turns them into <see cref="RekallAgeRuntimeInputState"/>,
/// the same generic runtime input contract <c>Rekall.Age.Player.Web.RekallAgeWebInputBridge</c>
/// produces for the web player. <c>Program.cs</c>'s real Veldrid/SDL2 window feeds this class one raw
/// key/button/motion fact at a time as they arrive from <c>_window.PumpEvents()</c> -- extracting that
/// bookkeeping here (rather than leaving it as inline instance-field mutation on the player class) means
/// the actual physical-key-through-gameplay path can be exercised by a unit test with no window, no OS
/// focus, and no human at a keyboard required.
/// </summary>
public sealed class RekallAgeWindowsInputBridge
{
    private readonly HashSet<string> _pressedKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pressedKeysThisFrame = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _releasedKeysThisFrame = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pressedButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pressedButtonsThisFrame = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _releasedButtonsThisFrame = new(StringComparer.OrdinalIgnoreCase);
    private double _pendingMouseDeltaX;
    private double _pendingMouseDeltaY;
    private double _pendingMouseWheelDelta;

    public bool IsPressed(string key) => _pressedKeys.Contains(key);

    public bool IsPressedThisFrame(string key) => _pressedKeysThisFrame.Contains(key);

    /// <summary>Records one raw key transition. Returns true only on an actual edge (a key going down
    /// that was not already held, or a key going up that was held) -- callers use this to know whether
    /// the event is new, e.g. to trigger a first-press action.</summary>
    public bool RecordKey(string key, bool down)
    {
        if (down)
        {
            var isNewPress = _pressedKeys.Add(key);
            if (isNewPress)
            {
                _pressedKeysThisFrame.Add(key);
            }

            return isNewPress;
        }

        var wasReleased = _pressedKeys.Remove(key);
        if (wasReleased)
        {
            _releasedKeysThisFrame.Add(key);
        }

        return wasReleased;
    }

    /// <summary>Records one raw mouse button transition. Same edge-only-on-change contract as
    /// <see cref="RecordKey"/>.</summary>
    public bool RecordMouseButton(string button, bool down)
    {
        if (down)
        {
            var isNewPress = _pressedButtons.Add(button);
            if (isNewPress)
            {
                _pressedButtonsThisFrame.Add(button);
            }

            return isNewPress;
        }

        var wasReleased = _pressedButtons.Remove(button);
        if (wasReleased)
        {
            _releasedButtonsThisFrame.Add(button);
        }

        return wasReleased;
    }

    public void RecordMouseDelta(double deltaX, double deltaY)
    {
        _pendingMouseDeltaX += deltaX;
        _pendingMouseDeltaY += deltaY;
    }

    public void RecordMouseWheel(double delta)
    {
        _pendingMouseWheelDelta += delta;
    }

    /// <summary>Drops any accumulated-but-unconsumed mouse motion. Used when mouse capture toggles, the
    /// same reset <c>Program.cs</c>'s own <c>SetMouseCapture</c> already performed on its raw fields.</summary>
    public void ResetPendingMouseDelta()
    {
        _pendingMouseDeltaX = 0;
        _pendingMouseDeltaY = 0;
    }

    /// <summary>Turns everything accumulated since the last call into one <see cref="RekallAgeRuntimeInputState"/>
    /// and clears the per-frame edge sets (held state persists). Mirrors an idle fast path when nothing at all
    /// changed, matching <c>Program.cs</c>'s prior inline behavior exactly.</summary>
    public RekallAgeRuntimeInputState ConsumeRuntimeInput(
        double mouseX,
        double mouseY,
        double viewportWidth,
        double viewportHeight,
        IReadOnlyList<RekallAgeRuntimeControllerState>? controllers)
    {
        var wheelDelta = _pendingMouseWheelDelta;
        var mouseDeltaX = _pendingMouseDeltaX;
        var mouseDeltaY = _pendingMouseDeltaY;
        if (wheelDelta == 0
            && mouseDeltaX == 0
            && mouseDeltaY == 0
            && _pressedKeys.Count == 0
            && _pressedButtons.Count == 0
            && _pressedKeysThisFrame.Count == 0
            && _releasedKeysThisFrame.Count == 0
            && _pressedButtonsThisFrame.Count == 0
            && _releasedButtonsThisFrame.Count == 0)
        {
            return new RekallAgeRuntimeInputState(
                MouseX: mouseX,
                MouseY: mouseY,
                PressedKeys: SnapshotSetOrNull(_pressedKeys),
                PressedButtons: SnapshotSetOrNull(_pressedButtons),
                ViewportWidth: viewportWidth,
                ViewportHeight: viewportHeight,
                Controllers: controllers);
        }

        var pressedKeysThisFrame = SnapshotSetOrNull(_pressedKeysThisFrame);
        var releasedKeysThisFrame = SnapshotSetOrNull(_releasedKeysThisFrame);
        var pressedButtonsThisFrame = SnapshotSetOrNull(_pressedButtonsThisFrame);
        var releasedButtonsThisFrame = SnapshotSetOrNull(_releasedButtonsThisFrame);
        var pressedKeys = SnapshotSetOrNull(_pressedKeys);
        var pressedButtons = SnapshotSetOrNull(_pressedButtons);
        _pendingMouseWheelDelta = 0;
        _pressedKeysThisFrame.Clear();
        _releasedKeysThisFrame.Clear();
        _pressedButtonsThisFrame.Clear();
        _releasedButtonsThisFrame.Clear();
        _pendingMouseDeltaX = 0;
        _pendingMouseDeltaY = 0;

        return new RekallAgeRuntimeInputState(
            MouseX: mouseX,
            MouseY: mouseY,
            MouseDeltaX: mouseDeltaX,
            MouseDeltaY: mouseDeltaY,
            MouseWheelDelta: wheelDelta,
            PressedKeys: pressedKeys,
            PressedKeysThisFrame: pressedKeysThisFrame,
            ReleasedKeysThisFrame: releasedKeysThisFrame,
            PressedButtons: pressedButtons,
            PressedButtonsThisFrame: pressedButtonsThisFrame,
            ReleasedButtonsThisFrame: releasedButtonsThisFrame,
            ViewportWidth: viewportWidth,
            ViewportHeight: viewportHeight,
            Controllers: controllers);
    }

    private static IReadOnlySet<string>? SnapshotSetOrNull(HashSet<string> source) =>
        source.Count == 0 ? null : source.ToHashSet(StringComparer.OrdinalIgnoreCase);
}
