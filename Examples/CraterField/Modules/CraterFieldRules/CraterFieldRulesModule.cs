using System.Text.Json.Nodes;
using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Game.Modules.CraterFieldRules;

[RekallAgeModule("CraterFieldRules", "Crater Field Rules")]
[RekallAgeRequiresCapability("world")]
public sealed class CraterFieldRulesModule : RekallAgeModule
{
    public override void Configure(RekallAgeModuleBuilder builder)
    {
        builder.RegisterComponent<SpawnerState>();
        builder.RegisterComponent<GrenadeFuse>();
        builder.RegisterRuntimeSystem<CraterFieldRulesSystem>();
    }
}

[RekallAgeComponent("Spawner State")]
public sealed class SpawnerState : RekallAgeComponent
{
    [RekallAgeProperty]
    public bool Enabled { get; init; } = true;

    [RekallAgeProperty]
    public double IntervalSeconds { get; init; } = 3;

    [RekallAgeProperty]
    public double Elapsed { get; init; }

    [RekallAgeProperty]
    public double MinX { get; init; } = -9;

    [RekallAgeProperty]
    public double MaxX { get; init; } = 9;

    [RekallAgeProperty]
    public double MinZ { get; init; } = -9;

    [RekallAgeProperty]
    public double MaxZ { get; init; } = 9;

    [RekallAgeProperty]
    public double SpawnHeight { get; init; } = 10;

    [RekallAgeProperty]
    public double FuseSeconds { get; init; } = 1.5;

    [RekallAgeProperty]
    public double CraterRadius { get; init; } = 2.5;

    [RekallAgeProperty]
    public double CraterDepth { get; init; } = 1.25;

    [RekallAgeProperty]
    public double ExplosionImpulse { get; init; } = 6;

    [RekallAgeProperty]
    public string TerrainEntityId { get; init; } = "terrain";

    [RekallAgeProperty]
    public double SpawnCount { get; init; }

    [RekallAgeProperty]
    public double ExplosionCount { get; init; }
}

[RekallAgeComponent("Grenade Fuse")]
public sealed class GrenadeFuse : RekallAgeComponent
{
    [RekallAgeProperty]
    public double RemainingSeconds { get; init; } = 1.5;
}

public sealed class CraterFieldRulesSystem : IRekallAgeRuntimeModuleSystem
{
    private const string SpawnerComponentType = "Game.Modules.CraterFieldRules.SpawnerState";
    private const string FuseComponentType = "Game.Modules.CraterFieldRules.GrenadeFuse";
    private static readonly string[] GrenadeChunkAssetIds =
    [
        "grenade-chunk-0",
        "grenade-chunk-1",
        "grenade-chunk-2",
        "grenade-chunk-3",
        "grenade-chunk-4"
    ];

    public string Id => nameof(CraterFieldRulesSystem);

    public int Priority => 0;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeModuleFrameContext context)
    {
        var seconds = context.DeltaTime.TotalSeconds;
        var frameSeed = context.FrameIndex;

        world = TickFuses(world, seconds, frameSeed);
        world = TickSpawners(world, seconds, frameSeed);

        return ValueTask.FromResult(world);
    }

    private static RekallAgeRuntimeWorld TickFuses(RekallAgeRuntimeWorld world, double seconds, int frameSeed)
    {
        var expired = new List<string>();

        var updated = world.UpdateEntitiesWithComponent(FuseComponentType, entity =>
        {
            var remaining = entity.ComponentNumber(FuseComponentType, "remainingSeconds", 0) - seconds;
            if (remaining > 0)
            {
                return entity.WithComponentNumber(FuseComponentType, "remainingSeconds", remaining);
            }

            expired.Add(entity.Id);
            return entity;
        });

        foreach (var entityId in expired)
        {
            var entity = updated.FindEntity(entityId);
            if (entity is null)
            {
                continue;
            }

            var chunkAssets = new JsonArray(GrenadeChunkAssetIds.Select(assetId => (JsonNode)assetId).ToArray());
            var destructibleProperties = new JsonObject
            {
                ["triggered"] = true,
                ["chunkMeshAssetIds"] = chunkAssets,
                ["explosionImpulse"] = entity.ComponentNumber(FuseComponentType, "explosionImpulse", 6),
                ["terrainEntityId"] = entity.ComponentString(FuseComponentType, "terrainEntityId", "terrain"),
                ["craterRadius"] = entity.ComponentNumber(FuseComponentType, "craterRadius", 2.5),
                ["craterDepth"] = entity.ComponentNumber(FuseComponentType, "craterDepth", 1.25)
            };

            var detonated = entity
                .UpsertComponent("Rekall.Destructible", destructibleProperties)
                .WithTag("detonated");

            updated = updated.UpdateEntity(entityId, _ => detonated);
        }

        return updated;
    }

    private static RekallAgeRuntimeWorld TickSpawners(RekallAgeRuntimeWorld world, double seconds, int frameSeed)
    {
        var spawned = new List<RekallAgeRuntimeEntity>();

        var updated = world.UpdateEntitiesWithComponent(SpawnerComponentType, entity =>
        {
            if (!entity.ComponentBoolean(SpawnerComponentType, "enabled", true))
            {
                return entity;
            }

            var interval = entity.ComponentNumber(SpawnerComponentType, "intervalSeconds", 3);
            var elapsed = entity.ComponentNumber(SpawnerComponentType, "elapsed", 0) + seconds;
            if (elapsed < interval)
            {
                return entity.WithComponentNumber(SpawnerComponentType, "elapsed", elapsed);
            }

            var spawnCount = entity.ComponentNumber(SpawnerComponentType, "spawnCount", 0);
            var sequenceBase = frameSeed * 1000L + (long)spawnCount;

            var minX = entity.ComponentNumber(SpawnerComponentType, "minX", -9);
            var maxX = entity.ComponentNumber(SpawnerComponentType, "maxX", 9);
            var minZ = entity.ComponentNumber(SpawnerComponentType, "minZ", -9);
            var maxZ = entity.ComponentNumber(SpawnerComponentType, "maxZ", 9);
            var spawnHeight = entity.ComponentNumber(SpawnerComponentType, "spawnHeight", 10);
            var fuseSeconds = entity.ComponentNumber(SpawnerComponentType, "fuseSeconds", 1.5);
            var craterRadius = entity.ComponentNumber(SpawnerComponentType, "craterRadius", 2.5);
            var craterDepth = entity.ComponentNumber(SpawnerComponentType, "craterDepth", 1.25);
            var explosionImpulse = entity.ComponentNumber(SpawnerComponentType, "explosionImpulse", 6);
            var terrainEntityId = entity.ComponentString(SpawnerComponentType, "terrainEntityId", "terrain")!;

            var x = RekallAgeRuntimeModuleSdk.DeterministicRange(0, sequenceBase * 2, minX, maxX);
            var z = RekallAgeRuntimeModuleSdk.DeterministicRange(0, sequenceBase * 2 + 1, minZ, maxZ);

            var grenadeId = $"grenade-{frameSeed}-{(long)spawnCount}";
            var grenade = RekallAgeRuntimeModuleSdk.CreateEntity(grenadeId, "Grenade")
                .WithTag("grenade")
                .WithPosition3D(new RekallAgeRuntimeVector3(x, spawnHeight, z))
                .UpsertComponent("Rekall.MeshAssetReference", new JsonObject { ["assetId"] = "grenade-body" })
                .UpsertComponent("Rekall.MeshRenderer", new JsonObject())
                .UpsertComponent(FuseComponentType, new JsonObject
                {
                    ["remainingSeconds"] = fuseSeconds,
                    ["explosionImpulse"] = explosionImpulse,
                    ["terrainEntityId"] = terrainEntityId,
                    ["craterRadius"] = craterRadius,
                    ["craterDepth"] = craterDepth
                });

            spawned.Add(grenade);

            return entity
                .WithComponentNumber(SpawnerComponentType, "elapsed", elapsed - interval)
                .WithComponentNumber(SpawnerComponentType, "spawnCount", spawnCount + 1);
        });

        foreach (var grenade in spawned)
        {
            updated = updated.AddEntity(grenade);
        }

        return updated;
    }
}
