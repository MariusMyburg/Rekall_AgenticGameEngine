using System.Text.Json.Nodes;
using Rekall.Age.Assets;
using Rekall.Age.Modules;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Runtime;

public sealed class RuntimeAnimationStateGraphTests
{
    [Fact]
    public async Task GraphCrossFadesRealGenericClipTracksAndCompletes()
    {
        var root = await CreateClipCatalogAsync();
        var world = World(Graph(phase: 1, transitionSeconds: 1));
        var loop = RekallAgeRuntimeExecutionLoop.CreateDefault(root);

        var halfway = await loop.RunAsync(world, 30, CancellationToken.None);
        var halfwayActor = Assert.Single(halfway.World.Entities);
        Assert.Equal(5, halfwayActor.Transform.Position3D.X, precision: 3);
        var graphState = halfwayActor.FindComponent("Rekall.AnimationGraphState")!;
        Assert.Equal("active", graphState.Properties["activeState"]!.GetValue<string>());
        Assert.Equal("idle", graphState.Properties["previousState"]!.GetValue<string>());
        Assert.Equal(0.5, graphState.Properties["transitionProgress"]!.GetValue<double>(), precision: 3);
        var projected = Assert.Single(halfway.World.Subsystems.Animation.Players);
        Assert.Equal("AnimationStateGraph", projected.Kind);
        Assert.Equal("active", projected.StateName);
        Assert.Equal("idle", projected.PreviousStateName);
        Assert.Equal("clip-active", projected.ClipAssetId);
        Assert.Equal(0.5, projected.TransitionProgress, precision: 3);
        Assert.Equal(2, projected.LayerCount);

        var completed = await loop.RunAsync(halfway.World, 30, CancellationToken.None);
        var completedActor = Assert.Single(completed.World.Entities);
        Assert.Equal(10, completedActor.Transform.Position3D.X, precision: 3);
        graphState = completedActor.FindComponent("Rekall.AnimationGraphState")!;
        Assert.Equal("active", graphState.Properties["activeState"]!.GetValue<string>());
        Assert.True(graphState.Properties["previousState"] is null, graphState.Properties.ToJsonString());
        Assert.Equal(1, graphState.Properties["transitionProgress"]!.GetValue<double>(), precision: 3);
    }

    [Fact]
    public async Task GraphEmitsBoundBeginEnterExitAndEndFactsExactlyOnce()
    {
        var root = await CreateClipCatalogAsync();
        var graph = Graph(phase: 1, transitionSeconds: 1.0 / 60.0)
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.EventBindings",
                new JsonObject
                {
                    ["events"] = new JsonArray(
                        Binding("animation.state.exit"),
                        Binding("animation.transition.begin"),
                        Binding("animation.state.enter"),
                        Binding("animation.transition.end"))
                }));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault(root)
            .RunAsync(World(graph), 1, CancellationToken.None);

        Assert.Equal(
            ["animation.state.enter", "animation.state.exit", "animation.transition.begin", "animation.transition.end"],
            result.World.Subsystems.Events.Events.Select(item => item.Type));
        Assert.All(result.World.Subsystems.Events.Events, item =>
        {
            Assert.Equal("idle", item.Payload["from"]!.GetValue<string>());
            Assert.Equal("active", item.Payload["to"]!.GetValue<string>());
            Assert.Equal("on-" + item.Type, item.Handler);
        });
    }

    [Fact]
    public async Task SplitRunMatchesContinuousGraphClocksWeightsAndValues()
    {
        var root = await CreateClipCatalogAsync();
        var initial = World(Graph(phase: 1, transitionSeconds: 1));

        var first = await RekallAgeRuntimeExecutionLoop.CreateDefault(root).RunAsync(initial, 17, CancellationToken.None);
        var resumed = await RekallAgeRuntimeExecutionLoop.CreateDefault(root).RunAsync(first.World, 43, CancellationToken.None);
        var continuous = await RekallAgeRuntimeExecutionLoop.CreateDefault(root).RunAsync(initial, 60, CancellationToken.None);

        var resumedActor = Assert.Single(resumed.World.Entities);
        var continuousActor = Assert.Single(continuous.World.Entities);
        Assert.Equal(continuousActor.Transform.Position3D.X, resumedActor.Transform.Position3D.X, precision: 9);
        Assert.Equal(
            continuousActor.FindComponent("Rekall.AnimationGraphState")!.Properties.ToJsonString(),
            resumedActor.FindComponent("Rekall.AnimationGraphState")!.Properties.ToJsonString());
        Assert.Equal(
            continuousActor.FindComponent("Rekall.AnimationState")!.Properties.ToJsonString(),
            resumedActor.FindComponent("Rekall.AnimationState")!.Properties.ToJsonString());
    }

    [Fact]
    public async Task InvalidGraphFailsClosedAndSuppressesOtherAnimationDrivers()
    {
        var actor = Graph(phase: 0, transitionSeconds: 1);
        var graph = actor.Components.Single(component => component.Type == "Rekall.AnimationStateGraph");
        graph.Properties["initialState"] = "missing";
        actor = actor
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AnimationClip",
                new JsonObject
                {
                    ["durationSeconds"] = 1,
                    ["tracks"] = new JsonArray(ScalarTrack("Rekall.Transform3D", "x", 0, 99))
                }))
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.AnimationPlayer", new JsonObject()));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(World(actor), 30, CancellationToken.None);

        Assert.Equal(0, Assert.Single(result.World.Entities).Transform.Position3D.X);
        Assert.Contains(result.World.Observations, item => item.Code == "runtime.animation.graph_initial_state_invalid");
    }

    [Fact]
    public async Task PausedGraphDoesNotAdvanceClocksOrEvaluateTransitions()
    {
        var root = await CreateClipCatalogAsync();
        var actor = Graph(phase: 1, transitionSeconds: 1);
        actor.Components.Single(component => component.Type == "Rekall.AnimationStateGraph")
            .Properties["playing"] = false;

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault(root)
            .RunAsync(World(actor), 30, CancellationToken.None);

        var runtimeActor = Assert.Single(result.World.Entities);
        Assert.Equal(0, runtimeActor.Transform.Position3D.X);
        var state = runtimeActor.FindComponent("Rekall.AnimationGraphState")!.Properties;
        Assert.Equal("idle", state["activeState"]!.GetValue<string>());
        Assert.Equal(0, state["stateClocks"]!["idle"]!.GetValue<double>());
        Assert.Empty(result.World.Subsystems.Events.Events);
    }

    [Fact]
    public async Task ActiveTransitionCannotBeInterruptedByChangedParameters()
    {
        var root = await CreateClipCatalogAsync();
        var actor = Graph(phase: 1, transitionSeconds: 1);
        var authored = actor.Components.Single(component => component.Type == "Rekall.AnimationStateGraph").Properties;
        ((JsonArray)authored["states"]!).Add(State("final", "clip-active"));
        ((JsonArray)authored["transitions"]!).Add(new JsonObject
        {
            ["from"] = "*", ["to"] = "final", ["durationSeconds"] = 0,
            ["conditions"] = new JsonArray
            {
                new JsonObject { ["parameter"] = "phase", ["operator"] = "greater", ["value"] = 1 }
            }
        });
        var loop = RekallAgeRuntimeExecutionLoop.CreateDefault(root);
        var started = await loop.RunAsync(World(actor), 30, CancellationToken.None);
        var changedEntity = Assert.Single(started.World.Entities).UpdateComponent(
            "Rekall.AnimationStateGraph",
            properties =>
            {
                ((JsonObject)properties["parameters"]!)["phase"] = 2;
                return properties;
            });
        var changedWorld = started.World with { Entities = [changedEntity] };

        var stillTransitioning = await loop.RunAsync(changedWorld, 10, CancellationToken.None);

        var state = Assert.Single(stillTransitioning.World.Entities)
            .FindComponent("Rekall.AnimationGraphState")!.Properties;
        Assert.Equal("active", state["activeState"]!.GetValue<string>());
        Assert.Equal("idle", state["previousState"]!.GetValue<string>());

        var completed = await loop.RunAsync(stillTransitioning.World, 20, CancellationToken.None);
        var nextFrame = await loop.RunAsync(completed.World, 1, CancellationToken.None);
        state = Assert.Single(nextFrame.World.Entities).FindComponent("Rekall.AnimationGraphState")!.Properties;
        Assert.Equal("final", state["activeState"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(true, 0.0166666)]
    [InlineData(false, 1.2666666)]
    public async Task TransitionTargetClockCanResetOrResume(bool resetTime, double expected)
    {
        var root = await CreateClipCatalogAsync();
        var actor = Graph(phase: 1, transitionSeconds: 1);
        var graph = actor.Components.Single(component => component.Type == "Rekall.AnimationStateGraph");
        ((JsonObject)((JsonArray)graph.Properties["transitions"]!)[0]!)["resetTime"] = resetTime;
        actor = actor.AddComponent(RekallAgeComponentDocument.Create(
            "Rekall.AnimationGraphState",
            new JsonObject
            {
                ["activeState"] = "idle",
                ["playing"] = true,
                ["stateClocks"] = new JsonObject { ["idle"] = 0.5, ["active"] = 1.25 }
            }));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault(root)
            .RunAsync(World(actor), 1, CancellationToken.None);

        var state = Assert.Single(result.World.Entities).FindComponent("Rekall.AnimationGraphState")!.Properties;
        Assert.Equal(expected, state["stateClocks"]!["active"]!.GetValue<double>(), precision: 6);
    }

    [Fact]
    public async Task RuntimeClockMapContainsOnlyTheBoundedDeclaredStates()
    {
        var actor = Graph(phase: 0, transitionSeconds: 1);
        var states = (JsonArray)actor.Components.Single(component => component.Type == "Rekall.AnimationStateGraph")
            .Properties["states"]!;
        for (var index = states.Count; index < 64; index++)
        {
            states.Add(State($"state-{index}", "clip-idle"));
        }

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(World(actor), 1, CancellationToken.None);

        var clocks = (JsonObject)Assert.Single(result.World.Entities)
            .FindComponent("Rekall.AnimationGraphState")!.Properties["stateClocks"]!;
        Assert.Equal(64, clocks.Count);
        Assert.DoesNotContain("undeclared", clocks.Select(pair => pair.Key));
    }

    private static RekallAgeEntityDocument Graph(double phase, double transitionSeconds) =>
        RekallAgeEntityDocument.Create("Actor", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject { ["x"] = 0 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AnimationStateGraph",
                new JsonObject
                {
                    ["version"] = 1,
                    ["playing"] = true,
                    ["initialState"] = "idle",
                    ["parameters"] = new JsonObject { ["phase"] = phase },
                    ["states"] = new JsonArray
                    {
                        State("idle", "clip-idle"),
                        State("active", "clip-active")
                    },
                    ["transitions"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["from"] = "idle", ["to"] = "active", ["durationSeconds"] = transitionSeconds,
                            ["resetTime"] = true,
                            ["conditions"] = new JsonArray
                            {
                                new JsonObject { ["parameter"] = "phase", ["operator"] = "greater", ["value"] = 0 }
                            }
                        }
                    }
                }));

    private static RekallAgeRuntimeWorld World(RekallAgeEntityDocument actor) =>
        new RekallAgeRuntimeWorldBuilder().Build(
            RekallAgeSceneDocument.Create("Main", ["world", "animation"]).AddEntity(actor));

    private static async Task<string> CreateClipCatalogAsync()
    {
        var root = TestPaths.CreateTempDirectory();
        var directory = Path.Combine(root, "Assets", "animation");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "idle.json"), Clip(0).ToJsonString());
        await File.WriteAllTextAsync(Path.Combine(directory, "active.json"), Clip(10).ToJsonString());
        await new RekallAgeAssetCatalogStore().SaveAsync(
            root,
            new RekallAgeAssetCatalogDocument(
            [
                new("clip-idle", "idle", "Idle", "animation", string.Empty, "Assets/animation/idle.json", "test"),
                new("clip-active", "active", "Active", "animation", string.Empty, "Assets/animation/active.json", "test")
            ]),
            CancellationToken.None);
        return root;
    }

    private static JsonObject Clip(double value) => new()
    {
        ["version"] = 1,
        ["durationSeconds"] = 2,
        ["tracks"] = new JsonArray(ScalarTrack("Rekall.Transform3D", "x", value, value))
    };

    private static JsonObject ScalarTrack(string component, string property, double from, double to) => new()
    {
        ["component"] = component,
        ["property"] = property,
        ["interpolation"] = "linear",
        ["keys"] = new JsonArray
        {
            new JsonObject { ["time"] = 0, ["value"] = from },
            new JsonObject { ["time"] = 2, ["value"] = to }
        }
    };

    private static JsonObject State(string name, string clip) => new()
    {
        ["name"] = name, ["clip"] = clip, ["speed"] = 1, ["loopMode"] = "loop", ["startTimeSeconds"] = 0
    };

    private static JsonObject Binding(string type) => new()
    {
        ["event"] = type, ["handler"] = "on-" + type
    };
}
