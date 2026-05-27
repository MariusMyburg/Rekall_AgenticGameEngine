using System.Text.Json.Nodes;
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
                <Diffuse Id="Earth_Diffuse" Path="Textures/Earth_Diffuse.ktx2" Category="Terrain" />
                <Normal Id="Earth_Normal" Path="Textures/Earth_Normal.ktx2" Category="Terrain" />
                <Rotation DefinitionFrame="Ecliptic">
                  <SiderealPeriod Hours="23.9344695944" />
                  <Tilt Degrees="23.441522" />
                  <Azimuth Degrees="-0.363633" />
                  <InitialParentFacingLongitude Degrees="-118" />
                </Rotation>
                <Atmosphere>
                  <Visual>
                    <RayleighScattering>
                      <Coefficients R="0.006" G="0.012" B="0.028" />
                      <ScaleHeight Km="8" />
                    </RayleighScattering>
                    <MieScattering>
                      <Coefficients R="0.004" G="0.004" B="0.004" />
                      <ScaleHeight Km="1.2" />
                      <PhaseFunctionAsymmetry X="0.76" />
                    </MieScattering>
                    <Ozone>
                      <Coefficients R="0.004" G="0.008" B="0.018" />
                    </Ozone>
                  </Visual>
                </Atmosphere>
                <Ocean>
                  <ColorTexture Path="Textures/Earth_Ocean_Color_4096.ktx2" Category="Terrain" />
                </Ocean>
                <Clouds>
                  <Layer Id="EarthCloudsBase">
                    <Texture Id="EarthCloudsBase" Path="Textures/Clouds/Compressed/Earth_clouds_base_layer.dds" Category="Terrain" />
                    <TwoDimensionalCloud>
                      <UseAlphaOnly Value="true" />
                      <Color R="0.82" G="0.9" B="1.0" A="0.7" />
                      <Lambertian Value="0.28" />
                    </TwoDimensionalCloud>
                  </Layer>
                  <Layer Id="EarthCloudsTop">
                    <Texture Id="EarthCloudsTop" Path="Textures/Clouds/Compressed/Earth_clouds_top_layer.dds" Category="Terrain" />
                    <TwoDimensionalCloud>
                      <UseAlphaOnly Value="true" />
                      <Color R="1.0" G="0.96" B="0.88" />
                      <Lambertian Value="0.35" />
                    </TwoDimensionalCloud>
                  </Layer>
                </Clouds>
              </AtmosphericBody>
              <AtmosphericBody Id="Saturn" Parent="Sol">
                <Orbit>
                  <SemiMajorAxis Km="1433449370" />
                </Orbit>
                <MeanRadius Km="58232" />
                <Mass Earths="95.159" />
                <Color R="0.95" G="0.83" B="0.58" />
                <Diffuse Id="Saturn_Diffuse" Path="Textures/Saturn_Diffuse.ktx2" Category="Terrain" />
                <Rings DefinitionFrame="Equatorial">
                  <InnerRadius Km="74500" />
                  <OuterRadius Km="140220" />
                  <Texture Id="SaturnRings" Path="Textures/Planets/Saturn/Rings.png" />
                </Rings>
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
              <LoadFromLibrary Id="Saturn" Parent="Sol" />
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
        await File.WriteAllBytesAsync(Path.Combine(textures, "Earth_Normal.ktx2"), CreateKtx2Header(144, 1024, 512, 7, 2));
        await File.WriteAllBytesAsync(Path.Combine(textures, "Earth_Ocean_Color_4096.ktx2"), CreateKtx2Header(131, 512, 256, 7, 2));
        await File.WriteAllBytesAsync(Path.Combine(textures, "Luna_Diffuse.ktx2"), CreateKtx2Header(146, 512, 256, 6, 2));
        await File.WriteAllBytesAsync(Path.Combine(textures, "Saturn_Diffuse.ktx2"), CreateKtx2Header(146, 512, 256, 6, 2));
        Directory.CreateDirectory(Path.Combine(textures, "Clouds", "Compressed"));
        await File.WriteAllBytesAsync(Path.Combine(textures, "Clouds", "Compressed", "Earth_clouds_base_layer.dds"), CreateDdsHeader(512, 256, "DXT5"));
        await File.WriteAllBytesAsync(Path.Combine(textures, "Clouds", "Compressed", "Earth_clouds_top_layer.dds"), CreateDdsHeader(512, 256, "DXT5"));
        Directory.CreateDirectory(Path.Combine(textures, "Planets", "Saturn"));
        await File.WriteAllBytesAsync(Path.Combine(textures, "Planets", "Saturn", "Rings.png"), CreateTinyPng());

        var result = await new ImportKsaSolarSystemCommand().ExecuteAsync(
            new ImportKsaSolarSystemRequest(
                projectRoot,
                "Solar",
                ksaRoot,
                SystemFileName: "SolSystem.xml",
                ImportDiffuseTextures: true,
                DistanceScale: 0.000001,
                RadiusScale: 0.00002),
            new RekallAgeCommandContext("test", RekallAgeTransaction.Begin("import ksa solar"), CancellationToken.None));

        Assert.True(result.Ok, result.Summary);
        Assert.Equal(5, result.Value.BodyCount);
        Assert.Equal(8, result.Value.ImportedAssetCount);
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
        var solHalo = sol.Components.Single(component => component.Type == "Rekall.HaloRenderer");
        Assert.Equal("#ffb347cc", solHalo.Properties["color"]!.GetValue<string>());
        Assert.True(solHalo.Properties["radius"]!.GetValue<double>() > 100);
        Assert.Equal(6, solHalo.Properties["rings"]!.GetValue<double>());
        Assert.Equal(2.4, solHalo.Properties["falloff"]!.GetValue<double>(), precision: 6);
        Assert.Equal("camera-plane", solHalo.Properties["facingMode"]!.GetValue<string>());
        Assert.Equal("stellar-glow", solHalo.Properties["layer"]!.GetValue<string>());
        var solLight = sol.Components.Single(component => component.Type == "Rekall.PointLight");
        Assert.True(solLight.Properties["intensity"]!.GetValue<double>() > 1);
        Assert.Equal("#ffb347", solLight.Properties["color"]!.GetValue<string>());
        Assert.Contains(earth.Components, component => component.Type == "Rekall.CelestialBody");
        var orbit = earth.Components.Single(component => component.Type == "Rekall.KeplerOrbit");
        Assert.Equal("Sol", orbit.Properties["parentBodyId"]!.GetValue<string>());
        Assert.Equal(149539627.7103892, orbit.Properties["semiMajorAxisKm"]!.GetValue<double>(), precision: 3);
        Assert.Equal(2592000, orbit.Properties["timeScale"]!.GetValue<double>(), precision: 3);
        var earthOrbitPath = earth.Components.Single(component => component.Type == "Rekall.OrbitPathRenderer");
        Assert.Equal("orbit-guides", earthOrbitPath.Properties["layer"]!.GetValue<string>());
        var planet = earth.Components.Single(component => component.Type == "Rekall.PlanetRenderer");
        Assert.StartsWith("asset_earth-diffuse_", planet.Properties["surfaceTexture"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.StartsWith("asset_earth-normal_", planet.Properties["normalTexture"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.StartsWith("asset_earth-ocean-color_", planet.Properties["waterTexture"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.True(planet.Properties["waterSpecularStrength"]!.GetValue<double>() > 1);
        var virtualGeometry = earth.Components.Single(component => component.Type == "Rekall.VirtualGeometry");
        Assert.Equal(12000, virtualGeometry.Properties["maxSelectedTriangles"]!.GetValue<int>());
        Assert.Equal(128, virtualGeometry.Properties["clusterTriangleCount"]!.GetValue<int>());
        var earthMarker = earth.Components.Single(component => component.Type == "Rekall.MarkerRenderer");
        Assert.Equal("overview-markers", earthMarker.Properties["layer"]!.GetValue<string>());
        Assert.True(earthMarker.Properties["size"]!.GetValue<double>() > planet.Properties["radius"]!.GetValue<double>());
        Assert.True(earthMarker.Properties["size"]!.GetValue<double>() >= 40);
        Assert.Equal(planet.Properties["color"]!.GetValue<string>(), earthMarker.Properties["color"]!.GetValue<string>());
        var earthLabel = earth.Components.Single(component => component.Type == "Rekall.TextLabelRenderer");
        Assert.Equal("Earth", earthLabel.Properties["text"]!.GetValue<string>());
        Assert.Equal("overview-labels", earthLabel.Properties["layer"]!.GetValue<string>());
        Assert.Equal("camera-plane", earthLabel.Properties["facingMode"]!.GetValue<string>());
        Assert.Equal(2, earthLabel.Properties["minimumScreenHeightPixels"]!.GetValue<double>());
        Assert.InRange(earthLabel.Properties["size"]!.GetValue<double>(), 40, 70);
        var clouds = earth.Components
            .Where(component => component.Type == "Rekall.CloudLayerRenderer")
            .ToArray();
        var cloudComponent = Assert.Single(clouds);
        var cloudLayers = cloudComponent.Properties["layers"]!.AsArray();
        Assert.Equal(2, cloudLayers.Count);
        var baseClouds = cloudLayers[0]!.AsObject();
        var topClouds = cloudLayers[1]!.AsObject();
        Assert.StartsWith("asset_earth-earthcloudsbase_", baseClouds["texture"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.StartsWith("asset_earth-earthcloudstop_", topClouds["texture"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal("#d1e6ffb2", baseClouds["color"]!.GetValue<string>());
        Assert.Equal("#fff5e0", topClouds["color"]!.GetValue<string>());
        Assert.True(baseClouds["alphaFromTextureOnly"]!.GetValue<bool>());
        Assert.False(baseClouds["castShadows"]!.GetValue<bool>());
        Assert.True(topClouds["castShadows"]!.GetValue<bool>());
        Assert.True(topClouds["height"]!.GetValue<double>() > baseClouds["height"]!.GetValue<double>());
        var atmosphere = earth.Components.Single(component => component.Type == "Rekall.AtmosphereRenderer");
        Assert.Equal("#376dff", atmosphere.Properties["rayleighColor"]!.GetValue<string>());
        Assert.True(atmosphere.Properties["rayleighScattering"]!.GetValue<double>() > 0.005);
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
        var saturn = scene.Entities.Single(entity => entity.Name == "Saturn");
        var ring = saturn.Components.Single(component => component.Type == "Rekall.RingRenderer");
        Assert.True(ring.Properties["innerRadius"]!.GetValue<double>() > 0);
        Assert.True(ring.Properties["outerRadius"]!.GetValue<double>() > ring.Properties["innerRadius"]!.GetValue<double>());
        Assert.StartsWith("asset_saturn-rings_", ring.Properties["texture"]!.GetValue<string>(), StringComparison.Ordinal);

        var catalog = await new RekallAgeAssetCatalogStore().LoadAsync(projectRoot, CancellationToken.None);
        Assert.Equal(8, catalog.Assets.Count);
        Assert.All(catalog.Assets, asset => Assert.Equal("texture", asset.Kind));
        var cameraEntity = scene.Entities.Single(entity => entity.Name == "Main Camera");
        var camera = cameraEntity.Components.Single(component => component.Type == "Rekall.Camera3D");
        Assert.Equal("#000000", camera.Properties["clearColor"]!.GetValue<string>());
        Assert.Equal("orthographic", camera.Properties["projectionMode"]!.GetValue<string>());
        Assert.True(camera.Properties["orthographicSize"]!.GetValue<double>() > 100);
        var cameraTarget = cameraEntity.Components.Single(component => component.Type == "Rekall.CameraTarget3D");
        Assert.Equal("Sol", cameraTarget.Properties["targetName"]!.GetValue<string>());
        Assert.True(cameraTarget.Properties["offsetY"]!.GetValue<double>() > cameraTarget.Properties["offsetZ"]!.GetValue<double>());
        Assert.True(cameraTarget.Properties["lookAt"]!.GetValue<bool>());
        var cameraCycle = cameraEntity.Components.Single(component => component.Type == "Rekall.CameraTargetCycleInput");
        var tourTargets = Assert.IsType<JsonArray>(cameraCycle.Properties["targets"]);
        var overviewTarget = Assert.IsType<JsonObject>(tourTargets[0]);
        Assert.Equal("orthographic", overviewTarget["projectionMode"]!.GetValue<string>());
        Assert.True(overviewTarget["orthographicSize"]!.GetValue<double>() > 100);
        Assert.True(overviewTarget["offsetY"]!.GetValue<double>() > overviewTarget["offsetZ"]!.GetValue<double>());
        Assert.Contains(tourTargets.OfType<JsonObject>(), target => target["targetName"]?.GetValue<string>() == "Earth");
        Assert.Contains(tourTargets.OfType<JsonObject>(), target => target["targetName"]?.GetValue<string>() == "Saturn");
        Assert.Contains(tourTargets.OfType<JsonObject>(), target =>
            target["targetName"]?.GetValue<string>() == "Earth"
            && target["cullingMask"]?.GetValue<string>() == "default"
            && target["offsetReferenceName"]?.GetValue<string>() == "Sol"
            && target["offsetReferenceMode"]?.GetValue<string>() == "toward");
        Assert.Contains(cameraEntity.Components, component => component.Type == "Rekall.InputActionMap");
        var starfield = scene.Entities.Single(entity => entity.Name == "Deep Space Starfield");
        var starfieldRenderer = starfield.Components.Single(component => component.Type == "Rekall.StarfieldRenderer");
        Assert.True(starfieldRenderer.Properties["count"]!.GetValue<int>() >= 1000);
        Assert.True(starfieldRenderer.Properties["radius"]!.GetValue<double>() > 100);
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

    private static byte[] CreateDdsHeader(int width, int height, string fourCc)
    {
        var bytes = new byte[128];
        bytes[0] = (byte)'D';
        bytes[1] = (byte)'D';
        bytes[2] = (byte)'S';
        bytes[3] = (byte)' ';
        WriteUInt32(bytes, 4, 124);
        WriteUInt32(bytes, 8, 0x0002100f);
        WriteUInt32(bytes, 12, (uint)height);
        WriteUInt32(bytes, 16, (uint)width);
        WriteUInt32(bytes, 28, 1);
        WriteUInt32(bytes, 76, 32);
        WriteUInt32(bytes, 80, 0x4);
        bytes[84] = (byte)fourCc[0];
        bytes[85] = (byte)fourCc[1];
        bytes[86] = (byte)fourCc[2];
        bytes[87] = (byte)fourCc[3];
        return bytes;
    }

    private static byte[] CreateTinyPng()
    {
        return Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAFgwJ/l8f9NwAAAABJRU5ErkJggg==");
    }

    private static void WriteUInt32(byte[] bytes, int offset, uint value)
    {
        bytes[offset] = (byte)(value & 0xff);
        bytes[offset + 1] = (byte)((value >> 8) & 0xff);
        bytes[offset + 2] = (byte)((value >> 16) & 0xff);
        bytes[offset + 3] = (byte)((value >> 24) & 0xff);
    }
}
