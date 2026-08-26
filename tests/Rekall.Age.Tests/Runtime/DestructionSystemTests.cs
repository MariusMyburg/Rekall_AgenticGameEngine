using System.Text.Json.Nodes;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;
using Rekall.Age.Runtime;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Runtime;

public sealed class DestructionSystemTests
{
    [Fact]
    public async Task TriggeredDestructibleSpawnsOutwardMovingChunksAndRemovesTheSource()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeMeshAssetStore().SaveAsync(root, ChunkMesh("chunk-0"), CancellationToken.None);
        await new RekallAgeMeshAssetStore().SaveAsync(root, ChunkMesh("chunk-1"), CancellationToken.None);

        var scene = RekallAgeSceneDocument.Create("Main", ["world", "physics3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Explosive Crate", ["destructible"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D"))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Destructible", new JsonObject
                {
                    ["triggered"] = true,
                    ["chunkMeshAssetIds"] = new JsonArray("chunk-0", "chunk-1"),
                    ["explosionImpulse"] = 5.0
                })));

        using var loop = RekallAgeRuntimeExecutionLoop.CreateDefault(root);
        var result = await loop.RunAsync(
            new RekallAgeRuntimeWorldBuilder().Build(scene, root),
            1,
            CancellationToken.None);

        Assert.DoesNotContain(result.World.Entities, entity => entity.Name == "Explosive Crate");
        var chunks = result.World.Entities.Where(entity => entity.Tags.Contains("destructible-chunk")).ToArray();
        Assert.Equal(2, chunks.Length);
        Assert.All(chunks, chunk =>
        {
            var rigidbody = Assert.Single(chunk.Components, component => component.Type == "Rekall.Rigidbody3D");
            var vx = rigidbody.Properties["linearVelocityX"]!.GetValue<double>();
            var vy = rigidbody.Properties["linearVelocityY"]!.GetValue<double>();
            var vz = rigidbody.Properties["linearVelocityZ"]!.GetValue<double>();
            Assert.True(Math.Sqrt(vx * vx + vy * vy + vz * vz) > 0);
            Assert.Single(chunk.Components, component => component.Type == "Rekall.Transform3D");
        });
    }

    [Fact]
    public async Task TriggeredDestructibleWithATerrainReferenceCratersItsMesh()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeMeshAssetStore().SaveAsync(root, ChunkMesh("chunk-0"), CancellationToken.None);
        await new RekallAgeMeshAssetStore().SaveAsync(root, TerrainMesh(), CancellationToken.None);

        var scene = RekallAgeSceneDocument.Create("Main", ["world", "physics3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Terrain", ["level"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D"))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.MeshAssetReference", new JsonObject { ["assetId"] = "terrain-mesh" }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.MeshRenderer")) with { Id = "terrain" })
            .AddEntity(RekallAgeEntityDocument.Create("Grenade", ["destructible"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D"))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Destructible", new JsonObject
                {
                    ["triggered"] = true,
                    ["chunkMeshAssetIds"] = new JsonArray("chunk-0"),
                    ["explosionImpulse"] = 5.0,
                    ["terrainEntityId"] = "terrain",
                    ["craterRadius"] = 2.0,
                    ["craterDepth"] = 1.0
                })));

        using var loop = RekallAgeRuntimeExecutionLoop.CreateDefault(root);
        await loop.RunAsync(new RekallAgeRuntimeWorldBuilder().Build(scene, root), 1, CancellationToken.None);

        var cratered = await new RekallAgeMeshAssetStore().LoadAsync(root, "terrain-mesh", CancellationToken.None);
        Assert.All(cratered.Topology.Positions, position => Assert.True(position.Y < 0));
        Assert.Equal(2, cratered.Revision);
    }

    private static RekallAgeMeshAsset ChunkMesh(string assetId) => RekallAgeMeshAsset.Create(
        assetId,
        assetId,
        new(
            PointIds: [1, 2, 3, 4],
            Positions: [new(-0.25, -0.25, -0.25), new(0.25, -0.25, -0.25), new(0.25, 0.25, -0.25), new(-0.25, 0.25, -0.25)],
            EdgeIds: [11, 12, 13, 14],
            EdgePointIndices: [new(0, 1), new(1, 2), new(2, 3), new(3, 0)],
            FaceIds: [21],
            FaceOffsets: [0, 4],
            CornerIds: [31, 32, 33, 34],
            CornerPointIndices: [0, 1, 2, 3],
            CornerEdgeIndices: [0, 1, 2, 3]));

    private static RekallAgeMeshAsset TerrainMesh() => RekallAgeMeshAsset.Create(
        "terrain-mesh",
        "Terrain",
        new(
            PointIds: [1, 2, 3, 4],
            Positions: [new(-1, 0, -1), new(-1, 0, 1), new(1, 0, 1), new(1, 0, -1)],
            EdgeIds: [11, 12, 13, 14],
            EdgePointIndices: [new(0, 1), new(1, 2), new(2, 3), new(3, 0)],
            FaceIds: [21],
            FaceOffsets: [0, 4],
            CornerIds: [31, 32, 33, 34],
            CornerPointIndices: [0, 1, 2, 3],
            CornerEdgeIndices: [0, 1, 2, 3]));
}
