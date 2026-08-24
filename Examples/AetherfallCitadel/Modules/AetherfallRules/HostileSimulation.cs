using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Game.Modules.AetherfallRules;

internal static class HostileSimulation
{
    public static RekallAgeRuntimeWorld Update(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeModuleFrameContext context)
    {
        var warden = world.FindEntity(AetherfallConstants.WardenName);
        if (warden is null)
        {
            return world;
        }

        var seconds = Math.Clamp(context.DeltaTime.TotalSeconds, 0, AetherfallConstants.MaximumDeltaSeconds);
        var elapsed = world.ElapsedTime.TotalSeconds;
        foreach (var source in world.EntitiesWithComponent(AetherfallConstants.EnemyStateType)
                     .Where(entity => entity.ComponentBoolean(AetherfallConstants.EnemyStateType, "active", true))
                     .ToArray())
        {
            var position = source.Transform.Position3D;
            var target = warden.Transform.Position3D;
            var (directionX, directionZ) = DirectionTo(position, target);
            var archetype = source.ComponentString(AetherfallConstants.EnemyStateType, "archetype", "sentinel") ?? "sentinel";
            var speed = source.ComponentNumber(AetherfallConstants.EnemyStateType, "speed", 2);
            var preferredRange = source.ComponentNumber(AetherfallConstants.EnemyStateType, "preferredRange", 6);
            var next = position;
            if (archetype.Equals("orbiter", StringComparison.OrdinalIgnoreCase))
            {
                var spawnX = source.ComponentNumber(AetherfallConstants.EnemyStateType, "spawnX", position.X);
                var spawnZ = source.ComponentNumber(AetherfallConstants.EnemyStateType, "spawnZ", position.Z);
                var phase = Math.Abs(spawnX * 0.17 + spawnZ * 0.11);
                next = new RekallAgeRuntimeVector3(
                    spawnX + Math.Cos(elapsed * speed * 0.45 + phase) * 1.8,
                    position.Y,
                    spawnZ + Math.Sin(elapsed * speed * 0.45 + phase) * 1.8);
            }
            else
            {
                var distance = Math.Sqrt(
                    (target.X - position.X) * (target.X - position.X)
                    + (target.Z - position.Z) * (target.Z - position.Z));
                var shouldMove = archetype.Equals("lancer", StringComparison.OrdinalIgnoreCase)
                    || distance > preferredRange;
                if (shouldMove)
                {
                    next = new RekallAgeRuntimeVector3(
                        position.X + directionX * speed * seconds,
                        position.Y,
                        position.Z + directionZ * speed * seconds);
                }
            }

            var attackClock = source.ComponentNumber(AetherfallConstants.EnemyStateType, "attackClock") - seconds;
            world = world.UpdateEntity(source.Id, entity => entity
                .WithPosition3D(next)
                .WithComponentNumber(AetherfallConstants.EnemyStateType, "attackClock", attackClock));
            if (attackClock > 0)
            {
                continue;
            }

            var cadence = source.ComponentNumber(AetherfallConstants.EnemyStateType, "attackCadence", 1.5);
            world = world.UpdateEntity(source.Id, entity => entity.WithComponentNumber(
                AetherfallConstants.EnemyStateType,
                "attackClock",
                cadence));
            world = world.AddEntity(AetherfallEntityFactory.CreateHostilePulse(
                world.FrameIndex,
                source.Id,
                new RekallAgeRuntimeVector3(next.X + directionX, next.Y, next.Z + directionZ),
                directionX,
                directionZ));
        }

        return UpdateGuardian(world, seconds);
    }

    private static RekallAgeRuntimeWorld UpdateGuardian(RekallAgeRuntimeWorld world, double seconds)
    {
        var guardian = world.FindEntity("guardian");
        if (guardian is null
            || guardian.ComponentBoolean(AetherfallConstants.GuardianStateType, "defeated"))
        {
            return world;
        }

        var stage = guardian.ComponentString(AetherfallConstants.GuardianStateType, "stage", "sealed") ?? "sealed";
        if (stage.Equals("sealed", StringComparison.OrdinalIgnoreCase))
        {
            return world;
        }

        var attackClock = guardian.ComponentNumber(AetherfallConstants.GuardianStateType, "attackClock") - seconds;
        if (attackClock > 0)
        {
            return world.UpdateEntity(guardian.Id, entity => entity.WithComponentNumber(
                AetherfallConstants.GuardianStateType,
                "attackClock",
                attackClock));
        }

        var origin = guardian.Transform.Position3D;
        for (var index = 0; index < 8; index++)
        {
            var angle = index * Math.PI * 2 / 8;
            var directionX = Math.Cos(angle);
            var directionZ = Math.Sin(angle);
            world = world.AddEntity(AetherfallEntityFactory.CreateGuardianPulse(
                world.FrameIndex,
                index,
                new RekallAgeRuntimeVector3(
                    origin.X + directionX * 4.2,
                    origin.Y,
                    origin.Z + directionZ * 4.2),
                directionX,
                directionZ));
        }

        var cadence = stage.Equals("enraged", StringComparison.OrdinalIgnoreCase) ? 1.0 : 1.8;
        return world.UpdateEntity(guardian.Id, entity => entity.WithComponentNumber(
            AetherfallConstants.GuardianStateType,
            "attackClock",
            cadence));
    }

    private static (double X, double Z) DirectionTo(
        RekallAgeRuntimeVector3 origin,
        RekallAgeRuntimeVector3 target)
    {
        var x = target.X - origin.X;
        var z = target.Z - origin.Z;
        var length = Math.Sqrt(x * x + z * z);
        return length <= 0.0001 ? (0, -1) : (x / length, z / length);
    }
}
