using System.Text.Json.Nodes;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;
using Rekall.Age.Runtime;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Runtime;

public sealed class MeshAssetPhysicsTests
{
    [Fact]
    public async Task StaticMeshColliderCooksFromTheSameEditableMeshSnapshotAsRendering()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeMeshAssetStore().SaveAsync(root, GroundMesh(), CancellationToken.None);
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "physics3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Editable Ground", ["level"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D"))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.MeshAssetReference",
                    new JsonObject { ["assetId"] = "ground" }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.MeshCollider")))
            .AddEntity(RekallAgeEntityDocument.Create("Falling Sphere", ["actor"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["y"] = 3 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Rigidbody3D",
                    new JsonObject { ["mass"] = 1 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.SphereCollider3D",
                    new JsonObject { ["radius"] = 0.5 })));

        using var loop = RekallAgeRuntimeExecutionLoop.CreateDefault(root);
        var result = await loop.RunAsync(
            new RekallAgeRuntimeWorldBuilder().Build(scene, root),
            180,
            CancellationToken.None);

        var sphere = Assert.Single(result.World.Entities, entity => entity.Name == "Falling Sphere");
        Assert.InRange(sphere.Transform.Position3D.Y, 0.45, 0.65);
    }

    private static RekallAgeMeshAsset GroundMesh() => RekallAgeMeshAsset.Create(
        "ground",
        "Ground",
        new(
            PointIds: [1, 2, 3, 4],
            Positions: [new(-10, 0, -10), new(-10, 0, 10), new(10, 0, 10), new(10, 0, -10)],
            EdgeIds: [11, 12, 13, 14],
            EdgePointIndices: [new(0, 1), new(1, 2), new(2, 3), new(3, 0)],
            FaceIds: [21],
            FaceOffsets: [0, 4],
            CornerIds: [31, 32, 33, 34],
            CornerPointIndices: [0, 1, 2, 3],
            CornerEdgeIndices: [0, 1, 2, 3]));
}
