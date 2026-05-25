using Rekall.Age.Assets;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.LevelDesign.Commands;
using Rekall.Age.World;

namespace Rekall.Age.Tests.LevelDesign;

public sealed class KsaSolarSystemImportCommandTests
{
    [Fact]
    public async Task ImportKsaSolarSystemCommandCreatesGenericCelestialEntitiesFromLibraryAndSystemXml()
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
              <StellarBody Id="Sol">
                <MeanRadius Km="695700" />
                <Mass Suns="1" />
                <Color R="1" G="0.85" B="0.4" />
              </StellarBody>
              <AtmosphericBody Id="Earth" Parent="Sol">
                <Orbit>
                  <SemiMajorAxis Km="149539627.7103892" />
                  <Inclination Degrees="0.0045" />
                  <Eccentricity Value="0.01657" />
                  <LongitudeOfAscendingNode Degrees="200.1" />
                  <ArgumentOfPeriapsis Degrees="264.7" />
                </Orbit>
                <MeanRadius Km="6371" />
                <Mass Earths="1" />
                <Color R="0.2" G="0.4" B="0.8" />
                <Rotation DefinitionFrame="Ecliptic">
                  <SiderealPeriod Hours="23.9344695944" />
                  <Tilt Degrees="23.441522" />
                  <Azimuth Degrees="-0.363633" />
                  <InitialParentFacingLongitude Degrees="-118" />
                </Rotation>
                <Atmosphere />
              </AtmosphericBody>
              <PlanetaryBody Id="Luna" Parent="Earth">
                <Orbit>
                  <SemiMajorAxis Km="380863.3349807188" />
                  <Inclination Degrees="5.02" />
                  <Eccentricity Value="0.067" />
                  <LongitudeOfAscendingNode Degrees="343.9" />
                  <ArgumentOfPeriapsis Degrees="90.9" />
                </Orbit>
                <MeanRadius Km="1737.4" />
                <Mass Lunars="1" />
                <Color R="0.7" G="0.7" B="0.68" />
              </PlanetaryBody>
            </Assets>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(core, "SolSystem.xml"),
            """
            <System Id="Sol">
              <LoadFromLibrary Id="Sol" />
              <LoadFromLibrary Id="Earth" Parent="Sol" HomeBody="true" />
              <LoadFromLibrary Id="Luna" Parent="Earth" />
              <MinorBody Id="Ceres" Parent="Sol">
                <Orbit>
                  <SemiMajorAxis Au="2.769" />
                  <Inclination Degrees="10.594" />
                  <Eccentricity Value="0.0758" />
                  <LongitudeOfAscendingNode Degrees="80.3056" />
                  <ArgumentOfPeriapsis Degrees="73.5977" />
                </Orbit>
                <MeanRadius Km="469.73" />
                <Mass Zg="938.392" />
                <Color R="0.72" G="0.72" B="0.72" />
              </MinorBody>
            </System>
            """);
        await File.WriteAllBytesAsync(Path.Combine(textures, "Earth_Diffuse.ktx2"), CreateKtx2Header(146, 1024, 512, 7, 2));
        await File.WriteAllBytesAsync(Path.Combine(textures, "Luna_Diffuse.ktx2"), CreateKtx2Header(146, 512, 256, 6, 2));

        var result = await new ImportKsaSolarSystemCommand().ExecuteAsync(
            new ImportKsaSolarSystemRequest(
                projectRoot,
                "Solar",
                ksaRoot,
                SystemFileName: "SolSystem.xml",
                ImportDiffuseTextures: true,
                DistanceScale: 0.000001,
                RadiusScale: 0.001),
            new RekallAgeCommandContext("test", RekallAgeTransaction.Begin("import ksa solar"), CancellationToken.None));

        Assert.True(result.Ok, result.Summary);
        Assert.Equal(4, result.Value.BodyCount);
        Assert.Equal(2, result.Value.ImportedAssetCount);
        Assert.Contains("Earth", result.Value.BodyIds);
        Assert.Contains("Ceres", result.Value.BodyIds);

        var scene = await new RekallAgeSceneStore().LoadAsync(projectRoot, "Solar", CancellationToken.None);
        var earth = scene.Entities.Single(entity => entity.Name == "Earth");
        var sol = scene.Entities.Single(entity => entity.Name == "Sol");
        Assert.Equal(sol.Id, earth.ParentId);
        var solCelestial = sol.Components.Single(component => component.Type == "Rekall.CelestialBody");
        Assert.Equal("#ffb347", solCelestial.Properties["color"]!.GetValue<string>());
        var solRenderer = sol.Components.Single(component => component.Type == "Rekall.PlanetRenderer");
        Assert.Equal("#ffb347", solRenderer.Properties["color"]!.GetValue<string>());
        var solMaterial = sol.Components.Single(component => component.Type == "Rekall.Material");
        Assert.Equal("#ffb347", solMaterial.Properties["baseColor"]!.GetValue<string>());
        Assert.Equal("#ffb347", solMaterial.Properties["emissiveColor"]!.GetValue<string>());
        Assert.True(solMaterial.Properties["emissiveStrength"]!.GetValue<double>() > 1);
        var solLight = sol.Components.Single(component => component.Type == "Rekall.PointLight");
        Assert.True(solLight.Properties["intensity"]!.GetValue<double>() > 1);
        Assert.Equal("#ffb347", solLight.Properties["color"]!.GetValue<string>());
        Assert.Contains(earth.Components, component => component.Type == "Rekall.CelestialBody");
        var orbit = earth.Components.Single(component => component.Type == "Rekall.KeplerOrbit");
        Assert.Equal("Sol", orbit.Properties["parentBodyId"]!.GetValue<string>());
        Assert.Equal(149539627.7103892, orbit.Properties["semiMajorAxisKm"]!.GetValue<double>(), precision: 3);
        Assert.Contains(earth.Components, component => component.Type == "Rekall.OrbitPathRenderer");
        var planet = earth.Components.Single(component => component.Type == "Rekall.PlanetRenderer");
        Assert.StartsWith("asset_earth-diffuse_", planet.Properties["surfaceTexture"]!.GetValue<string>(), StringComparison.Ordinal);
        var rotation = earth.Components.Single(component => component.Type == "Rekall.CelestialRotation");
        Assert.Equal(86164.09053984, rotation.Properties["siderealPeriodSeconds"]!.GetValue<double>(), precision: 3);
        Assert.Equal(23.441522, rotation.Properties["tiltDegrees"]!.GetValue<double>(), precision: 6);
        Assert.Equal(-118, rotation.Properties["initialLongitudeDegrees"]!.GetValue<double>(), precision: 6);
        Assert.True(rotation.Properties["timeScale"]!.GetValue<double>() > 1);
        var luna = scene.Entities.Single(entity => entity.Name == "Luna");
        Assert.Equal(earth.Id, luna.ParentId);
        var lunaOrbit = luna.Components.Single(component => component.Type == "Rekall.KeplerOrbit");
        Assert.Equal("Earth", lunaOrbit.Properties["parentBodyId"]!.GetValue<string>());
        Assert.True(lunaOrbit.Properties["distanceScale"]!.GetValue<double>() > 0.000001);
        Assert.Contains(luna.Components, component => component.Type == "Rekall.OrbitPathRenderer");

        var catalog = await new RekallAgeAssetCatalogStore().LoadAsync(projectRoot, CancellationToken.None);
        Assert.Equal(2, catalog.Assets.Count);
        Assert.All(catalog.Assets, asset => Assert.Equal("texture", asset.Kind));
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
