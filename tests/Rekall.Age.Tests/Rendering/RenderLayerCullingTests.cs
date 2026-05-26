using System.Text.Json.Nodes;
using Rekall.Age.Modules;
using Rekall.Age.Modules.BuiltIns;
using Rekall.Age.Rendering;
using Rekall.Age.Runtime;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Rendering;

public sealed class RenderLayerCullingTests
{
    [Fact]
    public void RuntimeRenderFrameFiltersRenderablesByActiveCameraCullingMask()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject
                {
                    ["active"] = true,
                    ["cullingMask"] = "world"
                })))
            .AddEntity(RekallAgeEntityDocument.Create("Visible Cube", ["prop"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "cube" }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.RenderLayer", new JsonObject { ["layer"] = "world" })))
            .AddEntity(RekallAgeEntityDocument.Create("Hidden Cube", ["prop"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "cube" }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.RenderLayer", new JsonObject { ["layer"] = "helpers" })));
        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);

        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 800, 600, debugOverlay: false);

        Assert.Equal("world", frame.ActiveCamera?.CullingMask);
        Assert.Contains(frame.Renderables, renderable => renderable.EntityName == "Visible Cube" && renderable.Layer == "world");
        Assert.DoesNotContain(frame.Renderables, renderable => renderable.EntityName == "Hidden Cube");
        Assert.Contains(frame.Culling.CulledRenderables, renderable =>
            renderable.EntityName == "Hidden Cube"
            && renderable.Layer == "helpers"
            && renderable.Reason == "camera-culling-mask");
    }

    [Fact]
    public void RuntimeRenderFrameSupportsCameraCullingMaskExclusions()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject
                {
                    ["active"] = true,
                    ["cullingMask"] = "*, !helpers"
                })))
            .AddEntity(RekallAgeEntityDocument.Create("World Cube", ["prop"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "cube" }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.RenderLayer", new JsonObject { ["layer"] = "world" })))
            .AddEntity(RekallAgeEntityDocument.Create("Helper Cube", ["prop"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "cube" }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.RenderLayer", new JsonObject { ["layer"] = "helpers" })));
        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);

        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 800, 600, debugOverlay: false);

        Assert.Contains(frame.Renderables, renderable => renderable.EntityName == "World Cube");
        Assert.DoesNotContain(frame.Renderables, renderable => renderable.EntityName == "Helper Cube");
        Assert.Contains(frame.Culling.CulledRenderables, renderable =>
            renderable.EntityName == "Helper Cube"
            && renderable.CullingMask == "*, !helpers");
    }

    [Fact]
    public void BuiltInModuleExposesRenderLayerAndCameraCullingMaskSchemas()
    {
        var index = RekallAgeModuleIndexer.IndexAssembly(typeof(RekallAgeBuiltInModule).Assembly);
        var module = Assert.Single(index.Modules, item => item.Id == "rekall.builtins");

        var layer = Assert.Single(module.Components, component => component.DisplayName == "Render Layer");
        var camera = Assert.Single(module.Components, component => component.DisplayName == "Camera 3D");

        Assert.Contains(layer.Properties, property => property.Name == "Layer" && property.Kind == "string");
        Assert.Contains(camera.Properties, property => property.Name == "CullingMask" && property.Kind == "string");
    }
}
