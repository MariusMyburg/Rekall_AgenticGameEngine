using System.Text.Json.Nodes;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;
using Rekall.Age.Modules;
using Rekall.Age.Modules.BuiltIns;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Runtime;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Rendering;

public sealed class GrassRendererTests
{
    [Fact]
    public void GrassRendererIsARegisteredGenericComponent()
    {
        Assert.True(RekallAgeBuiltInComponentTypeCatalog.IsKnown("Rekall.GrassRenderer"));
        var modules = RekallAgeModuleIndexer.IndexAssembly(typeof(RekallAgeBuiltInModule).Assembly);
        var builtIns = Assert.Single(modules.Modules, module => module.Id == "rekall.builtins");
        Assert.Contains(builtIns.Components, component => component.DisplayName == "Grass Renderer");
    }

    [Fact]
    public async Task RuntimeFrameScattersGrassBladesAcrossTheGroundMesh()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeMeshAssetStore();
        await store.SaveAsync(root, FlatGround(), CancellationToken.None);
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Ground", ["terrain"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D"))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.MeshAssetReference",
                    new JsonObject { ["assetId"] = "ground" }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.MeshRenderer"))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.GrassRenderer",
                    new JsonObject { ["bladeCount"] = 250, ["windStrength"] = 0 })));

        var world = new RekallAgeRuntimeWorldBuilder().Build(scene, root);
        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 320, 180, false);

        var grass = Assert.Single(frame.Renderables, renderable => renderable.AssetId == "rekall.geometry.grass");
        var geometry = Assert.IsType<RekallAgeRuntimeViewportGeometryMesh>(grass.GeometryMesh);
        Assert.Equal(250 * 4, geometry.Vertices.Count);
        Assert.Equal(250 * 6, geometry.Indices.Count);
        Assert.All(geometry.Vertices, vertex =>
        {
            // A blade's half-width can push its base a little past the sampled triangle edge
            // when its root lands right on the ground mesh's boundary, so allow a small margin
            // beyond the ground's own [-1, 1] extent rather than the exact bound.
            Assert.InRange(vertex.X, -1.1, 1.1);
            Assert.InRange(vertex.Z, -1.1, 1.1);
            Assert.InRange(vertex.Y, -0.01, 0.5);
        });
    }

    [Fact]
    public async Task GrassBladesSwayWithWindAsElapsedTimeAdvances()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeMeshAssetStore();
        await store.SaveAsync(root, FlatGround(), CancellationToken.None);
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Ground", ["terrain"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D"))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.MeshAssetReference",
                    new JsonObject { ["assetId"] = "ground" }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.MeshRenderer"))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.GrassRenderer",
                    new JsonObject { ["bladeCount"] = 40, ["windStrength"] = 0.5, ["seed"] = 7 })));

        var worldAtZero = new RekallAgeRuntimeWorldBuilder().Build(scene, root);
        var frameAtZero = new RekallAgeRuntimeRenderFrameBuilder().Build(worldAtZero, 320, 180, false);
        var worldLater = worldAtZero with { ElapsedTime = TimeSpan.FromSeconds(1.7) };
        var frameLater = new RekallAgeRuntimeRenderFrameBuilder().Build(worldLater, 320, 180, false);

        var geometryAtZero = Assert.IsType<RekallAgeRuntimeViewportGeometryMesh>(
            Assert.Single(frameAtZero.Renderables, renderable => renderable.AssetId == "rekall.geometry.grass").GeometryMesh);
        var geometryLater = Assert.IsType<RekallAgeRuntimeViewportGeometryMesh>(
            Assert.Single(frameLater.Renderables, renderable => renderable.AssetId == "rekall.geometry.grass").GeometryMesh);

        Assert.NotEqual(geometryAtZero.Vertices[2].X, geometryLater.Vertices[2].X);
    }

    [Fact]
    public async Task GrassIsAbsentWhenTheOnlyEligibleSurfaceIsTooSteep()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeMeshAssetStore();
        await store.SaveAsync(root, VerticalWall(), CancellationToken.None);
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Wall", ["prop"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D"))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.MeshAssetReference",
                    new JsonObject { ["assetId"] = "wall" }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.MeshRenderer"))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.GrassRenderer",
                    new JsonObject { ["bladeCount"] = 100, ["maxSlopeDegrees"] = 35 })));

        var world = new RekallAgeRuntimeWorldBuilder().Build(scene, root);
        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 320, 180, false);

        Assert.DoesNotContain(frame.Renderables, renderable => renderable.AssetId == "rekall.geometry.grass");
    }

    private static RekallAgeMeshAsset FlatGround() => RekallAgeMeshAsset.Create(
        "ground",
        "Ground",
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

    private static RekallAgeMeshAsset VerticalWall() => RekallAgeMeshAsset.Create(
        "wall",
        "Wall",
        new(
            PointIds: [1, 2, 3, 4],
            Positions: [new(-1, 0, 0), new(-1, 2, 0), new(1, 2, 0), new(1, 0, 0)],
            EdgeIds: [11, 12, 13, 14],
            EdgePointIndices: [new(0, 1), new(1, 2), new(2, 3), new(3, 0)],
            FaceIds: [21],
            FaceOffsets: [0, 4],
            CornerIds: [31, 32, 33, 34],
            CornerPointIndices: [0, 1, 2, 3],
            CornerEdgeIndices: [0, 1, 2, 3]));
}
