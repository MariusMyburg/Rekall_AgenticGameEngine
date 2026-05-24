using Rekall.Age.Assets;
using Rekall.Age.Assets.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;

namespace Rekall.Age.Tests.Assets;

public sealed class AssetCommandTests
{
    [Fact]
    public async Task ImportAssetCopiesFileAndAddsCatalogEntry()
    {
        var root = TestPaths.CreateTempDirectory();
        var source = Path.Combine(root, "source.png");
        await File.WriteAllBytesAsync(source, [1, 2, 3, 4]);
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("import asset"), CancellationToken.None);
        var command = new ImportAssetCommand();

        var result = await command.ExecuteAsync(
            new ImportAssetRequest(root, source, "sprite", "Player Ship"),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.Equal("player-ship", result.Value.Asset.Name);
        Assert.Equal("sprite", result.Value.Asset.Kind);
        Assert.True(File.Exists(result.Value.Asset.ImportedPath));
        Assert.StartsWith("asset_player-ship_", result.Value.Asset.Id, StringComparison.Ordinal);

        var catalog = await new RekallAgeAssetCatalogStore().LoadAsync(root, CancellationToken.None);
        var asset = Assert.Single(catalog.Assets);
        Assert.Equal(result.Value.Asset.Id, asset.Id);
    }

    [Fact]
    public async Task ListAssetsReturnsAssetsSortedByKindThenName()
    {
        var root = TestPaths.CreateTempDirectory();
        var sourceA = Path.Combine(root, "z.wav");
        var sourceB = Path.Combine(root, "a.png");
        await File.WriteAllBytesAsync(sourceA, [9]);
        await File.WriteAllBytesAsync(sourceB, [8]);
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("list assets"), CancellationToken.None);
        await new ImportAssetCommand().ExecuteAsync(new ImportAssetRequest(root, sourceA, "audio", "Zap"), context);
        await new ImportAssetCommand().ExecuteAsync(new ImportAssetRequest(root, sourceB, "sprite", "Avatar"), context);

        var result = await new ListAssetsCommand().ExecuteAsync(new ListAssetsRequest(root), context);

        Assert.True(result.Ok, result.Summary);
        Assert.Collection(
            result.Value.Assets,
            asset => Assert.Equal("audio", asset.Kind),
            asset => Assert.Equal("sprite", asset.Kind));
    }
}
