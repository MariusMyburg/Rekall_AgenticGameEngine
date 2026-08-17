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

    public IReadOnlyList<InspectSceneRuntimeEntityState> EntityStates { get; init; } =
        Array.Empty<InspectSceneRuntimeEntityState>();

    public bool EntityStatesTruncated { get; init; }
}

public sealed record InspectSceneRuntimeEntityState(
    string EntityId,
    string EntityName,
    bool Visible,
    RekallAgeRuntimeTransform Transform,
    IReadOnlyList<string> ComponentTypes);

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
        "Builds a deterministic runtime snapshot for a scene and reports subsystem readiness.",
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

        var world = await new RekallAgeRuntimeSnapshotService().InspectSceneAsync(
            request.ProjectRoot,
            request.SceneName,
            Math.Max(0, request.Frames),
            request.Inputs,
            context.CancellationToken);
        var result = ToResult(world);
        return RekallAgeCommandResult<InspectSceneRuntimeResult>.Success(
            result,
            $"Runtime {result.SceneName} frame {result.FrameIndex}: {result.EntityCount} entities, {result.RenderableCount} renderable.");
    }

    private static InspectSceneRuntimeResult ToResult(RekallAgeRuntimeWorld world)
    {
        var rendering = world.Subsystems.Rendering;
        var physics = world.Subsystems.Physics;
        var audio = world.Subsystems.Audio;
        var animation = world.Subsystems.Animation;
        var ui = world.Subsystems.Ui;
        var xr = world.Subsystems.Xr;
        var culling = BuildCullingSummary(rendering);
        const int maximumEntityStates = 32;
        var entityStates = world.Entities
            .OrderBy(entity => entity.Name, StringComparer.Ordinal)
            .ThenBy(entity => entity.Id, StringComparer.Ordinal)
            .Take(maximumEntityStates)
            .Select(entity => new InspectSceneRuntimeEntityState(
                entity.Id,
                entity.Name,
                entity.Visible,
                entity.Transform,
                entity.Components
                    .Select(component => component.Type)
                    .OrderBy(type => type, StringComparer.Ordinal)
                    .ToArray()))
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
            EntityStates = entityStates,
            EntityStatesTruncated = world.Entities.Count > maximumEntityStates
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
