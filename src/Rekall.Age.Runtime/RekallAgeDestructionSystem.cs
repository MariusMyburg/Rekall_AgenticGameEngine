using System.Text.Json.Nodes;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Runtime;

/// <summary>
/// Generic destruction: an entity carrying <c>Rekall.Destructible</c> with its "triggered"
/// property set (by any game module - a health-reaches-zero check, a grenade timer, anything)
/// is removed and replaced by one dynamic rigid-body entity per pre-authored chunk mesh, given an
/// outward impulse from the source entity's position. If the component references a terrain
/// entity, the crater_stamp mesh operation (the same one reachable from Studio/CLI/MCP) is
/// applied to that terrain's editable mesh asset, persisted the same revision-safe way any other
/// live mesh edit is - this system knows nothing about grenades or any other specific game.
/// </summary>
public sealed class RekallAgeDestructionSystem : IRekallAgeRuntimeWorldSystem
{
    private const string DestructibleComponent = "Rekall.Destructible";
    private readonly RekallAgeMeshAssetStore _meshStore;
    private readonly RekallAgeMeshOperationExecutor _operations;
    private readonly Random _random;

    public RekallAgeDestructionSystem(
        RekallAgeMeshAssetStore? meshStore = null,
        RekallAgeMeshOperationExecutor? operations = null,
        Random? random = null)
    {
        _meshStore = meshStore ?? new RekallAgeMeshAssetStore();
        _operations = operations ?? new RekallAgeMeshOperationExecutor();
        _random = random ?? new Random();
    }

    public string Id => "runtime.destruction";

    public int Priority => 20;

    public async ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeWorldFrameContext context)
    {
        var triggered = world.Entities
            .Where(entity => entity.Visible)
            .Select(entity => (Entity: entity, Component: entity.Components.FirstOrDefault(component => component.Type.Equals(DestructibleComponent, StringComparison.Ordinal))))
            .Where(item => item.Component is not null && ReadBoolean(item.Component!, "triggered"))
            .ToArray();
        if (triggered.Length == 0)
        {
            return world;
        }

        var observations = new List<RekallAgeRuntimeObservation>(world.Observations);
        var entities = world.Entities.ToList();
        foreach (var (source, component) in triggered)
        {
            entities.RemoveAll(entity => entity.Id.Equals(source.Id, StringComparison.Ordinal));

            var chunkAssetIds = ReadStringArray(component!, "chunkMeshAssetIds");
            var impulse = ReadDouble(component!, "explosionImpulse", 6);
            var origin = source.Transform.Position3D;
            for (var index = 0; index < chunkAssetIds.Count; index++)
            {
                var direction = RandomOutwardDirection();
                entities.Add(BuildChunkEntity(
                    $"{source.Id}-chunk-{index}-{context.FrameIndex}",
                    chunkAssetIds[index],
                    origin,
                    direction,
                    impulse));
            }

            var terrainEntityId = ReadOptionalString(component!, "terrainEntityId");
            if (world.ProjectRoot is not null && terrainEntityId is not null)
            {
                var radius = ReadDouble(component!, "craterRadius", 2);
                var depth = ReadDouble(component!, "craterDepth", 1);
                var applied = await TryStampCraterAsync(world.ProjectRoot, terrainEntityId, entities, origin, radius, depth, context.FrameIndex, observations)
                    .ConfigureAwait(false);
                if (!applied)
                {
                    observations.Add(new RekallAgeRuntimeObservation(
                        context.FrameIndex,
                        "runtime.destruction.terrain_entity_not_found",
                        "warning",
                        "physics",
                        source.Id,
                        source.Name,
                        Id,
                        $"Destructible referenced terrain entity '{terrainEntityId}' which was not found or has no mesh reference.",
                        [source.Id]));
                }
            }
        }

        return world with { Entities = entities, Observations = observations };
    }

    private async ValueTask<bool> TryStampCraterAsync(
        string projectRoot,
        string terrainEntityId,
        List<RekallAgeRuntimeEntity> entities,
        RekallAgeRuntimeVector3 origin,
        double radius,
        double depth,
        int frame,
        List<RekallAgeRuntimeObservation> observations)
    {
        var terrain = entities.FirstOrDefault(entity => entity.Id.Equals(terrainEntityId, StringComparison.Ordinal));
        var reference = terrain?.Components.FirstOrDefault(component => component.Type.Equals("Rekall.MeshAssetReference", StringComparison.Ordinal));
        var assetId = reference is null ? null : ReadOptionalString(reference, "assetId");
        if (assetId is null)
        {
            return false;
        }

        try
        {
            var loaded = await _meshStore.LoadVersionedAsync(projectRoot, assetId, CancellationToken.None).ConfigureAwait(false);
            var request = new RekallAgeMeshOperationRequest(
                "crater_stamp",
                RekallAgeGeometryDomain.Point,
                loaded.Value.Topology.PointIds,
                new JsonObject
                {
                    ["axis"] = "y",
                    ["centerX"] = origin.X,
                    ["centerY"] = origin.Y,
                    ["centerZ"] = origin.Z,
                    ["radius"] = radius,
                    ["depth"] = depth
                });
            var result = _operations.Execute(loaded.Value, request);
            await _meshStore.SaveIfRevisionAsync(projectRoot, result.Mesh, loaded.Revision, CancellationToken.None).ConfigureAwait(false);
            return true;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or RekallAgeMeshOperationException or Rekall.Age.Core.Persistence.RekallAgeDocumentRevisionException)
        {
            observations.Add(new RekallAgeRuntimeObservation(
                frame, "runtime.destruction.crater_stamp_failed", "error", "physics",
                terrainEntityId, terrain?.Name ?? terrainEntityId, Id, error.Message, [terrainEntityId]));
            return false;
        }
    }

    private RekallAgeRuntimeVector3 RandomOutwardDirection()
    {
        // A uniform-ish spread on the unit sphere via rejection-free spherical coordinates -
        // good enough for visually varied debris, not claiming a perfectly uniform distribution.
        var theta = _random.NextDouble() * Math.PI * 2;
        var z = _random.NextDouble() * 2 - 1;
        var planar = Math.Sqrt(Math.Max(0, 1 - z * z));
        return new RekallAgeRuntimeVector3(planar * Math.Cos(theta), Math.Abs(z) * 0.6 + 0.4, planar * Math.Sin(theta));
    }

    private static RekallAgeRuntimeEntity BuildChunkEntity(
        string entityId,
        string chunkMeshAssetId,
        RekallAgeRuntimeVector3 origin,
        RekallAgeRuntimeVector3 direction,
        double impulse)
    {
        return new RekallAgeRuntimeEntity(
            entityId,
            entityId,
            ["destructible-chunk"],
            null,
            null,
            true,
            false,
            RekallAgeRuntimeTransform.Identity with { Position3D = origin },
            [
                new RekallAgeRuntimeComponent("Rekall.MeshAssetReference", new JsonObject { ["assetId"] = chunkMeshAssetId }),
                new RekallAgeRuntimeComponent("Rekall.MeshRenderer", new JsonObject()),
                new RekallAgeRuntimeComponent("Rekall.MeshCollider", new JsonObject { ["convex"] = true }),
                new RekallAgeRuntimeComponent("Rekall.Rigidbody3D", new JsonObject
                {
                    ["mass"] = 1.0,
                    ["linearVelocityX"] = direction.X * impulse,
                    ["linearVelocityY"] = direction.Y * impulse,
                    ["linearVelocityZ"] = direction.Z * impulse
                })
            ]);
    }

    private static bool ReadBoolean(RekallAgeRuntimeComponent component, string name) =>
        component.Properties.TryGetPropertyValue(name, out var value) && value is JsonValue jsonValue && jsonValue.TryGetValue<bool>(out var result) && result;

    private static double ReadDouble(RekallAgeRuntimeComponent component, string name, double fallback) =>
        component.Properties.TryGetPropertyValue(name, out var value) && value is JsonValue jsonValue && jsonValue.TryGetValue<double>(out var result) ? result : fallback;

    private static string? ReadOptionalString(RekallAgeRuntimeComponent component, string name) =>
        component.Properties.TryGetPropertyValue(name, out var value) && value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var result) && !string.IsNullOrWhiteSpace(result)
            ? result
            : null;

    private static IReadOnlyList<string> ReadStringArray(RekallAgeRuntimeComponent component, string name)
    {
        if (!component.Properties.TryGetPropertyValue(name, out var value) || value is not JsonArray array)
        {
            return [];
        }
        return array.OfType<JsonValue>()
            .Select(item => item.TryGetValue<string>(out var text) ? text : null)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text!)
            .ToArray();
    }
}
