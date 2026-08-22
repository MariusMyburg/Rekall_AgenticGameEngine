using System.Runtime.InteropServices;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Player.Windows;

public sealed class RekallAgeSdlControllerInput : IDisposable
{
    private const uint SdlInitJoystick = 0x00000200;
    private const uint SdlInitGameController = 0x00002000;
    private static readonly string[] GamepadAxes = ["LeftX", "LeftY", "RightX", "RightY", "TriggerLeft", "TriggerRight"];
    private static readonly string[] GamepadButtons =
    [
        "A", "B", "X", "Y", "Back", "Guide", "Start", "LeftStick", "RightStick",
        "LeftShoulder", "RightShoulder", "DPadUp", "DPadDown", "DPadLeft", "DPadRight"
    ];

    private readonly Dictionary<int, OpenDevice> _devices = [];
    private readonly RekallAgeControllerInputTracker _tracker = new();
    private readonly bool _ownsSubsystems;
    private bool _disposed;

    public RekallAgeSdlControllerInput()
    {
        _ownsSubsystems = Native.SDL_InitSubSystem(SdlInitJoystick | SdlInitGameController) == 0;
    }

    public IReadOnlyList<RekallAgeRuntimeControllerState> Poll()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Native.SDL_GameControllerUpdate();
        Native.SDL_JoystickUpdate();
        DiscoverDevices();

        foreach (var disconnected in _devices.Values.Where(device => !device.Attached()).ToArray())
        {
            disconnected.Dispose();
            _devices.Remove(disconnected.InstanceId);
        }

        var states = _devices.Values
            .OrderBy(device => device.PlayerIndex)
            .ThenBy(device => device.InstanceId)
            .Select(device => device.Read())
            .ToArray();
        return _tracker.Update(states);
    }

    private void DiscoverDevices()
    {
        var count = Math.Max(0, Native.SDL_NumJoysticks());
        for (var deviceIndex = 0; deviceIndex < count; deviceIndex++)
        {
            var instanceId = Native.SDL_JoystickGetDeviceInstanceID(deviceIndex);
            if (instanceId < 0 || _devices.ContainsKey(instanceId))
            {
                continue;
            }

            var device = Native.SDL_IsGameController(deviceIndex) != 0
                ? OpenDevice.OpenGamepad(deviceIndex, instanceId)
                : OpenDevice.OpenJoystick(deviceIndex, instanceId);
            if (device is not null)
            {
                _devices.Add(instanceId, device);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var device in _devices.Values)
        {
            device.Dispose();
        }

        _devices.Clear();
        if (_ownsSubsystems)
        {
            Native.SDL_QuitSubSystem(SdlInitJoystick | SdlInitGameController);
        }

        _disposed = true;
    }

    private sealed class OpenDevice : IDisposable
    {
        private readonly IntPtr _handle;
        private readonly bool _gamepad;

        private OpenDevice(IntPtr handle, bool gamepad, int instanceId, int playerIndex)
        {
            _handle = handle;
            _gamepad = gamepad;
            InstanceId = instanceId;
            PlayerIndex = playerIndex;
        }

        public int InstanceId { get; }

        public int PlayerIndex { get; }

        public static OpenDevice? OpenGamepad(int deviceIndex, int instanceId)
        {
            var handle = Native.SDL_GameControllerOpen(deviceIndex);
            return handle == IntPtr.Zero ? null : new OpenDevice(handle, true, instanceId, deviceIndex);
        }

        public static OpenDevice? OpenJoystick(int deviceIndex, int instanceId)
        {
            var handle = Native.SDL_JoystickOpen(deviceIndex);
            return handle == IntPtr.Zero ? null : new OpenDevice(handle, false, instanceId, deviceIndex);
        }

        public bool Attached() => _gamepad
            ? Native.SDL_GameControllerGetAttached(_handle) != 0
            : Native.SDL_JoystickGetAttached(_handle) != 0;

        public RekallAgeRuntimeControllerState Read() => _gamepad ? ReadGamepad() : ReadJoystick();

        private RekallAgeRuntimeControllerState ReadGamepad()
        {
            var axes = GamepadAxes.Select((name, index) =>
                new RekallAgeRuntimeControllerAxis(name, NormalizeAxis(Native.SDL_GameControllerGetAxis(_handle, index)))).ToArray();
            var buttons = GamepadButtons.Where((_, index) =>
                Native.SDL_GameControllerGetButton(_handle, index) != 0).ToArray();
            var hatX = (buttons.Contains("DPadRight", StringComparer.Ordinal) ? 1 : 0)
                - (buttons.Contains("DPadLeft", StringComparer.Ordinal) ? 1 : 0);
            var hatY = (buttons.Contains("DPadUp", StringComparer.Ordinal) ? 1 : 0)
                - (buttons.Contains("DPadDown", StringComparer.Ordinal) ? 1 : 0);
            return State("gamepad", axes, buttons,
                [new RekallAgeRuntimeControllerHat("DPad", hatX, hatY)]);
        }

        private RekallAgeRuntimeControllerState ReadJoystick()
        {
            var axes = Enumerable.Range(0, Math.Max(0, Native.SDL_JoystickNumAxes(_handle)))
                .Select(index => new RekallAgeRuntimeControllerAxis(
                    $"Axis{index}", NormalizeAxis(Native.SDL_JoystickGetAxis(_handle, index))))
                .ToArray();
            var buttons = Enumerable.Range(0, Math.Max(0, Native.SDL_JoystickNumButtons(_handle)))
                .Where(index => Native.SDL_JoystickGetButton(_handle, index) != 0)
                .Select(index => $"Button{index}")
                .ToArray();
            var hats = Enumerable.Range(0, Math.Max(0, Native.SDL_JoystickNumHats(_handle)))
                .Select(index => ToHat(index, Native.SDL_JoystickGetHat(_handle, index)))
                .ToArray();
            return State("joystick", axes, buttons, hats);
        }

        private RekallAgeRuntimeControllerState State(
            string kind,
            IReadOnlyList<RekallAgeRuntimeControllerAxis> axes,
            IReadOnlyList<string> buttons,
            IReadOnlyList<RekallAgeRuntimeControllerHat> hats) =>
            new($"sdl:{InstanceId}", kind, PlayerIndex, axes, buttons, [], [], hats);

        private static RekallAgeRuntimeControllerHat ToHat(int index, byte value)
        {
            var x = ((value & 0x02) != 0 ? 1 : 0) - ((value & 0x08) != 0 ? 1 : 0);
            var y = ((value & 0x01) != 0 ? 1 : 0) - ((value & 0x04) != 0 ? 1 : 0);
            return new RekallAgeRuntimeControllerHat($"Hat{index}", x, y);
        }

        private static double NormalizeAxis(short value) => value < 0 ? value / 32768.0 : value / 32767.0;

        public void Dispose()
        {
            if (_gamepad)
            {
                Native.SDL_GameControllerClose(_handle);
            }
            else
            {
                Native.SDL_JoystickClose(_handle);
            }
        }
    }

    private static class Native
    {
        private const string Library = "SDL2";

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_InitSubSystem(uint flags);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_QuitSubSystem(uint flags);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_NumJoysticks();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_IsGameController(int joystickIndex);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_JoystickGetDeviceInstanceID(int deviceIndex);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_GameControllerOpen(int joystickIndex);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_GameControllerClose(IntPtr controller);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_GameControllerGetAttached(IntPtr controller);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern short SDL_GameControllerGetAxis(IntPtr controller, int axis);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_GameControllerGetButton(IntPtr controller, int button);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_GameControllerUpdate();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_JoystickOpen(int deviceIndex);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_JoystickClose(IntPtr joystick);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_JoystickGetAttached(IntPtr joystick);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_JoystickNumAxes(IntPtr joystick);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern short SDL_JoystickGetAxis(IntPtr joystick, int axis);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_JoystickNumButtons(IntPtr joystick);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_JoystickGetButton(IntPtr joystick, int button);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_JoystickNumHats(IntPtr joystick);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern byte SDL_JoystickGetHat(IntPtr joystick, int hat);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_JoystickUpdate();
    }
}
