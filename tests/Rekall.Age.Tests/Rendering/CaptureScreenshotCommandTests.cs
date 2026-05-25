using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.GameTemplates.Commands;
using Rekall.Age.Playback;
using Rekall.Age.Rendering;
using Rekall.Age.Runtime;
using Rekall.Age.Rendering.Commands;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Rendering;

public sealed class CaptureScreenshotCommandTests
{
    [Fact]
    public async Task CaptureScreenshotCommandWritesPngAndReturnsStructuredResult()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("capture"), CancellationToken.None);
        await new CreateGameFromTemplateCommand()
            .ExecuteAsync(new CreateGameFromTemplateRequest(root, "Puzzle Capture", "puzzle"), context);
        var command = new CaptureScreenshotCommand();

        var result = await command.ExecuteAsync(
            new CaptureScreenshotRequest(root, "Main", Path.Combine(root, "Shots")),
            context);

        Assert.True(result.Ok);
        Assert.True(result.Value.NonBlank);
        Assert.True(File.Exists(result.Value.ScreenshotPath));
        Assert.Contains("Main_preview.png", result.Value.ScreenshotPath);
        Assert.True(result.Value.VisibleRenderers >= 1);
        Assert.NotNull(result.Value.ActiveCamera);
    }

    [Fact]
    public async Task RuntimeSoftwareRendererWritesNonBlankFrame()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering2d"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera2D", new JsonObject { ["active"] = true })))
            .AddEntity(RekallAgeEntityDocument.Create("Player", ["player"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform2D", new JsonObject { ["x"] = 4, ["y"] = 8 }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.SpriteRenderer", new JsonObject { ["sprite"] = "asset_player" })));
        var world = new RekallAgeRuntimeWorldBuilder().Build(scene) with
        {
            FrameIndex = 2,
            ElapsedTime = TimeSpan.FromSeconds(2.0 / 60.0)
        };
        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 320, 180, debugOverlay: true);

        var capture = await new RekallAgeRuntimeSoftwareRenderer()
            .CaptureAsync(frame, Path.Combine(root, "Viewport"), "Main_runtime_002.png", CancellationToken.None);

        Assert.True(capture.NonBlank);
        Assert.Equal(320, capture.Width);
        Assert.Equal(180, capture.Height);
        Assert.Equal(2, capture.FrameIndex);
        Assert.Equal("Camera", capture.ActiveCamera);
        Assert.True(File.Exists(capture.ScreenshotPath));
    }

    [Fact]
    public async Task CapturePlayableFrameCommandRasterizesModuleDrawCommands()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("play capture"), CancellationToken.None);
        await new CreatePlayableGameFromTemplateCommand()
            .ExecuteAsync(new CreatePlayableGameFromTemplateRequest(root, "Captured Pong", "pong"), context);
        var command = new CapturePlayableFrameCommand();

        var result = await command.ExecuteAsync(
            new CapturePlayableFrameRequest(
                root,
                "Main",
                Path.Combine(root, "PlayCaptures"),
                1,
                320,
                180,
                [
                    new RekallAgePlaybackInput(1, PrimaryAction: true)
                ]),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.True(result.Value.Captured);
        Assert.True(result.Value.NonBlank);
        Assert.True(File.Exists(result.Value.OutputPath));
        Assert.EndsWith("Main_play_frame_001.png", result.Value.OutputPath, StringComparison.Ordinal);
        Assert.Equal(320, result.Value.Width);
        Assert.Equal(180, result.Value.Height);
        Assert.Equal(1, result.Value.FrameIndex);
        Assert.Equal("pong", result.Value.Kind);
        Assert.Equal(5, result.Value.DrawCommandCount);
        Assert.Equal(["clear", "rect", "rect", "circle", "text"], result.Value.DrawCommandKinds);
        Assert.True(result.Value.NonBackgroundPixels > 0);
        Assert.Contains("Score 10", result.Value.Text, StringComparison.Ordinal);
        Assert.Contains(result.Value.OutputPath, context.Transaction.ChangedResources);
    }
}
