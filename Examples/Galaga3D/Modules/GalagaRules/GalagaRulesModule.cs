using System.Text.Json.Nodes;
using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Game.Modules.GalagaRules;

[RekallAgeModule("galaga.rules", "Galaga Rules")]
[RekallAgeRequiresCapability("world")]
public sealed class GalagaRulesModule : RekallAgeModule
{
    public override void Configure(RekallAgeModuleBuilder builder)
    {
        builder.RegisterComponent<GalagaState>();
        builder.RegisterComponent<Enemy>();
        builder.RegisterComponent<Projectile>();
        builder.RegisterRuntimeSystem<GalagaRulesSystem>();
    }
}

[RekallAgeComponent("Galaga State", Description = "Match state: score, lives, and phase, carried on PlayerShip.")]
public sealed class GalagaState : RekallAgeComponent
{
    [RekallAgeProperty] public double Score { get; init; }
    [RekallAgeProperty] public double Lives { get; init; } = 3;
    [RekallAgeProperty] public string Phase { get; init; } = "playing";
    [RekallAgeProperty] public double FireCooldown { get; init; }
}

[RekallAgeComponent("Enemy", Description = "Formation/dive state for one enemy ship.")]
public sealed class Enemy : RekallAgeComponent
{
    [RekallAgeProperty] public bool Alive { get; init; } = true;
    [RekallAgeProperty] public double BaseX { get; init; }
    [RekallAgeProperty] public double BaseZ { get; init; }
    [RekallAgeProperty] public string DivePhase { get; init; } = "formation";
    [RekallAgeProperty] public double DiveTimer { get; init; } = 3;
}

[RekallAgeComponent("Projectile", Description = "A dynamically spawned player bullet moving along +Z.")]
public sealed class Projectile : RekallAgeComponent
{
    [RekallAgeProperty] public double VelocityZ { get; init; } = 12;
}

public sealed class GalagaRulesSystem : IRekallAgeRuntimeModuleSystem
{
    private const string StateType = "Game.Modules.GalagaRules.GalagaState";
    private const string EnemyType = "Game.Modules.GalagaRules.Enemy";
    private const string ProjectileType = "Game.Modules.GalagaRules.Projectile";
    private const string LabelType = "Rekall.Label";

    private const double ArenaHalfWidth = 6.3;
    private const double PlayerSpeed = 7.0;
    private const double PlayerZ = -6;
    private const double FireInterval = 0.35;
    private const double ProjectileSpeed = 12;
    private const double HitRadius = 0.85;
    private const double DiveSpeed = 3.2;
    private const double DiveSwayAmplitude = 2.0;
    private const double FormationSwayAmplitude = 0.6;
    private const double FormationSwaySpeed = 0.8;
    private const int StartingLives = 3;
    private static readonly double[] DiveTimerOffsets = [3.0, 4.3, 5.6, 6.9, 3.6, 4.9, 6.2, 7.5];

    public string Id => nameof(GalagaRulesSystem);

    public int Priority => 0;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeModuleFrameContext context)
    {
        var seconds = context.DeltaTime.TotalSeconds;
        var resetPressed = world.WasInputActionPressed("reset");

        var playerEntity = world.Entities.FirstOrDefault(entity => entity.Name == "PlayerShip");
        if (playerEntity is null)
        {
            return ValueTask.FromResult(world);
        }

        var phase = playerEntity.ComponentString(StateType, "phase", "playing") ?? "playing";

        if (resetPressed)
        {
            world = ResetMatch(world, playerEntity);
            return ValueTask.FromResult(SyncHud(world));
        }

        if (phase != "playing")
        {
            return ValueTask.FromResult(SyncHud(world));
        }

        // Player movement.
        var axis = world.InputActionValue("player.move");
        var playerPosition = playerEntity.Transform.Position3D;
        var newPlayerX = Math.Clamp(playerPosition.X + axis * PlayerSpeed * seconds, -ArenaHalfWidth, ArenaHalfWidth);
        world = world.UpdateEntity(playerEntity.Id, entity =>
            entity.WithPosition3D(new RekallAgeRuntimeVector3(newPlayerX, playerPosition.Y, PlayerZ)));

        // Fire cooldown + spawn.
        var cooldown = playerEntity.ComponentNumber(StateType, "fireCooldown", 0) - seconds;
        var firing = world.InputActionValue("fire") > 0;
        if (firing && cooldown <= 0)
        {
            cooldown = FireInterval;
            // FrameIndex is deterministic and unique per simulated frame, and this branch
            // spawns at most one bullet per frame (gated by the cooldown above), so it is
            // a safe id source without any mutable instance state on this system.
            var bulletId = $"galaga-bullet-{world.FrameIndex}";
            var bullet = RekallAgeRuntimeModuleSdk.CreateEntity(bulletId, bulletId)
                .WithPosition3D(new RekallAgeRuntimeVector3(newPlayerX, 0.3, PlayerZ + 0.9))
                .UpsertComponent(ProjectileType, new JsonObject { ["velocityZ"] = ProjectileSpeed })
                .UpsertComponent("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "sphere", ["color"] = "#ffe066" })
                .UpsertComponent("Rekall.MeshRenderer", new JsonObject { ["mesh"] = "rekall.geometry.sphere" })
                .UpsertComponent("Rekall.Transform3D", new JsonObject
                {
                    ["x"] = newPlayerX,
                    ["y"] = 0.3,
                    ["z"] = PlayerZ + 0.9,
                    ["scaleX"] = 0.18,
                    ["scaleY"] = 0.18,
                    ["scaleZ"] = 0.32
                });
            world = world.AddEntity(bullet);
        }

        world = world.UpdateEntity(playerEntity.Id, entity =>
            entity.WithComponentNumber(StateType, "fireCooldown", Math.Max(cooldown, 0)));

        // Advance projectiles; drop any past the far arena edge.
        var projectilesToRemove = new List<string>();
        world = world.UpdateEntitiesWithComponent(ProjectileType, entity =>
        {
            var velocityZ = entity.ComponentNumber(ProjectileType, "velocityZ", ProjectileSpeed);
            var position = entity.Transform.Position3D;
            var newZ = position.Z + velocityZ * seconds;
            if (newZ > 11)
            {
                projectilesToRemove.Add(entity.Id);
                return entity;
            }

            return entity.WithPosition3D(new RekallAgeRuntimeVector3(position.X, position.Y, newZ));
        });
        foreach (var id in projectilesToRemove)
        {
            world = world.RemoveEntity(id);
        }

        // Advance enemies: gentle formation sway, or a dive toward the player.
        var elapsed = world.ElapsedTime.TotalSeconds;
        var enemyIndex = 0;
        var livesLostThisFrame = 0;
        var scoreGainedThisFrame = 0;
        var currentPlayerX = newPlayerX;
        world = world.UpdateEntitiesWithComponent(EnemyType, entity =>
        {
            var index = enemyIndex++;
            if (!entity.ComponentBoolean(EnemyType, "alive", true))
            {
                return entity;
            }

            var baseX = entity.ComponentNumber(EnemyType, "baseX", 0);
            var baseZ = entity.ComponentNumber(EnemyType, "baseZ", 0);
            var divePhase = entity.ComponentString(EnemyType, "divePhase", "formation") ?? "formation";
            var diveTimer = entity.ComponentNumber(EnemyType, "diveTimer", DiveTimerOffsets[index % DiveTimerOffsets.Length]);

            double x;
            double z;
            if (divePhase == "formation")
            {
                diveTimer -= seconds;
                x = baseX + Math.Sin(elapsed * FormationSwaySpeed + index) * FormationSwayAmplitude;
                z = baseZ;
                if (diveTimer <= 0)
                {
                    divePhase = "diving";
                }

                entity = entity
                    .WithComponentString(EnemyType, "divePhase", divePhase)
                    .WithComponentNumber(EnemyType, "diveTimer", diveTimer);
            }
            else
            {
                var position = entity.Transform.Position3D;
                x = position.X + Math.Sin(elapsed * 1.7 + index) * DiveSwayAmplitude * seconds;
                z = position.Z - DiveSpeed * seconds;
                if (z < PlayerZ - 1.5)
                {
                    // Missed the player: recycle back to formation.
                    entity = entity
                        .WithComponentString(EnemyType, "divePhase", "formation")
                        .WithComponentNumber(EnemyType, "diveTimer", DiveTimerOffsets[index % DiveTimerOffsets.Length]);
                    x = baseX;
                    z = baseZ;
                }
                else if (Math.Abs(x - currentPlayerX) <= HitRadius && Math.Abs(z - PlayerZ) <= HitRadius)
                {
                    // Hit the player: recycle this enemy back to formation and cost a life.
                    livesLostThisFrame++;
                    entity = entity
                        .WithComponentString(EnemyType, "divePhase", "formation")
                        .WithComponentNumber(EnemyType, "diveTimer", DiveTimerOffsets[index % DiveTimerOffsets.Length]);
                    x = baseX;
                    z = baseZ;
                }
            }

            return entity.WithPosition3D(new RekallAgeRuntimeVector3(x, 0, z));
        });

        // Player-projectile vs enemy collisions.
        var remainingProjectiles = world.EntitiesWithComponent(ProjectileType);
        var hitProjectileIds = new HashSet<string>();
        world = world.UpdateEntitiesWithComponent(EnemyType, entity =>
        {
            if (!entity.ComponentBoolean(EnemyType, "alive", true))
            {
                return entity;
            }

            var enemyPosition = entity.Transform.Position3D;
            foreach (var projectile in remainingProjectiles)
            {
                if (hitProjectileIds.Contains(projectile.Id))
                {
                    continue;
                }

                var projectilePosition = projectile.Transform.Position3D;
                if (Math.Abs(projectilePosition.X - enemyPosition.X) <= HitRadius
                    && Math.Abs(projectilePosition.Z - enemyPosition.Z) <= HitRadius)
                {
                    hitProjectileIds.Add(projectile.Id);
                    scoreGainedThisFrame += 100;
                    return entity
                        .WithComponentBoolean(EnemyType, "alive", false)
                        .WithVisible(false)
                        .WithPosition3D(new RekallAgeRuntimeVector3(enemyPosition.X, -20, enemyPosition.Z));
                }
            }

            return entity;
        });
        foreach (var hitId in hitProjectileIds)
        {
            world = world.RemoveEntity(hitId);
        }

        var anyAlive = world.EntitiesWithComponent(EnemyType)
            .Any(entity => entity.ComponentBoolean(EnemyType, "alive", true));

        world = world.UpdateEntity(playerEntity.Id, entity =>
        {
            var score = entity.ComponentNumber(StateType, "score", 0) + scoreGainedThisFrame;
            var lives = entity.ComponentNumber(StateType, "lives", StartingLives) - livesLostThisFrame;
            var nextPhase = "playing";
            if (lives <= 0)
            {
                lives = 0;
                nextPhase = "gameover";
            }
            else if (!anyAlive)
            {
                nextPhase = "win";
            }

            return entity
                .WithComponentNumber(StateType, "score", score)
                .WithComponentNumber(StateType, "lives", lives)
                .WithComponentString(StateType, "phase", nextPhase);
        });

        return ValueTask.FromResult(SyncHud(world));
    }

    private static RekallAgeRuntimeWorld ResetMatch(RekallAgeRuntimeWorld world, RekallAgeRuntimeEntity playerEntity)
    {
        world = world.UpdateEntity(playerEntity.Id, entity => entity
            .WithComponentNumber(StateType, "score", 0)
            .WithComponentNumber(StateType, "lives", StartingLives)
            .WithComponentString(StateType, "phase", "playing")
            .WithComponentNumber(StateType, "fireCooldown", 0)
            .WithPosition3D(new RekallAgeRuntimeVector3(0, 0, PlayerZ)));

        var index = 0;
        world = world.UpdateEntitiesWithComponent(EnemyType, entity =>
        {
            var baseX = entity.ComponentNumber(EnemyType, "baseX", 0);
            var baseZ = entity.ComponentNumber(EnemyType, "baseZ", 0);
            var updated = entity
                .WithComponentBoolean(EnemyType, "alive", true)
                .WithComponentString(EnemyType, "divePhase", "formation")
                .WithComponentNumber(EnemyType, "diveTimer", DiveTimerOffsets[index % DiveTimerOffsets.Length])
                .WithVisible(true)
                .WithPosition3D(new RekallAgeRuntimeVector3(baseX, 0, baseZ));
            index++;
            return updated;
        });

        foreach (var projectile in world.EntitiesWithComponent(ProjectileType))
        {
            world = world.RemoveEntity(projectile.Id);
        }

        return world;
    }

    private static RekallAgeRuntimeWorld SyncHud(RekallAgeRuntimeWorld world)
    {
        var playerEntity = world.Entities.FirstOrDefault(entity => entity.Name == "PlayerShip");
        if (playerEntity is null)
        {
            return world;
        }

        var score = playerEntity.ComponentNumber(StateType, "score", 0);
        var lives = playerEntity.ComponentNumber(StateType, "lives", StartingLives);
        var phase = playerEntity.ComponentString(StateType, "phase", "playing") ?? "playing";
        var livesText = phase switch
        {
            "gameover" => "GAME OVER",
            "win" => "YOU WIN!",
            _ => $"LIVES {lives:0}"
        };

        return world.UpdateEntitiesWithComponent(LabelType, entity => entity.Name switch
        {
            "ScoreLabel" => entity.WithComponentString(LabelType, "text", $"SCORE {score:0}"),
            "LivesLabel" => entity.WithComponentString(LabelType, "text", livesText),
            _ => entity,
        });
    }
}
