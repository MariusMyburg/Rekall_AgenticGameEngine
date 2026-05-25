using Rekall.Age.Assets;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.LevelDesign.Commands;
using Rekall.Age.World;

namespace Rekall.Age.Tests.LevelDesign;

public sealed class KsaPlanetImportCommandTests
{
    [Fact]
    public async Task ImportKsaPlanetCommandCopiesPlanetTexturesAndCreatesRenderablePlanet()
    {
        var projectRoot = TestPaths.CreateTempDirectory();
        var ksaRoot = TestPaths.CreateTempDirectory();
        var core = Path.Combine(ksaRoot, "Content", "Core");
        var textures = Path.Combine(core, "Textures");
        Directory.CreateDirectory(textures);
        await File.WriteAllTextAsync(
            Path.Combine(core, "Astronomicals.xml"),
            """
            <Assets>
              <AtmosphericBody Id="Earth" Parent="Sol">
                <MeanRadius Km="6" />
                <Color R="0.2" G="0.4" B="0.8" />
                <Atmosphere />
              </AtmosphericBody>
            </Assets>
            """);
        await File.WriteAllBytesAsync(Path.Combine(textures, "Earth_Diffuse.ktx2"), CreateKtx2Header(146, 1024, 512, 7, 2));
        await File.WriteAllBytesAsync(Path.Combine(textures, "Earth_Height.ktx2"), CreateKtx2Header(37, 1024, 512, 1, 0));
        await File.WriteAllBytesAsync(Path.Combine(textures, "Earth_Normal.ktx2"), CreateKtx2Header(145, 1024, 512, 7, 2));

        var result = await new ImportKsaPlanetCommand().ExecuteAsync(
            new ImportKsaPlanetRequest(projectRoot, "Planets", ksaRoot, "Earth", "Gaia"),
            new RekallAgeCommandContext("test", RekallAgeTransaction.Begin("import ksa"), CancellationToken.None));

        Assert.True(result.Ok);
        Assert.Equal(3, result.Value.ImportedAssetCount);
        Assert.NotNull(result.Value.SurfaceTextureAssetId);
        Assert.True(result.Value.Atmosphere);
        var catalog = await new RekallAgeAssetCatalogStore().LoadAsync(projectRoot, CancellationToken.None);
        Assert.Equal(3, catalog.Assets.Count);
        Assert.All(catalog.Assets, asset =>
        {
            Assert.Equal("texture", asset.Kind);
            Assert.True(File.Exists(asset.ImportedPath));
            Assert.NotNull(asset.TextureMetadata);
            Assert.Equal("ktx2", asset.TextureMetadata.Container);
            Assert.Equal(1024, asset.TextureMetadata.Width);
            Assert.Equal(512, asset.TextureMetadata.Height);
        });
        Assert.Contains(catalog.Assets, asset => asset.TextureMetadata?.Format == "VK_FORMAT_BC7_SRGB_BLOCK");
        var scene = await new RekallAgeSceneStore().LoadAsync(projectRoot, "Planets", CancellationToken.None);
        var planet = scene.Entities.Single(entity => entity.Name == "Gaia");
        Assert.Contains(planet.Components, component => component.Type == "Rekall.PlanetRenderer");
        Assert.Contains(planet.Components, component => component.Type == "Rekall.AtmosphereRenderer");
        Assert.Contains(scene.Entities, entity => entity.Components.Any(component => component.Type == "Rekall.Camera3D"));
        Assert.Contains(scene.Entities, entity => entity.Components.Any(component => component.Type == "Rekall.DirectionalLight"));
    }

    [Fact]
    public async Task ImportKsaPlanetCommandSupportsMinorBodies()
    {
        var projectRoot = TestPaths.CreateTempDirectory();
        var ksaRoot = TestPaths.CreateTempDirectory();
        var core = Path.Combine(ksaRoot, "Content", "Core");
        var textures = Path.Combine(core, "Textures");
        Directory.CreateDirectory(textures);
        await File.WriteAllTextAsync(
            Path.Combine(core, "Astronomicals.xml"),
            """
            <Assets>
              <MinorBody Id="Phobos" Parent="Mars">
                <MeanRadius Km="0.5" />
                <Color R="0.7" G="0.68" B="0.62" />
              </MinorBody>
            </Assets>
            """);
        await File.WriteAllBytesAsync(Path.Combine(textures, "Phobos_Diffuse.ktx2"), CreateKtx2Header(146, 512, 512, 6, 2));

        var result = await new ImportKsaPlanetCommand().ExecuteAsync(
            new ImportKsaPlanetRequest(projectRoot, "Planets", ksaRoot, "Phobos"),
            new RekallAgeCommandContext("test", RekallAgeTransaction.Begin("import ksa minor"), CancellationToken.None));

        Assert.True(result.Ok);
        Assert.Equal("Phobos", result.Value.BodyId);
        Assert.False(result.Value.Atmosphere);
        Assert.Equal(1, result.Value.ImportedAssetCount);
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
