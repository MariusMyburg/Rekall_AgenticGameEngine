using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;

namespace Rekall.Age.Tests.Rendering;

public sealed class RenderBackendCatalogTests
{
    [Fact]
    public void CatalogIsVulkanFirstAndExtensible()
    {
        var catalog = RekallAgeRenderBackendCatalog.CreateDefault();

        Assert.Equal("vulkan", catalog.Backends[0].Id);
        Assert.Contains(catalog.Backends, backend => backend.Id == "direct3d12");
        Assert.All(catalog.Backends, backend => Assert.NotEmpty(backend.AgentExposedCapabilities));
    }

    [Fact]
    public async Task ListRenderBackendsCommandExposesLowLevelCapabilitiesToAgents()
    {
        var command = new ListRenderBackendsCommand();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("render backends"), CancellationToken.None);

        var result = await command.ExecuteAsync(new ListRenderBackendsRequest(), context);

        Assert.True(result.Ok, result.Summary);
        Assert.Equal("vulkan", result.Value.Backends[0].Id);
        Assert.Contains(result.Value.Backends[0].AgentExposedCapabilities, capability => capability == "command-buffers");
        Assert.Contains(result.Value.Backends[0].AgentExposedCapabilities, capability => capability == "render-pass-submit-clear");
        Assert.Contains(result.Value.Backends[0].AgentExposedCapabilities, capability => capability == "render-pass-read-clear");
        Assert.Contains(result.Value.Backends[0].AgentExposedCapabilities, capability => capability == "render-pass-capture-clear");
        Assert.Contains(result.Value.Backends[0].AgentExposedCapabilities, capability => capability == "single-pass-multiview-ready");
        Assert.Contains(result.Value.Backends[0].AgentExposedCapabilities, capability => capability == "openxr-primary-stereo-metadata");
        Assert.Contains(result.Value.Backends[0].AgentExposedCapabilities, capability => capability == "openxr-runtime-probe");
        Assert.Contains(result.Value.Backends[0].AgentExposedCapabilities, capability => capability == "openxr-session-bootstrap");
        Assert.Contains(result.Value.Backends[0].AgentExposedCapabilities, capability => capability == "openxr-headset-frame-plan");
        Assert.Contains(result.Value.Backends[0].AgentExposedCapabilities, capability => capability == "openxr-vulkan-enable2-readiness");
        Assert.Contains(result.Value.Backends[0].AgentExposedCapabilities, capability => capability == "openxr-vulkan-graphics-requirements");
        Assert.Contains(result.Value.Backends[0].AgentExposedCapabilities, capability => capability == "openxr-vulkan-graphics-binding-interop");
        Assert.Contains(result.Value.Backends[0].AgentExposedCapabilities, capability => capability == "openxr-compositor-session-bootstrap");
        Assert.Contains(result.Value.Backends[0].AgentExposedCapabilities, capability => capability == "openxr-swapchain-format-enumeration");
        Assert.Contains(result.Value.Backends[0].AgentExposedCapabilities, capability => capability == "openxr-color-depth-swapchain-allocation");
        Assert.Contains(result.Value.Backends[0].AgentExposedCapabilities, capability => capability == "openxr-vulkan-swapchain-image-enumeration");
        Assert.Contains(result.Value.Backends[0].AgentExposedCapabilities, capability => capability == "openxr-frame-loop-probe");
        Assert.Contains(result.Value.Backends[0].AgentExposedCapabilities, capability => capability == "openxr-swapchain-image-acquire-release");
        Assert.Contains(result.Value.Backends[0].AgentExposedCapabilities, capability => capability == "openxr-zero-layer-frame-submit");
        Assert.Contains(result.Value.Backends[0].AgentExposedCapabilities, capability => capability == "openxr-primary-stereo-view-enumeration");
        Assert.Contains(result.Value.Backends[0].AgentExposedCapabilities, capability => capability == "openxr-external-vkimage-swapchain-wrapping");
    }
}
