using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.LevelDesign.Commands;
using Rekall.Age.Rendering;
using Rekall.Age.Runtime;
using Rekall.Age.World;

namespace Rekall.Age.Tests.LevelDesign;

public sealed class GeometryRecipeCommandTests
{
    [Fact]
    public async Task CreateGeometryRecipeCommandCreatesMultiPartRenderableMesh()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeSceneStore();
        await store.SaveAsync(root, RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"]), CancellationToken.None);
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("create recipe mesh"), CancellationToken.None);

        var result = await new CreateGeometryRecipeCommand().ExecuteAsync(
            new CreateGeometryRecipeRequest(
                root,
                "Main",
                "A-Pose Blockout",
                [
                    new CreateGeometryRecipePart("ellipsoid", Y: 1.2, ScaleX: 0.9, ScaleY: 1.5, ScaleZ: 0.45, Color: "#7aa2ff"),
                    new CreateGeometryRecipePart("ellipsoid", Y: 2.25, ScaleX: 0.5, ScaleY: 0.55, ScaleZ: 0.5, Color: "#f0c0a0"),
                    new CreateGeometryRecipePart("capsule", X: -0.78, Y: 1.35, Roll: 72, ScaleX: 0.18, ScaleY: 1.15, ScaleZ: 0.18, Color: "#7aa2ff"),
                    new CreateGeometryRecipePart("capsule", X: 0.78, Y: 1.35, Roll: -72, ScaleX: 0.18, ScaleY: 1.15, ScaleZ: 0.18, Color: "#7aa2ff")
                ],
                X: 1,
                Y: 2,
                Z: 3,
                Color: "#88aaff"),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.Equal(4, result.Value.PartCount);
        Assert.True(result.Value.VertexCount > 300);
        Assert.True(result.Value.IndexCount > 600);

        var scene = await store.LoadAsync(root, "Main", CancellationToken.None);
        var entity = Assert.Single(scene.Entities, item => item.Id == result.Value.EntityId);
        Assert.Contains("recipe", entity.Tags);
        var recipe = Assert.Single(entity.Components, component => component.Type == "Rekall.GeometryRecipe");
        Assert.Equal(4, recipe.Properties["parts"]!.AsArray().Count);

        var geometry = Assert.Single(entity.Components, component => component.Type == "Rekall.GeometryMesh");
        Assert.Equal(result.Value.VertexCount, geometry.Properties["vertices"]!.AsArray().Count);
        Assert.Equal(result.Value.IndexCount, geometry.Properties["indices"]!.AsArray().Count);
        Assert.Contains(
            geometry.Properties["vertices"]!.AsArray(),
            vertex => vertex!["r"] is not null && vertex["g"] is not null && vertex["b"] is not null);

        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);
        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 320, 180, debugOverlay: false);
        var renderable = Assert.Single(frame.Renderables);
        Assert.Equal("rekall.geometry.mesh", renderable.Variant);
        Assert.NotNull(renderable.GeometryMesh);
        Assert.Equal(result.Value.VertexCount, renderable.GeometryMesh.Vertices.Count);
        Assert.Contains(store.GetScenePath(root, "Main"), context.Transaction.ChangedResources);
    }

    [Fact]
    public async Task CreateGeometryRecipeCommandRejectsEmptyRecipe()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"]),
            CancellationToken.None);

        var result = await new CreateGeometryRecipeCommand().ExecuteAsync(
            new CreateGeometryRecipeRequest(root, "Main", "Empty", []),
            new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("bad recipe"), CancellationToken.None));

        Assert.False(result.Ok);
        Assert.Equal(string.Empty, result.Value.EntityId);
        Assert.Contains(result.Errors, error => error.Code == "REKALL_GEOMETRY_RECIPE_INVALID");
    }
}
