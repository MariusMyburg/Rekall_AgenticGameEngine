using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Commands;

namespace Rekall.Age.Tests.Rendering;

public sealed class VulkanRenderPassCaptureTests
{
    [Fact]
    public async Task CaptureClearRenderPassCommandReturnsGpuPngArtifact()
    {
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("vulkan capture clear"), CancellationToken.None);
        var capture = new FakeVulkanRenderPassCapture(new RekallAgeVulkanRenderPassCaptureResult(
            Captured: true,
            OutputPath: Path.Combine(TestPaths.CreateTempDirectory(), "vulkan-clear.png"),
            LoaderName: "fake-vulkan",
            SelectedDevice: new RekallAgeVulkanSelectedDevice(
                "Fake RTX",
                "discrete-gpu",
                "1.4.0",
                new RekallAgeVulkanQueueFamilyInfo(0, ["graphics"], 8)),
            Width: 64,
            Height: 64,
            Format: "R8G8B8A8_UNorm",
            ClearColor: RekallAgeVulkanClearColor.Default,
            BytesRead: 16384,
            NonZeroBytes: 16384,
            FirstPixel: new RekallAgeVulkanReadbackPixel(20, 25, 36, 255),
            ByteChecksum: 1376256,
            Errors: []));

        var result = await new CaptureClearVulkanRenderPassCommand(capture).ExecuteAsync(
            new CaptureClearVulkanRenderPassRequest(64, 64, "R8G8B8A8_UNorm", "discrete-gpu", TestPaths.CreateTempDirectory()),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.True(result.Value.Captured);
        Assert.EndsWith(".png", result.Value.OutputPath, StringComparison.Ordinal);
        Assert.Equal(16384ul, result.Value.BytesRead);
        Assert.Equal(20, result.Value.FirstPixel.R);
        Assert.True(result.Value.ByteChecksum > 0);
    }

    [Fact]
    public async Task CaptureClearRenderPassCommandReportsFailureWithoutThrowing()
    {
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("vulkan capture clear failure"), CancellationToken.None);
        var capture = new FakeVulkanRenderPassCapture(new RekallAgeVulkanRenderPassCaptureResult(
            Captured: false,
            OutputPath: string.Empty,
            LoaderName: null,
            SelectedDevice: null,
            Width: 64,
            Height: 64,
            Format: "R8G8B8A8_UNorm",
            ClearColor: RekallAgeVulkanClearColor.Default,
            BytesRead: 0,
            NonZeroBytes: 0,
            FirstPixel: new RekallAgeVulkanReadbackPixel(0, 0, 0, 0),
            ByteChecksum: 0,
            Errors: ["Vulkan clear render pass readback failed."]));

        var result = await new CaptureClearVulkanRenderPassCommand(capture).ExecuteAsync(
            new CaptureClearVulkanRenderPassRequest(64, 64, "R8G8B8A8_UNorm", "discrete-gpu", TestPaths.CreateTempDirectory()),
            context);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Code == "REKALL_VULKAN_RENDER_PASS_CAPTURE_FAILED");
    }

    private sealed class FakeVulkanRenderPassCapture : IRekallAgeVulkanRenderPassCapture
    {
        private readonly RekallAgeVulkanRenderPassCaptureResult _result;

        public FakeVulkanRenderPassCapture(RekallAgeVulkanRenderPassCaptureResult result)
        {
            _result = result;
        }

        public ValueTask<RekallAgeVulkanRenderPassCaptureResult> CaptureClearRenderPassAsync(
            uint width,
            uint height,
            string format,
            string? preferredDeviceType,
            string outputDirectory,
            RekallAgeVulkanClearColor clearColor,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(_result);
        }
    }
}
