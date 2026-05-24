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
}
