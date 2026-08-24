using System.Text.Json.Nodes;
using Rekall.Age.Build.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modules.Commands;
using Rekall.Age.Playback;
using Rekall.Age.Rendering;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;
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
        await TestProjectAuthoring.CreateRenderableProjectAsync(root, context, "Renderable Capture");
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
        await TestProjectAuthoring.CreateProjectWithSceneAsync(root, context, "Captured Playable");
        await new ScaffoldPlayableModuleCommand().ExecuteAsync(
            new ScaffoldPlayableModuleRequest(root, "agent.capture", "Agent Capture", "AgentCapture"),
            context);
        await new WriteModuleSourceCommand().ExecuteAsync(
            new WriteModuleSourceRequest(root, "AgentCapture", "AgentCaptureModule.cs", CreateCaptureModuleSource()),
            context);
        var build = await new BuildModulesCommand().ExecuteAsync(new BuildModulesRequest(root), context);
        Assert.True(build.Ok, build.Summary);
        var command = new CapturePlayableFrameCommand();
        IReadOnlyList<RekallAgePlaybackInput> legacyInputs =
        [
            new RekallAgePlaybackInput(1, PrimaryAction: true)
        ];

        var result = await command.ExecuteAsync(
            new CapturePlayableFrameRequest(
                root,
                "Main",
                Path.Combine(root, "PlayCaptures"),
                1,
                320,
                180,
                legacyInputs),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.True(result.Value.Captured);
        Assert.True(result.Value.NonBlank);
        Assert.True(File.Exists(result.Value.OutputPath));
        Assert.EndsWith("Main_play_frame_001.png", result.Value.OutputPath, StringComparison.Ordinal);
        Assert.Equal(320, result.Value.Width);
        Assert.Equal(180, result.Value.Height);
        Assert.Equal(1, result.Value.FrameIndex);
        Assert.Equal("agent-authored", result.Value.Kind);
        Assert.Equal(4, result.Value.DrawCommandCount);
        Assert.Equal(["clear", "rect", "circle", "text"], result.Value.DrawCommandKinds);
        Assert.True(result.Value.NonBackgroundPixels > 0);
        Assert.Contains("Score 10", result.Value.Text, StringComparison.Ordinal);
        Assert.Contains(result.Value.OutputPath, context.Transaction.ChangedResources);
    }

    [Fact]
    public async Task CapturePlayableFrameProjectsSemanticActionsAndInputEdgesIntoThePlayableModule()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("semantic play capture"), CancellationToken.None);
        await TestProjectAuthoring.CreateProjectWithSceneAsync(root, context, "Semantic Captured Playable");
        var store = new RekallAgeSceneStore();
        var scene = await store.LoadAsync(root, "Main", CancellationToken.None);
        await store.SaveAsync(root, scene.AddEntity(
            RekallAgeEntityDocument.Create("Input", ["input"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.InputActionMap",
                    new JsonObject
                    {
                        ["actions"] = new JsonArray
                        {
                            new JsonObject { ["name"] = "capture.move", ["positiveKey"] = "D" }
                        }
                    }))), CancellationToken.None);
        await new ScaffoldPlayableModuleCommand().ExecuteAsync(
            new ScaffoldPlayableModuleRequest(root, "agent.semantic.capture", "Agent Semantic Capture", "AgentSemanticCapture"),
            context);
        await new WriteModuleSourceCommand().ExecuteAsync(
            new WriteModuleSourceRequest(root, "AgentSemanticCapture", "AgentSemanticCaptureModule.cs", CreateSemanticCaptureModuleSource()),
            context);
        var build = await new BuildModulesCommand().ExecuteAsync(new BuildModulesRequest(root), context);
        Assert.True(build.Ok, build.Summary);

        var result = await new CapturePlayableFrameCommand().ExecuteAsync(
            new CapturePlayableFrameRequest(
                root,
                "Main",
                Path.Combine(root, "PlayCaptures"),
                3,
                InputFrames:
                [
                    new RekallAgeRuntimeInputFrame(SemanticActions: [new("capture.move", 1, true, true)]),
                    new RekallAgeRuntimeInputFrame(SemanticActions: [new("capture.move", 1, true)]),
                    new RekallAgeRuntimeInputFrame(SemanticActions: [new("capture.move", 0, false, false, true)])
                ]),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.Contains("held=2", result.Value.Text, StringComparison.Ordinal);
        Assert.Contains("pressed=1", result.Value.Text, StringComparison.Ordinal);
        Assert.Contains("released=1", result.Value.Text, StringComparison.Ordinal);
        Assert.Contains("value=0", result.Value.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void CapturePlayableFrameSchemaDocumentsGenericInputFrames()
    {
        var description = new CapturePlayableFrameCommand().Schema.Description;

        Assert.Contains("semanticActions", description, StringComparison.Ordinal);
        Assert.Contains("pressedKeys", description, StringComparison.Ordinal);
        Assert.Contains("legacy", description, StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateCaptureModuleSource()
    {
        return """
using Rekall.Age.Modules;

namespace Game.Modules.AgentCapture;

[RekallAgeModule("agent.capture", "Agent Capture")]
[RekallAgeRequiresCapability("world")]
public sealed class AgentCaptureModule : RekallAgeModule, IRekallAgePlayableModule
{
    public string Kind => "agent-authored";

    public override void Configure(RekallAgeModuleBuilder builder)
    {
    }

    public RekallAgePlayableModuleState CreateInitialState(RekallAgePlayableModuleContext context)
    {
        var state = new RekallAgePlayableModuleState();
        state.Numbers["score"] = 0;
        return state;
    }

    public void Tick(RekallAgePlayableModuleState state, RekallAgePlayableModuleInput input)
    {
        if (input.PrimaryAction)
        {
            state.Numbers["score"] += 10;
        }
    }

    public RekallAgePlayableModuleFrame Render(RekallAgePlayableModuleState state)
    {
        var score = (int)state.Numbers["score"];
        var drawCommands = new[]
        {
            new RekallAgePlayableDrawCommand("clear", "background", 0, 0, 320, 180, "#101820"),
            new RekallAgePlayableDrawCommand("rect", "agent-actor", 128, 74, 40, 40, "#f2aa4c"),
            new RekallAgePlayableDrawCommand("circle", "agent-focus", 184, 86, 16, 16, "#f6f7f8"),
            new RekallAgePlayableDrawCommand("text", "agent-hud", 8, 8, 0, 0, "#ffffff", $"Score {score}")
        };
        return new RekallAgePlayableModuleFrame($"AGENT CAPTURE\nScore {score}", drawCommands);
    }
}
""";
    }

    private static string CreateSemanticCaptureModuleSource()
    {
        return """
using Rekall.Age.Modules;

namespace Game.Modules.AgentSemanticCapture;

[RekallAgeModule("agent.semantic.capture", "Agent Semantic Capture")]
[RekallAgeRequiresCapability("world")]
public sealed class AgentSemanticCaptureModule : RekallAgeModule, IRekallAgePlayableModule
{
    public string Kind => "agent-authored";

    public override void Configure(RekallAgeModuleBuilder builder)
    {
    }

    public RekallAgePlayableModuleState CreateInitialState(RekallAgePlayableModuleContext context)
    {
        return new RekallAgePlayableModuleState();
    }

    public void Tick(RekallAgePlayableModuleState state, RekallAgePlayableModuleInput input)
    {
        state.Numbers["held"] = state.Numbers.GetValueOrDefault("held") + (input.IsInputActionDown("capture.move") ? 1 : 0);
        state.Numbers["pressed"] = state.Numbers.GetValueOrDefault("pressed") + (input.WasInputActionPressed("capture.move") ? 1 : 0);
        state.Numbers["released"] = state.Numbers.GetValueOrDefault("released") + (input.WasInputActionReleased("capture.move") ? 1 : 0);
        state.Numbers["value"] = input.InputActionValue("capture.move");
    }

    public RekallAgePlayableModuleFrame Render(RekallAgePlayableModuleState state)
    {
        return new RekallAgePlayableModuleFrame($"held={(int)state.Numbers["held"]} pressed={(int)state.Numbers["pressed"]} released={(int)state.Numbers["released"]} value={state.Numbers["value"]:0}");
    }
}
""";
    }
}
