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
    }
}
