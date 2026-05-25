using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Rendering.Commands;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Rendering;

public sealed class CaptureRuntimeViewportCommandTests
{
    [Fact]
    public async Task CaptureRuntimeViewportCommandWritesFrameFromRuntimeSnapshot()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering2d"])
            .AddEntity(RekallAgeEntityDocument.Create("MainCamera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera2D", new JsonObject { ["active"] = true })))
            .AddEntity(RekallAgeEntityDocument.Create("Player", ["player"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform2D", new JsonObject { ["x"] = 12, ["y"] = 18 }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.SpriteRenderer", new JsonObject { ["sprite"] = "asset_player" })));
        await new RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None);
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("runtime viewport"), CancellationToken.None);
        var outputDirectory = Path.Combine(root, "RuntimeViewport");

        var result = await new CaptureRuntimeViewportCommand().ExecuteAsync(
            new CaptureRuntimeViewportRequest(root, "Main", 3, outputDirectory, 320, 180, true),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.True(result.Value.Captured);
        Assert.True(result.Value.NonBlank);
        Assert.Equal(320, result.Value.Width);
        Assert.Equal(180, result.Value.Height);
        Assert.Equal(3, result.Value.FrameIndex);
        Assert.Equal("MainCamera", result.Value.ActiveCamera);
        Assert.Equal(1, result.Value.RenderableCount);
        Assert.Equal(["sprite"], result.Value.RenderableKinds);
        Assert.Equal(0, result.Value.ObservationCount);
        Assert.EndsWith("Main_runtime_003.png", result.Value.ScreenshotPath, StringComparison.Ordinal);
        Assert.True(File.Exists(result.Value.ScreenshotPath));
        Assert.Contains(result.Value.ScreenshotPath, context.Transaction.ChangedResources);
    }

    [Fact]
    public async Task CaptureRuntimeViewportCommandRejectsInvalidCaptureSettings()
    {
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("runtime viewport"), CancellationToken.None);

        var result = await new CaptureRuntimeViewportCommand().ExecuteAsync(
            new CaptureRuntimeViewportRequest("missing", "Main", -1, "out", 0, 180, true),
            context);

        Assert.False(result.Ok);
        Assert.False(result.Value.Captured);
        Assert.Contains(result.Errors, error => error.Code == "REKALL_RUNTIME_VIEWPORT_INVALID_REQUEST");
    }
}
