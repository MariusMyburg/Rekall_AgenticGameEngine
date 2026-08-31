using Rekall.Age.Assets.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Editor;
using Rekall.Age.Project;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Editor;

public sealed class ContentBrowserReadModelTests
{
    [Fact]
    public async Task WorkbenchProjectsImportedAssetsIntoProviderNeutralContentWithoutChangingLegacyAssets()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeProjectStore().SaveAsync(
            root,
            RekallAgeProjectManifest.Create("Content Project", ["world", "rendering3d"]),
            CancellationToken.None);
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"]),
            CancellationToken.None);

        var textureSource = Path.Combine(root, "zebra.png");
        var modelSource = Path.Combine(root, "alpha.glb");
        await File.WriteAllBytesAsync(textureSource, CreatePngHeader(320, 180));
        await File.WriteAllBytesAsync(modelSource, CreateMinimalGlb());
        var context = new RekallAgeCommandContext(
            "test",
            RekallAgeTransaction.Begin("import content"),
            CancellationToken.None);
        var texture = await new ImportAssetCommand().ExecuteAsync(
            new(root, textureSource, "texture", "Zebra Texture"), context);
        var model = await new ImportAssetCommand().ExecuteAsync(
            new(root, modelSource, "model", "Alpha Model"), context);
        Assert.True(texture.Ok, texture.Summary);
        Assert.True(model.Ok, model.Summary);

        var workbench = await new RekallAgeWorkbenchModelBuilder()
            .BuildAsync(root, "Main", CancellationToken.None);

        Assert.Empty(workbench.Content.Warnings);
        Assert.Collection(
            workbench.Content.Items,
            first => Assert.Equal("model", first.Family),
            second => Assert.Equal("texture", second.Family));
        var modelItem = workbench.Content.Items[0];
        Assert.Equal(model.Value.Asset.Id, modelItem.Id);
        Assert.Equal("Alpha Model", modelItem.DisplayName);
        Assert.Equal("model", modelItem.Kind);
        Assert.Equal("Imported", modelItem.Origin);
        Assert.Equal(model.Value.Asset.ImportedPath, modelItem.Path);
        Assert.Equal(model.Value.Asset.SourcePath, modelItem.SourcePath);
        Assert.Equal(model.Value.Asset.ContentHash, modelItem.Revision);
        Assert.Equal(1, modelItem.Preview.MeshCount);
        Assert.Equal(1, modelItem.Preview.MaterialCount);
        Assert.Equal(1, modelItem.Preview.AnimationCount);
        Assert.Contains("place", modelItem.Capabilities);

        var textureItem = workbench.Content.Items[1];
        Assert.Equal(texture.Value.Asset.Id, textureItem.Id);
        Assert.Equal(320, textureItem.Preview.Width);
        Assert.Equal(180, textureItem.Preview.Height);
        Assert.Contains("assign", textureItem.Capabilities);

        Assert.Equal(
            workbench.Assets.Assets.Select(asset => (asset.AssetId, asset.DisplayName, asset.Kind, asset.ImportedPath, asset.ContentHash)),
            new[]
            {
                (model.Value.Asset.Id, "Alpha Model", "model", model.Value.Asset.ImportedPath, model.Value.Asset.ContentHash),
                (texture.Value.Asset.Id, "Zebra Texture", "texture", texture.Value.Asset.ImportedPath, texture.Value.Asset.ContentHash)
            });
    }

    private static byte[] CreatePngHeader(uint width, uint height)
    {
        var bytes = new byte[32];
        byte[] signature = [137, 80, 78, 71, 13, 10, 26, 10];
        Array.Copy(signature, bytes, signature.Length);
        WriteUInt32BigEndian(bytes, 16, width);
        WriteUInt32BigEndian(bytes, 20, height);
        return bytes;
    }

    private static byte[] CreateMinimalGlb()
    {
        const string json = """
        {
          "asset": { "version": "2.0" },
          "nodes": [{ "name": "Root", "mesh": 0 }],
          "meshes": [{ "name": "Mesh", "primitives": [{ "attributes": { "POSITION": 0 } }] }],
          "materials": [{ "name": "Material" }],
          "animations": [{ "name": "Idle", "samplers": [], "channels": [] }]
        }
        """;
        var jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);
        var paddedLength = (jsonBytes.Length + 3) / 4 * 4;
        var bytes = new byte[20 + paddedLength];
        WriteUInt32LittleEndian(bytes, 0, 0x46546C67);
        WriteUInt32LittleEndian(bytes, 4, 2);
        WriteUInt32LittleEndian(bytes, 8, (uint)bytes.Length);
        WriteUInt32LittleEndian(bytes, 12, (uint)paddedLength);
        WriteUInt32LittleEndian(bytes, 16, 0x4E4F534A);
        Array.Copy(jsonBytes, 0, bytes, 20, jsonBytes.Length);
        Array.Fill<byte>(bytes, 0x20, 20 + jsonBytes.Length, paddedLength - jsonBytes.Length);
        return bytes;
    }

    private static void WriteUInt32LittleEndian(byte[] bytes, int offset, uint value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
        bytes[offset + 3] = (byte)(value >> 24);
    }

    private static void WriteUInt32BigEndian(byte[] bytes, int offset, uint value)
    {
        bytes[offset] = (byte)(value >> 24);
        bytes[offset + 1] = (byte)(value >> 16);
        bytes[offset + 2] = (byte)(value >> 8);
        bytes[offset + 3] = (byte)value;
    }
}
