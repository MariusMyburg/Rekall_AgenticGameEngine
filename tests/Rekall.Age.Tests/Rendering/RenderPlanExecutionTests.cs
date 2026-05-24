using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Commands;

namespace Rekall.Age.Tests.Rendering;

public sealed class RenderPlanExecutionTests
{
    [Fact]
    public async Task ExecuteRenderPlanWritesDeterministicPreviewPng()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("execute render"), CancellationToken.None);
        await new CreateRenderPlanCommand().ExecuteAsync(new CreateRenderPlanRequest(root, "software", "Preview"), context);
        await new AddRenderResourceCommand().ExecuteAsync(
            new AddRenderResourceRequest(root, "preview-color", "image", "R8G8B8A8_UNorm", ["color-attachment"]),
            context);
        await new RecordRenderCommandBufferCommand().ExecuteAsync(
            new RecordRenderCommandBufferRequest(
                root,
                "main",
                "graphics",
                [
                    new RekallAgeRenderCommand("begin-render-pass", "preview", new Dictionary<string, string> { ["target"] = "preview-color" }),
                    new RekallAgeRenderCommand("draw-rect", "player", new Dictionary<string, string>
                    {
                        ["x"] = "8",
                        ["y"] = "8",
                        ["width"] = "24",
                        ["height"] = "16",
                        ["color"] = "#ffcc33"
                    }),
                    new RekallAgeRenderCommand("end-render-pass", "preview", new Dictionary<string, string>())
                ]),
            context);

        var result = await new ExecuteRenderPlanCommand().ExecuteAsync(
            new ExecuteRenderPlanRequest(root, Path.Combine(root, "Artifacts", "Render")),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.True(File.Exists(result.Value.OutputPath), result.Value.OutputPath);
        Assert.True(result.Value.NonBlank);
        Assert.Equal(160, result.Value.Width);
        Assert.Equal(90, result.Value.Height);
    }

    [Fact]
    public async Task ExecuteVulkanRenderPlanCapturesClearPassWithAgentColor()
    {
        var root = TestPaths.CreateTempDirectory();
        var outputDirectory = Path.Combine(root, "Artifacts", "Render");
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("execute vulkan render"), CancellationToken.None);
        var capture = new FakeVulkanRenderPassCapture();
        await new CreateRenderPlanCommand().ExecuteAsync(new CreateRenderPlanRequest(root, "vulkan", "NativePreview"), context);
        await new AddRenderResourceCommand().ExecuteAsync(
            new AddRenderResourceRequest(root, "frame-color", "image", "R8G8B8A8_UNorm", ["color-attachment", "transfer-src"]),
            context);
        await new RecordRenderCommandBufferCommand().ExecuteAsync(
            new RecordRenderCommandBufferRequest(
                root,
                "main",
                "graphics",
                [
                    new RekallAgeRenderCommand("begin-render-pass", "frame", new Dictionary<string, string>
                    {
                        ["target"] = "frame-color",
                        ["width"] = "32",
                        ["height"] = "16",
                        ["preferredDeviceType"] = "integrated-gpu"
                    }),
                    new RekallAgeRenderCommand("clear", "sky", new Dictionary<string, string>
                    {
                        ["r"] = "0.25",
                        ["g"] = "0.5",
                        ["b"] = "0.75",
                        ["a"] = "1"
                    }),
                    new RekallAgeRenderCommand("end-render-pass", "frame", new Dictionary<string, string>())
                ]),
            context);

        var result = await new ExecuteRenderPlanCommand(capture).ExecuteAsync(
            new ExecuteRenderPlanRequest(root, outputDirectory),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.True(result.Value.NonBlank);
        Assert.Equal(Path.Combine(outputDirectory, "vulkan-clear.png"), result.Value.OutputPath);
        Assert.Equal(32, result.Value.Width);
        Assert.Equal(16, result.Value.Height);
        Assert.Equal(32u, capture.Width);
        Assert.Equal(16u, capture.Height);
        Assert.Equal("R8G8B8A8_UNorm", capture.Format);
        Assert.Equal("integrated-gpu", capture.PreferredDeviceType);
        Assert.Equal(new RekallAgeVulkanClearColor(0.25f, 0.5f, 0.75f, 1f), capture.ClearColor);
    }

    [Fact]
    public async Task ExecuteVulkanRenderPlanRejectsUnsupportedDrawCommands()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("execute unsupported vulkan render"), CancellationToken.None);
        await new CreateRenderPlanCommand().ExecuteAsync(new CreateRenderPlanRequest(root, "vulkan", "NativePreview"), context);
        await new AddRenderResourceCommand().ExecuteAsync(
            new AddRenderResourceRequest(root, "frame-color", "image", "R8G8B8A8_UNorm", ["color-attachment", "transfer-src"]),
            context);
        await new RecordRenderCommandBufferCommand().ExecuteAsync(
            new RecordRenderCommandBufferRequest(
                root,
                "main",
                "graphics",
                [
                    new RekallAgeRenderCommand("begin-render-pass", "frame", new Dictionary<string, string> { ["target"] = "frame-color" }),
                    new RekallAgeRenderCommand("draw", "quad", new Dictionary<string, string> { ["vertexCount"] = "6" }),
                    new RekallAgeRenderCommand("end-render-pass", "frame", new Dictionary<string, string>())
                ]),
            context);

        var result = await new ExecuteRenderPlanCommand(new FakeVulkanRenderPassCapture()).ExecuteAsync(
            new ExecuteRenderPlanRequest(root, Path.Combine(root, "Artifacts", "Render")),
            context);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Code == "REKALL_RENDER_PLAN_EXECUTION_FAILED");
        Assert.Contains(result.Errors, error => error.Message.Contains("draw", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class FakeVulkanRenderPassCapture : IRekallAgeVulkanRenderPassCapture
    {
        public uint Width { get; private set; }

        public uint Height { get; private set; }

        public string Format { get; private set; } = string.Empty;

        public string? PreferredDeviceType { get; private set; }

        public RekallAgeVulkanClearColor ClearColor { get; private set; }

        public ValueTask<RekallAgeVulkanRenderPassCaptureResult> CaptureClearRenderPassAsync(
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
            Format = format;
            PreferredDeviceType = preferredDeviceType;
            ClearColor = clearColor;
            return ValueTask.FromResult(new RekallAgeVulkanRenderPassCaptureResult(
                Captured: true,
                OutputPath: Path.Combine(outputDirectory, "vulkan-clear.png"),
                LoaderName: "fake-vulkan",
                SelectedDevice: new RekallAgeVulkanSelectedDevice(
                    "Fake GPU",
                    preferredDeviceType ?? "discrete-gpu",
                    "1.4.0",
                    new RekallAgeVulkanQueueFamilyInfo(0, ["graphics"], 1)),
                Width: width,
                Height: height,
                Format: format,
                ClearColor: clearColor,
                BytesRead: width * height * 4,
                NonZeroBytes: width * height * 4,
                FirstPixel: new RekallAgeVulkanReadbackPixel(64, 127, 191, 255),
                ByteChecksum: 652288,
                Errors: []));
        }
    }
}
