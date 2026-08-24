using System.Text.Json.Nodes;
using Rekall.Age.Runtime;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Runtime;

/// <summary>
/// Before this fix, RekallAgeBepuPhysicsSystem.CreatePhysicsEntity only ever looked for a sibling
/// Rekall.MeshAssetReference when resolving a Rekall.MeshCollider's geometry; a same-entity
/// Rekall.ModelAssetReference (what rekall.scene.instantiate_asset actually places) was never
/// checked at all, so a placed Model Asset's dynamic MeshCollider body had no shape to cook and
/// never actually simulated. Confirmed empirically before fixing it via a real MCP publish/
/// instantiate/runtime-inspect sequence (Physics bodies: 1, Physics colliders: 1, and the entity's
/// Y position actually falling under gravity once fixed). Mirrors MeshAssetPhysicsTests' coverage of
/// the older Rekall.MeshAssetReference path.
/// </summary>
public sealed class ModelAssetPhysicsTests
{
    [Fact]
    public async Task DynamicMeshColliderCooksFromAPlacedModelAssetsCompiledSnapshot()
    {
        var root = TestPaths.CreateTempDirectory();
        await Rendering.ModelAssetRenderingTests.PublishBoxModelAssetAsync(root, "falling-box-model");
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "physics3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Falling Box", ["model-asset"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["y"] = 5 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.ModelAssetReference",
                    new JsonObject { ["assetId"] = "falling-box-model" }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.MeshRenderer"))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.MeshCollider",
                    new JsonObject { ["convex"] = true }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Rigidbody3D",
                    new JsonObject { ["mass"] = 1 })));

        using var loop = RekallAgeRuntimeExecutionLoop.CreateDefault(root);
        var result = await loop.RunAsync(
            new RekallAgeRuntimeWorldBuilder().Build(scene, root),
            30,
            CancellationToken.None);

        var box = Assert.Single(result.World.Entities, entity => entity.Name == "Falling Box");
        // Free fall for 30 frames at the default 60 fps timestep: y(t) = 5 - 0.5 * 9.81 * t^2 with
        // t = 0.5s gives y ~ 3.77; a wide range just proves the body is being simulated at all
        // (started at y=5, is meaningfully lower, and has not fallen straight through with no
        // collider at all producing an unrealistic freefall well past this range).
        Assert.InRange(box.Transform.Position3D.Y, 2.0, 4.5);
    }
}
