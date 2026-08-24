using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Game.Modules.AetherfallRules;

internal static class WorldInteractionSimulation
{
    public static RekallAgeRuntimeWorld Update(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeModuleFrameContext context)
    {
        var seconds = Math.Clamp(
            context.DeltaTime.TotalSeconds,
            0,
            AetherfallConstants.MaximumDeltaSeconds);
        foreach (var projectile in world.EntitiesWithComponent(AetherfallConstants.ProjectileStateType))
        {
            world = AdvanceProjectile(world, projectile, seconds);
        }

        world = AdvanceHazards(world);
        world = CollectPickups(world);
        world = AdvanceEffects(world, seconds);

        return world;
    }

    private static RekallAgeRuntimeWorld AdvanceHazards(RekallAgeRuntimeWorld world)
    {
        var elapsed = world.ElapsedTime.TotalSeconds;
        world = world.UpdateEntitiesWithComponent(AetherfallConstants.HazardStateType, entity =>
        {
            var originX = entity.ComponentNumber(AetherfallConstants.HazardStateType, "originX");
            var originZ = entity.ComponentNumber(AetherfallConstants.HazardStateType, "originZ");
            var speed = entity.ComponentNumber(AetherfallConstants.HazardStateType, "speed", 1);
            var phase = entity.ComponentNumber(AetherfallConstants.HazardStateType, "phaseOffset");
            var angle = elapsed * speed + phase;
            var motion = entity.ComponentString(AetherfallConstants.HazardStateType, "motionKind", "linear") ?? "linear";
            double x;
            double z;
            if (motion.Equals("orbit", StringComparison.OrdinalIgnoreCase))
            {
                var radius = entity.ComponentNumber(AetherfallConstants.HazardStateType, "radius", 1);
                x = originX + Math.Cos(angle) * radius;
                z = originZ + Math.Sin(angle) * radius;
            }
            else
            {
                var amplitude = entity.ComponentNumber(AetherfallConstants.HazardStateType, "amplitude", 1);
                x = originX + Math.Sin(angle) * amplitude;
                z = originZ;
            }

            return entity.WithPosition3D(new RekallAgeRuntimeVector3(x, entity.Transform.Position3D.Y, z));
        });

        var warden = world.FindEntity(AetherfallConstants.WardenName);
        if (warden is null
            || warden.ComponentNumber(AetherfallConstants.WardenStateType, "invulnerability") > 0)
        {
            return world;
        }

        var hit = world.EntitiesWithComponent(AetherfallConstants.HazardStateType)
            .FirstOrDefault(hazard => Overlaps(warden.Transform.Position3D, 0.7, hazard.Transform.Position3D, 0.8));
        if (hit is null)
        {
            return world;
        }

        var damage = hit.ComponentNumber(AetherfallConstants.HazardStateType, "damage", 20);
        world = world.UpdateEntity(warden.Id, entity => entity
            .WithComponentNumber(
                AetherfallConstants.WardenStateType,
                "integrity",
                Math.Max(0, entity.ComponentNumber(AetherfallConstants.WardenStateType, "integrity", 100) - damage))
            .WithComponentNumber(AetherfallConstants.WardenStateType, "invulnerability", 0.6));
        return world.EmitEvent(hit, "hazard.hit", nameof(WorldInteractionSimulation));
    }

    private static RekallAgeRuntimeWorld AdvanceEffects(RekallAgeRuntimeWorld world, double seconds)
    {
        foreach (var effect in world.EntitiesWithComponent(AetherfallConstants.EffectStateType))
        {
            var age = effect.ComponentNumber(AetherfallConstants.EffectStateType, "age") + seconds;
            var lifetime = effect.ComponentNumber(AetherfallConstants.EffectStateType, "lifetime", 0.3);
            if (age >= lifetime)
            {
                world = world.RemoveEntity(effect.Id);
                continue;
            }

            var start = effect.ComponentNumber(AetherfallConstants.EffectStateType, "startScale", 0.4);
            var end = effect.ComponentNumber(AetherfallConstants.EffectStateType, "endScale", 2.2);
            var scale = start + (end - start) * Math.Clamp(age / lifetime, 0, 1);
            world = world.UpdateEntity(effect.Id, entity => entity
                .WithComponentNumber(AetherfallConstants.EffectStateType, "age", age)
                .WithScale3D(new RekallAgeRuntimeVector3(scale, 0.12, scale)));
        }

        return world;
    }

    private static RekallAgeRuntimeWorld CollectPickups(RekallAgeRuntimeWorld world)
    {
        var warden = world.FindEntity(AetherfallConstants.WardenName);
        if (warden is null)
        {
            return world;
        }

        foreach (var pickup in world.EntitiesWithComponent(AetherfallConstants.PickupStateType))
        {
            if (pickup.ComponentBoolean(AetherfallConstants.PickupStateType, "collected")
                || !Overlaps(warden.Transform.Position3D, 0.7, pickup.Transform.Position3D, 0.65))
            {
                continue;
            }

            var kind = pickup.ComponentString(AetherfallConstants.PickupStateType, "kind", "shard") ?? "shard";
            var value = pickup.ComponentNumber(AetherfallConstants.PickupStateType, "value", 1);
            world = world.UpdateEntity(pickup.Id, entity => entity
                .WithComponentBoolean(AetherfallConstants.PickupStateType, "collected", true)
                .WithVisible(false));
            world = world.UpdateEntity(warden.Id, entity => kind.Equals("aether", StringComparison.OrdinalIgnoreCase)
                ? entity.WithComponentNumber(
                    AetherfallConstants.WardenStateType,
                    "aether",
                    Math.Min(100, entity.ComponentNumber(AetherfallConstants.WardenStateType, "aether", 100) + value))
                : entity.WithComponentNumber(
                    AetherfallConstants.WardenStateType,
                    "shardCount",
                    entity.ComponentNumber(AetherfallConstants.WardenStateType, "shardCount") + value));
            world = world.EmitEvent(pickup, "pickup.collected", nameof(WorldInteractionSimulation));
            warden = world.FindEntity(AetherfallConstants.WardenName)!;
        }

        return world;
    }

    private static RekallAgeRuntimeWorld AdvanceProjectile(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeEntity projectile,
        double seconds)
    {
        var position = projectile.Transform.Position3D;
        var velocityX = projectile.ComponentNumber(AetherfallConstants.ProjectileStateType, "velocityX");
        var velocityZ = projectile.ComponentNumber(AetherfallConstants.ProjectileStateType, "velocityZ");
        var nextPosition = new RekallAgeRuntimeVector3(
            position.X + velocityX * seconds,
            position.Y,
            position.Z + velocityZ * seconds);
        var projectileRadius = projectile.ComponentNumber(
            AetherfallConstants.ProjectileStateType,
            "radius",
            AetherfallConstants.PulseRadius);
        var hit = world.EntitiesWithComponent(AetherfallConstants.EnemyStateType)
            .FirstOrDefault(enemy =>
                enemy.ComponentBoolean(AetherfallConstants.EnemyStateType, "active", true)
                && Overlaps(nextPosition, projectileRadius, enemy.Transform.Position3D, 0.8));
        if (hit is not null)
        {
            var damage = projectile.ComponentNumber(
                AetherfallConstants.ProjectileStateType,
                "damage",
                AetherfallConstants.PulseDamage);
            world = world.UpdateEntity(hit.Id, entity =>
            {
                var health = Math.Max(
                    0,
                    entity.ComponentNumber(AetherfallConstants.EnemyStateType, "health", 1) - damage);
                return entity
                    .WithComponentNumber(AetherfallConstants.EnemyStateType, "health", health)
                    .WithComponentBoolean(AetherfallConstants.EnemyStateType, "active", health > 0)
                    .WithVisible(health > 0);
            });
            world = world.EmitEvent(hit, "combat.hit", nameof(WorldInteractionSimulation));
            return world.RemoveEntity(projectile.Id);
        }

        var lifetime = projectile.ComponentNumber(
            AetherfallConstants.ProjectileStateType,
            "remainingLifetime",
            AetherfallConstants.PulseLifetimeSeconds) - seconds;
        if (lifetime <= 0)
        {
            return world.RemoveEntity(projectile.Id);
        }

        return world.UpdateEntity(projectile.Id, entity => entity
            .WithPosition3D(nextPosition)
            .WithComponentNumber(AetherfallConstants.ProjectileStateType, "remainingLifetime", lifetime));
    }

    private static bool Overlaps(
        RekallAgeRuntimeVector3 a,
        double radiusA,
        RekallAgeRuntimeVector3 b,
        double radiusB)
    {
        var x = a.X - b.X;
        var z = a.Z - b.Z;
        var radius = radiusA + radiusB;
        return x * x + z * z <= radius * radius;
    }
}
