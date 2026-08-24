using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Game.Modules.AetherfallRules;

internal static class WardenSimulation
{
    public static RekallAgeRuntimeWorld Update(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeModuleFrameContext context)
    {
        var warden = world.FindEntity(AetherfallConstants.WardenName);
        if (warden is null)
        {
            return world.EmitSceneObservation(
                "AETHERFALL_WARDEN_MISSING",
                "blocking",
                "gameplay",
                nameof(WardenSimulation),
                "The authored AetherWarden entity is missing from the active scene.");
        }

        var seconds = Math.Clamp(
            context.DeltaTime.TotalSeconds,
            0,
            AetherfallConstants.MaximumDeltaSeconds);
        var (moveX, moveZ) = AetherfallMath.NormalizePlanar(
            world.InputActionValue(AetherfallConstants.MoveHorizontalAction),
            world.InputActionValue(AetherfallConstants.MoveVerticalAction));
        var velocityX = moveX * AetherfallConstants.WardenSpeed;
        var velocityZ = moveZ * AetherfallConstants.WardenSpeed;
        var position = warden.Transform.Position3D;
        var nextX = Math.Clamp(
            position.X + velocityX * seconds,
            AetherfallConstants.ArrivalMinimumX,
            AetherfallConstants.ArrivalMaximumX);
        var nextZ = Math.Clamp(
            position.Z + velocityZ * seconds,
            AetherfallConstants.ArrivalMinimumZ,
            AetherfallConstants.ArrivalMaximumZ);
        var hasDirection = Math.Abs(moveX) > 0.0001 || Math.Abs(moveZ) > 0.0001;

        return world.UpdateEntity(warden.Id, entity =>
        {
            var updated = entity
                .WithPosition3D(new RekallAgeRuntimeVector3(nextX, position.Y, nextZ))
                .WithComponentNumber(AetherfallConstants.WardenStateType, "velocityX", velocityX)
                .WithComponentNumber(AetherfallConstants.WardenStateType, "velocityZ", velocityZ);
            return hasDirection
                ? updated
                    .WithComponentNumber(AetherfallConstants.WardenStateType, "facingX", moveX)
                    .WithComponentNumber(AetherfallConstants.WardenStateType, "facingZ", moveZ)
                : updated;
        });
    }
}
