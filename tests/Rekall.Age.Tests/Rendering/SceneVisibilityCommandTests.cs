using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Rendering.Commands;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Rendering;

public sealed class SceneVisibilityCommandTests
{
    [Fact]
    public async Task InspectSceneVisibilityReportsPerCameraLayerVisibility()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("World Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject
                {
                    ["active"] = true,
                    ["cullingMask"] = "world"
                })))
            .AddEntity(RekallAgeEntityDocument.Create("Ui Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject
                {
                    ["active"] = false,
                    ["cullingMask"] = "ui, !debug"
                })))
            .AddEntity(RekallAgeEntityDocument.Create("World Cube", ["prop"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.RenderLayer", new JsonObject { ["layer"] = "world" }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "cube" })))
            .AddEntity(RekallAgeEntityDocument.Create("Ui Panel", ["ui"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.RenderLayer", new JsonObject { ["layer"] = "ui" }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "plane" })))
            .AddEntity(RekallAgeEntityDocument.Create("Debug Gizmo", ["debug"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.RenderLayer", new JsonObject { ["layer"] = "debug" }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "sphere" })));
        await new RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None);
        var context = new RekallAgeCommandContext("test", RekallAgeTransaction.Begin("visibility"), CancellationToken.None);

        var result = await new InspectSceneVisibilityCommand().ExecuteAsync(
            new InspectSceneVisibilityRequest(root, "Main"),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.Equal(3, result.Value.TotalRenderableCount);
        var worldCamera = Assert.Single(result.Value.Cameras, camera => camera.EntityName == "World Camera");
        Assert.True(worldCamera.Active);
        Assert.Equal("world", worldCamera.CullingMask);
        Assert.Equal(0, worldCamera.RenderOrder);
        Assert.Equal(0, worldCamera.ViewportX);
        Assert.Equal(0, worldCamera.ViewportY);
        Assert.Equal(1, worldCamera.ViewportWidth);
        Assert.Equal(1, worldCamera.ViewportHeight);
        Assert.Contains(worldCamera.VisibleRenderables, renderable => renderable.EntityName == "World Cube");
        Assert.Contains(worldCamera.CulledRenderables, renderable => renderable.EntityName == "Ui Panel" && renderable.Layer == "ui");
        Assert.Contains(worldCamera.CulledRenderables, renderable => renderable.EntityName == "Debug Gizmo" && renderable.Layer == "debug");

        var uiCamera = Assert.Single(result.Value.Cameras, camera => camera.EntityName == "Ui Camera");
        Assert.False(uiCamera.Active);
        Assert.Equal("ui, !debug", uiCamera.CullingMask);
        Assert.Contains(uiCamera.VisibleRenderables, renderable => renderable.EntityName == "Ui Panel");
        Assert.Contains(uiCamera.CulledRenderables, renderable => renderable.EntityName == "Debug Gizmo");

        Assert.Contains(result.Value.UnseenByActiveCameraRenderables, renderable => renderable.EntityName == "Ui Panel");
        Assert.Contains(result.Value.UnseenByActiveCameraRenderables, renderable => renderable.EntityName == "Debug Gizmo");
        Assert.DoesNotContain(result.Value.UnseenByAnyCameraRenderables, renderable => renderable.EntityName == "Ui Panel");
        Assert.Contains(result.Value.UnseenByAnyCameraRenderables, renderable => renderable.EntityName == "Debug Gizmo");
    }
}
