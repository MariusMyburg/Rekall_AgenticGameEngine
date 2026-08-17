using System.Text.Json.Nodes;
using Rekall.Age.Rendering;
using Rekall.Age.Runtime;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Runtime;

public sealed class RuntimeUiTests
{
    [Fact]
    public async Task UiRuntimeReadsPublishedPascalCaseComponentProperties()
    {
        var canvas = RekallAgeEntityDocument.Create("HUD", ["ui"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.UiCanvas",
                new JsonObject { ["ReferenceWidth"] = 400, ["ReferenceHeight"] = 200, ["Layer"] = 7 }));
        var button = RekallAgeEntityDocument.Create("Confirm", ["ui"]) with { ParentId = canvas.Id };
        button = button.AddComponent(RekallAgeComponentDocument.Create(
            "Rekall.Button",
            new JsonObject
            {
                ["X"] = 40,
                ["Y"] = 20,
                ["Width"] = 160,
                ["Height"] = 50,
                ["Text"] = "Confirm",
                ["Interactive"] = true
            }));
        var scene = RekallAgeSceneDocument.Create("Main", ["ui"])
            .AddEntity(canvas)
            .AddEntity(button);

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault().RunAsync(
            new RekallAgeRuntimeWorldBuilder().Build(scene),
            1,
            CancellationToken.None,
            new Rekall.Age.Runtime.Abstractions.RekallAgeRuntimeInputState(
                MouseX: 50,
                MouseY: 30,
                ViewportWidth: 400,
                ViewportHeight: 200));

        var element = Assert.Single(result.World.Subsystems.Ui.Elements);
        Assert.Equal("Confirm", element.Text);
        Assert.True(element.Interactive);
        Assert.Equal(40, element.Layout!.X);
        Assert.Equal(20, element.Layout.Y);
        Assert.Equal(160, element.Layout.Width);
        Assert.Equal(50, element.Layout.Height);
        var inputState = result.World.Entities.Single(entity => entity.Name == "HUD")
            .Components.Single(component => component.Type == "Rekall.UiInputState");
        Assert.Equal(button.Id, inputState.Properties["hoveredEntityId"]!.GetValue<string>());
    }

    [Fact]
    public async Task UiNavigationUsesSemanticActionsAndDeterministicAuthoredOrder()
    {
        var canvas = RekallAgeEntityDocument.Create("Menu", ["ui"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.UiCanvas", new JsonObject()));
        RekallAgeEntityDocument Button(string id, string name, int order) => new RekallAgeEntityDocument(
            id,
            name,
            ["ui"],
            [
                RekallAgeComponentDocument.Create(
                    "Rekall.Button",
                    new JsonObject { ["Text"] = name, ["Interactive"] = true, ["NavigationOrder"] = order }),
                RekallAgeComponentDocument.Create(
                    "Rekall.EventBindings",
                    new JsonObject
                    {
                        ["Events"] = new JsonArray
                        {
                            new JsonObject { ["Event"] = "ui.focus", ["Handler"] = $"focus-{name.ToLowerInvariant()}" },
                            new JsonObject { ["Event"] = "ui.activate", ["Handler"] = $"activate-{name.ToLowerInvariant()}" }
                        }
                    })
            ])
        {
            ParentId = canvas.Id
        };
        var first = Button("button-first", "First", 10);
        var second = Button("button-second", "Second", 20);
        var input = RekallAgeEntityDocument.Create("Menu Input", ["input"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.InputActionMap",
                new JsonObject
                {
                    ["Actions"] = new JsonArray
                    {
                        new JsonObject { ["Name"] = "ui.next", ["Key"] = "Tab" },
                        new JsonObject { ["Name"] = "ui.activate", ["Key"] = "Enter" }
                    }
                }));
        var scene = RekallAgeSceneDocument.Create("Main", ["ui", "input"])
            .AddEntity(canvas)
            .AddEntity(second)
            .AddEntity(first)
            .AddEntity(input);
        var loop = RekallAgeRuntimeExecutionLoop.CreateDefault();
        var world = new RekallAgeRuntimeWorldBuilder().Build(scene);

        var selectedFirst = await loop.RunAsync(
            world,
            1,
            CancellationToken.None,
            new Rekall.Age.Runtime.Abstractions.RekallAgeRuntimeInputState(
                PressedKeysThisFrame: new HashSet<string>(["Tab"], StringComparer.OrdinalIgnoreCase)));
        var selectedSecond = await loop.RunAsync(
            selectedFirst.World,
            1,
            CancellationToken.None,
            new Rekall.Age.Runtime.Abstractions.RekallAgeRuntimeInputState(
                PressedKeysThisFrame: new HashSet<string>(["Tab"], StringComparer.OrdinalIgnoreCase)));
        var activated = await loop.RunAsync(
            selectedSecond.World,
            1,
            CancellationToken.None,
            new Rekall.Age.Runtime.Abstractions.RekallAgeRuntimeInputState(
                PressedKeysThisFrame: new HashSet<string>(["Enter"], StringComparer.OrdinalIgnoreCase)));

        var menuState = activated.World.Entities.Single(entity => entity.Id == canvas.Id)
            .Components.Single(component => component.Type == "Rekall.UiInputState");
        Assert.Equal(second.Id, menuState.Properties["focusedEntityId"]!.GetValue<string>());
        Assert.Contains(selectedFirst.World.Subsystems.Events.Events, runtimeEvent =>
            runtimeEvent.Type == "ui.focus" && runtimeEvent.Handler == "focus-first");
        Assert.Contains(activated.World.Subsystems.Events.Events, runtimeEvent =>
            runtimeEvent.Type == "ui.activate" && runtimeEvent.Handler == "activate-second");
    }

    [Fact]
    public async Task UiContainerAppliesDeterministicVerticalStackPaddingGapAndAlignment()
    {
        var canvas = RekallAgeEntityDocument.Create("Canvas", ["ui"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.UiCanvas",
                new JsonObject { ["ReferenceWidth"] = 300, ["ReferenceHeight"] = 200 }));
        var panel = RekallAgeEntityDocument.Create("Stack", ["ui"]) with { ParentId = canvas.Id };
        panel = panel.AddComponent(RekallAgeComponentDocument.Create(
            "Rekall.Panel",
            new JsonObject
            {
                ["X"] = 10, ["Y"] = 10, ["Width"] = 200, ["Height"] = 100,
                ["LayoutDirection"] = "vertical", ["PaddingLeft"] = 10, ["PaddingRight"] = 10,
                ["PaddingTop"] = 5, ["PaddingBottom"] = 5, ["Gap"] = 4
            }));
        var first = RekallAgeEntityDocument.Create("First", ["ui"]) with { ParentId = panel.Id };
        first = first.AddComponent(RekallAgeComponentDocument.Create(
            "Rekall.Label",
            new JsonObject
            {
                ["Width"] = 50, ["Height"] = 20, ["LayoutOrder"] = 10,
                ["HorizontalAlignment"] = "center"
            }));
        var second = RekallAgeEntityDocument.Create("Second", ["ui"]) with { ParentId = panel.Id };
        second = second.AddComponent(RekallAgeComponentDocument.Create(
            "Rekall.Button",
            new JsonObject
            {
                ["Width"] = 60, ["Height"] = 25, ["LayoutOrder"] = 20,
                ["HorizontalAlignment"] = "end"
            }));
        var scene = RekallAgeSceneDocument.Create("Main", ["ui"])
            .AddEntity(canvas)
            .AddEntity(second)
            .AddEntity(panel)
            .AddEntity(first);

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault().RunAsync(
            new RekallAgeRuntimeWorldBuilder().Build(scene), 1, CancellationToken.None);
        var firstLayout = result.World.Subsystems.Ui.Elements.Single(element => element.EntityName == "First").Layout!;
        var secondLayout = result.World.Subsystems.Ui.Elements.Single(element => element.EntityName == "Second").Layout!;

        Assert.Equal(85, firstLayout.X);
        Assert.Equal(15, firstLayout.Y);
        Assert.Equal(50, firstLayout.Width);
        Assert.Equal(20, firstLayout.Height);
        Assert.Equal(140, secondLayout.X);
        Assert.Equal(39, secondLayout.Y);
        Assert.Equal(60, secondLayout.Width);
        Assert.Equal(25, secondLayout.Height);
    }

    [Fact]
    public async Task UiImageUsesResolvedAssetPixelsInSoftwareAndOverlayRendering()
    {
        var canvas = RekallAgeEntityDocument.Create("HUD", ["ui"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.UiCanvas",
                new JsonObject { ["referenceWidth"] = 2, ["referenceHeight"] = 2 }));
        var image = RekallAgeEntityDocument.Create("Portrait", ["ui"])
            with { ParentId = canvas.Id };
        image = image.AddComponent(RekallAgeComponentDocument.Create(
            "Rekall.Image",
            new JsonObject
            {
                ["x"] = 0,
                ["y"] = 0,
                ["width"] = 2,
                ["height"] = 2,
                ["assetId"] = "portrait"
            }));
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "ui"])
            .AddEntity(canvas)
            .AddEntity(image);
        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(new RekallAgeRuntimeWorldBuilder().Build(scene), 1, CancellationToken.None);
        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(result.World, 2, 2, debugOverlay: false);
        var assets = new RekallAgeRuntimeViewportAssetSet(
            new Dictionary<string, RekallAgeRgbaImage>(StringComparer.Ordinal)
            {
                ["portrait"] = new RekallAgeRgbaImage(1, 1, [0x11, 0x88, 0xee, 0xff])
            },
            new Dictionary<string, IReadOnlyList<RekallAgeVulkanSceneMesh>>(StringComparer.Ordinal),
            []);

        var renderer = new RekallAgeRuntimeSoftwareRenderer();
        var rendered = renderer.RenderRgba(frame, assets);
        var overlay = renderer.RenderUiOverlayRgba(frame, assets);

        Assert.Equal([0x11, 0x88, 0xee, 0xff], rendered.Rgba[..4]);
        Assert.Equal([0x11, 0x88, 0xee, 0xff], overlay[..4]);
        Assert.Equal(1, rendered.AssetBackedRenderableCount);
        Assert.Equal(0, rendered.FallbackRenderableCount);
    }

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
