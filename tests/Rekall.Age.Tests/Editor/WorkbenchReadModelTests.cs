using System.Text.Json.Nodes;
using Rekall.Age.Assets;
using Rekall.Age.Editor;
using Rekall.Age.Project;
using Rekall.Age.World;

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
                }));
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
        Assert.Equal("Rekall.Transform2D", Assert.Single(model.Inspector.Components).Type);
        Assert.Contains(model.Inspector.Components[0].Properties, property => property.Name == "x" && property.Value == "4");
        Assert.Equal("asset_player_12345678", Assert.Single(model.Assets.Assets).AssetId);
        Assert.Contains(model.Diagnostics.Issues, issue => issue.Code == "REKALL_CAMERA_MISSING");
    }
}
