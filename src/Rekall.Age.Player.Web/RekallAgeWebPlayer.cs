using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Player.Web;

/// <summary>
/// Bounded, read-only debug status for one <see cref="RekallAgeWebPlayer"/> tick: build/scene identity is owned by
/// the caller (<see cref="RekallAgeWebBootstrapEvidence"/>/<see cref="RekallAgeWebGameManifest"/> already expose
/// it), so this only reports the facts that change every tick -- sequence, simulated frame identity, whether a
/// visual frame was presented, and rendered entity/draw counts.
/// </summary>
public sealed record RekallAgeWebPlayerTickResult(
    long TickSequence,
    int FrameIndex,
    int StepsSimulated,
    bool Rendered,
    int RenderedEntityCount,
    int DrawCount,
    IReadOnlyList<RekallAgeGraphicsDiagnostic> Diagnostics);

/// <summary>
/// Drives the browser simulation/presentation loop for one bootstrapped web game: advances
/// <see cref="RekallAgeRuntimeSimulationClock"/> with the latest browser input, builds the current viewport frame,
/// projects it into backend-neutral scene meshes, and presents through
/// <see cref="RekallAgeRenderingDeviceSceneRenderer"/> once per visual tick -- regardless of whether the fixed-step
/// clock simulated zero, one, or several steps that tick, so paused or sub-frame-rate calls still redraw the
/// current world instead of leaving a stale or blank canvas.
/// </summary>
public sealed class RekallAgeWebPlayer
{
    private readonly RekallAgeRuntimeExecutionLoop _executionLoop;
    private readonly RekallAgeRuntimeSimulationClock _clock;
    private readonly RekallAgeWebInputBridge _inputBridge = new();
    private readonly RekallAgeRuntimeRenderFrameBuilder _frameBuilder = new();
    private readonly RekallAgeVulkanSceneMeshBuilder _meshBuilder = new();
    private readonly RekallAgeRenderingDeviceSceneRenderer _renderer;
    private RekallAgeRuntimeWorld _world;
    private long _tickSequence;

    public RekallAgeWebPlayer(
        RekallAgeRuntimeWorld initialWorld,
        RekallAgeRuntimeExecutionLoop executionLoop,
        IRekallAgeRenderingDevice device,
        RekallAgeRuntimeSimulationClockOptions? clockOptions = null)
    {
        _world = initialWorld ?? throw new ArgumentNullException(nameof(initialWorld));
        _executionLoop = executionLoop ?? throw new ArgumentNullException(nameof(executionLoop));
        ArgumentNullException.ThrowIfNull(device);
        _clock = new RekallAgeRuntimeSimulationClock(executionLoop, TimeSpan.Zero, clockOptions);
        _renderer = new RekallAgeRenderingDeviceSceneRenderer(device);
    }

    public bool Paused { get; private set; }

    public long TickSequence => _tickSequence;

    public RekallAgeRuntimeWorld World => _world;

    public void Pause() => Paused = true;

    public void Resume() => Paused = false;

    public async ValueTask<RekallAgeWebPlayerTickResult> TickAsync(
        double elapsedSeconds,
        RekallAgeWebInputSnapshot inputSnapshot,
        RekallAgeGraphicsResourceHandle colorTarget,
        RekallAgeTextureFormat colorFormat,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inputSnapshot);
        _tickSequence++;

        // The input bridge captures every tick, paused or not, so held-key/button edges are never lost or
        // double-fired across a pause boundary.
        var inputState = _inputBridge.Capture(inputSnapshot);

        var stepsSimulated = 0;
        if (!Paused && elapsedSeconds > 0)
        {
            var advance = await _clock.AdvanceByAsync(
                _world,
                TimeSpan.FromSeconds(elapsedSeconds),
                cancellationToken,
                _ => inputState).ConfigureAwait(false);
            _world = advance.World;
            stepsSimulated = advance.StepsSimulated;
        }

        var width = (int)Math.Max(1, Math.Round(inputSnapshot.ViewportWidth));
        var height = (int)Math.Max(1, Math.Round(inputSnapshot.ViewportHeight));
        var frame = _frameBuilder.Build(_world, width, height, debugOverlay: false);
        var meshes = _meshBuilder.BuildMeshes(frame);
        var renderResult = _renderer.RenderFrame(frame, meshes, colorTarget, colorFormat);

        return new RekallAgeWebPlayerTickResult(
            _tickSequence,
            _world.FrameIndex,
            stepsSimulated,
            renderResult.Rendered,
            frame.Renderables.Count,
            renderResult.DrawCount,
            renderResult.Diagnostics);
    }
}
