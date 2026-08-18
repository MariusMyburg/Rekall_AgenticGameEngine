using System.Text.Json.Nodes;
using Rekall.Age.Assets;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Project;
using Rekall.Age.Rendering.Commands;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Rendering;

public sealed class InspectSceneMeshGeometryCommandTests
{
    [Fact]
    public async Task CommandReportsPostMorphBoundsWithoutVertexPayloads()
    {
        var root = TestPaths.CreateTempDirectory();
        var model = Path.Combine(root, "morph.glb");
        await File.WriteAllBytesAsync(model, GlbTestMeshFactory.CreateMorphTriangleGlb());
        await new RekallAgeProjectStore().SaveAsync(
            root, RekallAgeProjectManifest.Create("Morph", ["world", "animation", "rendering3d"]), CancellationToken.None);
        await new RekallAgeAssetCatalogStore().SaveAsync(
            root,
            new RekallAgeAssetCatalogDocument([new("morph-asset", "morph", "Morph", "model", model, model, "hash")]),
            CancellationToken.None);
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "animation", "rendering3d"])
                .AddEntity(RekallAgeEntityDocument.Create("Actor", ["actor"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject()))
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.MeshRenderer", new JsonObject { ["mesh"] = "morph-asset" }))
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.MorphWeights", new JsonObject { ["weights"] = new JsonArray(0.5, -0.25) }))),
            CancellationToken.None);
        var context = new RekallAgeCommandContext("test", RekallAgeTransaction.Begin("inspect"), CancellationToken.None);

        var result = await new InspectSceneMeshGeometryCommand().ExecuteAsync(
            new InspectSceneMeshGeometryRequest(root, "Main", 1), context);

        Assert.True(result.Ok, result.Summary);
        var mesh = Assert.Single(result.Value.Meshes);
        Assert.Equal(3, mesh.VertexCount);
        Assert.Equal(2, mesh.MorphTargetCount);
        Assert.Equal("authored", mesh.MorphWeightSource);
        Assert.Equal(8.5, mesh.Minimum.X, precision: 4);
        Assert.Equal(21, mesh.Minimum.Y, precision: 4);
        Assert.Equal(30, mesh.Minimum.Z, precision: 4);
        Assert.Equal(10.5, mesh.Maximum.X, precision: 4);
        Assert.Equal(23, mesh.Maximum.Y, precision: 4);
        Assert.Empty(result.Value.AssetIssues);
    }

    [Fact]
    public async Task CommandRejectsInvalidBoundsRequest()
    {
        var result = await new InspectSceneMeshGeometryCommand().ExecuteAsync(
            new InspectSceneMeshGeometryRequest(".", "Main", -1, 0, 180),
            new RekallAgeCommandContext("test", RekallAgeTransaction.Begin("invalid"), CancellationToken.None));

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Code == "REKALL_RENDER_MESH_INSPECTION_INVALID");
    }
}
