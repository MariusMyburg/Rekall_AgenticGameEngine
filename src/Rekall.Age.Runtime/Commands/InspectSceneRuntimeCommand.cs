using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Rendering;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Runtime.Commands;

public sealed record InspectSceneRuntimeRequest(
    string ProjectRoot,
    string SceneName,
    int Frames,
    IReadOnlyList<RekallAgeRuntimeInputFrame>? Inputs = null);

public sealed record InspectSceneRuntimeResult(
    string SceneName,
    int FrameIndex,
    double ElapsedSeconds,
    int EntityCount,
    int RenderableCount,
    int PhysicsBodyCount,
    int PhysicsColliderCount,
    int AudioListenerCount,
    int AudioEmitterCount,
    int AnimationPlayerCount,
    int UiElementCount,
    int InputActionCount,
    IReadOnlyList<RekallAgeRuntimeInputAction> InputActions,
    int EventCount,
    IReadOnlyList<RekallAgeRuntimeEvent> Events,
    int XrRigCount,
    int XrControllerCount,
    int XrPoseCount,
    int XrActionCount,
    IReadOnlyList<RekallAgeRuntimeXrAction> XrActions,
    IReadOnlyList<string> SystemsRun,
    IReadOnlyList<RekallAgeRuntimeObservation> Observations,
    int VisibleRenderableCount,
    int CulledRenderableCount,
    IReadOnlyList<InspectSceneRuntimeCulledRenderable> CulledRenderables)
{
    public int ActiveAudioVoiceCount { get; init; }

    public int AudioBusCount { get; init; }

    public double AudioPeakGain { get; init; }

    public int AudioMixedSampleCount { get; init; }

    public IReadOnlyList<RekallAgeRuntimeAudioVoice> AudioVoices { get; init; } =
        Array.Empty<RekallAgeRuntimeAudioVoice>();

    public bool AudioVoicesTruncated { get; init; }

    public IReadOnlyList<RekallAgeRuntimeAnimationPlayer> AnimationPlayers { get; init; } =
        Array.Empty<RekallAgeRuntimeAnimationPlayer>();

    public bool AnimationPlayersTruncated { get; init; }

    public IReadOnlyList<RekallAgeRuntimeMorphState> MorphStates { get; init; } =
        Array.Empty<RekallAgeRuntimeMorphState>();

    public bool MorphStatesTruncated { get; init; }

    public IReadOnlyList<InspectSceneRuntimeEntityState> EntityStates { get; init; } =
        Array.Empty<InspectSceneRuntimeEntityState>();

    public bool EntityStatesTruncated { get; init; }

    public int UiCanvasCount { get; init; }

    public int InteractiveUiElementCount { get; init; }

    public IReadOnlyList<RekallAgeRuntimeUiCanvas> UiCanvases { get; init; } =
        Array.Empty<RekallAgeRuntimeUiCanvas>();

    public IReadOnlyList<RekallAgeRuntimeUiElement> UiElements { get; init; } =
        Array.Empty<RekallAgeRuntimeUiElement>();

    public bool UiElementsTruncated { get; init; }
}

public sealed record InspectSceneRuntimeEntityState(
    string EntityId,
    string EntityName,
    bool Visible,
    RekallAgeRuntimeTransform Transform,
    IReadOnlyList<string> ComponentTypes)
{
    public RekallAgeRuntimeTransform InitialTransform { get; init; } = RekallAgeRuntimeTransform.Identity;

    public RekallAgeRuntimeVector2 PositionDelta2D { get; init; } = new(0, 0);

    public RekallAgeRuntimeVector3 PositionDelta3D { get; init; } = new(0, 0, 0);
}

public sealed record InspectSceneRuntimeCulledRenderable(
    string EntityId,
    string EntityName,
    string Kind,
    string Layer,
    string Reason,
    string? CameraEntityName,
    string CullingMask);

public sealed class InspectSceneRuntimeCommand : IRekallAgeCommand<InspectSceneRuntimeRequest, InspectSceneRuntimeResult>
{
    public string Name => "rekall.runtime.inspect_scene";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Inspects deterministic built-in scene simulation after a requested frame count without requiring a compiled playable module; reports physics, animation, UI, audio, events, and entity states.",
        typeof(InspectSceneRuntimeRequest).FullName!,
        typeof(InspectSceneRuntimeResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<InspectSceneRuntimeResult>> ExecuteAsync(
        InspectSceneRuntimeRequest request,
        RekallAgeCommandContext context)
    {
        if (request.Frames < 0)
        {
            var empty = new InspectSceneRuntimeResult(
                request.SceneName,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                Array.Empty<RekallAgeRuntimeInputAction>(),
                0,
                Array.Empty<RekallAgeRuntimeEvent>(),
                0,
                0,
                0,
                0,
                Array.Empty<RekallAgeRuntimeXrAction>(),
                Array.Empty<string>(),
                Array.Empty<RekallAgeRuntimeObservation>(),
                0,
                0,
                Array.Empty<InspectSceneRuntimeCulledRenderable>());
            return RekallAgeCommandResult<InspectSceneRuntimeResult>.Failure(
                empty,
                "Runtime inspection requires a non-negative frame count.",
                [
                    new RekallAgeCommandError(
                        "REKALL_RUNTIME_INVALID_FRAMES",
                        "Frame count cannot be negative.",
                        request.SceneName)
                ]);
        }

        var snapshotService = new RekallAgeRuntimeSnapshotService();
        var initialWorld = await snapshotService.InspectSceneAsync(
            request.ProjectRoot,
            request.SceneName,
            0,
            null,
            context.CancellationToken);
        var world = await snapshotService.InspectSceneAsync(
            request.ProjectRoot,
            request.SceneName,
            Math.Max(0, request.Frames),
            request.Inputs,
            context.CancellationToken);
        var result = ToResult(world, initialWorld);
        return RekallAgeCommandResult<InspectSceneRuntimeResult>.Success(
            result,
            $"Runtime {result.SceneName} frame {result.FrameIndex}: {result.EntityCount} entities, {result.RenderableCount} renderable.");
    }

    private static InspectSceneRuntimeResult ToResult(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeWorld initialWorld)
    {
        var rendering = world.Subsystems.Rendering;
        var physics = world.Subsystems.Physics;
        var audio = world.Subsystems.Audio;
        var animation = world.Subsystems.Animation;
        var ui = world.Subsystems.Ui;
        var xr = world.Subsystems.Xr;
        var culling = BuildCullingSummary(rendering);
        const int maximumEntityStates = 32;
        const int maximumSubsystemItems = 32;
        const int maximumUiElements = 32;
        var initialEntities = initialWorld.Entities.ToDictionary(entity => entity.Id, StringComparer.Ordinal);
        var entityStates = world.Entities
            .OrderBy(entity => entity.Name, StringComparer.Ordinal)
            .ThenBy(entity => entity.Id, StringComparer.Ordinal)
            .Take(maximumEntityStates)
            .Select(entity =>
            {
                var initial = initialEntities.TryGetValue(entity.Id, out var initialEntity)
                    ? initialEntity.Transform
                    : entity.Transform;
                return new InspectSceneRuntimeEntityState(
                    entity.Id,
                    entity.Name,
                    entity.Visible,
                    entity.Transform,
                    entity.Components
                        .Select(component => component.Type)
                        .OrderBy(type => type, StringComparer.Ordinal)
                        .ToArray())
                {
                    InitialTransform = initial,
                    PositionDelta2D = new RekallAgeRuntimeVector2(
                        entity.Transform.Position2D.X - initial.Position2D.X,
                        entity.Transform.Position2D.Y - initial.Position2D.Y),
                    PositionDelta3D = new RekallAgeRuntimeVector3(
                        entity.Transform.Position3D.X - initial.Position3D.X,
                        entity.Transform.Position3D.Y - initial.Position3D.Y,
                        entity.Transform.Position3D.Z - initial.Position3D.Z)
                };
            })
            .ToArray();

        return new InspectSceneRuntimeResult(
            world.SceneName,
            world.FrameIndex,
            world.ElapsedTime.TotalSeconds,
            world.Entities.Count,
            rendering.Cameras.Count + rendering.Sprites.Count + rendering.Meshes.Count + rendering.Lights.Count + rendering.UiLayers.Count,
            physics.RigidBodies.Count,
            physics.Colliders.Count,
            audio.Listeners.Count,
            audio.Emitters.Count,
            animation.Players.Count,
            ui.Elements.Count,
            world.Subsystems.Input.Actions.Count,
            world.Subsystems.Input.Actions,
            world.Subsystems.Events.Events.Count,
            world.Subsystems.Events.Events,
            xr.Rigs.Count,
            xr.Controllers.Count,
            xr.Poses.Count,
            xr.Actions.Count,
            xr.Actions,
            world.SystemsRun,
            world.Observations,
            culling.VisibleRenderableCount,
            culling.CulledRenderables.Count,
            culling.CulledRenderables)
        {
            ActiveAudioVoiceCount = audio.MixFrame.ActiveVoiceCount,
            AudioBusCount = audio.Buses.Count,
            AudioPeakGain = audio.MixFrame.PeakGain,
            AudioMixedSampleCount = audio.MixFrame.Samples?.Count ?? 0,
            AudioVoices = audio.Voices.Take(maximumSubsystemItems).ToArray(),
            AudioVoicesTruncated = audio.Voices.Count > maximumSubsystemItems,
            AnimationPlayers = animation.Players.Take(maximumSubsystemItems).ToArray(),
            AnimationPlayersTruncated = animation.Players.Count > maximumSubsystemItems,
            MorphStates = animation.MorphStates.Take(maximumSubsystemItems).ToArray(),
            MorphStatesTruncated = animation.MorphStates.Count > maximumSubsystemItems,
            EntityStates = entityStates,
            EntityStatesTruncated = world.Entities.Count > maximumEntityStates,
            UiCanvasCount = ui.Canvases.Count,
            InteractiveUiElementCount = ui.InteractiveElementCount,
            UiCanvases = ui.Canvases.Take(maximumSubsystemItems).ToArray(),
            UiElements = ui.Elements
                .OrderBy(element => element.EntityName, StringComparer.Ordinal)
                .ThenBy(element => element.EntityId, StringComparer.Ordinal)
                .Take(maximumUiElements)
                .ToArray(),
            UiElementsTruncated = ui.Elements.Count > maximumUiElements
        };
    }

    private static RuntimeCullingSummary BuildCullingSummary(RekallAgeRuntimeRenderView rendering)
    {
        var activeCamera = rendering.Cameras
            .OrderByDescending(camera => camera.Active)
            .ThenBy(camera => camera.EntityName, StringComparer.Ordinal)
            .ThenBy(camera => camera.EntityId, StringComparer.Ordinal)
            .FirstOrDefault();
        var candidates = EnumerateRenderableCandidates(rendering).ToArray();
        var culled = candidates
            .Where(candidate => !RekallAgeRenderLayerMask.IncludesLayer(candidate.Layer, activeCamera?.CullingMask))
            .Select(candidate => new InspectSceneRuntimeCulledRenderable(
                candidate.EntityId,
                candidate.EntityName,
                candidate.Kind,
                candidate.Layer,
                "camera-culling-mask",
                activeCamera?.EntityName,
                activeCamera?.CullingMask ?? "*"))
            .OrderBy(candidate => candidate.EntityName, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.EntityId, StringComparer.Ordinal)
            .ToArray();

        return new RuntimeCullingSummary(candidates.Length - culled.Length, culled);
    }

    private static IEnumerable<RuntimeRenderableCandidate> EnumerateRenderableCandidates(
        RekallAgeRuntimeRenderView rendering)
    {
        foreach (var sprite in rendering.Sprites)
        {
            yield return new RuntimeRenderableCandidate(sprite.EntityId, sprite.EntityName, "sprite", RekallAgeRenderLayerMask.NormalizeLayer(sprite.Layer));
        }

        foreach (var mesh in rendering.Meshes)
        {
            yield return new RuntimeRenderableCandidate(mesh.EntityId, mesh.EntityName, "mesh", RekallAgeRenderLayerMask.NormalizeLayer(mesh.Layer));
        }

        foreach (var light in rendering.Lights)
        {
            yield return new RuntimeRenderableCandidate(light.EntityId, light.EntityName, "light", RekallAgeRenderLayerMask.NormalizeLayer(light.Layer));
        }

        foreach (var uiLayer in rendering.UiLayers)
        {
            yield return new RuntimeRenderableCandidate(uiLayer.EntityId, uiLayer.EntityName, "ui", "default");
        }
    }

    private sealed record RuntimeCullingSummary(
        int VisibleRenderableCount,
        IReadOnlyList<InspectSceneRuntimeCulledRenderable> CulledRenderables);

    private sealed record RuntimeRenderableCandidate(
        string EntityId,
        string EntityName,
        string Kind,
        string Layer);
}
