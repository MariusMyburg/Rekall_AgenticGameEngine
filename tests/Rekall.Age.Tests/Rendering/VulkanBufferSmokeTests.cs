using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Commands;

namespace Rekall.Age.Tests.Rendering;

public sealed class VulkanBufferSmokeTests
{
    [Fact]
    public void MemoryTypeSelectorFindsHostVisibleCoherentType()
    {
        var memoryTypes = new[]
        {
            new RekallAgeVulkanMemoryTypeInfo(0, ["device-local"]),
            new RekallAgeVulkanMemoryTypeInfo(1, ["host-visible", "host-coherent"]),
            new RekallAgeVulkanMemoryTypeInfo(2, ["host-visible"])
        };

        var selection = RekallAgeVulkanMemoryTypeSelector.Select(
            memoryTypes,
            memoryTypeBits: 0b110,
            requiredProperties: ["host-visible", "host-coherent"]);

        Assert.Equal(1u, selection);
    }

    [Fact]
    public async Task BufferSmokeCommandReturnsMappedBufferDetails()
    {
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("vulkan buffer"), CancellationToken.None);
        var smoke = new FakeVulkanBufferSmoke(new RekallAgeVulkanBufferSmokeResult(
            Created: true,
            LoaderName: "fake-vulkan",
            SelectedDevice: new RekallAgeVulkanSelectedDevice(
                "Fake RTX",
                "discrete-gpu",
                "1.4.0",
                new RekallAgeVulkanQueueFamilyInfo(0, ["graphics"], 8)),
            SizeBytes: 256,
            Usage: "vertex-buffer",
            MemoryTypeIndex: 1,
            MemoryProperties: ["host-visible", "host-coherent"],
            Bound: true,
            Mapped: true,
            BytesWritten: 16,
            Errors: []));

        var result = await new CreateMappedVulkanBufferCommand(smoke).ExecuteAsync(
            new CreateMappedVulkanBufferRequest(256, "vertex-buffer", "discrete-gpu"),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.True(result.Value.Created);
        Assert.True(result.Value.Bound);
        Assert.True(result.Value.Mapped);
        Assert.Equal(16, result.Value.BytesWritten);
        Assert.Equal(1u, result.Value.MemoryTypeIndex);
    }

    [Fact]
    public async Task BufferSmokeCommandReportsFailureWithoutThrowing()
    {
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("vulkan buffer failure"), CancellationToken.None);
        var smoke = new FakeVulkanBufferSmoke(new RekallAgeVulkanBufferSmokeResult(
            Created: false,
            LoaderName: null,
            SelectedDevice: null,
            SizeBytes: 256,
            Usage: "vertex-buffer",
            MemoryTypeIndex: null,
            MemoryProperties: [],
            Bound: false,
            Mapped: false,
            BytesWritten: 0,
            Errors: ["No compatible Vulkan memory type was found."]));

        var result = await new CreateMappedVulkanBufferCommand(smoke).ExecuteAsync(
            new CreateMappedVulkanBufferRequest(256, "vertex-buffer", "discrete-gpu"),
            context);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Code == "REKALL_VULKAN_BUFFER_CREATE_FAILED");
    }

    private sealed class FakeVulkanBufferSmoke : IRekallAgeVulkanBufferSmoke
    {
        private readonly RekallAgeVulkanBufferSmokeResult _result;

        public FakeVulkanBufferSmoke(RekallAgeVulkanBufferSmokeResult result)
        {
            _result = result;
        }

        public ValueTask<RekallAgeVulkanBufferSmokeResult> CreateMappedBufferAsync(
            ulong sizeBytes,
            string usage,
            string? preferredDeviceType,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(_result);
        }
    }
}
