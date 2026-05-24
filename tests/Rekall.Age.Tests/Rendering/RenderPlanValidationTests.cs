using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Commands;

namespace Rekall.Age.Tests.Rendering;

public sealed class RenderPlanValidationTests
{
    [Fact]
    public async Task ValidateRenderPlanReportsMissingCommandTargets()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("validate render"), CancellationToken.None);
        await new CreateRenderPlanCommand().ExecuteAsync(new CreateRenderPlanRequest(root, "vulkan", "MainFrame"), context);
        await new RecordRenderCommandBufferCommand().ExecuteAsync(
            new RecordRenderCommandBufferRequest(
                root,
                "main",
                "graphics",
                [
                    new RekallAgeRenderCommand("begin-render-pass", "main-pass", new Dictionary<string, string> { ["target"] = "swapchain-color" })
                ]),
            context);

        var result = await new ValidateRenderPlanCommand().ExecuteAsync(new ValidateRenderPlanRequest(root), context);

        Assert.False(result.Value.Valid);
        Assert.Contains(result.Value.Issues, issue => issue.Code == "REKALL_RENDER_RESOURCE_MISSING");
    }

    [Fact]
    public async Task ValidateRenderPlanAcceptsCommandTargetsDefinedAsResources()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("validate render ok"), CancellationToken.None);
        await new CreateRenderPlanCommand().ExecuteAsync(new CreateRenderPlanRequest(root, "vulkan", "MainFrame"), context);
        await new AddRenderResourceCommand().ExecuteAsync(
            new AddRenderResourceRequest(root, "swapchain-color", "image", "B8G8R8A8_UNorm", ["color-attachment", "present"]),
            context);
        await new RecordRenderCommandBufferCommand().ExecuteAsync(
            new RecordRenderCommandBufferRequest(
                root,
                "main",
                "graphics",
                [
                    new RekallAgeRenderCommand("begin-render-pass", "main-pass", new Dictionary<string, string> { ["target"] = "swapchain-color" })
                ]),
            context);

        var result = await new ValidateRenderPlanCommand().ExecuteAsync(new ValidateRenderPlanRequest(root), context);

        Assert.True(result.Value.Valid);
        Assert.Empty(result.Value.Issues);
    }
}
