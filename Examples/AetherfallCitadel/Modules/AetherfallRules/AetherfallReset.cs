using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Game.Modules.AetherfallRules;

internal static class AetherfallReset
{
    public static RekallAgeRuntimeWorld Apply(RekallAgeRuntimeWorld world)
    {
        foreach (var dynamicEntity in world.Entities
                     .Where(entity => entity.HasTag("projectile") || entity.HasTag("effect"))
                     .ToArray())
        {
            world = world.RemoveEntity(dynamicEntity.Id);
        }

        world = world.UpdateEntity("warden", entity => entity
            .WithPosition3D(new RekallAgeRuntimeVector3(
                entity.ComponentNumber(AetherfallConstants.WardenStateType, "spawnX"),
                entity.ComponentNumber(AetherfallConstants.WardenStateType, "spawnY", 0.8),
                entity.ComponentNumber(AetherfallConstants.WardenStateType, "spawnZ", -12)))
            .WithComponentNumber(AetherfallConstants.WardenStateType, "velocityX", 0)
            .WithComponentNumber(AetherfallConstants.WardenStateType, "velocityZ", 0)
            .WithComponentNumber(AetherfallConstants.WardenStateType, "integrity", 100)
            .WithComponentNumber(AetherfallConstants.WardenStateType, "aether", 100)
            .WithComponentNumber(AetherfallConstants.WardenStateType, "score", 0)
            .WithComponentNumber(AetherfallConstants.WardenStateType, "combo", 0)
            .WithComponentNumber(AetherfallConstants.WardenStateType, "dashCooldown", 0)
            .WithComponentNumber(AetherfallConstants.WardenStateType, "pulseCooldown", 0)
            .WithComponentNumber(AetherfallConstants.WardenStateType, "invulnerability", 0)
            .WithComponentNumber(AetherfallConstants.WardenStateType, "shardCount", 0)
            .WithComponentString(AetherfallConstants.WardenStateType, "objectivePhase", "arrival")
            .WithComponentString(AetherfallConstants.WardenStateType, "phase", "playing")
            .WithComponentNumber(AetherfallConstants.WardenStateType, "facingX", 0)
            .WithComponentNumber(AetherfallConstants.WardenStateType, "facingZ", 1));
        world = world.UpdateEntitiesWithComponent(AetherfallConstants.PickupStateType, entity => entity
            .WithComponentBoolean(AetherfallConstants.PickupStateType, "collected", false)
            .WithVisible(true));
        world = world.UpdateEntitiesWithComponent(AetherfallConstants.ConduitStateType, entity => entity
            .WithComponentBoolean(AetherfallConstants.ConduitStateType, "active", false)
            .WithComponentNumber(AetherfallConstants.ConduitStateType, "activationProgress", 0));
        world = world.UpdateEntitiesWithComponent(AetherfallConstants.EnemyStateType, entity =>
        {
            var archetype = entity.ComponentString(AetherfallConstants.EnemyStateType, "archetype", "sentinel") ?? "sentinel";
            var defaultHealth = archetype.Equals("lancer", StringComparison.OrdinalIgnoreCase)
                ? 100
                : archetype.Equals("orbiter", StringComparison.OrdinalIgnoreCase) ? 80 : 60;
            var health = entity.ComponentNumber(AetherfallConstants.EnemyStateType, "maximumHealth", defaultHealth);
            var arrival = entity.HasTag("zone.arrival");
            return entity
                .WithPosition3D(new RekallAgeRuntimeVector3(
                    entity.ComponentNumber(AetherfallConstants.EnemyStateType, "spawnX"),
                    entity.ComponentNumber(AetherfallConstants.EnemyStateType, "spawnY", 0.8),
                    entity.ComponentNumber(AetherfallConstants.EnemyStateType, "spawnZ")))
                .WithComponentNumber(AetherfallConstants.EnemyStateType, "health", health)
                .WithComponentBoolean(AetherfallConstants.EnemyStateType, "active", arrival)
                .WithComponentString(AetherfallConstants.EnemyStateType, "phase", arrival ? "idle" : "dormant")
                .WithVisible(true);
        });
        world = world.UpdateEntity("guardian", entity => entity
            .WithComponentNumber("Game.Modules.AetherfallRules.GuardianState", "health", 500)
            .WithComponentNumber("Game.Modules.AetherfallRules.GuardianState", "shield", 100)
            .WithComponentString("Game.Modules.AetherfallRules.GuardianState", "stage", "sealed")
            .WithComponentNumber("Game.Modules.AetherfallRules.GuardianState", "attackClock", 0)
            .WithComponentBoolean("Game.Modules.AetherfallRules.GuardianState", "vulnerable", false)
            .WithComponentBoolean("Game.Modules.AetherfallRules.GuardianState", "defeated", false)
            .WithVisible(true));
        world = world.UpdateEntitiesWithTag("gate", entity => entity.WithVisible(true));
        return world.UpdateEntity("encounter", entity => entity
            .WithComponentString(AetherfallConstants.EncounterStateType, "activeZone", "arrival")
            .WithComponentNumber(AetherfallConstants.EncounterStateType, "wave", 0)
            .WithComponentNumber(AetherfallConstants.EncounterStateType, "remainingEnemies", 0)
            .WithComponentString(AetherfallConstants.EncounterStateType, "gateState", "sealed")
            .WithComponentNumber(AetherfallConstants.EncounterStateType, "elapsedTime", 0)
            .WithComponentBoolean(AetherfallConstants.EncounterStateType, "completed", false));
    }
}
