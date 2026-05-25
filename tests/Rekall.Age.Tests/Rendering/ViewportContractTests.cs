using System.Text.Json.Nodes;
using Rekall.Age.Editor;
using Rekall.Age.Rendering;
using Rekall.Age.Runtime;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Rendering;

public sealed class ViewportContractTests
{
    [Fact]
    public async Task ViewportModelExtractsCameraAndRenderableSprites()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering2d"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera2D", new JsonObject { ["active"] = true })))
            .AddEntity(RekallAgeEntityDocument.Create("Player", ["player"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.SpriteRenderer", new JsonObject { ["sprite"] = "asset_player" })));
        await new RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None);

        var viewport = await new RekallAgeViewportModelBuilder().BuildAsync(root, "Main", CancellationToken.None);

        Assert.Equal("Main", viewport.SceneName);
        Assert.Equal("Camera", viewport.ActiveCameraName);
        Assert.Single(viewport.RenderWorld.Sprites);
        Assert.Equal("Player", viewport.RenderWorld.Sprites[0].EntityName);
    }

    [Fact]
    public void RuntimeFrameBuilderUsesRuntimeRenderProjection()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering2d"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera2D", new JsonObject { ["active"] = true })))
            .AddEntity(RekallAgeEntityDocument.Create("Player", ["player"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform2D", new JsonObject { ["x"] = 4, ["y"] = 8 }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.SpriteRenderer", new JsonObject { ["sprite"] = "asset_player" })))
            .AddEntity(RekallAgeEntityDocument.Create("Light", ["lighting"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.PointLight", new JsonObject { ["intensity"] = 1 })));
        var world = new RekallAgeRuntimeWorldBuilder().Build(scene) with
        {
            FrameIndex = 2,
            ElapsedTime = TimeSpan.FromSeconds(2.0 / 60.0)
        };

        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 320, 180, debugOverlay: true);

        Assert.Equal("Main", frame.SceneName);
        Assert.Equal(2, frame.FrameIndex);
        Assert.Equal(320, frame.Width);
        Assert.Equal(180, frame.Height);
        Assert.Equal("Camera", frame.ActiveCamera?.EntityName);
        Assert.Contains(frame.Renderables, item => item.Kind == "sprite" && item.AssetId == "asset_player");
        Assert.Contains(frame.Renderables, item => item.Kind == "light" && item.EntityName == "Light");
        Assert.True(frame.DebugOverlay.Enabled);
    }
}
