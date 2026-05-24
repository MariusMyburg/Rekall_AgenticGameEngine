using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Commands;

namespace Rekall.Age.Tests.Rendering;

public sealed class VulkanRenderPassReadbackTests
{
    [Fact]
    public async Task RenderPassReadClearCommandReturnsGpuReadbackDetails()
    {
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("vulkan render pass readback"), CancellationToken.None);
        var readback = new FakeVulkanRenderPassReadback(new RekallAgeVulkanRenderPassReadbackResult(
            Readback: true,
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
            Submitted: true,
            BufferCreated: true,
            BufferBound: true,
            BufferMapped: true,
            BytesRead: 16384,
            NonZeroBytes: 16384,
            FirstPixel: new RekallAgeVulkanReadbackPixel(20, 26, 36, 255),
            ByteChecksum: 1380352,
            Errors: []));

        var result = await new ReadClearVulkanRenderPassCommand(readback).ExecuteAsync(
            new ReadClearVulkanRenderPassRequest(64, 64, "R8G8B8A8_UNorm", "discrete-gpu"),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.True(result.Value.Readback);
        Assert.True(result.Value.Submitted);
        Assert.True(result.Value.BufferCreated);
        Assert.True(result.Value.BufferBound);
        Assert.True(result.Value.BufferMapped);
        Assert.Equal(16384ul, result.Value.BytesRead);
        Assert.Equal(20, result.Value.FirstPixel.R);
        Assert.Equal(255, result.Value.FirstPixel.A);
        Assert.True(result.Value.ByteChecksum > 0);
    }

    [Fact]
    public async Task RenderPassReadClearCommandReportsFailureWithoutThrowing()
    {
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("vulkan render pass readback failure"), CancellationToken.None);
        var readback = new FakeVulkanRenderPassReadback(new RekallAgeVulkanRenderPassReadbackResult(
            Readback: false,
            LoaderName: null,
            SelectedDevice: null,
            Width: 64,
            Height: 64,
            Format: "R8G8B8A8_UNorm",
            ClearColor: RekallAgeVulkanClearColor.Default,
            Submitted: false,
            BufferCreated: true,
            BufferBound: false,
            BufferMapped: false,
            BytesRead: 0,
            NonZeroBytes: 0,
            FirstPixel: new RekallAgeVulkanReadbackPixel(0, 0, 0, 0),
            ByteChecksum: 0,
            Errors: ["vkBindBufferMemory failed with VkResult -2."]));

        var result = await new ReadClearVulkanRenderPassCommand(readback).ExecuteAsync(
            new ReadClearVulkanRenderPassRequest(64, 64, "R8G8B8A8_UNorm", "discrete-gpu"),
            context);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Code == "REKALL_VULKAN_RENDER_PASS_READBACK_FAILED");
    }

    private sealed class FakeVulkanRenderPassReadback : IRekallAgeVulkanRenderPassReadback
    {
        private readonly RekallAgeVulkanRenderPassReadbackResult _result;

        public FakeVulkanRenderPassReadback(RekallAgeVulkanRenderPassReadbackResult result)
        {
            _result = result;
        }

        public ValueTask<RekallAgeVulkanRenderPassReadbackResult> ReadClearRenderPassAsync(
            uint width,
            uint height,
            string format,
            string? preferredDeviceType,
            RekallAgeVulkanClearColor clearColor,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(_result);
        }
    }
}
