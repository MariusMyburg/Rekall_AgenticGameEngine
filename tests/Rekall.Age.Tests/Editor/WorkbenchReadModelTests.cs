using System.Text.Json.Nodes;
using Rekall.Age.Assets;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Editor;
using Rekall.Age.Project;
using Rekall.Age.World;
using Rekall.Age.World.Commands;

namespace Rekall.Age.Tests.Editor;

public sealed class WorkbenchReadModelTests
{
    [Fact]
    public async Task WorkbenchModelUsesStableIdsAndInspectorProperties()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeProjectStore().SaveAsync(
            root,
            RekallAgeProjectManifest.Create("Crystal Mines", ["world", "rendering2d"]),
            CancellationToken.None);

        var sceneStore = new RekallAgeSceneStore();
        var player = RekallAgeEntityDocument.Create("Player", ["player"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Transform2D",
                new JsonObject
                {
                    ["x"] = 4,
                    ["y"] = 8
                }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.SpriteRenderer",
                new JsonObject { ["sprite"] = "asset_player_12345678" }));
        await sceneStore.SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "rendering2d"]).AddEntity(player),
            CancellationToken.None);

        var assetStore = new RekallAgeAssetCatalogStore();
        await assetStore.SaveAsync(
            root,
            RekallAgeAssetCatalogDocument.Empty.AddOrReplace(new RekallAgeAssetDocument(
                "asset_player_12345678",
                "player",
                "Player",
                "sprite",
                "source.png",
                "Assets/sprite/asset_player_12345678.png",
                "1234567890abcdef")),
            CancellationToken.None);

        var model = await new RekallAgeWorkbenchModelBuilder().BuildAsync(root, "Main", CancellationToken.None);

        Assert.Equal("Crystal Mines", model.Project.Name);
        Assert.Equal("Main", model.Scene.Name);
        var node = Assert.Single(model.Scene.RootEntities);
        Assert.Equal(player.Id, node.EntityId);
        Assert.Equal("Player", node.Name);
        var transform = model.Inspector.Components.Single(component => component.Type == "Rekall.Transform2D");
        Assert.Contains(transform.Properties, property => property.Name == "x" && property.Value == "4");
        Assert.Equal("asset_player_12345678", Assert.Single(model.Assets.Assets).AssetId);
        Assert.Contains(model.Diagnostics.Issues, issue => issue.Code == "REKALL_CAMERA_MISSING");
        Assert.Equal("Main", model.Runtime.SceneName);
        Assert.Equal(0, model.Runtime.FrameIndex);
        Assert.Equal(1, model.Runtime.EntityCount);
        Assert.Equal(1, model.Runtime.RenderableCount);
        Assert.Null(model.Runtime.ActiveCameraName);
        Assert.Equal("rekall.render.capture_runtime_viewport", model.Runtime.ViewportCaptureTool);
        Assert.DoesNotContain(model.Runtime.Observations, observation => observation.Severity == "blocking");
    }

    [Fact]
    public async Task WorkbenchRuntimePanelReportsActiveCameraAndViewportCaptureTool()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeProjectStore().SaveAsync(
            root,
            RekallAgeProjectManifest.Create("Viewport Project", ["world", "rendering2d"]),
            CancellationToken.None);

        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "rendering2d"])
                .AddEntity(RekallAgeEntityDocument.Create("MainCamera", ["camera"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera2D", new JsonObject { ["active"] = true })))
                .AddEntity(RekallAgeEntityDocument.Create("Player", ["player"])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform2D", new JsonObject { ["x"] = 2, ["y"] = 3 }))
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.SpriteRenderer", new JsonObject { ["sprite"] = "asset_player" }))),
            CancellationToken.None);

        var model = await new RekallAgeWorkbenchModelBuilder().BuildAsync(root, "Main", CancellationToken.None);

        Assert.Equal("MainCamera", model.Runtime.ActiveCameraName);
        Assert.Equal("rekall.render.capture_runtime_viewport", model.Runtime.ViewportCaptureTool);
        Assert.Equal(2, model.Runtime.EntityCount);
        Assert.Equal(2, model.Runtime.RenderableCount);
    }

    [Fact]
    public async Task WorkbenchModelLoadsPersistedTransactionHistory()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeProjectStore().SaveAsync(
            root,
            RekallAgeProjectManifest.Create("Transaction Project", ["world"]),
            CancellationToken.None);

        var context = new RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("create scene through command"),
            CancellationToken.None);
        var createScene = await new CreateSceneCommand().ExecuteAsync(
            new CreateSceneRequest(root, "Main", ["world"]),
            context);
        Assert.True(createScene.Ok, createScene.Summary);

        await new RekallAgeTransactionLogStore().AppendAsync(
            root,
            context.Transaction,
            context.Actor,
            CancellationToken.None);

        var model = await new RekallAgeWorkbenchModelBuilder().BuildAsync(root, "Main", CancellationToken.None);

        var transaction = Assert.Single(model.Transactions.Transactions);
        Assert.Equal(context.Transaction.Id, transaction.Id);
        Assert.Equal("create scene through command", transaction.Name);
        Assert.Contains(context.Transaction.ChangedResources.Single(), transaction.ChangedResources);
    }
}
