using System.Runtime.InteropServices;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Player.Windows;

internal sealed class RekallAgeSdlAudioOutput : IDisposable
{
    private const uint SdlInitAudio = 0x00000010;
    private const ushort AudioF32System = 0x8120;
    private const int MaximumQueuedMilliseconds = 250;
    private uint _device;
    private bool _ownsAudioSubsystem;

    public int SubmittedFrameCount { get; private set; }

    public uint QueuedBytes => _device == 0 ? 0 : SdlNative.SDL_GetQueuedAudioSize(_device);

    private RekallAgeSdlAudioOutput(uint device, bool ownsAudioSubsystem)
    {
        _device = device;
        _ownsAudioSubsystem = ownsAudioSubsystem;
    }

    public static RekallAgeSdlAudioOutput? TryCreate(out string status)
    {
        try
        {
            var ownsAudioSubsystem = SdlNative.SDL_InitSubSystem(SdlInitAudio) == 0;
            if (!ownsAudioSubsystem)
            {
                status = $"SDL audio subsystem initialization failed: {SdlNative.GetError()}";
                return null;
            }

            var desired = new SdlAudioSpec
            {
                Frequency = 48_000,
                Format = AudioF32System,
                Channels = 2,
                Samples = 800
            };
            var device = SdlNative.SDL_OpenAudioDevice(null, 0, ref desired, out var obtained, 0);
            if (device == 0 || obtained.Format != AudioF32System || obtained.Channels != 2 || obtained.Frequency != 48_000)
            {
                if (device != 0)
                {
                    SdlNative.SDL_CloseAudioDevice(device);
                }

                SdlNative.SDL_QuitSubSystem(SdlInitAudio);
                status = device == 0
                    ? $"SDL audio device open failed: {SdlNative.GetError()}"
                    : $"SDL audio device format mismatch: obtained {obtained.Frequency} Hz, {obtained.Channels} channels, format 0x{obtained.Format:x4}.";
                return null;
            }

            SdlNative.SDL_PauseAudioDevice(device, 0);
            status = $"SDL audio device ready: {obtained.Frequency} Hz, {obtained.Channels} channels, float32.";
            return new RekallAgeSdlAudioOutput(device, ownsAudioSubsystem);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            status = $"SDL audio unavailable: {exception.Message}";
            return null;
        }
    }

    public void Submit(IReadOnlyList<RekallAgeRuntimeAudioMixFrame> frames)
    {
        if (_device == 0)
        {
            return;
        }

        const int bytesPerFrame = sizeof(float) * 2;
        var maximumQueuedBytes = 48_000 * bytesPerFrame * MaximumQueuedMilliseconds / 1_000;
        foreach (var frame in frames)
        {
            if (frame.SampleRate != 48_000 || frame.Channels != 2 || frame.Samples is not { Count: > 0 } samples ||
                SdlNative.SDL_GetQueuedAudioSize(_device) >= maximumQueuedBytes)
            {
                continue;
            }

            var buffer = samples as float[] ?? samples.ToArray();
            var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                if (SdlNative.SDL_QueueAudio(
                        _device,
                        handle.AddrOfPinnedObject(),
                        checked((uint)(buffer.Length * sizeof(float)))) == 0)
                {
                    SubmittedFrameCount++;
                }
            }
            finally
            {
                handle.Free();
            }
        }
    }

    public void Dispose()
    {
        if (_device != 0)
        {
            SdlNative.SDL_ClearQueuedAudio(_device);
            SdlNative.SDL_CloseAudioDevice(_device);
            _device = 0;
        }

        if (_ownsAudioSubsystem)
        {
            SdlNative.SDL_QuitSubSystem(SdlInitAudio);
            _ownsAudioSubsystem = false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SdlAudioSpec
    {
        public int Frequency;
        public ushort Format;
        public byte Channels;
        public byte Silence;
        public ushort Samples;
        public ushort Padding;
        public uint Size;
        public IntPtr Callback;
        public IntPtr UserData;
    }

    private static class SdlNative
    {
        [DllImport("SDL2.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int SDL_InitSubSystem(uint flags);

        [DllImport("SDL2.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void SDL_QuitSubSystem(uint flags);

        [DllImport("SDL2.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern uint SDL_OpenAudioDevice(
            string? device,
            int isCapture,
            ref SdlAudioSpec desired,
            out SdlAudioSpec obtained,
            int allowedChanges);

        [DllImport("SDL2.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void SDL_CloseAudioDevice(uint device);

        [DllImport("SDL2.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void SDL_PauseAudioDevice(uint device, int pauseOn);

        [DllImport("SDL2.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int SDL_QueueAudio(uint device, IntPtr data, uint length);

        [DllImport("SDL2.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern uint SDL_GetQueuedAudioSize(uint device);

        [DllImport("SDL2.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void SDL_ClearQueuedAudio(uint device);

        [DllImport("SDL2.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr SDL_GetError();

        public static string GetError() => Marshal.PtrToStringUTF8(SDL_GetError()) ?? "unknown SDL error";
    }
}
