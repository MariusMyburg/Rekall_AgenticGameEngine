using System.Text.Json.Nodes;
using Rekall.Age.Modules;
using Rekall.Age.Modules.BuiltIns;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Rendering;

public sealed class ShapeRenderer2DTests
{
    [Fact]
    public void ShapeRenderer2DIsRegisteredWithInspectableDefaults()
    {
        var modules = RekallAgeModuleIndexer.IndexAssembly(typeof(RekallAgeBuiltInModule).Assembly);
        var builtIns = Assert.Single(modules.Modules, module => module.Id == "rekall.builtins");
        var schema = Assert.Single(
            builtIns.Components,
            component => component.TypeName == "Rekall.ShapeRenderer2D");

        Assert.Equal("Shape Renderer 2D", schema.DisplayName);
        Assert.Contains("XY plane", schema.Description, StringComparison.Ordinal);
        Assert.Contains(schema.Properties, property =>
            property.Name == "Shape"
            && property.Kind == "string"
            && property.AllowedValues.SequenceEqual(["rectangle", "circle"]));
        Assert.Contains(schema.Properties, property => property.Name == "Width" && property.Minimum == 0.0001);
        Assert.Contains(schema.Properties, property => property.Name == "Height" && property.Minimum == 0.0001);
        Assert.Contains(schema.Properties, property => property.Name == "Radius" && property.Minimum == 0.0001);
        Assert.Contains(schema.Properties, property => property.Name == "Color" && property.Kind == "color");
        Assert.Contains(schema.Properties, property => property.Name == "Active" && property.Kind == "boolean");
        Assert.True(RekallAgeBuiltInComponentTypeCatalog.IsKnown("Rekall.ShapeRenderer2D"));

        var defaults = new RekallAgeShapeRenderer2DComponent();
        Assert.Equal("rectangle", defaults.Shape);
        Assert.Equal(1, defaults.Width);
        Assert.Equal(1, defaults.Height);
        Assert.Equal(0.5, defaults.Radius);
        Assert.Equal("#ffffff", defaults.Color);
        Assert.True(defaults.Active);
    }

    [Fact]
    public void ActiveVisibleShapeProjectsAsAnAssetFreeLayeredMesh()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering2d"])
            .AddEntity(RekallAgeEntityDocument.Create("Shape", ["foreground"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform2D"))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.RenderLayer",
                    new JsonObject { ["layer"] = "foreground" }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.ShapeRenderer2D",
                    new JsonObject
                    {
                        ["shape"] = "circle",
                        ["color"] = "#f97316",
                        ["active"] = true
                    })));

        var projected = new RekallAgeRuntimeProjectionBuilder()
            .Project(new RekallAgeRuntimeWorldBuilder().Build(scene));
        var mesh = Assert.Single(projected.Subsystems.Rendering.Meshes);

        Assert.Null(mesh.AssetId);
        Assert.Equal("rekall.shape2d", mesh.Variant);
        Assert.Equal("mesh", mesh.Kind);
        Assert.Equal(100, mesh.SortKey);
        Assert.Equal("#f97316", mesh.MaterialColor);
        Assert.Equal("foreground", mesh.Layer);
        Assert.Equal(RekallAgeRuntimeProjectionSources.BuiltIn, mesh.ProjectionSource);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void InactiveOrInvisibleShapeDoesNotProject(bool active, bool visible)
    {
        var entity = RekallAgeEntityDocument.Create("Shape", [])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform2D"))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.ShapeRenderer2D",
                new JsonObject { ["active"] = active })) with
        {
            Visible = visible
        };
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering2d"])
            .AddEntity(entity);

        var projected = new RekallAgeRuntimeProjectionBuilder()
            .Project(new RekallAgeRuntimeWorldBuilder().Build(scene));

        Assert.Empty(projected.Subsystems.Rendering.Meshes);
    }

    [Fact]
    public void ReprojectionPreservesAnAuthoredMeshAlongsideBuiltInShapeProjection()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering2d"])
            .AddEntity(RekallAgeEntityDocument.Create("Shape", [])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform2D"))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.ShapeRenderer2D")));
        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);
        var authored = new RekallAgeRuntimeRenderMesh(
            "module-mesh",
            "Module Mesh",
            "rekall.geometry.cube");
        world = world with
        {
            Subsystems = world.Subsystems with
            {
                Rendering = world.Subsystems.Rendering with { Meshes = [authored] }
            }
        };

        var projected = new RekallAgeRuntimeProjectionBuilder().Project(world);

        Assert.Contains(projected.Subsystems.Rendering.Meshes, mesh =>
            mesh.EntityId == "module-mesh"
            && mesh.ProjectionSource == RekallAgeRuntimeProjectionSources.Authored);
        Assert.Contains(projected.Subsystems.Rendering.Meshes, mesh =>
            mesh.EntityName == "Shape"
            && mesh.ProjectionSource == RekallAgeRuntimeProjectionSources.BuiltIn);
    }
}
