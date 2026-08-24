using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Game.Modules.AetherfallRules;

internal static class PresentationSimulation
{
    private const string LabelType = "Rekall.Label";

    public static RekallAgeRuntimeWorld Update(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeModuleFrameContext context)
    {
        var warden = world.FindEntity(AetherfallConstants.WardenName);
        var camera = world.FindEntity("CitadelCamera");
        var status = world.FindEntity("HudStatus");
        var objective = world.FindEntity("HudObjective");
        var guardianHud = world.FindEntity("HudGuardian");
        var guardian = world.FindEntity("CitadelGuardian");
        if (warden is null || camera is null || status is null || objective is null || guardianHud is null || guardian is null)
        {
            return world.EmitSceneObservation(
                "AETHERFALL_PRESENTATION_ENTITY_MISSING",
                "warning",
                "presentation",
                nameof(PresentationSimulation),
                "The authored camera, warden, guardian, or required HUD label is missing.");
        }

        var seconds = Math.Clamp(context.DeltaTime.TotalSeconds, 0, AetherfallConstants.MaximumDeltaSeconds);
        var targetX = Math.Clamp(warden.Transform.Position3D.X * 0.65, -8, 8);
        var targetZ = Math.Clamp(warden.Transform.Position3D.Z - 15, -27, 28);
        var smoothing = 1 - Math.Exp(-6 * seconds);
        var cameraPosition = camera.Transform.Position3D;
        world = world.UpdateEntity(camera.Id, entity => entity.WithPosition3D(new RekallAgeRuntimeVector3(
            cameraPosition.X + (targetX - cameraPosition.X) * smoothing,
            18,
            cameraPosition.Z + (targetZ - cameraPosition.Z) * smoothing)));

        var integrity = Math.Round(warden.ComponentNumber(AetherfallConstants.WardenStateType, "integrity", 100));
        var aether = Math.Round(warden.ComponentNumber(AetherfallConstants.WardenStateType, "aether", 100));
        var shards = Math.Round(warden.ComponentNumber(AetherfallConstants.WardenStateType, "shardCount"));
        var score = Math.Round(warden.ComponentNumber(AetherfallConstants.WardenStateType, "score"));
        var combo = Math.Round(warden.ComponentNumber(AetherfallConstants.WardenStateType, "combo"));
        var phase = warden.ComponentString(AetherfallConstants.WardenStateType, "phase", "playing") ?? "playing";
        var objectivePhase = warden.ComponentString(AetherfallConstants.WardenStateType, "objectivePhase", "arrival") ?? "arrival";
        var objectiveText = objectivePhase.ToLowerInvariant() switch
        {
            "resonance" => "OBJECTIVE: Clear the Resonance Court",
            "observatory" => "OBJECTIVE: Break the Guardian shield",
            _ => "OBJECTIVE: Awaken the Arrival Conduit"
        };
        if (phase.Equals("victory", StringComparison.OrdinalIgnoreCase)) objectiveText = "OBJECTIVE COMPLETE: The Citadel answers your call";
        if (phase.Equals("defeat", StringComparison.OrdinalIgnoreCase)) objectiveText = "WARDEN LOST: Press R to reconstruct";
        if (phase.Equals("paused", StringComparison.OrdinalIgnoreCase)) objectiveText = "PAUSED: Press Escape to resume";

        world = world.UpdateEntity(status.Id, entity => entity
            .WithComponentString(LabelType, "text", $"INTEGRITY {integrity}   AETHER {aether}   SHARDS {shards}   SCORE {score}   COMBO x{combo}")
            .WithComponentString(LabelType, "foregroundColor", integrity <= 25 ? "#ff6677" : "#dffcff"));
        world = world.UpdateEntity(objective.Id, entity => entity.WithComponentString(LabelType, "text", objectiveText));

        var guardianStage = guardian.ComponentString(AetherfallConstants.GuardianStateType, "stage", "sealed") ?? "sealed";
        var guardianHealth = Math.Round(guardian.ComponentNumber(AetherfallConstants.GuardianStateType, "health", 500));
        var guardianShield = Math.Round(guardian.ComponentNumber(AetherfallConstants.GuardianStateType, "shield", 100));
        var guardianText = guardianStage.Equals("sealed", StringComparison.OrdinalIgnoreCase)
            ? "GUARDIAN: SEALED"
            : guardianStage.Equals("defeated", StringComparison.OrdinalIgnoreCase)
                ? "GUARDIAN: DEFEATED"
                : $"GUARDIAN {guardianStage.ToUpperInvariant()}   CORE {guardianHealth}   SHIELD {guardianShield}";
        world = world.UpdateEntity(guardianHud.Id, entity => entity
            .WithComponentString(LabelType, "text", guardianText)
            .WithComponentString(LabelType, "foregroundColor", guardianStage == "enraged" ? "#ff6677" : "#ffb86b"));

        world = world.UpdateEntitiesWithComponent(AetherfallConstants.EnemyStateType, entity => entity.WithVisible(
            entity.ComponentBoolean(AetherfallConstants.EnemyStateType, "active")
            && entity.ComponentNumber(AetherfallConstants.EnemyStateType, "health") > 0));
        return world.UpdateEntity(guardian.Id, entity => entity.WithVisible(
            !guardianStage.Equals("sealed", StringComparison.OrdinalIgnoreCase)
            && !guardianStage.Equals("defeated", StringComparison.OrdinalIgnoreCase)));
    }
}
