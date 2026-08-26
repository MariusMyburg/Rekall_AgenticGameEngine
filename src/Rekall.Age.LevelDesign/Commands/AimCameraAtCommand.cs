using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.World;

namespace Rekall.Age.LevelDesign.Commands;

public sealed record AimCameraAtRequest(
    string ProjectRoot,
    string SceneName,
    string CameraEntityId,
    string? TargetEntityId = null,
    double? TargetX = null,
    double? TargetY = null,
    double? TargetZ = null);

public sealed record AimCameraAtResult(
    RekallAgeSceneDocument Scene,
    double Pitch,
    double Yaw);

/// <summary>
/// Points a <c>Rekall.Camera3D</c> entity's <c>Rekall.Transform3D</c> pitch/yaw at a target
/// entity or literal world point, using the exact rotation convention the renderer applies
/// (<c>forward = (cos(pitch)*sin(yaw), -sin(pitch), cos(pitch)*cos(yaw))</c> with roll held at
/// zero). Hand-deriving a correct pitch/yaw pair for a look-at shot is easy to get backwards --
/// this closes that authoring gap directly instead of relying on trial-and-error captures.
/// </summary>
public sealed class AimCameraAtCommand : IRekallAgeCommand<AimCameraAtRequest, AimCameraAtResult>
{
    private readonly RekallAgeSceneStore _store = new();

    public string Name => "rekall.level.camera.aim_at";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Sets a Rekall.Camera3D entity's Rekall.Transform3D pitch and yaw so it looks at a target entity (by id) or a literal targetX/targetY/targetZ point. Roll is left unchanged. Exactly one of targetEntityId or the target coordinates must be provided.",
        typeof(AimCameraAtRequest).FullName!,
        typeof(AimCameraAtResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<AimCameraAtResult>> ExecuteAsync(
        AimCameraAtRequest request,
        RekallAgeCommandContext context)
    {
        var hasTargetEntity = !string.IsNullOrWhiteSpace(request.TargetEntityId);
        var hasTargetPoint = request.TargetX.HasValue || request.TargetY.HasValue || request.TargetZ.HasValue;
        if (hasTargetEntity == hasTargetPoint)
        {
            var error = new RekallAgeCommandError(
                "REKALL_CAMERA_AIM_TARGET_AMBIGUOUS",
                "Provide exactly one of targetEntityId or targetX/targetY/targetZ.",
                request.CameraEntityId);
            return RekallAgeCommandResult<AimCameraAtResult>.Failure(default!, error.Message, [error]);
        }

        var loaded = await _store.LoadVersionedAsync(request.ProjectRoot, request.SceneName, context.CancellationToken);
        var scene = loaded.Value;
        var camera = scene.GetRequiredEntity(request.CameraEntityId);
        var (eyeX, eyeY, eyeZ) = ReadPosition(camera);

        double targetX, targetY, targetZ;
        if (hasTargetEntity)
        {
            var target = scene.GetRequiredEntity(request.TargetEntityId!);
            (targetX, targetY, targetZ) = ReadPosition(target);
        }
        else
        {
            targetX = request.TargetX ?? eyeX;
            targetY = request.TargetY ?? eyeY;
            targetZ = request.TargetZ ?? eyeZ;
        }

        var dx = targetX - eyeX;
        var dy = targetY - eyeY;
        var dz = targetZ - eyeZ;
        var distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (distance <= 0.0001)
        {
            var error = new RekallAgeCommandError(
                "REKALL_CAMERA_AIM_TARGET_COINCIDENT",
                "The target position is coincident with the camera position; no direction to aim.",
                request.CameraEntityId);
            return RekallAgeCommandResult<AimCameraAtResult>.Failure(default!, error.Message, [error]);
        }

        var nx = dx / distance;
        var ny = dy / distance;
        var nz = dz / distance;
        var pitch = -Math.Asin(Math.Clamp(ny, -1, 1)) * (180.0 / Math.PI);
        var yaw = Math.Atan2(nx, nz) * (180.0 / Math.PI);

        var updated = scene.UpdateEntity(
            request.CameraEntityId,
            entity => entity.UpdateComponent(
                "Rekall.Transform3D",
                component => component
                    .SetProperty("pitch", JsonValue.Create(pitch))
                    .SetProperty("yaw", JsonValue.Create(yaw))));
        var scenePath = _store.GetScenePath(request.ProjectRoot, request.SceneName);
        context.Transaction.CaptureResourcePreimage(scenePath);
        await _store.SaveIfRevisionAsync(request.ProjectRoot, updated, loaded.Revision, context.CancellationToken);
        context.Transaction.RecordChangedResource(scenePath);

        return RekallAgeCommandResult<AimCameraAtResult>.Success(
            new AimCameraAtResult(updated, pitch, yaw),
            $"Aimed camera '{request.CameraEntityId}' with pitch={pitch:F2}, yaw={yaw:F2}.");
    }

    private static (double X, double Y, double Z) ReadPosition(RekallAgeEntityDocument entity)
    {
        var transform = entity.Components.FirstOrDefault(component =>
            component.Type.Equals("Rekall.Transform3D", StringComparison.Ordinal));
        return (
            ReadNumber(transform, "x"),
            ReadNumber(transform, "y"),
            ReadNumber(transform, "z"));
    }

    private static double ReadNumber(RekallAgeComponentDocument? component, string name)
    {
        if (component is null)
        {
            return 0;
        }

        var value = component.Properties
            .FirstOrDefault(property => property.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
            .Value;
        return value is JsonValue jsonValue && jsonValue.TryGetValue<double>(out var number) ? number : 0;
    }
}
