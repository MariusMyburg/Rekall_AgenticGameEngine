using System.Text.Json.Nodes;
using Rekall.Age.Rendering;
using Rekall.Age.Runtime;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Runtime;

public sealed class RuntimeUiTests
{
    [Fact]
    public async Task UiSystemLaysOutAndSoftwareRendererDrawsAuthoredButton()
    {
        var canvas = RekallAgeEntityDocument.Create("HUD", ["ui"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.UiCanvas",
                new JsonObject { ["referenceWidth"] = 200, ["referenceHeight"] = 100, ["layer"] = 3 }));
        var button = RekallAgeEntityDocument.Create("Launch", ["ui"])
            with { ParentId = canvas.Id };
        button = button.AddComponent(RekallAgeComponentDocument.Create(
            "Rekall.Button",
            new JsonObject
            {
                ["x"] = 20,
                ["y"] = 10,
                ["width"] = 80,
                ["height"] = 30,
                ["text"] = "Launch",
                ["backgroundColor"] = "#224466",
                ["foregroundColor"] = "#ffffff",
                ["borderColor"] = "#88ccff",
                ["borderWidth"] = 2,
                ["fontSize"] = 12
            }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.EventBindings",
                new JsonObject
                {
                    ["events"] = new JsonArray
                    {
                        new JsonObject { ["event"] = "pointer.click", ["handler"] = "launch-game" },
                        new JsonObject { ["event"] = "ui.focus", ["handler"] = "focus-control" }
                    }
                }));
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "ui"])
            .AddEntity(canvas)
            .AddEntity(button);

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(new RekallAgeRuntimeWorldBuilder().Build(scene), 1, CancellationToken.None);

        var element = Assert.Single(result.World.Subsystems.Ui.Elements);
        Assert.True(element.Interactive);
        Assert.Equal("Launch", element.Text);
        Assert.NotNull(element.Layout);
        Assert.Equal(20, element.Layout.X);
        Assert.Equal(10, element.Layout.Y);
        Assert.Equal(80, element.Layout.Width);
        Assert.Equal(30, element.Layout.Height);
        Assert.Contains("runtime.ui", result.SystemsRun);

        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(result.World, 200, 100, debugOverlay: false);
        var uiRenderable = Assert.Single(frame.Renderables, renderable => renderable.UiVisual is not null);
        Assert.Equal("Launch", uiRenderable.UiVisual!.Text);
        var rendered = new RekallAgeRuntimeSoftwareRenderer().RenderRgba(
            frame,
            RekallAgeRuntimeViewportAssetSet.Empty);
        var pixel = (15 * 200 + 25) * 4;
        Assert.Equal(0x22, rendered.Rgba[pixel]);
        Assert.Equal(0x44, rendered.Rgba[pixel + 1]);
        Assert.Equal(0x66, rendered.Rgba[pixel + 2]);

        var interactionLoop = RekallAgeRuntimeExecutionLoop.CreateDefault();
        var pressed = await interactionLoop.RunAsync(
            new RekallAgeRuntimeWorldBuilder().Build(scene),
            1,
            CancellationToken.None,
            new Rekall.Age.Runtime.Abstractions.RekallAgeRuntimeInputState(
                MouseX: 25,
                MouseY: 15,
                PressedButtons: new HashSet<string>(["Left"], StringComparer.OrdinalIgnoreCase),
                PressedButtonsThisFrame: new HashSet<string>(["Left"], StringComparer.OrdinalIgnoreCase),
                ViewportWidth: 200,
                ViewportHeight: 100));
        var released = await interactionLoop.RunAsync(
            pressed.World,
            1,
            CancellationToken.None,
            new Rekall.Age.Runtime.Abstractions.RekallAgeRuntimeInputState(
                MouseX: 25,
                MouseY: 15,
                ReleasedButtonsThisFrame: new HashSet<string>(["Left"], StringComparer.OrdinalIgnoreCase),
                ViewportWidth: 200,
                ViewportHeight: 100));
        Assert.Contains(released.World.Subsystems.Events.Events, runtimeEvent =>
            runtimeEvent.Type == "pointer.click" && runtimeEvent.Handler == "launch-game");
        Assert.Contains(released.World.Subsystems.Events.Events, runtimeEvent =>
            runtimeEvent.Type == "ui.focus" && runtimeEvent.Handler == "focus-control");
    }
}
