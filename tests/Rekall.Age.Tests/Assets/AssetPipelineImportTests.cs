using Rekall.Age.AssetPipeline.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;

namespace Rekall.Age.Tests.Assets;

public sealed class AssetPipelineImportTests
{
    [Fact]
    public async Task ImportWithReportWritesSourceImportedAndCookedRecords()
    {
        var root = TestPaths.CreateTempDirectory();
        var source = Path.Combine(root, "player.png");
        await File.WriteAllBytesAsync(source, [1, 2, 3, 4, 5], CancellationToken.None);
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new ImportAssetWithReportCommand());
        var context = new RekallAgeCommandContext("test", RekallAgeTransaction.Begin("import"), CancellationToken.None);

        var result = await registry.ExecuteAsync<ImportAssetWithReportRequest, ImportAssetWithReportResult>(
            "rekall.asset.import_report",
            new ImportAssetWithReportRequest(root, source, "sprite", "Player"),
            context);

        Assert.True(result.Ok);
        Assert.True(result.Value.Report.Imported);
        Assert.Equal("sprite", result.Value.Report.Kind);
        Assert.Single(result.Value.Pipeline.Sources);
        Assert.Single(result.Value.Pipeline.Imported);
        Assert.Single(result.Value.Pipeline.CookedArtifacts);
        Assert.Contains(
            "asset-pipeline.age.json",
            context.Transaction.ChangedResources.Single(path => path.EndsWith("asset-pipeline.age.json", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ImportWithReportExtractsGlbMetadataForAgentInspection()
    {
        var root = TestPaths.CreateTempDirectory();
        var source = Path.Combine(root, "robot.glb");
        await File.WriteAllBytesAsync(source, CreateMinimalGlb(), CancellationToken.None);
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new ImportAssetWithReportCommand());
        var context = new RekallAgeCommandContext("test", RekallAgeTransaction.Begin("import glb"), CancellationToken.None);

        var result = await registry.ExecuteAsync<ImportAssetWithReportRequest, ImportAssetWithReportResult>(
            "rekall.asset.import_report",
            new ImportAssetWithReportRequest(root, source, "model", "Robot"),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.NotNull(result.Value.Report.GlbMetadata);
        Assert.Equal(2, result.Value.Report.GlbMetadata.MeshCount);
        Assert.Equal(1, result.Value.Report.GlbMetadata.MaterialCount);
        Assert.Equal(1, result.Value.Report.GlbMetadata.ImageCount);
        Assert.Equal(2, result.Value.Report.GlbMetadata.NodeCount);
        Assert.Equal(1, result.Value.Report.GlbMetadata.AnimationCount);
        Assert.Contains(result.Value.Report.GlbMetadata.Meshes, mesh =>
            mesh.Name == "RobotBody" && mesh.PrimitiveCount == 1);
        Assert.Contains(result.Value.Report.GlbMetadata.Materials, material => material.Name == "Paint");
        Assert.Contains(result.Value.Report.GlbMetadata.Images, image => image.Name == "PaintTexture");
        Assert.Equal("model/gltf-binary", result.Value.Pipeline.Imported.Single().MimeType);
        Assert.NotNull(result.Value.Pipeline.Imported.Single().GlbMetadata);
    }

    [Fact]
    public async Task ImportWithReportExtractsTextureMetadataForAgentInspection()
    {
        var root = TestPaths.CreateTempDirectory();
        var source = Path.Combine(root, "terrain.ktx2");
        await File.WriteAllBytesAsync(source, CreateKtx2Header(145, 4096, 2048, 12, 0), CancellationToken.None);
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new ImportAssetWithReportCommand());
        var context = new RekallAgeCommandContext("test", RekallAgeTransaction.Begin("import texture"), CancellationToken.None);

        var result = await registry.ExecuteAsync<ImportAssetWithReportRequest, ImportAssetWithReportResult>(
            "rekall.asset.import_report",
            new ImportAssetWithReportRequest(root, source, "texture", "Terrain"),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.NotNull(result.Value.Report.TextureMetadata);
        Assert.Equal("ktx2", result.Value.Report.TextureMetadata.Container);
        Assert.Equal("VK_FORMAT_BC7_UNORM_BLOCK", result.Value.Report.TextureMetadata.Format);
        Assert.Equal(4096, result.Value.Report.TextureMetadata.Width);
        Assert.Equal(2048, result.Value.Report.TextureMetadata.Height);
        Assert.Equal("image/ktx2", result.Value.Pipeline.Imported.Single().MimeType);
        Assert.NotNull(result.Value.Pipeline.Imported.Single().TextureMetadata);
    }

    private static byte[] CreateMinimalGlb()
    {
        const string json = """
        {
          "asset": { "version": "2.0", "generator": "Rekall AGE Test" },
          "scene": 0,
          "scenes": [{ "name": "MainScene", "nodes": [0] }],
          "nodes": [
            { "name": "RobotRoot", "mesh": 0 },
            { "name": "RobotChild", "mesh": 1 }
          ],
          "meshes": [
            { "name": "RobotBody", "primitives": [{ "attributes": { "POSITION": 0 }, "material": 0 }] },
            { "name": "RobotAntenna", "primitives": [{ "attributes": { "POSITION": 1 } }] }
          ],
          "materials": [{ "name": "Paint" }],
          "images": [{ "name": "PaintTexture", "mimeType": "image/png" }],
          "animations": [{ "name": "Idle" }]
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

    private static byte[] CreateKtx2Header(uint vkFormat, uint width, uint height, uint mipLevels, uint supercompression)
    {
        var bytes = new byte[80];
        var identifier = new byte[] { 0xAB, 0x4B, 0x54, 0x58, 0x20, 0x32, 0x30, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A };
        Array.Copy(identifier, bytes, identifier.Length);
        WriteUInt32(bytes, 12, vkFormat);
        WriteUInt32(bytes, 16, 1);
        WriteUInt32(bytes, 20, width);
        WriteUInt32(bytes, 24, height);
        WriteUInt32(bytes, 36, 1);
        WriteUInt32(bytes, 40, mipLevels);
        WriteUInt32(bytes, 44, supercompression);
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
