using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Game.Modules.AetherfallRules;

internal static class EncounterSimulation
{
    public static RekallAgeRuntimeWorld Update(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeModuleFrameContext context)
    {
        world = UpdateElapsedTime(world, context);
        world = AdvanceCompletedResonanceEncounter(world);

        if (!world.WasInputActionPressed(AetherfallConstants.InteractAction))
        {
            return world;
        }

        var warden = world.FindEntity(AetherfallConstants.WardenName);
        if (warden is null)
        {
            return world;
        }

        var conduit = world.EntitiesWithComponent(AetherfallConstants.ConduitStateType)
            .FirstOrDefault(entity =>
                !entity.ComponentBoolean(AetherfallConstants.ConduitStateType, "active")
                && PlanarDistanceSquared(entity.Transform.Position3D, warden.Transform.Position3D)
                    <= AetherfallConstants.ConduitInteractionRadius * AetherfallConstants.ConduitInteractionRadius);
        if (conduit is null)
        {
            return world;
        }

        var required = conduit.ComponentNumber(AetherfallConstants.ConduitStateType, "requiredShards", 1);
        var shards = warden.ComponentNumber(AetherfallConstants.WardenStateType, "shardCount");
        if (shards < required)
        {
            return world.EmitObservation(
                conduit,
                "AETHERFALL_CONDUIT_REQUIRES_SHARDS",
                "info",
                "gameplay",
                nameof(EncounterSimulation),
                $"Conduit requires {required} echo shards; the warden has {shards}.");
        }

        var linkedGate = conduit.ComponentString(AetherfallConstants.ConduitStateType, "linkedGate", string.Empty);
        world = world.UpdateEntity(conduit.Id, entity => entity
            .WithComponentBoolean(AetherfallConstants.ConduitStateType, "active", true)
            .WithComponentNumber(AetherfallConstants.ConduitStateType, "activationProgress", 1));
        if (!string.IsNullOrWhiteSpace(linkedGate))
        {
            world = world.UpdateEntity(linkedGate, entity => entity.WithVisible(false));
        }

        world = world.UpdateEntity(warden.Id, entity => entity
            .WithComponentString(AetherfallConstants.WardenStateType, "objectivePhase", "resonance"));
        world = world.UpdateEntity("encounter", entity => entity
            .WithComponentString(AetherfallConstants.EncounterStateType, "activeZone", "resonance")
            .WithComponentNumber(AetherfallConstants.EncounterStateType, "wave", 1)
            .WithComponentString(AetherfallConstants.EncounterStateType, "gateState", "arrival-open"));
        world = world.UpdateEntitiesWithTagAndComponent(
            "zone.resonance",
            AetherfallConstants.EnemyStateType,
            entity => entity
                .WithComponentBoolean(AetherfallConstants.EnemyStateType, "active", true)
                .WithComponentString(AetherfallConstants.EnemyStateType, "phase", "engaged")
                .WithVisible(true));
        world = world.EmitEvent(conduit, "conduit.activated", nameof(EncounterSimulation));
        return world;
    }

    private static RekallAgeRuntimeWorld AdvanceCompletedResonanceEncounter(RekallAgeRuntimeWorld world)
    {
        var encounter = world.FindEntity("CitadelEncounter");
        if (encounter is null
            || !string.Equals(
                encounter.ComponentString(AetherfallConstants.EncounterStateType, "activeZone", "arrival"),
                "resonance",
                StringComparison.OrdinalIgnoreCase))
        {
            return world;
        }

        var remaining = world.EntitiesWithTagAndComponent("zone.resonance", AetherfallConstants.EnemyStateType)
            .Count(enemy => enemy.ComponentNumber(AetherfallConstants.EnemyStateType, "health") > 0);
        world = world.UpdateEntity(encounter.Id, entity => entity.WithComponentNumber(
            AetherfallConstants.EncounterStateType,
            "remainingEnemies",
            remaining));
        if (remaining > 0)
        {
            return world;
        }

        var warden = world.FindEntity(AetherfallConstants.WardenName);
        if (warden is not null)
        {
            world = world.UpdateEntity(warden.Id, entity => entity.WithComponentString(
            AetherfallConstants.WardenStateType,
            "objectivePhase",
            "observatory"));
        }
        world = world.UpdateEntity(encounter.Id, entity => entity
            .WithComponentString(AetherfallConstants.EncounterStateType, "activeZone", "observatory")
            .WithComponentNumber(AetherfallConstants.EncounterStateType, "wave", 2)
            .WithComponentString(AetherfallConstants.EncounterStateType, "gateState", "observatory-open"));
        world = world.UpdateEntity("observatory-gate", entity => entity.WithVisible(false));
        world = world.UpdateEntity("guardian", entity => entity
            .WithComponentString(AetherfallConstants.GuardianStateType, "stage", "shielded")
            .WithComponentBoolean(AetherfallConstants.GuardianStateType, "vulnerable", false)
            .WithVisible(true));
        return world.EmitEvent(encounter, "encounter.completed", nameof(EncounterSimulation));
    }

    private static RekallAgeRuntimeWorld UpdateElapsedTime(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeModuleFrameContext context)
    {
        var seconds = Math.Clamp(context.DeltaTime.TotalSeconds, 0, AetherfallConstants.MaximumDeltaSeconds);
        return world.UpdateEntity("encounter", entity => entity.WithComponentNumber(
            AetherfallConstants.EncounterStateType,
            "elapsedTime",
            entity.ComponentNumber(AetherfallConstants.EncounterStateType, "elapsedTime") + seconds));
    }

    private static double PlanarDistanceSquared(RekallAgeRuntimeVector3 a, RekallAgeRuntimeVector3 b)
    {
        var x = a.X - b.X;
        var z = a.Z - b.Z;
        return x * x + z * z;
    }
}
