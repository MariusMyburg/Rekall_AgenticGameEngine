using Rekall.Age.Core.Commands;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Runtime.Commands;

public sealed record InspectSceneRuntimeRequest(
    string ProjectRoot,
    string SceneName,
    int Frames);

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
    IReadOnlyList<string> SystemsRun,
    IReadOnlyList<RekallAgeRuntimeObservation> Observations);

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
                Array.Empty<string>(),
                Array.Empty<RekallAgeRuntimeObservation>());
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
            world.SystemsRun,
            world.Observations);
    }
}
