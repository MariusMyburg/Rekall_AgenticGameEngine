using System.Text.Json.Nodes;
using Rekall.Age.Modules;
using Rekall.Age.Modules.BuiltIns;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
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
        Assert.Contains(camera.Properties, property => property.Name == "RenderOrder" && property.Kind == "number");
        Assert.Contains(camera.Properties, property => property.Name == "ViewportWidth" && property.Kind == "number");
    }

    [Fact]
    public void RuntimeRenderFrameOrdersCamerasAndPreservesViewportRectangles()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Ui Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject
                {
                    ["active"] = true,
                    ["renderOrder"] = 100,
                    ["viewportX"] = 0.75,
                    ["viewportY"] = 0,
                    ["viewportWidth"] = 0.25,
                    ["viewportHeight"] = 0.25,
                    ["cullingMask"] = "ui"
                })))
            .AddEntity(RekallAgeEntityDocument.Create("World Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject
                {
                    ["active"] = true,
                    ["renderOrder"] = -10,
                    ["viewportX"] = 0,
                    ["viewportY"] = 0,
                    ["viewportWidth"] = 1,
                    ["viewportHeight"] = 1,
                    ["cullingMask"] = "world"
                })));
        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);

        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 1280, 720, debugOverlay: false);

        Assert.Equal("World Camera", frame.ActiveCamera?.EntityName);
        Assert.Collection(
            frame.Cameras,
            camera =>
            {
                Assert.Equal("World Camera", camera.EntityName);
                Assert.Equal(-10, camera.RenderOrder);
                Assert.Equal(0, camera.ViewportX);
                Assert.Equal(0, camera.ViewportY);
                Assert.Equal(1, camera.ViewportWidth);
                Assert.Equal(1, camera.ViewportHeight);
            },
            camera =>
            {
                Assert.Equal("Ui Camera", camera.EntityName);
                Assert.Equal(100, camera.RenderOrder);
                Assert.Equal(0.75, camera.ViewportX);
                Assert.Equal(0, camera.ViewportY);
                Assert.Equal(0.25, camera.ViewportWidth);
                Assert.Equal(0.25, camera.ViewportHeight);
            });
    }

    [Fact]
    public void RuntimeRenderFrameBuildsPerCameraViewsWithVisibleRenderablesAndPixelRectangles()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("World Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject
                {
                    ["active"] = true,
                    ["renderOrder"] = 0,
                    ["viewportWidth"] = 0.5,
                    ["cullingMask"] = "world"
                })))
            .AddEntity(RekallAgeEntityDocument.Create("Ui Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject
                {
                    ["active"] = true,
                    ["renderOrder"] = 10,
                    ["viewportX"] = 0.5,
                    ["viewportWidth"] = 0.5,
                    ["cullingMask"] = "ui"
                })))
            .AddEntity(RekallAgeEntityDocument.Create("World Cube", ["prop"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "cube" }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.RenderLayer", new JsonObject { ["layer"] = "world" })))
            .AddEntity(RekallAgeEntityDocument.Create("Ui Panel", ["ui"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "plane" }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.RenderLayer", new JsonObject { ["layer"] = "ui" })));
        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);

        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 1000, 500, debugOverlay: false);

        Assert.Collection(
            frame.CameraViews,
            view =>
            {
                Assert.Equal("World Camera", view.Camera.EntityName);
                Assert.Equal(new RekallAgeRuntimeViewportCameraRect(0, 0, 500, 500), view.PixelRect);
                Assert.Contains(view.Renderables, renderable => renderable.EntityName == "World Cube");
                Assert.DoesNotContain(view.Renderables, renderable => renderable.EntityName == "Ui Panel");
                Assert.Contains(view.CulledRenderables, renderable => renderable.EntityName == "Ui Panel");
            },
            view =>
            {
                Assert.Equal("Ui Camera", view.Camera.EntityName);
                Assert.Equal(new RekallAgeRuntimeViewportCameraRect(500, 0, 500, 500), view.PixelRect);
                Assert.Contains(view.Renderables, renderable => renderable.EntityName == "Ui Panel");
                Assert.DoesNotContain(view.Renderables, renderable => renderable.EntityName == "World Cube");
                Assert.Contains(view.CulledRenderables, renderable => renderable.EntityName == "World Cube");
            });
    }
}
