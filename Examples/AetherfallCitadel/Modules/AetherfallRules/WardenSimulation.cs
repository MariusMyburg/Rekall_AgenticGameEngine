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
        var objectivePhase = warden.ComponentString(
            AetherfallConstants.WardenStateType,
            "objectivePhase",
            "arrival") ?? "arrival";
        var minimumX = objectivePhase == "arrival"
            ? AetherfallConstants.ArrivalMinimumX
            : AetherfallConstants.CitadelMinimumX;
        var maximumX = objectivePhase == "arrival"
            ? AetherfallConstants.ArrivalMaximumX
            : AetherfallConstants.CitadelMaximumX;
        var minimumZ = objectivePhase == "arrival"
            ? AetherfallConstants.ArrivalMinimumZ
            : AetherfallConstants.CitadelMinimumZ;
        var maximumZ = objectivePhase == "arrival"
            ? AetherfallConstants.ArrivalMaximumZ
            : AetherfallConstants.CitadelMaximumZ;
        var nextX = Math.Clamp(
            position.X + velocityX * seconds,
            minimumX,
            maximumX);
        var nextZ = Math.Clamp(
            position.Z + velocityZ * seconds,
            minimumZ,
            maximumZ);
        var hasDirection = Math.Abs(moveX) > 0.0001 || Math.Abs(moveZ) > 0.0001;
        var combatStarted = warden.ComponentBoolean(AetherfallConstants.WardenStateType, "combatStarted")
            || hasDirection
            || world.WasInputActionPressed(AetherfallConstants.PulseAction)
            || world.WasInputActionPressed(AetherfallConstants.DashAction)
            || world.WasInputActionPressed(AetherfallConstants.InteractAction);
        var aether = warden.ComponentNumber(AetherfallConstants.WardenStateType, "aether", 100);
        var dashCooldown = Math.Max(
            0,
            warden.ComponentNumber(AetherfallConstants.WardenStateType, "dashCooldown") - seconds);
        var invulnerability = Math.Max(
            0,
            warden.ComponentNumber(AetherfallConstants.WardenStateType, "invulnerability") - seconds);
        if (world.WasInputActionPressed(AetherfallConstants.DashAction)
            && hasDirection
            && dashCooldown <= 0
            && aether >= AetherfallConstants.DashCost)
        {
            nextX = Math.Clamp(
                nextX + moveX * AetherfallConstants.DashDistance,
                minimumX,
                maximumX);
            nextZ = Math.Clamp(
                nextZ + moveZ * AetherfallConstants.DashDistance,
                minimumZ,
                maximumZ);
            aether -= AetherfallConstants.DashCost;
            dashCooldown = AetherfallConstants.DashCooldownSeconds;
            invulnerability = AetherfallConstants.DashInvulnerabilitySeconds;
            world = world.AddEntity(AetherfallEntityFactory.CreateDashEffect(
                world.FrameIndex,
                new RekallAgeRuntimeVector3(nextX, position.Y, nextZ)));
            world = world.EmitEvent(warden, "warden.dashed", nameof(WardenSimulation));
        }

        var pulseCooldown = Math.Max(
            0,
            warden.ComponentNumber(AetherfallConstants.WardenStateType, "pulseCooldown") - seconds);
        var pulsePressed = world.WasInputActionPressed(AetherfallConstants.PulseAction);
        if (pulsePressed && pulseCooldown <= 0 && aether >= AetherfallConstants.PulseCost)
        {
            var facingX = warden.ComponentNumber(AetherfallConstants.WardenStateType, "facingX");
            var facingZ = warden.ComponentNumber(AetherfallConstants.WardenStateType, "facingZ", 1);
            var (pulseX, pulseZ) = AetherfallMath.NormalizePlanar(facingX, facingZ);
            var pulse = AetherfallEntityFactory.CreateWardenPulse(
                world.FrameIndex,
                new RekallAgeRuntimeVector3(nextX + pulseX, position.Y, nextZ + pulseZ),
                pulseX,
                pulseZ);
            world = world.AddEntity(pulse);
            pulseCooldown = AetherfallConstants.PulseCooldownSeconds;
            aether -= AetherfallConstants.PulseCost;
        }

        return world.UpdateEntity(warden.Id, entity =>
        {
            var updated = entity
                .WithPosition3D(new RekallAgeRuntimeVector3(nextX, position.Y, nextZ))
                .WithComponentNumber(AetherfallConstants.WardenStateType, "velocityX", velocityX)
                .WithComponentNumber(AetherfallConstants.WardenStateType, "velocityZ", velocityZ)
                .WithComponentNumber(AetherfallConstants.WardenStateType, "pulseCooldown", pulseCooldown)
                .WithComponentNumber(AetherfallConstants.WardenStateType, "dashCooldown", dashCooldown)
                .WithComponentNumber(AetherfallConstants.WardenStateType, "invulnerability", invulnerability)
                .WithComponentBoolean(AetherfallConstants.WardenStateType, "combatStarted", combatStarted)
                .WithComponentNumber(AetherfallConstants.WardenStateType, "aether", aether);
            return hasDirection
                ? updated
                    .WithComponentNumber(AetherfallConstants.WardenStateType, "facingX", moveX)
                    .WithComponentNumber(AetherfallConstants.WardenStateType, "facingZ", moveZ)
                : updated;
        });
    }
}
