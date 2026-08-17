using System.Text.Json.Nodes;
using Rekall.Age.Runtime;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Runtime;

public sealed class RuntimeAnimationTests
{
    [Fact]
    public async Task UnsupportedAnimationClipVersionProducesStructuredObservation()
    {
        var actor = RekallAgeEntityDocument.Create("Actor", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AnimationClip",
                new JsonObject { ["version"] = 99, ["durationSeconds"] = 1, ["tracks"] = new JsonArray() }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AnimationPlayer",
                new JsonObject { ["playing"] = true }));
        var world = new RekallAgeRuntimeWorldBuilder().Build(
            RekallAgeSceneDocument.Create("Main", ["world", "animation"]).AddEntity(actor));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(world, 1, CancellationToken.None);

        var observation = Assert.Single(result.World.Observations, item =>
            item.Code == "runtime.animation.unsupported_clip_version");
        Assert.Equal("animation", observation.Subsystem);
        Assert.Equal("Actor", observation.TargetName);
    }

    [Fact]
    public async Task AnimationClipSamplesGenericScalarAndSpriteTracksDeterministically()
    {
        var actor = RekallAgeEntityDocument.Create("Actor", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Transform3D",
                new JsonObject { ["x"] = 0, ["y"] = 0, ["z"] = 0 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.SpriteRenderer",
                new JsonObject
                {
                    ["sprite"] = "idle",
                    ["opacity"] = 1,
                    ["offset"] = new JsonArray(0, 10),
                    ["tint"] = "#000000"
                }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AnimationClip",
                new JsonObject
                {
                    ["version"] = 1,
                    ["durationSeconds"] = 1,
                    ["tracks"] = new JsonArray
                    {
                        ScalarTrack("Rekall.Transform3D", "x", 0, 10, "linear"),
                        ScalarTrack("Rekall.SpriteRenderer", "opacity", 1, 0, "linear"),
                        ValueTrack(
                            "Rekall.SpriteRenderer",
                            "offset",
                            new JsonArray(0, 10),
                            new JsonArray(10, 20)),
                        ValueTrack("Rekall.SpriteRenderer", "tint", "#000000", "#ffffff"),
                        new JsonObject
                        {
                            ["component"] = "Rekall.SpriteRenderer",
                            ["property"] = "sprite",
                            ["interpolation"] = "step",
                            ["keys"] = new JsonArray
                            {
                                new JsonObject { ["time"] = 0, ["value"] = "idle" },
                                new JsonObject { ["time"] = 0.5, ["value"] = "run" }
                            }
                        }
                    }
                }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AnimationPlayer",
                new JsonObject { ["playing"] = true, ["loopMode"] = "clamp" }));
        var world = new RekallAgeRuntimeWorldBuilder().Build(
            RekallAgeSceneDocument.Create("Main", ["world", "animation"]).AddEntity(actor));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(world, 30, CancellationToken.None);

        var runtimeActor = Assert.Single(result.World.Entities);
        Assert.Equal(5, runtimeActor.Transform.Position3D.X, precision: 4);
        var sprite = Assert.Single(runtimeActor.Components, component => component.Type == "Rekall.SpriteRenderer");
        Assert.Equal(0.5, sprite.Properties["opacity"]!.GetValue<double>(), precision: 4);
        Assert.Equal("run", sprite.Properties["sprite"]!.GetValue<string>());
        Assert.Equal(5, sprite.Properties["offset"]![0]!.GetValue<double>(), precision: 4);
        Assert.Equal(15, sprite.Properties["offset"]![1]!.GetValue<double>(), precision: 4);
        Assert.Equal("#808080", sprite.Properties["tint"]!.GetValue<string>());
        var state = Assert.Single(runtimeActor.Components, component => component.Type == "Rekall.AnimationState");
        Assert.Equal(0.5, state.Properties["timeSeconds"]!.GetValue<double>(), precision: 4);
        var player = Assert.Single(result.World.Subsystems.Animation.Players);
        Assert.True(player.InlineClip);
        Assert.True(player.Playing);
        Assert.Equal(0.5, player.TimeSeconds, precision: 4);
        Assert.DoesNotContain(result.World.Observations, observation =>
            observation.Code == "REKALL_ANIMATION_MISSING_CLIP");
    }

    [Fact]
    public async Task AnimationClipLoopsAndEmitsBoundMarkerFacts()
    {
        var actor = RekallAgeEntityDocument.Create("Actor", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Transform3D",
                new JsonObject { ["x"] = 0 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AnimationClip",
                new JsonObject
                {
                    ["version"] = 1,
                    ["durationSeconds"] = 0.25,
                    ["tracks"] = new JsonArray { ScalarTrack("Rekall.Transform3D", "x", 0, 1, "linear") },
                    ["events"] = new JsonArray
                    {
                        new JsonObject { ["time"] = 0.125, ["name"] = "midpoint" }
                    }
                }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AnimationPlayer",
                new JsonObject { ["playing"] = true, ["loopMode"] = "loop", ["speed"] = 1 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.EventBindings",
                new JsonObject
                {
                    ["events"] = new JsonArray
                    {
                        new JsonObject { ["event"] = "animation.event", ["handler"] = "on-marker" }
                    }
                }));
        var world = new RekallAgeRuntimeWorldBuilder().Build(
            RekallAgeSceneDocument.Create("Main", ["world", "animation"]).AddEntity(actor));
        var loop = RekallAgeRuntimeExecutionLoop.CreateDefault();

        var result = await loop.RunAsync(world, 8, CancellationToken.None);

        var marker = Assert.Single(result.World.Subsystems.Events.Events, runtimeEvent =>
            runtimeEvent.Type == "animation.event" && runtimeEvent.Handler == "on-marker");
        Assert.Equal("midpoint", marker.Payload["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task AnimationPlayerSupportsClampAndPingPongTimeModes()
    {
        static RekallAgeEntityDocument Animated(string name, string mode) =>
            RekallAgeEntityDocument.Create(name, ["actor"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject { ["x"] = 0 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.AnimationClip",
                    new JsonObject
                    {
                        ["version"] = 1,
                        ["durationSeconds"] = 1,
                        ["tracks"] = new JsonArray { ScalarTrack("Rekall.Transform3D", "x", 0, 10, "linear") }
                    }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.AnimationPlayer",
                    new JsonObject { ["playing"] = true, ["loopMode"] = mode }));
        var world = new RekallAgeRuntimeWorldBuilder().Build(
            RekallAgeSceneDocument.Create("Main", ["world", "animation"])
                .AddEntity(Animated("Clamp", "clamp"))
                .AddEntity(Animated("PingPong", "pingpong")));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(world, 75, CancellationToken.None);

        var clamp = Assert.Single(result.World.Entities, entity => entity.Name == "Clamp");
        var pingPong = Assert.Single(result.World.Entities, entity => entity.Name == "PingPong");
        Assert.Equal(10, clamp.Transform.Position3D.X, precision: 4);
        Assert.False(result.World.Subsystems.Animation.Players.Single(player => player.EntityName == "Clamp").Playing);
        Assert.Equal(7.5, pingPong.Transform.Position3D.X, precision: 3);
    }

    private static JsonObject ScalarTrack(
        string component,
        string property,
        double from,
        double to,
        string interpolation)
    {
        return new JsonObject
        {
            ["component"] = component,
            ["property"] = property,
            ["interpolation"] = interpolation,
            ["keys"] = new JsonArray
            {
                new JsonObject { ["time"] = 0, ["value"] = from },
                new JsonObject { ["time"] = 1, ["value"] = to }
            }
        };
    }

    private static JsonObject ValueTrack(
        string component,
        string property,
        JsonNode from,
        JsonNode to)
    {
        return new JsonObject
        {
            ["component"] = component,
            ["property"] = property,
            ["interpolation"] = "linear",
            ["keys"] = new JsonArray
            {
                new JsonObject { ["time"] = 0, ["value"] = from },
                new JsonObject { ["time"] = 1, ["value"] = to }
            }
        };
    }
}
