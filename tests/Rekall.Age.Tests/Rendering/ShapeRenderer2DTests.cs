using System.Numerics;
using System.Text.Json.Nodes;
using Rekall.Age.Modules;
using Rekall.Age.Modules.BuiltIns;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
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
        Assert.Contains(schema.Properties, property => property.Name == "SortOrder" && property.Kind == "integer");
        Assert.Contains(schema.Properties, property => property.Name == "Active" && property.Kind == "boolean");
        Assert.True(RekallAgeBuiltInComponentTypeCatalog.IsKnown("Rekall.ShapeRenderer2D"));

        var defaults = new RekallAgeShapeRenderer2DComponent();
        Assert.Equal("rectangle", defaults.Shape);
        Assert.Equal(1, defaults.Width);
        Assert.Equal(1, defaults.Height);
        Assert.Equal(0.5, defaults.Radius);
        Assert.Equal("#ffffff", defaults.Color);
        Assert.Equal(0, defaults.SortOrder);
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
                        ["sortOrder"] = 7,
                        ["active"] = true
                    })));

        var projected = new RekallAgeRuntimeProjectionBuilder()
            .Project(new RekallAgeRuntimeWorldBuilder().Build(scene));
        var mesh = Assert.Single(projected.Subsystems.Rendering.Meshes);

        Assert.Null(mesh.AssetId);
        Assert.Equal("rekall.shape2d", mesh.Variant);
        Assert.Equal("mesh", mesh.Kind);
        Assert.Equal(107, mesh.SortKey);
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

    [Fact]
    public void RectangleShapeBuildsExpectedXyGeometryAndPreservesTransform()
    {
        var frame = BuildShapeFrame(new JsonObject
        {
            ["shape"] = "rectangle",
            ["width"] = 4,
            ["height"] = 2,
            ["color"] = "#22c55e"
        });

        var renderable = Assert.Single(frame.Renderables, item => item.EntityName == "Shape");
        var geometry = Assert.IsType<RekallAgeRuntimeViewportGeometryMesh>(renderable.GeometryMesh);

        Assert.Equal(4, geometry.Vertices.Count);
        Assert.Equal(6, geometry.Indices.Count);
        Assert.Equal(-2, geometry.Vertices.Min(vertex => vertex.X));
        Assert.Equal(2, geometry.Vertices.Max(vertex => vertex.X));
        Assert.Equal(-1, geometry.Vertices.Min(vertex => vertex.Y));
        Assert.Equal(1, geometry.Vertices.Max(vertex => vertex.Y));
        Assert.All(geometry.Vertices, vertex => Assert.Equal(0, vertex.Z));
        Assert.Equal((3d, 5d, 15d), (renderable.X, renderable.Y, renderable.RotationZ));
        Assert.Equal((2d, 0.5d), (renderable.ScaleX, renderable.ScaleY));
        Assert.Equal("#22c55e", renderable.MaterialColor);
    }

    [Fact]
    public void ShapeSortOrderProvidesStablePainterDepth()
    {
        var frame = BuildShapeFrame(new JsonObject
        {
            ["shape"] = "rectangle",
            ["sortOrder"] = 25
        });

        var renderable = Assert.Single(frame.Renderables, item => item.EntityName == "Shape");
        Assert.Equal(125, renderable.SortKey);
        Assert.Equal(-0.0025, renderable.Z, 8);
    }

    [Fact]
    public void CircleShapeBuildsClosedFiniteTriangleFanAndRendersWithoutFallback()
    {
        var frame = BuildShapeFrame(new JsonObject
        {
            ["shape"] = "circle",
            ["radius"] = 1.5,
            ["color"] = "#38bdf8"
        });
        var renderable = Assert.Single(frame.Renderables, item => item.EntityName == "Shape");
        var geometry = Assert.IsType<RekallAgeRuntimeViewportGeometryMesh>(renderable.GeometryMesh);

        Assert.Equal(50, geometry.Vertices.Count);
        Assert.Equal(144, geometry.Indices.Count);
        Assert.All(geometry.Vertices, vertex =>
        {
            Assert.True(double.IsFinite(vertex.X));
            Assert.True(double.IsFinite(vertex.Y));
            Assert.True(double.IsFinite(vertex.Z));
        });
        Assert.InRange(geometry.Vertices.Min(vertex => vertex.X), -1.501, -1.499);
        Assert.InRange(geometry.Vertices.Max(vertex => vertex.X), 1.499, 1.501);
        Assert.InRange(geometry.Vertices.Min(vertex => vertex.Y), -1.501, -1.499);
        Assert.InRange(geometry.Vertices.Max(vertex => vertex.Y), 1.499, 1.501);
        Assert.Equal(geometry.Vertices[1].X, geometry.Vertices[^1].X, 8);
        Assert.Equal(geometry.Vertices[1].Y, geometry.Vertices[^1].Y, 8);

        var meshes = new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame);
        Assert.Single(meshes);
        var rendered = new RekallAgeRuntimeSoftwareRenderer().RenderRgba(
            frame,
            RekallAgeRuntimeViewportAssetSet.Empty);
        Assert.Equal(0, rendered.FallbackRenderableCount);
        Assert.Equal(0, rendered.MissingAssetCount);
        Assert.True(rendered.NonBlank);
    }

    [Fact]
    public void Camera2DUsesTransform2DPositionToFrameWorldShapes()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering2d"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", [])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform2D",
                    new JsonObject { ["x"] = 40, ["y"] = 12 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Camera2D",
                    new JsonObject { ["active"] = true, ["orthographicSize"] = 10 })))
            .AddEntity(RekallAgeEntityDocument.Create("Shape", [])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform2D",
                    new JsonObject { ["x"] = 41, ["y"] = 12 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.ShapeRenderer2D",
                    new JsonObject
                    {
                        ["shape"] = "rectangle",
                        ["width"] = 4,
                        ["height"] = 2,
                        ["color"] = "#f97316"
                    })));
        var world = new RekallAgeRuntimeProjectionBuilder()
            .Project(new RekallAgeRuntimeWorldBuilder().Build(scene));

        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 320, 180, false);

        var camera = Assert.IsType<RekallAgeRuntimeViewportCamera>(frame.ActiveCamera);
        Assert.Equal((40d, 12d), (camera.X, camera.Y));
        var rendered = new RekallAgeRuntimeSoftwareRenderer().RenderRgba(
            frame,
            RekallAgeRuntimeViewportAssetSet.Empty);
        Assert.Equal(0, rendered.FallbackRenderableCount);
        Assert.Equal(0, rendered.MissingAssetCount);
        Assert.True(rendered.NonBlank);

        var meshes = new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame);
        var cameraPlan = new RekallAgeVulkanSceneBatchBuilder().Build(frame, meshes).EffectiveCamera;
        var clip = Vector4.Transform(
            new Vector4(41, 12, 0, 1),
            cameraPlan.SoftwareViewProjection);
        Assert.True(clip.X / clip.W > 0, "World +X must appear on the right side of a Camera2D viewport.");
    }

    [Theory]
    [InlineData("triangle")]
    [InlineData("")]
    public void UnknownShapeNormalizesToClampedRectangle(string shape)
    {
        var frame = BuildShapeFrame(new JsonObject
        {
            ["shape"] = shape,
            ["width"] = 0,
            ["height"] = -2
        });
        var geometry = Assert.IsType<RekallAgeRuntimeViewportGeometryMesh>(
            Assert.Single(frame.Renderables, item => item.EntityName == "Shape").GeometryMesh);

        Assert.Equal(4, geometry.Vertices.Count);
        Assert.Equal(0.0001, geometry.Vertices.Max(vertex => vertex.X) - geometry.Vertices.Min(vertex => vertex.X), 8);
        Assert.Equal(0.0001, geometry.Vertices.Max(vertex => vertex.Y) - geometry.Vertices.Min(vertex => vertex.Y), 8);
    }

    private static RekallAgeRuntimeViewportFrame BuildShapeFrame(JsonObject shapeProperties)
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering2d"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", [])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["z"] = -20 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Camera2D",
                    new JsonObject { ["active"] = true, ["orthographicSize"] = 20 })))
            .AddEntity(RekallAgeEntityDocument.Create("Shape", [])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform2D",
                    new JsonObject
                    {
                        ["x"] = 3,
                        ["y"] = 5,
                        ["rotation"] = 15,
                        ["scaleX"] = 2,
                        ["scaleY"] = 0.5
                    }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.ShapeRenderer2D",
                    shapeProperties)));
        var world = new RekallAgeRuntimeProjectionBuilder()
            .Project(new RekallAgeRuntimeWorldBuilder().Build(scene));

        return new RekallAgeRuntimeRenderFrameBuilder().Build(world, 320, 180, false);
    }
}
