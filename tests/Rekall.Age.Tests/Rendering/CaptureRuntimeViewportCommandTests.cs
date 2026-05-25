using System.Text.Json.Nodes;
using Rekall.Age.Assets.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Commands;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Rendering;

public sealed class CaptureRuntimeViewportCommandTests
{
    [Fact]
    public async Task CaptureRuntimeViewportCommandWritesFrameFromRuntimeSnapshot()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering2d"])
            .AddEntity(RekallAgeEntityDocument.Create("MainCamera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera2D", new JsonObject { ["active"] = true })))
            .AddEntity(RekallAgeEntityDocument.Create("Player", ["player"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform2D", new JsonObject { ["x"] = 12, ["y"] = 18 }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.SpriteRenderer", new JsonObject { ["sprite"] = "asset_player" })));
        await new RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None);
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("runtime viewport"), CancellationToken.None);
        var outputDirectory = Path.Combine(root, "RuntimeViewport");

        var result = await new CaptureRuntimeViewportCommand().ExecuteAsync(
            new CaptureRuntimeViewportRequest(root, "Main", 3, outputDirectory, 320, 180, true),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.True(result.Value.Captured);
        Assert.True(result.Value.NonBlank);
        Assert.Equal(320, result.Value.Width);
        Assert.Equal(180, result.Value.Height);
        Assert.Equal("software", result.Value.BackendId);
        Assert.False(result.Value.HardwareAccelerated);
        Assert.Equal("software-rasterized", result.Value.AccelerationStatus);
        Assert.Equal(3, result.Value.FrameIndex);
        Assert.Equal("MainCamera", result.Value.ActiveCamera);
        Assert.Equal(1, result.Value.RenderableCount);
        Assert.Equal(["sprite"], result.Value.RenderableKinds);
        Assert.Equal(0, result.Value.ObservationCount);
        Assert.EndsWith("Main_runtime_003.png", result.Value.ScreenshotPath, StringComparison.Ordinal);
        Assert.True(File.Exists(result.Value.ScreenshotPath));
        Assert.Contains(result.Value.ScreenshotPath, context.Transaction.ChangedResources);
    }

    [Fact]
    public async Task CaptureRuntimeViewportCommandRejectsInvalidCaptureSettings()
    {
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("runtime viewport"), CancellationToken.None);

        var result = await new CaptureRuntimeViewportCommand().ExecuteAsync(
            new CaptureRuntimeViewportRequest("missing", "Main", -1, "out", 0, 180, true),
            context);

        Assert.False(result.Ok);
        Assert.False(result.Value.Captured);
        Assert.Contains(result.Errors, error => error.Code == "REKALL_RUNTIME_VIEWPORT_INVALID_REQUEST");
    }

    [Fact]
    public async Task CaptureRuntimeViewportCommandUsesActiveCameraClearColor()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject
                {
                    ["active"] = true,
                    ["clearColor"] = "#112233"
                })));
        await new RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None);

        var result = await new CaptureRuntimeViewportCommand().ExecuteAsync(
            new CaptureRuntimeViewportRequest(root, "Main", 0, Path.Combine(root, "Viewport"), 16, 12, false),
            new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("camera clear viewport"), CancellationToken.None));
        var output = await RekallAgePngReader.ReadRgbaAsync(result.Value.ScreenshotPath, CancellationToken.None);

        Assert.True(result.Ok, result.Summary);
        Assert.Equal(0x11, output.Rgba[0]);
        Assert.Equal(0x22, output.Rgba[1]);
        Assert.Equal(0x33, output.Rgba[2]);
        Assert.Equal(255, output.Rgba[3]);
        Assert.True(result.Value.FrameAnalysis.Analyzed);
        Assert.False(result.Value.FrameAnalysis.VisuallyInformative);
        Assert.Contains("REKALL_VIEWPORT_FLAT_COLOR", result.Value.FrameAnalysis.WarningCodes);
    }

    [Fact]
    public async Task CaptureRuntimeViewportCommandResolvesImportedSpritePng()
    {
        var root = TestPaths.CreateTempDirectory();
        var source = Path.Combine(root, "player.png");
        await RekallAgePngWriter.WriteRgbaAsync(
            source,
            2,
            2,
            [
                40, 220, 90, 255,
                40, 220, 90, 255,
                40, 220, 90, 255,
                40, 220, 90, 255
            ],
            CancellationToken.None);
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("asset viewport"), CancellationToken.None);
        var import = await new ImportAssetCommand().ExecuteAsync(
            new ImportAssetRequest(root, source, "sprite", "Player"),
            context);
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering2d"])
            .AddEntity(RekallAgeEntityDocument.Create("MainCamera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera2D", new JsonObject { ["active"] = true })))
            .AddEntity(RekallAgeEntityDocument.Create("Player", ["player"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform2D", new JsonObject { ["x"] = 1, ["y"] = 2 }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.SpriteRenderer", new JsonObject { ["sprite"] = import.Value.Asset.Id })));
        await new RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None);

        var result = await new CaptureRuntimeViewportCommand().ExecuteAsync(
            new CaptureRuntimeViewportRequest(root, "Main", 1, Path.Combine(root, "Viewport"), 160, 90, false),
            context);
        var output = await RekallAgePngReader.ReadRgbaAsync(result.Value.ScreenshotPath, CancellationToken.None);

        Assert.True(result.Ok, result.Summary);
        Assert.Equal(1, result.Value.AssetBackedRenderableCount);
        Assert.Equal(0, result.Value.FallbackRenderableCount);
        Assert.Contains(Enumerable.Range(0, output.Rgba.Length / 4), pixel =>
        {
            var index = pixel * 4;
            return output.Rgba[index] == 40 && output.Rgba[index + 1] == 220 && output.Rgba[index + 2] == 90;
        });
    }

    [Fact]
    public async Task CaptureRuntimeViewportCommandReportsMissingSpriteAssetsAsFallbacks()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering2d"])
            .AddEntity(RekallAgeEntityDocument.Create("MainCamera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera2D", new JsonObject { ["active"] = true })))
            .AddEntity(RekallAgeEntityDocument.Create("Player", ["player"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.SpriteRenderer", new JsonObject { ["sprite"] = "asset_missing" })));
        await new RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None);

        var result = await new CaptureRuntimeViewportCommand().ExecuteAsync(
            new CaptureRuntimeViewportRequest(root, "Main", 0, Path.Combine(root, "Viewport"), 160, 90, false),
            new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("missing asset viewport"), CancellationToken.None));

        Assert.True(result.Ok, result.Summary);
        Assert.True(result.Value.NonBlank);
        Assert.Equal(0, result.Value.AssetBackedRenderableCount);
        Assert.Equal(1, result.Value.FallbackRenderableCount);
        Assert.Equal(1, result.Value.MissingAssetCount);
        Assert.Contains("REKALL_RENDER_ASSET_MISSING", result.Value.AssetIssueCodes);
    }

    [Fact]
    public async Task CaptureRuntimeViewportCommandCanUseVulkanForClearOnlyRuntimeFrames()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("MainCamera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject
                {
                    ["active"] = true,
                    ["clearColor"] = "#336699"
                })));
        await new RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None);
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("vulkan viewport"), CancellationToken.None);
        var vulkan = new FakeVulkanViewportCapture();

        var result = await new CaptureRuntimeViewportCommand(vulkan).ExecuteAsync(
            new CaptureRuntimeViewportRequest(
                root,
                "Main",
                2,
                Path.Combine(root, "Viewport"),
                96,
                48,
                DebugOverlay: false,
                BackendId: "vulkan",
                PreferredDeviceType: "integrated-gpu"),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.True(result.Value.Captured);
        Assert.True(result.Value.NonBlank);
        Assert.Equal("vulkan", result.Value.BackendId);
        Assert.True(result.Value.HardwareAccelerated);
        Assert.Equal("vulkan-clear-pass", result.Value.AccelerationStatus);
        Assert.Equal("Fake GPU", result.Value.SelectedDeviceName);
        Assert.Equal(new RekallAgeVulkanClearColor(0x33 / 255f, 0x66 / 255f, 0x99 / 255f, 1), vulkan.ClearColor);
        Assert.Equal("integrated-gpu", vulkan.PreferredDeviceType);
        Assert.Equal(96u, vulkan.Width);
        Assert.Equal(48u, vulkan.Height);
        Assert.Contains(result.Value.ScreenshotPath, context.Transaction.ChangedResources);
    }

    [Fact]
    public async Task CaptureRuntimeViewportCommandRoutesVulkanSceneRenderablesToSceneCapture()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("MainCamera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject { ["active"] = true })))
            .AddEntity(RekallAgeEntityDocument.Create("Cube", ["prop"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.MeshRenderer", new JsonObject { ["mesh"] = "rekall.primitive.cube" })));
        await new RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None);

        var sceneCapture = new FakeVulkanSceneCapture();
        var result = await new CaptureRuntimeViewportCommand(new FakeVulkanViewportCapture(), sceneCapture).ExecuteAsync(
            new CaptureRuntimeViewportRequest(
                root,
                "Main",
                0,
                Path.Combine(root, "Viewport"),
                BackendId: "vulkan"),
            new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("vulkan scene viewport"), CancellationToken.None));

        Assert.True(result.Ok, result.Summary);
        Assert.True(result.Value.Captured);
        Assert.Equal("vulkan", result.Value.BackendId);
        Assert.True(result.Value.HardwareAccelerated);
        Assert.Equal("vulkan-scene-rendered", result.Value.AccelerationStatus);
        Assert.Equal("Fake Scene GPU", result.Value.SelectedDeviceName);
        Assert.Equal(1, sceneCapture.MeshRenderableCount);
        Assert.Equal(["mesh"], result.Value.RenderableKinds);
    }

    [Fact]
    public async Task CaptureRuntimeViewportCommandRendersAnimatedPrimitiveCubeWithDirectionalLight()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d", "animation"])
            .AddEntity(RekallAgeEntityDocument.Create("MainCamera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject { ["active"] = true })))
            .AddEntity(RekallAgeEntityDocument.Create("SlowSpinningCube", ["prop"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["scaleX"] = 2, ["scaleY"] = 2, ["scaleZ"] = 2 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.TransformAnimation",
                    new JsonObject { ["yawDegreesPerSecond"] = 45 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.MeshRenderer",
                    new JsonObject { ["mesh"] = "rekall.primitive.cube" })))
            .AddEntity(RekallAgeEntityDocument.Create("KeyDirectionalLight", ["light"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["pitch"] = -35, ["yaw"] = -45 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.DirectionalLight",
                    new JsonObject { ["intensity"] = 1.0 })));
        await new RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None);

        var result = await new CaptureRuntimeViewportCommand().ExecuteAsync(
            new CaptureRuntimeViewportRequest(root, "Main", 60, Path.Combine(root, "Viewport"), 180, 120, false),
            new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("animated cube viewport"), CancellationToken.None));
        var output = await RekallAgePngReader.ReadRgbaAsync(result.Value.ScreenshotPath, CancellationToken.None);

        Assert.True(result.Ok, result.Summary);
        Assert.True(result.Value.NonBlank);
        Assert.Equal(60, result.Value.FrameIndex);
        Assert.Equal(2, result.Value.RenderableCount);
        Assert.Equal(["light", "mesh"], result.Value.RenderableKinds);
        Assert.Equal(0, result.Value.FallbackRenderableCount);
        Assert.Contains(Enumerable.Range(0, output.Rgba.Length / 4), pixel =>
        {
            var index = pixel * 4;
            return output.Rgba[index] >= 45
                && output.Rgba[index + 1] >= 70
                && output.Rgba[index + 2] >= 100;
        });
    }

    private sealed class FakeVulkanViewportCapture : IRekallAgeVulkanRenderPassCapture
    {
        public uint Width { get; private set; }

        public uint Height { get; private set; }

        public string? PreferredDeviceType { get; private set; }

        public RekallAgeVulkanClearColor ClearColor { get; private set; }

        public async ValueTask<RekallAgeVulkanRenderPassCaptureResult> CaptureClearRenderPassAsync(
            uint width,
            uint height,
            string format,
            string? preferredDeviceType,
            string outputDirectory,
            RekallAgeVulkanClearColor clearColor,
            CancellationToken cancellationToken)
        {
            Width = width;
            Height = height;
            PreferredDeviceType = preferredDeviceType;
            ClearColor = clearColor;
            Directory.CreateDirectory(outputDirectory);
            var outputPath = Path.Combine(outputDirectory, "fake_vulkan_viewport.png");
            await RekallAgePngWriter.WriteRgbaAsync(
                outputPath,
                checked((int)width),
                checked((int)height),
                Enumerable.Repeat<byte>(128, checked((int)(width * height * 4))).ToArray(),
                cancellationToken);
            return new RekallAgeVulkanRenderPassCaptureResult(
                true,
                outputPath,
                "fake-vulkan",
                new RekallAgeVulkanSelectedDevice(
                    "Fake GPU",
                    "integrated-gpu",
                    "1.3.0",
                    new RekallAgeVulkanQueueFamilyInfo(0, ["graphics"], 1)),
                width,
                height,
                format,
                clearColor,
                width * height * 4,
                width * height * 4,
                new RekallAgeVulkanReadbackPixel(128, 128, 128, 128),
                128,
                []);
        }
    }

    private sealed class FakeVulkanSceneCapture : IRekallAgeVulkanSceneCapture
    {
        public int MeshRenderableCount { get; private set; }

        public async ValueTask<RekallAgeVulkanSceneCaptureResult> CaptureSceneAsync(
            Rekall.Age.Rendering.Abstractions.RekallAgeRuntimeViewportFrame frame,
            RekallAgeRuntimeViewportAssetSet assets,
            string outputDirectory,
            string? preferredDeviceType,
            CancellationToken cancellationToken)
        {
            MeshRenderableCount = frame.Renderables.Count(renderable => renderable.Kind.Equals("mesh", StringComparison.Ordinal));
            Directory.CreateDirectory(outputDirectory);
            var outputPath = Path.Combine(outputDirectory, "fake_vulkan_scene.png");
            await RekallAgePngWriter.WriteRgbaAsync(
                outputPath,
                frame.Width,
                frame.Height,
                Enumerable.Repeat<byte>(128, frame.Width * frame.Height * 4).ToArray(),
                cancellationToken);
            return new RekallAgeVulkanSceneCaptureResult(
                true,
                outputPath,
                "fake-vulkan",
                new RekallAgeVulkanSelectedDevice(
                    "Fake Scene GPU",
                    "discrete-gpu",
                    "1.3.0",
                    new RekallAgeVulkanQueueFamilyInfo(0, ["graphics"], 1)),
                checked((uint)frame.Width),
                checked((uint)frame.Height),
                "R8G8B8A8_UNorm",
                (ulong)(frame.Width * frame.Height * 4),
                (ulong)(frame.Width * frame.Height * 4),
                new RekallAgeVulkanReadbackPixel(128, 128, 128, 255),
                128,
                DrawCallCount: MeshRenderableCount,
                MeshCount: MeshRenderableCount,
                SpriteCount: 0,
                UnsupportedRenderableCount: 0,
                UnsupportedRenderableKinds: [],
                ColorTargetCreated: true,
                DepthTargetCreated: true,
                RenderPassCreated: true,
                FramebufferCreated: true,
                VertexBufferCreated: true,
                IndexBufferCreated: true,
                UniformBufferCreated: true,
                DescriptorSetLayoutCreated: true,
                PipelineLayoutCreated: true,
                GraphicsPipelineCreated: true,
                TextureResourcesCreated: false,
                Errors: []);
        }
    }
}
