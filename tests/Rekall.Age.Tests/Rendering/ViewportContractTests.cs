using System.Text.Json.Nodes;
using Rekall.Age.Editor;
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
}
