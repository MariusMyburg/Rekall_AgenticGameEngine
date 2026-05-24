using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Commands;

namespace Rekall.Age.Tests.Rendering;

public sealed class RenderPlanCommandTests
{
    [Fact]
    public async Task CreateRenderPlanPersistsVulkanPlan()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("render plan"), CancellationToken.None);

        var result = await new CreateRenderPlanCommand().ExecuteAsync(
            new CreateRenderPlanRequest(root, "vulkan", "MainFrame"),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.Equal("vulkan", result.Value.Plan.BackendId);
        Assert.Equal("MainFrame", result.Value.Plan.Name);
        Assert.True(File.Exists(new RekallAgeRenderPlanStore().GetPlanPath(root)));
    }

    [Fact]
    public async Task AddRenderResourceStoresLowLevelResourceDescriptor()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("resource"), CancellationToken.None);
        await new CreateRenderPlanCommand().ExecuteAsync(new CreateRenderPlanRequest(root, "vulkan", "MainFrame"), context);

        var result = await new AddRenderResourceCommand().ExecuteAsync(
            new AddRenderResourceRequest(
                root,
                "swapchain-color",
                "image",
                "B8G8R8A8_UNorm",
                ["color-attachment", "present"]),
            context);

        Assert.True(result.Ok, result.Summary);
        var resource = Assert.Single(result.Value.Plan.Resources);
        Assert.Equal("swapchain-color", resource.Id);
        Assert.Contains("present", resource.Usage);
    }

    [Fact]
    public async Task RecordCommandBufferStoresVulkanStyleCommandsInOrder()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("command buffer"), CancellationToken.None);
        await new CreateRenderPlanCommand().ExecuteAsync(new CreateRenderPlanRequest(root, "vulkan", "MainFrame"), context);

        var result = await new RecordRenderCommandBufferCommand().ExecuteAsync(
            new RecordRenderCommandBufferRequest(
                root,
                "main-command-buffer",
                "graphics",
                [
                    new RekallAgeRenderCommand("begin-render-pass", "main-pass", new Dictionary<string, string> { ["target"] = "swapchain-color" }),
                    new RekallAgeRenderCommand("bind-pipeline", "sprite-pipeline", new Dictionary<string, string> { ["bindPoint"] = "graphics" }),
                    new RekallAgeRenderCommand("draw", "quad", new Dictionary<string, string> { ["vertexCount"] = "6" }),
                    new RekallAgeRenderCommand("end-render-pass", "main-pass", new Dictionary<string, string>())
                ]),
            context);

        Assert.True(result.Ok, result.Summary);
        var commandBuffer = Assert.Single(result.Value.Plan.CommandBuffers);
        Assert.Equal("main-command-buffer", commandBuffer.Id);
        Assert.Equal("graphics", commandBuffer.Queue);
        Assert.Collection(
            commandBuffer.Commands,
            command => Assert.Equal("begin-render-pass", command.Op),
            command => Assert.Equal("bind-pipeline", command.Op),
            command => Assert.Equal("draw", command.Op),
            command => Assert.Equal("end-render-pass", command.Op));
    }

    [Fact]
    public async Task InspectRenderPlanReturnsAgentReadablePlan()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("inspect render"), CancellationToken.None);
        await new CreateRenderPlanCommand().ExecuteAsync(new CreateRenderPlanRequest(root, "vulkan", "MainFrame"), context);
        await new AddRenderResourceCommand().ExecuteAsync(
            new AddRenderResourceRequest(root, "vertex-buffer", "buffer", "R32G32B32_SFloat", ["vertex-buffer"]),
            context);

        var result = await new InspectRenderPlanCommand().ExecuteAsync(new InspectRenderPlanRequest(root), context);

        Assert.True(result.Ok, result.Summary);
        Assert.Equal("vulkan", result.Value.Plan.BackendId);
        Assert.Single(result.Value.Plan.Resources);
    }
}
