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

    [Fact]
    public async Task ImportAssetStoresGlbMetadataInCatalog()
    {
        var root = TestPaths.CreateTempDirectory();
        var source = Path.Combine(root, "prop.glb");
        await File.WriteAllBytesAsync(source, CreateMinimalGlb("PropMesh"));
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("import glb asset"), CancellationToken.None);

        var result = await new ImportAssetCommand().ExecuteAsync(
            new ImportAssetRequest(root, source, "model", "Prop"),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.NotNull(result.Value.Asset.GlbMetadata);
        Assert.Equal(1, result.Value.Asset.GlbMetadata.MeshCount);
        Assert.Equal("PropMesh", Assert.Single(result.Value.Asset.GlbMetadata.Meshes).Name);
        var catalog = await new RekallAgeAssetCatalogStore().LoadAsync(root, CancellationToken.None);
        var catalogAsset = Assert.Single(catalog.Assets);
        Assert.NotNull(catalogAsset.GlbMetadata);
        Assert.Equal("PropMesh", Assert.Single(catalogAsset.GlbMetadata.Meshes).Name);
    }

    private static byte[] CreateMinimalGlb(string meshName)
    {
        var json = $$"""
        {
          "asset": { "version": "2.0" },
          "nodes": [{ "name": "Root", "mesh": 0 }],
          "meshes": [{ "name": "{{meshName}}", "primitives": [{ "attributes": { "POSITION": 0 } }] }]
        }
        """;
        var jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);
        var paddedJsonLength = (jsonBytes.Length + 3) / 4 * 4;
        var bytes = new byte[12 + 8 + paddedJsonLength];
        WriteUInt32(bytes, 0, 0x46546C67);
        WriteUInt32(bytes, 4, 2);
        WriteUInt32(bytes, 8, (uint)bytes.Length);
        WriteUInt32(bytes, 12, (uint)paddedJsonLength);
        WriteUInt32(bytes, 16, 0x4E4F534A);
        Array.Copy(jsonBytes, 0, bytes, 20, jsonBytes.Length);
        Array.Fill<byte>(bytes, 0x20, 20 + jsonBytes.Length, paddedJsonLength - jsonBytes.Length);
        return bytes;
    }

    private static void WriteUInt32(byte[] bytes, int offset, uint value)
    {
        bytes[offset] = (byte)(value & 0xff);
        bytes[offset + 1] = (byte)((value >> 8) & 0xff);
        bytes[offset + 2] = (byte)((value >> 16) & 0xff);
        bytes[offset + 3] = (byte)((value >> 24) & 0xff);
    }
}
