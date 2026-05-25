using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.LevelDesign.Commands;
using Rekall.Age.Rendering;
using Rekall.Age.Runtime;
using Rekall.Age.World;

namespace Rekall.Age.Tests.LevelDesign;

public sealed class GeometryPrimitiveCommandTests
{
    [Fact]
    public async Task CreateGeometryPrimitiveCommandCreatesRenderable3DPrimitive()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeSceneStore();
        await store.SaveAsync(root, RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"]), CancellationToken.None);
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("create geometry"), CancellationToken.None);
        var command = new CreateGeometryPrimitiveCommand();

        var result = await command.ExecuteAsync(
            new CreateGeometryPrimitiveRequest(
                root,
                "Main",
                "Crystal Spire",
                "sphere",
                X: 1,
                Y: 2,
                Z: 3,
                Pitch: 10,
                Yaw: 20,
                Roll: 30,
                ScaleX: 1.5,
                ScaleY: 2,
                ScaleZ: 1.25,
                Color: "#4fd1c5"),
            context);

        Assert.True(result.Ok, result.Summary);
        var scene = await store.LoadAsync(root, "Main", CancellationToken.None);
        var entity = Assert.Single(scene.Entities, item => item.Id == result.Value.EntityId);
        Assert.Equal(["geometry", "primitive", "sphere"], entity.Tags);
        Assert.Contains(entity.Components, component =>
            component.Type == "Rekall.Transform3D"
            && component.Properties["x"]!.GetValue<double>() == 1
            && component.Properties["yaw"]!.GetValue<double>() == 20
            && component.Properties["scaleY"]!.GetValue<double>() == 2);
        Assert.Contains(entity.Components, component =>
            component.Type == "Rekall.GeometryPrimitive"
            && component.Properties["primitive"]!.GetValue<string>() == "sphere"
            && component.Properties["color"]!.GetValue<string>() == "#4fd1c5");
        Assert.Contains(entity.Components, component =>
            component.Type == "Rekall.MeshRenderer"
            && component.Properties["mesh"]!.GetValue<string>() == "rekall.geometry.sphere");

        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);
        var mesh = Assert.Single(world.Subsystems.Rendering.Meshes);
        Assert.Equal("rekall.geometry.sphere", mesh.AssetId);
        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 160, 90, debugOverlay: false);
        var renderable = Assert.Single(frame.Renderables);
        Assert.Equal("rekall.geometry.sphere", renderable.Variant);
        Assert.Equal("#4fd1c5", renderable.MaterialColor);
        Assert.Contains(store.GetScenePath(root, "Main"), context.Transaction.ChangedResources);
    }

    [Fact]
    public async Task CreateGeometryPrimitiveCommandRejectsUnsupportedPrimitive()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"]),
            CancellationToken.None);
        var command = new CreateGeometryPrimitiveCommand();

        var result = await command.ExecuteAsync(
            new CreateGeometryPrimitiveRequest(root, "Main", "Portal", "torus"),
            new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("bad geometry"), CancellationToken.None));

        Assert.False(result.Ok);
        Assert.Equal(string.Empty, result.Value.EntityId);
        Assert.Contains(result.Errors, error => error.Code == "REKALL_GEOMETRY_PRIMITIVE_UNSUPPORTED");
    }
}
