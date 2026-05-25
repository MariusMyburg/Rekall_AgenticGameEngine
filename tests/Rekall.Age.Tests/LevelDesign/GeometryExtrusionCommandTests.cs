using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.LevelDesign.Commands;
using Rekall.Age.Rendering;
using Rekall.Age.Runtime;
using Rekall.Age.World;

namespace Rekall.Age.Tests.LevelDesign;

public sealed class GeometryExtrusionCommandTests
{
    [Fact]
    public void GeometryExtrusionMeshBuilderCreatesHardEdgedClosedMesh()
    {
        var mesh = RekallAgeGeometryExtrusionMeshBuilder.Build(
            [
                new CreateGeometryExtrusionPoint(-0.5, -0.5),
                new CreateGeometryExtrusionPoint(0.5, -0.5),
                new CreateGeometryExtrusionPoint(0.5, 0.5),
                new CreateGeometryExtrusionPoint(-0.5, 0.5)
            ],
            depth: 1);

        Assert.Equal(24, mesh.Vertices.Count);
        Assert.Equal(36, mesh.Indices.Count);
        Assert.Contains(mesh.Vertices, vertex => vertex.NormalZ == 1);
        Assert.Contains(mesh.Vertices, vertex => vertex.NormalZ == -1);
        Assert.Contains(mesh.Vertices, vertex => Math.Abs(vertex.NormalX ?? 0) == 1 || Math.Abs(vertex.NormalY ?? 0) == 1);
    }

    [Fact]
    public async Task CreateGeometryExtrusionCommandCreatesRenderableMesh()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeSceneStore();
        await store.SaveAsync(root, RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"]), CancellationToken.None);
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("create extrusion"), CancellationToken.None);
        var command = new CreateGeometryExtrusionCommand();

        var result = await command.ExecuteAsync(
            new CreateGeometryExtrusionRequest(
                root,
                "Main",
                "Agent Wedge",
                [
                    new CreateGeometryExtrusionPoint(-0.5, -0.5),
                    new CreateGeometryExtrusionPoint(0.6, -0.4),
                    new CreateGeometryExtrusionPoint(0.2, 0.7)
                ],
                Depth: 1.25,
                X: 1,
                Y: 2,
                Z: 3,
                Color: "#44ccff"),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.Equal(18, result.Value.VertexCount);
        Assert.Equal(24, result.Value.IndexCount);
        var scene = await store.LoadAsync(root, "Main", CancellationToken.None);
        var entity = Assert.Single(scene.Entities, item => item.Id == result.Value.EntityId);
        Assert.Equal(["extrusion", "geometry", "mesh"], entity.Tags);
        Assert.Contains(entity.Components, component =>
            component.Type == "Rekall.GeometryExtrusion"
            && component.Properties["depth"]!.GetValue<double>() == 1.25);
        Assert.Contains(entity.Components, component =>
            component.Type == "Rekall.GeometryMesh"
            && component.Properties["vertices"]!.AsArray().Count == 18
            && component.Properties["indices"]!.AsArray().Count == 24);

        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);
        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 160, 90, debugOverlay: false);
        var renderable = Assert.Single(frame.Renderables);
        Assert.Equal("rekall.geometry.mesh", renderable.Variant);
        Assert.NotNull(renderable.GeometryMesh);
        Assert.Contains(store.GetScenePath(root, "Main"), context.Transaction.ChangedResources);
    }

    [Fact]
    public async Task CreateGeometryExtrusionCommandRejectsDegenerateProfiles()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeSceneStore().SaveAsync(root, RekallAgeSceneDocument.Create("Main", ["world"]), CancellationToken.None);

        var result = await new CreateGeometryExtrusionCommand().ExecuteAsync(
            new CreateGeometryExtrusionRequest(
                root,
                "Main",
                "Line",
                [
                    new CreateGeometryExtrusionPoint(0, 0),
                    new CreateGeometryExtrusionPoint(1, 0),
                    new CreateGeometryExtrusionPoint(2, 0)
                ],
                Depth: 1),
            new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("bad extrusion"), CancellationToken.None));

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Code == "REKALL_GEOMETRY_EXTRUSION_INVALID");
    }
}
