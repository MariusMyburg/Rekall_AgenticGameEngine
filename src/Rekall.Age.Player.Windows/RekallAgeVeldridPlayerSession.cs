using Rekall.Age.Rendering.Recovery;

namespace Rekall.Age.Player.Windows;

internal sealed class RekallAgePlayerAudioUnavailableException()
    : Exception("Required SDL audio output is unavailable.");

internal enum RekallAgePlayerFaultMode
{
    None,
    DeviceLossOnce,
    DeviceLossAlways,
    FatalOnce
}

internal sealed class RekallAgePlayerFaultInjection(
    RekallAgePlayerFaultMode mode,
    int frame)
{
    private int _injected;

    public static RekallAgePlayerFaultInjection Parse(
        string[] args,
        Func<string[], string, int?> readPositiveInt,
        Func<string[], string, bool> hasOption)
    {
        var options = new[]
        {
            (Name: "--simulate-device-loss-frame", Mode: RekallAgePlayerFaultMode.DeviceLossOnce),
            (Name: "--simulate-device-loss-always-frame", Mode: RekallAgePlayerFaultMode.DeviceLossAlways),
            (Name: "--simulate-fatal-frame", Mode: RekallAgePlayerFaultMode.FatalOnce)
        };
        var selected = options.Where(option => hasOption(args, option.Name)).ToArray();
        if (selected.Length == 0)
        {
            return new RekallAgePlayerFaultInjection(RekallAgePlayerFaultMode.None, 0);
        }

        if (selected.Length != 1 || readPositiveInt(args, selected[0].Name) is not { } selectedFrame)
        {
            throw new ArgumentException("Exactly one diagnostic fault option with a positive frame is allowed.");
        }

        return new RekallAgePlayerFaultInjection(selected[0].Mode, selectedFrame);
    }

    public void BeforeFrame(int currentFrame)
    {
        if (mode == RekallAgePlayerFaultMode.None || currentFrame != frame)
        {
            return;
        }

        if (mode != RekallAgePlayerFaultMode.DeviceLossAlways && Interlocked.Exchange(ref _injected, 1) != 0)
        {
            return;
        }

        if (mode == RekallAgePlayerFaultMode.FatalOnce)
        {
            throw new InvalidOperationException("Injected fatal player-session failure.");
        }

        throw new RekallAgeGraphicsDeviceLostException("Injected graphics device loss.");
    }
}

internal sealed class RekallAgeVeldridPlayerSessionFactory(
    string projectRoot,
    string sceneName,
    bool syncToVerticalBlank,
    bool openXrRequested,
    bool simulateXrInput,
    bool probeOpenXrCompositor,
    bool playableMode,
    int sceneSupersampleFactor,
    int openXrEyeWidth,
    int openXrEyeHeight,
    bool debugHudEnabled,
    bool audioRequired,
    RekallAgePlayerFaultInjection faultInjection) : IRekallAgePlayerSessionFactory
{
    private int _audioSubmittedFrameCount;

    public int AudioSubmittedFrameCount => Volatile.Read(ref _audioSubmittedFrameCount);

    public async ValueTask<IRekallAgePlayerSession> CreateAsync(
        int attempt,
        CancellationToken cancellationToken)
    {
        PlayerLog.Write($"Creating supervised player session attempt={attempt}.");
        var player = await RekallAgeVeldridPlayer.CreateAsync(
            projectRoot,
            sceneName,
            syncToVerticalBlank,
            openXrRequested,
            simulateXrInput,
            probeOpenXrCompositor,
            playableMode,
            sceneSupersampleFactor,
            openXrEyeWidth,
            openXrEyeHeight,
            debugHudEnabled,
            cancellationToken).ConfigureAwait(false);
        if (audioRequired && !player.AudioOutputAvailable)
        {
            await player.DisposeAsync().ConfigureAwait(false);
            throw new RekallAgePlayerAudioUnavailableException();
        }

        return new RekallAgeVeldridPlayerSession(
            player,
            faultInjection,
            frames => Interlocked.Add(ref _audioSubmittedFrameCount, frames));
    }
}

internal sealed class RekallAgeVeldridPlayerSession(
    RekallAgeVeldridPlayer player,
    RekallAgePlayerFaultInjection faultInjection,
    Action<int> recordAudioFrames) : IRekallAgePlayerSession
{
    public ValueTask<RekallAgePlayerSessionRunResult> RunAsync(
        long? requestedFrames,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (requestedFrames > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedFrames));
        }

        var requested = requestedFrames is null ? 0 : checked((int)requestedFrames.Value);
        var completed = player.Run(requested, faultInjection.BeforeFrame);
        return ValueTask.FromResult(new RekallAgePlayerSessionRunResult(
            completed,
            requestedFrames is null || completed < requested));
    }

    public async ValueTask DisposeAsync()
    {
        recordAudioFrames(player.AudioSubmittedFrameCount);
        await player.DisposeAsync().ConfigureAwait(false);
    }
}
