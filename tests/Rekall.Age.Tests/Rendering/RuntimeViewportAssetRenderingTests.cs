using System.Text.Json.Nodes;
using Rekall.Age.Assets;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Runtime;
using Rekall.Age.World;
using ZstdSharp;

namespace Rekall.Age.Tests.Rendering;

public sealed class RuntimeViewportAssetRenderingTests
{
    [Fact]
    public async Task SoftwareRendererDrawsDecodedSpriteAssetPixels()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering2d"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera2D", new JsonObject { ["active"] = true })))
            .AddEntity(RekallAgeEntityDocument.Create("Player", ["player"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform2D", new JsonObject { ["x"] = 4, ["y"] = 8 }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.SpriteRenderer", new JsonObject { ["sprite"] = "asset_player" })));
        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);
        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 160, 90, debugOverlay: false);
        var asset = new RekallAgeRgbaImage(
            2,
            2,
            [
                250, 10, 20, 255,
                250, 10, 20, 255,
                250, 10, 20, 255,
                250, 10, 20, 255
            ]);

        var capture = await new RekallAgeRuntimeSoftwareRenderer().CaptureAsync(
            frame,
            Path.Combine(root, "captures"),
            "Main_runtime_000.png",
            new RekallAgeRuntimeViewportAssetSet(
                new Dictionary<string, RekallAgeRgbaImage>(StringComparer.Ordinal)
                {
                    ["asset_player"] = asset
                },
                new Dictionary<string, IReadOnlyList<RekallAgeVulkanSceneMesh>>(StringComparer.Ordinal),
                Array.Empty<RekallAgeRuntimeViewportAssetIssue>()),
            CancellationToken.None);
        var output = await RekallAgePngReader.ReadRgbaAsync(capture.ScreenshotPath, CancellationToken.None);

        Assert.True(capture.NonBlank);
        Assert.Equal(1, capture.AssetBackedRenderableCount);
        Assert.Equal(0, capture.FallbackRenderableCount);
        Assert.Contains(Enumerable.Range(0, output.Rgba.Length / 4), pixel =>
        {
            var index = pixel * 4;
            return output.Rgba[index] == 250 && output.Rgba[index + 1] == 10 && output.Rgba[index + 2] == 20;
        });
    }

    [Fact]
    public async Task SoftwareRendererRasterizesPrimitiveCubeWithDirectionalLighting()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("MainCamera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject { ["active"] = true })))
            .AddEntity(RekallAgeEntityDocument.Create("Cube", ["prop"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["yaw"] = 32, ["pitch"] = -8, ["scaleX"] = 2, ["scaleY"] = 2, ["scaleZ"] = 2 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.MeshRenderer",
                    new JsonObject { ["mesh"] = "rekall.primitive.cube" })))
            .AddEntity(RekallAgeEntityDocument.Create("KeyLight", ["light"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["pitch"] = -35, ["yaw"] = -35 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.DirectionalLight",
                    new JsonObject { ["intensity"] = 1.0 })));
        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);
        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 180, 120, debugOverlay: false);

        var capture = await new RekallAgeRuntimeSoftwareRenderer().CaptureAsync(
            frame,
            Path.Combine(root, "captures"),
            "Main_runtime_000.png",
            RekallAgeRuntimeViewportAssetSet.Empty,
            CancellationToken.None);
        var output = await RekallAgePngReader.ReadRgbaAsync(capture.ScreenshotPath, CancellationToken.None);
        var shadedCubePixels = Enumerable.Range(0, output.Rgba.Length / 4)
            .Select(pixel => pixel * 4)
            .Where(index =>
                output.Rgba[index] >= 45
                && output.Rgba[index] <= 190
                && output.Rgba[index + 1] >= 70
                && output.Rgba[index + 1] <= 210
                && output.Rgba[index + 2] >= 100)
            .Select(index => (R: output.Rgba[index], G: output.Rgba[index + 1], B: output.Rgba[index + 2]))
            .Distinct()
            .ToArray();

        Assert.True(capture.NonBlank);
        Assert.Equal(0, capture.FallbackRenderableCount);
        Assert.Contains("mesh", frame.Renderables.Select(renderable => renderable.Kind));
        Assert.Contains("light", frame.Renderables.Select(renderable => renderable.Kind));
        Assert.True(shadedCubePixels.Length >= 3);
    }

    [Fact]
    public async Task AssetResolverLoadsImportedGlbModelMeshesForMeshRenderables()
    {
        var root = TestPaths.CreateTempDirectory();
        var glbPath = Path.Combine(root, "station.glb");
        await File.WriteAllBytesAsync(glbPath, GlbTestMeshFactory.CreateTriangleGlb(), CancellationToken.None);
        await new RekallAgeAssetCatalogStore().SaveAsync(
            root,
            new RekallAgeAssetCatalogDocument(
            [
                new RekallAgeAssetDocument(
                    "asset_station",
                    "station",
                    "Station",
                    "model",
                    glbPath,
                    glbPath,
                    "hash")
            ]),
            CancellationToken.None);
        var frame = new Rekall.Age.Rendering.Abstractions.RekallAgeRuntimeViewportFrame(
            "Main",
            0,
            0,
            160,
            90,
            null,
            [],
            [
                new Rekall.Age.Rendering.Abstractions.RekallAgeRuntimeViewportRenderable(
                    "entity-station",
                    "Station",
                    "mesh",
                    "asset_station",
                    0,
                    0,
                    0,
                    1)
            ],
            0,
            new Rekall.Age.Rendering.Abstractions.RekallAgeRuntimeViewportOverlay(false, 0),
            []);

        var assets = await new RekallAgeRuntimeViewportAssetResolver().ResolveAsync(root, frame, CancellationToken.None);

        Assert.True(assets.Models.ContainsKey("asset_station"));
        var mesh = Assert.Single(assets.Models["asset_station"]);
        Assert.Equal(3, mesh.Vertices.Count);
        Assert.Empty(assets.Issues);
    }

    [Fact]
    public async Task AssetResolverLoadsTextureAssetsForMeshAndPlanetRenderables()
    {
        var root = TestPaths.CreateTempDirectory();
        var texturePath = Path.Combine(root, "earth.png");
        await RekallAgePngWriter.WriteRgbaAsync(
            texturePath,
            1,
            1,
            [70, 120, 210, 255],
            CancellationToken.None);
        await new RekallAgeAssetCatalogStore().SaveAsync(
            root,
            new RekallAgeAssetCatalogDocument(
            [
                new RekallAgeAssetDocument(
                    "asset_earth",
                    "earth",
                    "Earth",
                    "texture",
                    texturePath,
                    texturePath,
                    "hash")
            ]),
            CancellationToken.None);
        var frame = new Rekall.Age.Rendering.Abstractions.RekallAgeRuntimeViewportFrame(
            "Main",
            0,
            0,
            160,
            90,
            null,
            [],
            [
                new Rekall.Age.Rendering.Abstractions.RekallAgeRuntimeViewportRenderable(
                    "entity-planet",
                    "Planet",
                    "mesh",
                    "rekall.planet.surface",
                    0,
                    0,
                    0,
                    1,
                    Variant: "rekall.planet.surface",
                    TextureAssetId: "asset_earth")
            ],
            0,
            new Rekall.Age.Rendering.Abstractions.RekallAgeRuntimeViewportOverlay(false, 0),
            []);

        var assets = await new RekallAgeRuntimeViewportAssetResolver().ResolveAsync(root, frame, CancellationToken.None);

        Assert.True(assets.Images.ContainsKey("asset_earth"));
        Assert.Empty(assets.Issues);
    }

    [Fact]
    public async Task AssetResolverReportsCompressedTextureAssetsAsWaitingForTranscoding()
    {
        var root = TestPaths.CreateTempDirectory();
        var texturePath = Path.Combine(root, "planet.ktx2");
        await File.WriteAllBytesAsync(texturePath, CreateKtx2Header(146, 1024, 512, 7, 2), CancellationToken.None);
        await new RekallAgeAssetCatalogStore().SaveAsync(
            root,
            new RekallAgeAssetCatalogDocument(
            [
                new RekallAgeAssetDocument(
                    "asset_planet",
                    "planet",
                    "Planet",
                    "texture",
                    texturePath,
                    texturePath,
                    "hash")
                {
                    TextureMetadata = new RekallAgeTextureMetadata(
                        "ktx2",
                        1024,
                        512,
                        7,
                        "VK_FORMAT_BC7_SRGB_BLOCK",
                        "Zstandard",
                        true)
                }
            ]),
            CancellationToken.None);
        var frame = new Rekall.Age.Rendering.Abstractions.RekallAgeRuntimeViewportFrame(
            "Main",
            0,
            0,
            160,
            90,
            null,
            [],
            [
                new Rekall.Age.Rendering.Abstractions.RekallAgeRuntimeViewportRenderable(
                    "entity-planet",
                    "Planet",
                    "mesh",
                    "rekall.planet.surface",
                    0,
                    0,
                    0,
                    1,
                    Variant: "rekall.planet.surface",
                    TextureAssetId: "asset_planet")
            ],
            0,
            new Rekall.Age.Rendering.Abstractions.RekallAgeRuntimeViewportOverlay(false, 0),
            []);

        var assets = await new RekallAgeRuntimeViewportAssetResolver().ResolveAsync(root, frame, CancellationToken.None);

        Assert.False(assets.Images.ContainsKey("asset_planet"));
        var issue = Assert.Single(assets.Issues);
        Assert.Equal("REKALL_RENDER_TEXTURE_COMPRESSED_UNTRANSCODED", issue.Code);
        Assert.Contains("VK_FORMAT_BC7_SRGB_BLOCK", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AssetResolverExtractsGpuReadyKtx2TexturePayloads()
    {
        var root = TestPaths.CreateTempDirectory();
        var texturePath = Path.Combine(root, "planet.ktx2");
        var mipBytes = Enumerable.Range(0, 64).Select(value => (byte)value).ToArray();
        await File.WriteAllBytesAsync(texturePath, CreateKtx2Texture(145, 8, 8, mipBytes), CancellationToken.None);
        await new RekallAgeAssetCatalogStore().SaveAsync(
            root,
            new RekallAgeAssetCatalogDocument(
            [
                new RekallAgeAssetDocument(
                    "asset_planet",
                    "planet",
                    "Planet",
                    "texture",
                    texturePath,
                    texturePath,
                    "hash")
                {
                    TextureMetadata = new RekallAgeTextureMetadata(
                        "ktx2",
                        8,
                        8,
                        1,
                        "VK_FORMAT_BC7_UNORM_BLOCK",
                        null,
                        true)
                }
            ]),
            CancellationToken.None);

        var assets = await new RekallAgeRuntimeViewportAssetResolver().ResolveAsync(
            root,
            CreateTexturedPlanetFrame("asset_planet"),
            CancellationToken.None);

        var texture = Assert.Single(assets.Textures.Values);
        Assert.Equal("asset_planet", texture.AssetId);
        Assert.Equal("ktx2", texture.Container);
        Assert.Equal("VK_FORMAT_BC7_UNORM_BLOCK", texture.Format);
        Assert.True(texture.GpuCompressed);
        var mip = Assert.Single(texture.MipLevels);
        Assert.Equal(8, mip.Width);
        Assert.Equal(8, mip.Height);
        Assert.Equal(mipBytes, mip.Bytes);
        Assert.Empty(assets.Issues);
    }

    [Fact]
    public async Task AssetResolverDecompressesZstandardKtx2TexturePayloads()
    {
        var root = TestPaths.CreateTempDirectory();
        var texturePath = Path.Combine(root, "planet.ktx2");
        var uncompressed = Enumerable.Range(0, 64).Select(value => (byte)(value * 3)).ToArray();
        await File.WriteAllBytesAsync(texturePath, CreateZstdKtx2Texture(146, 8, 8, uncompressed), CancellationToken.None);
        await new RekallAgeAssetCatalogStore().SaveAsync(
            root,
            new RekallAgeAssetCatalogDocument(
            [
                new RekallAgeAssetDocument(
                    "asset_planet",
                    "planet",
                    "Planet",
                    "texture",
                    texturePath,
                    texturePath,
                    "hash")
                {
                    TextureMetadata = new RekallAgeTextureMetadata(
                        "ktx2",
                        8,
                        8,
                        1,
                        "VK_FORMAT_BC7_SRGB_BLOCK",
                        "Zstandard",
                        true)
                }
            ]),
            CancellationToken.None);

        var assets = await new RekallAgeRuntimeViewportAssetResolver().ResolveAsync(
            root,
            CreateTexturedPlanetFrame("asset_planet"),
            CancellationToken.None);

        var texture = Assert.Single(assets.Textures.Values);
        Assert.Equal("Zstandard", texture.Supercompression);
        Assert.Equal("VK_FORMAT_BC7_SRGB_BLOCK", texture.Format);
        Assert.Equal(uncompressed, Assert.Single(texture.MipLevels).Bytes);
        Assert.Empty(assets.Issues);
    }

    [Fact]
    public void BlockCompressedDecoderExpandsBc1TextureToRgbaPixels()
    {
        var block = new byte[] { 0x00, 0xf8, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        var texture = new RekallAgeRuntimeTextureAsset(
            "asset_planet",
            "ktx2",
            4,
            4,
            1,
            "VK_FORMAT_BC1_RGB_SRGB_BLOCK",
            "Zstandard",
            true,
            [new RekallAgeRuntimeTextureMipLevel(0, 4, 4, block)]);

        var image = RekallAgeBlockCompressedTextureDecoder.TryDecodeTopLevel(texture);

        Assert.NotNull(image);
        Assert.Equal(4, image.Width);
        Assert.Equal(4, image.Height);
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                Assert.Equal(new RekallAgeRgbaPixel(255, 0, 0, 255), image.GetPixel(x, y));
            }
        }
    }

    [Fact]
    public void BlockCompressedDecoderKeepsBc1RgbFourthPaletteEntryOpaque()
    {
        var block = new byte[] { 0x00, 0x00, 0x00, 0xf8, 0xff, 0xff, 0xff, 0xff };
        var texture = new RekallAgeRuntimeTextureAsset(
            "asset_planet",
            "ktx2",
            4,
            4,
            1,
            "VK_FORMAT_BC1_RGB_SRGB_BLOCK",
            "Zstandard",
            true,
            [new RekallAgeRuntimeTextureMipLevel(0, 4, 4, block)]);

        var image = RekallAgeBlockCompressedTextureDecoder.TryDecodeTopLevel(texture);

        Assert.NotNull(image);
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var pixel = image.GetPixel(x, y);
                Assert.Equal(255, pixel.A);
                Assert.True(pixel.R > 0, $"Expected an opaque interpolated red pixel at {x},{y}, got {pixel}.");
            }
        }
    }

    [Fact]
    public void BatchBuilderDisablesNormalPerturbationWhenMeshHasNoNormalTexture()
    {
        var frame = CreateTexturedPlanetFrame("asset_planet");
        var mesh = new RekallAgeVulkanSceneMesh(
            "entity-planet",
            "Planet",
            "triangle",
            [
                new RekallAgeVulkanSceneVertex(-1, -1, 0, 0, 0, 1, 1, 1, 1, 1, 0, 0),
                new RekallAgeVulkanSceneVertex(1, -1, 0, 0, 0, 1, 1, 1, 1, 1, 1, 0),
                new RekallAgeVulkanSceneVertex(0, 1, 0, 0, 0, 1, 1, 1, 1, 1, 0.5f, 1)
            ],
            [0, 1, 2],
            BaseColorTexture: new RekallAgeVulkanSceneTexture(
                "asset_planet",
                1,
                1,
                [255, 255, 255, 255],
                new RekallAgeVulkanSceneSampler(
                    RekallAgeVulkanSceneFilter.Linear,
                    RekallAgeVulkanSceneFilter.Linear,
                    RekallAgeVulkanSceneWrapMode.Repeat,
                    RekallAgeVulkanSceneWrapMode.Repeat)));

        var batch = new RekallAgeVulkanSceneBatchBuilder().Build(frame, [mesh]);

        var draw = Assert.Single(batch.Draws);
        Assert.Equal(0, draw.MaterialFactors.Z);
        Assert.Equal(0, draw.MaterialFactors.W);
    }

    [Fact]
    public async Task AssetResolverExtractsGpuReadyDdsTexturePayloads()
    {
        var root = TestPaths.CreateTempDirectory();
        var texturePath = Path.Combine(root, "paint.dds");
        var mipBytes = Enumerable.Range(0, 64).Select(value => (byte)(255 - value)).ToArray();
        await File.WriteAllBytesAsync(texturePath, CreateDdsTexture(8, 8, 1, "DXT5", mipBytes), CancellationToken.None);
        await new RekallAgeAssetCatalogStore().SaveAsync(
            root,
            new RekallAgeAssetCatalogDocument(
            [
                new RekallAgeAssetDocument(
                    "asset_paint",
                    "paint",
                    "Paint",
                    "texture",
                    texturePath,
                    texturePath,
                    "hash")
                {
                    TextureMetadata = new RekallAgeTextureMetadata(
                        "dds",
                        8,
                        8,
                        1,
                        "BC3_UNorm",
                        null,
                        true)
                }
            ]),
            CancellationToken.None);

        var assets = await new RekallAgeRuntimeViewportAssetResolver().ResolveAsync(
            root,
            CreateTexturedPlanetFrame("asset_paint"),
            CancellationToken.None);

        var texture = Assert.Single(assets.Textures.Values);
        Assert.Equal("asset_paint", texture.AssetId);
        Assert.Equal("dds", texture.Container);
        Assert.Equal("BC3_UNorm", texture.Format);
        Assert.True(texture.GpuCompressed);
        Assert.Equal(mipBytes, Assert.Single(texture.MipLevels).Bytes);
        Assert.Empty(assets.Issues);
    }

    [Fact]
    public async Task SoftwareRendererRasterizesGeometryPrimitiveVariantsWithMaterialColors()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("MainCamera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject { ["active"] = true })))
            .AddEntity(Primitive("CubeBlock", "cube", -6, "#ff3355"))
            .AddEntity(Primitive("Orb", "sphere", -3, "#33ff66"))
            .AddEntity(Primitive("Column", "cylinder", 0, "#ffcc33"))
            .AddEntity(Primitive("MarkerCone", "cone", 3, "#cc66ff"))
            .AddEntity(Primitive("GroundPlate", "plane", 6, "#33ddff"));
        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);
        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 320, 180, debugOverlay: false);

        var capture = await new RekallAgeRuntimeSoftwareRenderer().CaptureAsync(
            frame,
            Path.Combine(root, "captures"),
            "Main_runtime_000.png",
            RekallAgeRuntimeViewportAssetSet.Empty,
            CancellationToken.None);
        var output = await RekallAgePngReader.ReadRgbaAsync(capture.ScreenshotPath, CancellationToken.None);

        Assert.True(capture.NonBlank);
        Assert.Equal(0, capture.FallbackRenderableCount);
        Assert.Equal(5, frame.Renderables.Count(renderable => renderable.Kind == "mesh"));
        Assert.Contains(frame.Renderables, renderable => renderable.Variant == "rekall.geometry.sphere" && renderable.MaterialColor == "#33ff66");
        Assert.Contains(frame.Renderables, renderable => renderable.Variant == "rekall.geometry.cylinder" && renderable.MaterialColor == "#ffcc33");
        Assert.Contains(frame.Renderables, renderable => renderable.Variant == "rekall.geometry.cone" && renderable.MaterialColor == "#cc66ff");
        Assert.Contains(frame.Renderables, renderable => renderable.Variant == "rekall.geometry.plane" && renderable.MaterialColor == "#33ddff");
        Assert.Contains(Enumerable.Range(0, output.Rgba.Length / 4), pixel =>
        {
            var index = pixel * 4;
            return output.Rgba[index] > 150 && output.Rgba[index + 1] < 120 && output.Rgba[index + 2] < 140;
        });
        Assert.Contains(Enumerable.Range(0, output.Rgba.Length / 4), pixel =>
        {
            var index = pixel * 4;
            return output.Rgba[index + 1] > 150 && output.Rgba[index] < 120 && output.Rgba[index + 2] < 140;
        });
        Assert.Contains(Enumerable.Range(0, output.Rgba.Length / 4), pixel =>
        {
            var index = pixel * 4;
            return output.Rgba[index + 2] > 150 && output.Rgba[index] > 100 && output.Rgba[index + 1] < 130;
        });
    }

    [Fact]
    public async Task SoftwareRendererRasterizesAuthoredGeometryMeshWithMaterialColor()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("MainCamera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject { ["active"] = true })))
            .AddEntity(RekallAgeEntityDocument.Create("Triangle", ["geometry", "mesh"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["scaleX"] = 2.4, ["scaleY"] = 2.4, ["scaleZ"] = 2.4 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.GeometryMesh",
                    new JsonObject
                    {
                        ["color"] = "#ff6633",
                        ["vertices"] = new JsonArray
                        {
                            new JsonObject { ["x"] = -0.5, ["y"] = -0.4, ["z"] = 0, ["nx"] = 0, ["ny"] = 0, ["nz"] = 1 },
                            new JsonObject { ["x"] = 0.5, ["y"] = -0.4, ["z"] = 0, ["nx"] = 0, ["ny"] = 0, ["nz"] = 1 },
                            new JsonObject { ["x"] = 0, ["y"] = 0.6, ["z"] = 0, ["nx"] = 0, ["ny"] = 0, ["nz"] = 1 }
                        },
                        ["indices"] = new JsonArray { 0, 1, 2 }
                    }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.MeshRenderer",
                    new JsonObject { ["mesh"] = "rekall.geometry.mesh" })));
        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);
        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 180, 120, debugOverlay: false);

        var capture = await new RekallAgeRuntimeSoftwareRenderer().CaptureAsync(
            frame,
            Path.Combine(root, "captures"),
            "Main_runtime_000.png",
            RekallAgeRuntimeViewportAssetSet.Empty,
            CancellationToken.None);
        var output = await RekallAgePngReader.ReadRgbaAsync(capture.ScreenshotPath, CancellationToken.None);

        Assert.True(capture.NonBlank);
        Assert.Equal(0, capture.FallbackRenderableCount);
        Assert.Contains(frame.Renderables, renderable => renderable.GeometryMesh is not null);
        Assert.Contains(Enumerable.Range(0, output.Rgba.Length / 4), pixel =>
        {
            var index = pixel * 4;
            return output.Rgba[index] > 180 && output.Rgba[index + 1] > 60 && output.Rgba[index + 1] < 150 && output.Rgba[index + 2] < 90;
        });
    }

    [Fact]
    public async Task SoftwareRendererRasterizesViewportLineSegmentsWithoutFallback()
    {
        var root = TestPaths.CreateTempDirectory();
        var frame = new RekallAgeRuntimeViewportFrame(
            "Main",
            0,
            0,
            160,
            100,
            new RekallAgeRuntimeViewportCamera("camera", "Camera", "camera", true, ClearColor: "#080a0e"),
            [],
            [
                new RekallAgeRuntimeViewportRenderable(
                    "debug-lines",
                    "Debug Lines",
                    "mesh",
                    null,
                    0,
                    0,
                    0,
                    900,
                    Variant: "rekall.debug.lines",
                    MaterialColor: "#33ddff",
                    LineSegments: new RekallAgeRuntimeViewportLineSegments(
                    [
                        new RekallAgeRuntimeViewportLineSegment(-1, 0, 0, 1, 0, 0),
                        new RekallAgeRuntimeViewportLineSegment(0, -1, 0, 0, 1, 0)
                    ],
                    0.05))
            ],
            0,
            new RekallAgeRuntimeViewportOverlay(false, 0),
            []);

        var capture = await new RekallAgeRuntimeSoftwareRenderer().CaptureAsync(
            frame,
            Path.Combine(root, "captures"),
            "Main_runtime_000.png",
            RekallAgeRuntimeViewportAssetSet.Empty,
            CancellationToken.None);
        var output = await RekallAgePngReader.ReadRgbaAsync(capture.ScreenshotPath, CancellationToken.None);

        Assert.True(capture.NonBlank);
        Assert.Equal(0, capture.FallbackRenderableCount);
        Assert.Contains(Enumerable.Range(0, output.Rgba.Length / 4), pixel =>
        {
            var index = pixel * 4;
            return output.Rgba[index] < 90 && output.Rgba[index + 1] > 170 && output.Rgba[index + 2] > 200;
        });
    }

    private static RekallAgeEntityDocument Primitive(string name, string primitive, double x, string color)
    {
        return RekallAgeEntityDocument.Create(name, ["geometry", "primitive", primitive])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Transform3D",
                new JsonObject { ["x"] = x, ["yaw"] = x * 8, ["pitch"] = 12, ["scaleX"] = 1.4, ["scaleY"] = 1.4, ["scaleZ"] = 1.4 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.GeometryPrimitive",
                new JsonObject { ["primitive"] = primitive, ["color"] = color }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.MeshRenderer",
                new JsonObject { ["mesh"] = $"rekall.geometry.{primitive}" }));
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

    private static byte[] CreateKtx2Texture(uint vkFormat, uint width, uint height, byte[] mipBytes)
    {
        var header = CreateKtx2Header(vkFormat, width, height, 1, 0);
        var bytes = new byte[80 + 24 + mipBytes.Length];
        Array.Copy(header, bytes, header.Length);
        WriteUInt64(bytes, 80, 104);
        WriteUInt64(bytes, 88, (ulong)mipBytes.Length);
        WriteUInt64(bytes, 96, (ulong)mipBytes.Length);
        Array.Copy(mipBytes, 0, bytes, 104, mipBytes.Length);
        return bytes;
    }

    private static byte[] CreateZstdKtx2Texture(uint vkFormat, uint width, uint height, byte[] uncompressedMipBytes)
    {
        using var compressor = new Compressor();
        var compressed = new byte[Compressor.GetCompressBound(uncompressedMipBytes.Length)];
        var compressedLength = compressor.Wrap(uncompressedMipBytes, compressed, 0);
        var header = CreateKtx2Header(vkFormat, width, height, 1, 2);
        var bytes = new byte[80 + 24 + compressedLength];
        Array.Copy(header, bytes, header.Length);
        WriteUInt64(bytes, 80, 104);
        WriteUInt64(bytes, 88, (ulong)compressedLength);
        WriteUInt64(bytes, 96, (ulong)uncompressedMipBytes.Length);
        Array.Copy(compressed, 0, bytes, 104, compressedLength);
        return bytes;
    }

    private static byte[] CreateDdsTexture(uint width, uint height, uint mipLevels, string fourCc, byte[] mipBytes)
    {
        var bytes = new byte[128 + mipBytes.Length];
        bytes[0] = (byte)'D';
        bytes[1] = (byte)'D';
        bytes[2] = (byte)'S';
        bytes[3] = (byte)' ';
        WriteUInt32(bytes, 4, 124);
        WriteUInt32(bytes, 12, height);
        WriteUInt32(bytes, 16, width);
        WriteUInt32(bytes, 28, mipLevels);
        WriteUInt32(bytes, 76, 32);
        WriteUInt32(bytes, 80, 0x4);
        var fourCcBytes = System.Text.Encoding.ASCII.GetBytes(fourCc);
        Array.Copy(fourCcBytes, 0, bytes, 84, Math.Min(4, fourCcBytes.Length));
        Array.Copy(mipBytes, 0, bytes, 128, mipBytes.Length);
        return bytes;
    }

    private static Rekall.Age.Rendering.Abstractions.RekallAgeRuntimeViewportFrame CreateTexturedPlanetFrame(string textureAssetId)
    {
        return new Rekall.Age.Rendering.Abstractions.RekallAgeRuntimeViewportFrame(
            "Main",
            0,
            0,
            160,
            90,
            null,
            [],
            [
                new Rekall.Age.Rendering.Abstractions.RekallAgeRuntimeViewportRenderable(
                    "entity-planet",
                    "Planet",
                    "mesh",
                    "rekall.planet.surface",
                    0,
                    0,
                    0,
                    1,
                    Variant: "rekall.planet.surface",
                    TextureAssetId: textureAssetId)
            ],
            0,
            new Rekall.Age.Rendering.Abstractions.RekallAgeRuntimeViewportOverlay(false, 0),
            []);
    }

    private static void WriteUInt32(byte[] bytes, int offset, uint value)
    {
        bytes[offset] = (byte)(value & 0xff);
        bytes[offset + 1] = (byte)((value >> 8) & 0xff);
        bytes[offset + 2] = (byte)((value >> 16) & 0xff);
        bytes[offset + 3] = (byte)((value >> 24) & 0xff);
    }

    private static void WriteUInt64(byte[] bytes, int offset, ulong value)
    {
        for (var i = 0; i < 8; i++)
        {
            bytes[offset + i] = (byte)((value >> (i * 8)) & 0xff);
        }
    }
}
