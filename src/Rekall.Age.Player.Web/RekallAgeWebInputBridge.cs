using System.Text.Json;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Player.Web;

/// <summary>
/// A raw, device-semantic snapshot of browser input facts for exactly one poll. JavaScript supplies only
/// unmapped <c>KeyboardEvent.code</c> values, raw pointer/button/wheel/viewport facts, and raw per-gamepad
/// axis/button state -- it must not decide what any of this means for gameplay. <see cref="RekallAgeWebInputBridge"/>
/// is the only place that normalizes those facts into AGE's canonical, cross-platform input contract.
/// </summary>
public sealed record RekallAgeWebInputSnapshot(
    IReadOnlyList<string> HeldKeyCodes,
    double PointerX,
    double PointerY,
    double PointerDeltaX,
    double PointerDeltaY,
    double WheelDeltaY,
    IReadOnlyList<int> HeldPointerButtons,
    double ViewportWidth,
    double ViewportHeight,
    bool Focused = true,
    IReadOnlyList<RekallAgeWebGamepadSample>? Gamepads = null)
{
    public static RekallAgeWebInputSnapshot Empty(double viewportWidth = 0, double viewportHeight = 0) =>
        new(Array.Empty<string>(), 0, 0, 0, 0, 0, Array.Empty<int>(), viewportWidth, viewportHeight);
}

public sealed record RekallAgeWebGamepadSample(
    int Index,
    string Id,
    bool Connected,
    IReadOnlyList<double> Axes,
    IReadOnlyList<bool> HeldButtons);

/// <summary>
/// A stable, structured lifecycle fact for events that are not part of the per-frame input snapshot: resize,
/// visibility, pause/resume, fullscreen, and device-loss. These reach the same diagnostics surface every other
/// AGE subsystem uses instead of silently changing behavior.
/// </summary>
public sealed record RekallAgeWebPlayerLifecycleEvent(string Code, string Message, string? Target = null);

public static class RekallAgeWebInputLifecycle
{
    public static RekallAgeWebPlayerLifecycleEvent Resized(double width, double height) =>
        new("REKALL_WEB_VIEWPORT_RESIZED", $"Viewport resized to {width}x{height}.", $"{width}x{height}");

    public static RekallAgeWebPlayerLifecycleEvent VisibilityChanged(bool visible) =>
        new(
            visible ? "REKALL_WEB_VIEWPORT_VISIBLE" : "REKALL_WEB_VIEWPORT_HIDDEN",
            visible ? "The player tab or window became visible." : "The player tab or window became hidden.");

    public static RekallAgeWebPlayerLifecycleEvent Paused(string reason) =>
        new("REKALL_WEB_PLAYER_PAUSED", $"Playback paused: {reason}.", reason);

    public static RekallAgeWebPlayerLifecycleEvent Resumed() =>
        new("REKALL_WEB_PLAYER_RESUMED", "Playback resumed.");

    public static RekallAgeWebPlayerLifecycleEvent FullscreenChanged(bool fullscreen) =>
        new(
            fullscreen ? "REKALL_WEB_FULLSCREEN_ENTERED" : "REKALL_WEB_FULLSCREEN_EXITED",
            fullscreen ? "The player entered fullscreen." : "The player exited fullscreen.");

    public static RekallAgeWebPlayerLifecycleEvent DeviceLost(string reason) =>
        new("REKALL_WEB_GPU_DEVICE_LOST", $"The WebGPU device was lost: {reason}.", reason);
}

/// <summary>
/// Converts successive <see cref="RekallAgeWebInputSnapshot"/> polls into <see cref="RekallAgeRuntimeInputState"/>,
/// the same generic runtime input contract the Windows SDL2 player produces. It owns held/pressed/released edge
/// detection for keys, pointer buttons, and per-gamepad buttons so JavaScript only ever reports "is this held right
/// now", never "did this just change". Key codes are normalized against AGE's canonical key-name convention (the
/// same names produced on the Windows player) so an ordinary <c>Rekall.InputActionMap</c> authored once binds
/// identically in both environments.
/// </summary>
public sealed class RekallAgeWebInputBridge
{
    private readonly HashSet<string> _heldKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _heldButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, HashSet<string>> _heldControllerButtons = [];
    private bool _wasFocused = true;

    public static IReadOnlyDictionary<string, string> KeyCodeMap { get; } = BuildKeyCodeMap();

    public static IReadOnlyDictionary<int, string> PointerButtonMap { get; } = new Dictionary<int, string>
    {
        [0] = "Left",
        [1] = "Middle",
        [2] = "Right",
        [3] = "Back",
        [4] = "Forward"
    };

    public RekallAgeRuntimeInputState Capture(RekallAgeWebInputSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var currentKeys = NormalizeKeys(snapshot.HeldKeyCodes);
        var currentButtons = NormalizeButtons(snapshot.HeldPointerButtons);

        var pressedKeysThisFrame = new HashSet<string>(currentKeys, StringComparer.OrdinalIgnoreCase);
        pressedKeysThisFrame.ExceptWith(_heldKeys);
        var releasedKeysThisFrame = new HashSet<string>(_heldKeys, StringComparer.OrdinalIgnoreCase);
        releasedKeysThisFrame.ExceptWith(currentKeys);

        var pressedButtonsThisFrame = new HashSet<string>(currentButtons, StringComparer.OrdinalIgnoreCase);
        pressedButtonsThisFrame.ExceptWith(_heldButtons);
        var releasedButtonsThisFrame = new HashSet<string>(_heldButtons, StringComparer.OrdinalIgnoreCase);
        releasedButtonsThisFrame.ExceptWith(currentButtons);

        // A focus-loss device fact releases every held key/button in the same frame it happens, matching the
        // Windows player's mouse-capture release on focus loss instead of leaving phantom held input behind.
        if (_wasFocused && !snapshot.Focused)
        {
            foreach (var key in _heldKeys)
            {
                releasedKeysThisFrame.Add(key);
            }

            foreach (var button in _heldButtons)
            {
                releasedButtonsThisFrame.Add(button);
            }

            currentKeys.Clear();
            currentButtons.Clear();
            pressedKeysThisFrame.Clear();
            pressedButtonsThisFrame.Clear();
        }

        _wasFocused = snapshot.Focused;

        var controllers = BuildControllers(snapshot.Gamepads);

        var state = new RekallAgeRuntimeInputState(
            snapshot.PointerX,
            snapshot.PointerY,
            snapshot.PointerDeltaX,
            snapshot.PointerDeltaY,
            snapshot.WheelDeltaY,
            new HashSet<string>(currentKeys, StringComparer.OrdinalIgnoreCase),
            pressedKeysThisFrame,
            releasedKeysThisFrame,
            new HashSet<string>(currentButtons, StringComparer.OrdinalIgnoreCase),
            pressedButtonsThisFrame,
            releasedButtonsThisFrame,
            null,
            null,
            snapshot.ViewportWidth,
            snapshot.ViewportHeight,
            null,
            controllers);

        _heldKeys.Clear();
        _heldKeys.UnionWith(currentKeys);
        _heldButtons.Clear();
        _heldButtons.UnionWith(currentButtons);

        return state;
    }

    private static HashSet<string> NormalizeKeys(IReadOnlyList<string> rawCodes)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var code in rawCodes)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                continue;
            }

            result.Add(KeyCodeMap.TryGetValue(code, out var mapped) ? mapped : code.Trim());
        }

        return result;
    }

    private static HashSet<string> NormalizeButtons(IReadOnlyList<int> rawButtons)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var button in rawButtons)
        {
            result.Add(PointerButtonMap.TryGetValue(button, out var mapped) ? mapped : $"Button{button}");
        }

        return result;
    }

    private IReadOnlyList<RekallAgeRuntimeControllerState> BuildControllers(
        IReadOnlyList<RekallAgeWebGamepadSample>? gamepads)
    {
        if (gamepads is null || gamepads.Count == 0)
        {
            _heldControllerButtons.Clear();
            return Array.Empty<RekallAgeRuntimeControllerState>();
        }

        var seenIndexes = new HashSet<int>();
        var results = new List<RekallAgeRuntimeControllerState>(gamepads.Count);
        foreach (var pad in gamepads)
        {
            if (!pad.Connected)
            {
                continue;
            }

            seenIndexes.Add(pad.Index);
            if (!_heldControllerButtons.TryGetValue(pad.Index, out var previousHeld))
            {
                previousHeld = new HashSet<string>(StringComparer.Ordinal);
                _heldControllerButtons[pad.Index] = previousHeld;
            }

            var currentHeld = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < pad.HeldButtons.Count; i++)
            {
                if (pad.HeldButtons[i])
                {
                    currentHeld.Add($"Button{i}");
                }
            }

            var pressed = currentHeld.Where(button => !previousHeld.Contains(button)).ToArray();
            var released = previousHeld.Where(button => !currentHeld.Contains(button)).ToArray();
            var axes = pad.Axes
                .Select((value, index) => new RekallAgeRuntimeControllerAxis($"Axis{index}", value))
                .ToArray();

            results.Add(new RekallAgeRuntimeControllerState(
                $"gamepad-{pad.Index}",
                "gamepad",
                pad.Index,
                axes,
                currentHeld.ToArray(),
                pressed,
                released,
                Array.Empty<RekallAgeRuntimeControllerHat>()));

            previousHeld.Clear();
            previousHeld.UnionWith(currentHeld);
        }

        foreach (var staleIndex in _heldControllerButtons.Keys.Where(index => !seenIndexes.Contains(index)).ToArray())
        {
            _heldControllerButtons.Remove(staleIndex);
        }

        return results;
    }

    private static Dictionary<string, string> BuildKeyCodeMap()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Space"] = "Space",
            ["Quote"] = "Apostrophe",
            ["Comma"] = "Comma",
            ["Minus"] = "Minus",
            ["Period"] = "Period",
            ["Slash"] = "Slash",
            ["Semicolon"] = "Semicolon",
            ["Equal"] = "Equal",
            ["BracketLeft"] = "LeftBracket",
            ["Backslash"] = "BackSlash",
            ["BracketRight"] = "RightBracket",
            ["Backquote"] = "GraveAccent",
            ["Escape"] = "Escape",
            ["Enter"] = "Enter",
            ["Tab"] = "Tab",
            ["Backspace"] = "Backspace",
            ["Insert"] = "Insert",
            ["Delete"] = "Delete",
            ["ArrowRight"] = "Right",
            ["ArrowLeft"] = "Left",
            ["ArrowDown"] = "Down",
            ["ArrowUp"] = "Up",
            ["PageUp"] = "PageUp",
            ["PageDown"] = "PageDown",
            ["Home"] = "Home",
            ["End"] = "End",
            ["CapsLock"] = "CapsLock",
            ["ScrollLock"] = "ScrollLock",
            ["NumLock"] = "NumLock",
            ["PrintScreen"] = "PrintScreen",
            ["Pause"] = "Pause",
            ["ContextMenu"] = "Menu",
            ["ShiftLeft"] = "ShiftLeft",
            ["ShiftRight"] = "ShiftRight",
            ["ControlLeft"] = "ControlLeft",
            ["ControlRight"] = "ControlRight",
            ["AltLeft"] = "AltLeft",
            ["AltRight"] = "AltRight",
            ["MetaLeft"] = "SuperLeft",
            ["MetaRight"] = "SuperRight",
            ["NumpadEnter"] = "KeypadEnter",
            ["NumpadDecimal"] = "KeypadDecimal",
            ["NumpadDivide"] = "KeypadDivide",
            ["NumpadMultiply"] = "KeypadMultiply",
            ["NumpadSubtract"] = "KeypadSubtract",
            ["NumpadAdd"] = "KeypadAdd",
            ["NumpadEqual"] = "KeypadEqual"
        };

        for (var letter = 'A'; letter <= 'Z'; letter++)
        {
            map[$"Key{letter}"] = letter.ToString();
        }

        for (var digit = 0; digit <= 9; digit++)
        {
            map[$"Digit{digit}"] = $"Number{digit}";
            map[$"Numpad{digit}"] = $"Keypad{digit}";
        }

        for (var function = 1; function <= 24; function++)
        {
            map[$"F{function}"] = $"F{function}";
        }

        return map;
    }
}

/// <summary>
/// Parses the plain JSON object <c>web-input.js</c>'s <c>snapshot()</c> produces into a
/// <see cref="RekallAgeWebInputSnapshot"/>. Bounded like every other browser-supplied payload this player reads;
/// missing or malformed optional fields fall back to empty/zero rather than throwing, since a dropped browser event
/// must not stop the frame loop.
/// </summary>
public static class RekallAgeWebInputSnapshotJson
{
    public const int MaximumJsonBytes = 64 * 1024;

    public static RekallAgeWebInputSnapshot Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaximumJsonBytes)
        {
            throw new FormatException("Web input snapshot JSON exceeds the bounded size.");
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return new RekallAgeWebInputSnapshot(
            ReadStrings(root, "heldKeyCodes"),
            ReadDouble(root, "pointerX"),
            ReadDouble(root, "pointerY"),
            ReadDouble(root, "pointerDeltaX"),
            ReadDouble(root, "pointerDeltaY"),
            ReadDouble(root, "wheelDeltaY"),
            ReadInts(root, "heldPointerButtons"),
            ReadDouble(root, "viewportWidth"),
            ReadDouble(root, "viewportHeight"),
            !root.TryGetProperty("focused", out var focused) || focused.ValueKind != JsonValueKind.False,
            ReadGamepads(root));
    }

    private static double ReadDouble(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : 0;

    private static IReadOnlyList<string> ReadStrings(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } text)
            {
                result.Add(text);
            }
        }

        return result;
    }

    private static IReadOnlyList<int> ReadInts(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<int>();
        }

        var result = new List<int>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var parsed))
            {
                result.Add(parsed);
            }
        }

        return result;
    }

    private static IReadOnlyList<RekallAgeWebGamepadSample> ReadGamepads(JsonElement root)
    {
        if (!root.TryGetProperty("gamepads", out var gamepads) || gamepads.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<RekallAgeWebGamepadSample>();
        }

        var result = new List<RekallAgeWebGamepadSample>();
        foreach (var item in gamepads.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var index = item.TryGetProperty("index", out var indexValue) && indexValue.ValueKind == JsonValueKind.Number
                ? indexValue.GetInt32()
                : 0;
            var id = item.TryGetProperty("id", out var idValue) && idValue.ValueKind == JsonValueKind.String
                ? idValue.GetString() ?? string.Empty
                : string.Empty;
            var connected = !item.TryGetProperty("connected", out var connectedValue) || connectedValue.ValueKind != JsonValueKind.False;
            var axes = new List<double>();
            if (item.TryGetProperty("axes", out var axesValue) && axesValue.ValueKind == JsonValueKind.Array)
            {
                foreach (var axis in axesValue.EnumerateArray())
                {
                    axes.Add(axis.ValueKind == JsonValueKind.Number ? axis.GetDouble() : 0);
                }
            }

            var heldButtons = new List<bool>();
            if (item.TryGetProperty("heldButtons", out var buttonsValue) && buttonsValue.ValueKind == JsonValueKind.Array)
            {
                foreach (var button in buttonsValue.EnumerateArray())
                {
                    heldButtons.Add(button.ValueKind == JsonValueKind.True);
                }
            }

            result.Add(new RekallAgeWebGamepadSample(index, id, connected, axes, heldButtons));
        }

        return result;
    }
}
