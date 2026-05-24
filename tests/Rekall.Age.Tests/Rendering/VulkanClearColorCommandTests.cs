using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Commands;

namespace Rekall.Age.Tests.Rendering;

public sealed class VulkanClearColorCommandTests
{
    [Fact]
    public async Task ReadClearRenderPassCommandPassesAgentClearColorToBackend()
    {
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("vulkan color readback"), CancellationToken.None);
        var backend = new RecordingReadbackBackend();
        var color = new RekallAgeVulkanClearColor(0.25f, 0.50f, 0.75f, 1.0f);

        var result = await new ReadClearVulkanRenderPassCommand(backend).ExecuteAsync(
            new ReadClearVulkanRenderPassRequest(32, 32, "R8G8B8A8_UNorm", "discrete-gpu", color),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.Equal(color, backend.ClearColor);
        Assert.Equal(color, result.Value.ClearColor);
    }

    [Fact]
    public async Task CaptureClearRenderPassCommandPassesAgentClearColorToBackend()
    {
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("vulkan color capture"), CancellationToken.None);
        var backend = new RecordingCaptureBackend();
        var color = new RekallAgeVulkanClearColor(0.10f, 0.20f, 0.30f, 1.0f);

        var result = await new CaptureClearVulkanRenderPassCommand(backend).ExecuteAsync(
            new CaptureClearVulkanRenderPassRequest(32, 32, "R8G8B8A8_UNorm", "discrete-gpu", TestPaths.CreateTempDirectory(), color),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.Equal(color, backend.ClearColor);
        Assert.Equal(color, result.Value.ClearColor);
    }

    private sealed class RecordingReadbackBackend : IRekallAgeVulkanRenderPassReadback
    {
        public RekallAgeVulkanClearColor ClearColor { get; private set; }

        public ValueTask<RekallAgeVulkanRenderPassReadbackResult> ReadClearRenderPassAsync(
            uint width,
            uint height,
            string format,
            string? preferredDeviceType,
            RekallAgeVulkanClearColor clearColor,
            CancellationToken cancellationToken)
        {
            ClearColor = clearColor;
            return ValueTask.FromResult(new RekallAgeVulkanRenderPassReadbackResult(
                Readback: true,
                LoaderName: "fake-vulkan",
                SelectedDevice: new RekallAgeVulkanSelectedDevice(
                    "Fake RTX",
                    "discrete-gpu",
                    "1.4.0",
                    new RekallAgeVulkanQueueFamilyInfo(0, ["graphics"], 8)),
                Width: width,
                Height: height,
                Format: format,
                ClearColor: clearColor,
                Submitted: true,
                BufferCreated: true,
                BufferBound: true,
                BufferMapped: true,
                BytesRead: 4,
                NonZeroBytes: 4,
                FirstPixel: new RekallAgeVulkanReadbackPixel(64, 128, 191, 255),
                ByteChecksum: 638,
                Errors: []));
        }
    }

    private sealed class RecordingCaptureBackend : IRekallAgeVulkanRenderPassCapture
    {
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
            ClearColor = clearColor;
            return ValueTask.FromResult(new RekallAgeVulkanRenderPassCaptureResult(
                Captured: true,
                OutputPath: Path.Combine(outputDirectory, "vulkan-clear.png"),
                LoaderName: "fake-vulkan",
                SelectedDevice: new RekallAgeVulkanSelectedDevice(
                    "Fake RTX",
                    "discrete-gpu",
                    "1.4.0",
                    new RekallAgeVulkanQueueFamilyInfo(0, ["graphics"], 8)),
                Width: width,
                Height: height,
                Format: format,
                ClearColor: clearColor,
                BytesRead: 4,
                NonZeroBytes: 4,
                FirstPixel: new RekallAgeVulkanReadbackPixel(26, 51, 77, 255),
                ByteChecksum: 409,
                Errors: []));
        }
    }
}
