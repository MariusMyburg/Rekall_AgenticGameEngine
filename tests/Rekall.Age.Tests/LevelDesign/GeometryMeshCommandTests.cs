using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.LevelDesign.Commands;
using Rekall.Age.Rendering;
using Rekall.Age.Runtime;
using Rekall.Age.World;

namespace Rekall.Age.Tests.LevelDesign;

public sealed class GeometryMeshCommandTests
{
    [Fact]
    public async Task CreateGeometryMeshCommandCreatesRenderableAuthoredMesh()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeSceneStore();
        await store.SaveAsync(root, RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"]), CancellationToken.None);
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("create mesh"), CancellationToken.None);
        var command = new CreateGeometryMeshCommand();

        var result = await command.ExecuteAsync(
            new CreateGeometryMeshRequest(
                root,
                "Main",
                "Agent Triangle",
                [
                    new CreateGeometryMeshVertex(0, 0, 0, NormalX: 0, NormalY: 0, NormalZ: 1),
                    new CreateGeometryMeshVertex(1, 0, 0),
                    new CreateGeometryMeshVertex(0, 1, 0, R: 0, G: 1, B: 0, A: 0.8)
                ],
                [0, 1, 2],
                X: 1,
                Y: 2,
                Z: 3,
                Color: "#ff6633"),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.Equal(3, result.Value.VertexCount);
        Assert.Equal(3, result.Value.IndexCount);
        var scene = await store.LoadAsync(root, "Main", CancellationToken.None);
        var entity = Assert.Single(scene.Entities, item => item.Id == result.Value.EntityId);
        Assert.Equal(["geometry", "mesh"], entity.Tags);
        Assert.Contains(entity.Components, component => component.Type == "Rekall.Transform3D");
        Assert.Contains(entity.Components, component =>
            component.Type == "Rekall.GeometryMesh"
            && component.Properties["color"]!.GetValue<string>() == "#ff6633"
            && component.Properties["vertices"]!.AsArray().Count == 3
            && component.Properties["indices"]!.AsArray().Count == 3);
        var geometry = entity.Components.Single(component => component.Type == "Rekall.GeometryMesh");
        var vertices = geometry.Properties["vertices"]!.AsArray();
        Assert.Equal(1, vertices[0]!["nz"]!.GetValue<double>());
        Assert.Equal(1, vertices[1]!["nz"]!.GetValue<double>());
        Assert.Equal(1, vertices[2]!["nz"]!.GetValue<double>());
        Assert.Contains(entity.Components, component =>
            component.Type == "Rekall.MeshRenderer"
            && component.Properties["mesh"]!.GetValue<string>() == "rekall.geometry.mesh");

        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);
        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 160, 90, debugOverlay: false);
        var renderable = Assert.Single(frame.Renderables);
        Assert.Equal("rekall.geometry.mesh", renderable.Variant);
        Assert.Equal("#ff6633", renderable.MaterialColor);
        Assert.NotNull(renderable.GeometryMesh);
        Assert.Equal(3, renderable.GeometryMesh.Vertices.Count);
        Assert.Contains(store.GetScenePath(root, "Main"), context.Transaction.ChangedResources);
    }

    [Fact]
    public async Task CreateGeometryMeshCommandRejectsInvalidTriangleList()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"]),
            CancellationToken.None);
        var command = new CreateGeometryMeshCommand();

        var result = await command.ExecuteAsync(
            new CreateGeometryMeshRequest(
                root,
                "Main",
                "Broken",
                [new CreateGeometryMeshVertex(0, 0, 0), new CreateGeometryMeshVertex(1, 0, 0)],
                [0, 1, 2]),
            new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("bad mesh"), CancellationToken.None));

        Assert.False(result.Ok);
        Assert.Equal(string.Empty, result.Value.EntityId);
        Assert.Contains(result.Errors, error => error.Code == "REKALL_GEOMETRY_MESH_INVALID");
    }
}
