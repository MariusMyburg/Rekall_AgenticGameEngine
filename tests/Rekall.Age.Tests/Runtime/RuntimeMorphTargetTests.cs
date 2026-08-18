using System.Text.Json.Nodes;
using Rekall.Age.Assets;
using Rekall.Age.Modules;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Runtime;

public sealed class RuntimeMorphTargetTests
{
    [Fact]
    public async Task ValidWeightsPublishExactRuntimeStateAndProjection()
    {
        var result = await RunAsync(Actor(new JsonArray(0.25, -0.5, 1_000_000)), 1);

        var actor = Assert.Single(result.World.Entities);
        var state = Assert.IsType<JsonArray>(actor.FindComponent("Rekall.MorphState")!.Properties["weights"]);
        Assert.Equal([0.25, -0.5, 1_000_000], state.Select(ReadNumber));
        var projected = Assert.Single(result.World.Subsystems.Animation.MorphStates);
        Assert.Equal(actor.Id, projected.EntityId);
        Assert.Equal("Morph Actor", projected.EntityName);
        Assert.Equal([0.25, -0.5, 1_000_000], projected.Weights);
        Assert.DoesNotContain(result.World.Observations, item => item.Code == "runtime.animation.morph_weights_invalid");
    }

    [Fact]
    public async Task LinearClipSamplesWeightArrayBeforeMorphValidation()
    {
        var actor = Actor(new JsonArray(0, 0))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AnimationClip",
                Clip("linear",
                    new JsonObject { ["time"] = 0, ["value"] = new JsonArray(0, 0) },
                    new JsonObject { ["time"] = 1, ["value"] = new JsonArray(1, -2) })))
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.AnimationPlayer", new JsonObject()));

        var result = await RunAsync(actor, 30);

        Assert.Equal([0.5, -1], Assert.Single(result.World.Subsystems.Animation.MorphStates).Weights);
    }

    [Fact]
    public async Task CubicClipSamplesWeightArrayBeforeMorphValidation()
    {
        var actor = Actor(new JsonArray(0, 0))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AnimationClip",
                Clip("cubic",
                    new JsonObject
                    {
                        ["time"] = 0,
                        ["value"] = new JsonArray(0, 0),
                        ["inTangent"] = new JsonArray(0, 0),
                        ["outTangent"] = new JsonArray(2, -4)
                    },
                    new JsonObject
                    {
                        ["time"] = 1,
                        ["value"] = new JsonArray(1, -2),
                        ["inTangent"] = new JsonArray(0, 0),
                        ["outTangent"] = new JsonArray(0, 0)
                    })))
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.AnimationPlayer", new JsonObject()));

        var result = await RunAsync(actor, 30);

        Assert.Equal([0.75, -1.5], Assert.Single(result.World.Subsystems.Animation.MorphStates).Weights);
    }

    [Fact]
    public async Task InvalidWeightsRemoveStaleStateAndEmitOneBoundedObservation()
    {
        var actor = Actor(new JsonArray(1, "bad"))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.MorphState",
                new JsonObject { ["version"] = 1, ["weights"] = new JsonArray(9, 9) }));

        var result = await RunAsync(actor, 1);

        Assert.Null(Assert.Single(result.World.Entities).FindComponent("Rekall.MorphState"));
        Assert.Empty(result.World.Subsystems.Animation.MorphStates);
        var observation = Assert.Single(result.World.Observations, item => item.Code == "runtime.animation.morph_weights_invalid");
        Assert.Equal("Morph Actor", observation.EntityName);
        Assert.InRange(observation.Message.Length, 1, 512);
    }

    [Fact]
    public async Task InvalidWeightShapesAndValuesFailClosed()
    {
        var invalid = new[]
        {
            new JsonArray(),
            new JsonArray(Enumerable.Range(0, 65).Select(index => JsonValue.Create(index)).ToArray()),
            new JsonArray((JsonNode?)null),
            new JsonArray(new JsonArray(1)),
            new JsonArray(1_000_001),
            new JsonArray(double.NaN),
            new JsonArray(double.PositiveInfinity)
        };

        foreach (var weights in invalid)
        {
            var result = await RunAsync(Actor(weights), 1);

            Assert.Null(Assert.Single(result.World.Entities).FindComponent("Rekall.MorphState"));
            Assert.Empty(result.World.Subsystems.Animation.MorphStates);
            Assert.Single(result.World.Observations, item => item.Code == "runtime.animation.morph_weights_invalid");
        }
    }

    [Fact]
    public async Task MaximumWeightCountAndMagnitudeAreAcceptedWithoutClamping()
    {
        var weights = new JsonArray(Enumerable.Range(0, 64)
            .Select(index => JsonValue.Create(index % 2 == 0 ? -1_000_000d : 1_000_000d))
            .ToArray());

        var result = await RunAsync(Actor(weights), 1);

        var projected = Assert.Single(result.World.Subsystems.Animation.MorphStates);
        Assert.Equal(64, projected.Weights.Count);
        Assert.Equal(-1_000_000, projected.Weights[0]);
        Assert.Equal(1_000_000, projected.Weights[63]);
    }

    [Fact]
    public async Task RemovingAuthoredComponentRemovesPersistedAndProjectedState()
    {
        var valid = await RunAsync(Actor(new JsonArray(0.5)), 1);
        var actor = Assert.Single(valid.World.Entities) with
        {
            Components = Assert.Single(valid.World.Entities).Components
                .Where(component => component.Type != "Rekall.MorphWeights")
                .ToArray()
        };

        var resumed = await RekallAgeRuntimeExecutionLoop.CreateDefault().RunAsync(
            valid.World with { Entities = [actor] },
            1,
            CancellationToken.None);

        Assert.Null(Assert.Single(resumed.World.Entities).FindComponent("Rekall.MorphState"));
        Assert.Empty(resumed.World.Subsystems.Animation.MorphStates);
    }

    [Fact]
    public async Task AnimationStateGraphReusesCatalogClipForMorphWeights()
    {
        var root = TestPaths.CreateTempDirectory();
        var animationDirectory = Path.Combine(root, "Assets", "animation");
        Directory.CreateDirectory(animationDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(animationDirectory, "morph.json"),
            new JsonObject
            {
                ["version"] = 1,
                ["durationSeconds"] = 1,
                ["tracks"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["component"] = "Rekall.MorphWeights",
                        ["property"] = "weights",
                        ["interpolation"] = "linear",
                        ["keys"] = new JsonArray
                        {
                            new JsonObject { ["time"] = 0, ["value"] = new JsonArray(0, 0) },
                            new JsonObject { ["time"] = 1, ["value"] = new JsonArray(1, -1) }
                        }
                    }
                }
            }.ToJsonString());
        await new RekallAgeAssetCatalogStore().SaveAsync(
            root,
            new RekallAgeAssetCatalogDocument(
            [
                new("morph-clip", "morph", "Morph", "animation", string.Empty, "Assets/animation/morph.json", "test")
            ]),
            CancellationToken.None);
        var actor = Actor(new JsonArray(0, 0))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AnimationStateGraph",
                new JsonObject
                {
                    ["version"] = 1,
                    ["initialState"] = "active",
                    ["parameters"] = new JsonObject(),
                    ["states"] = new JsonArray
                    {
                        new JsonObject { ["name"] = "active", ["clip"] = "morph-clip", ["loopMode"] = "clamp" }
                    },
                    ["transitions"] = new JsonArray()
                }));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault(root)
            .RunAsync(World(actor), 30, CancellationToken.None);

        Assert.Equal([0.5, -0.5], Assert.Single(result.World.Subsystems.Animation.MorphStates).Weights);
    }

    [Fact]
    public async Task SplitRunMatchesContinuousMorphState()
    {
        var actor = Actor(new JsonArray(0, 0))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AnimationClip",
                Clip("linear",
                    new JsonObject { ["time"] = 0, ["value"] = new JsonArray(-2, 4) },
                    new JsonObject { ["time"] = 2, ["value"] = new JsonArray(2, -4) })))
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.AnimationPlayer", new JsonObject()));
        var initial = World(actor);
        var loop = RekallAgeRuntimeExecutionLoop.CreateDefault();

        var first = await loop.RunAsync(initial, 17, CancellationToken.None);
        var resumed = await loop.RunAsync(first.World, 43, CancellationToken.None);
        var continuous = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(initial, 60, CancellationToken.None);

        Assert.Equal(
            Assert.Single(continuous.World.Subsystems.Animation.MorphStates).Weights,
            Assert.Single(resumed.World.Subsystems.Animation.MorphStates).Weights);
    }

    private static RekallAgeEntityDocument Actor(JsonArray weights) =>
        RekallAgeEntityDocument.Create("Morph Actor", ["actor", "morph"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.MorphWeights",
                new JsonObject { ["weights"] = weights }));

    private static JsonObject Clip(string interpolation, JsonObject first, JsonObject second) => new()
    {
        ["version"] = 1,
        ["durationSeconds"] = 2,
        ["tracks"] = new JsonArray
        {
            new JsonObject
            {
                ["component"] = "Rekall.MorphWeights",
                ["property"] = "weights",
                ["interpolation"] = interpolation,
                ["keys"] = new JsonArray(first, second)
            }
        }
    };

    private static Task<RekallAgeRuntimeRunResult> RunAsync(RekallAgeEntityDocument actor, int frames) =>
        RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(World(actor), frames, CancellationToken.None).AsTask();

    private static RekallAgeRuntimeWorld World(RekallAgeEntityDocument actor) =>
        new RekallAgeRuntimeWorldBuilder().Build(
            RekallAgeSceneDocument.Create("Main", ["world", "animation", "rendering3d"]).AddEntity(actor));

    private static double ReadNumber(JsonNode? node) => node!.GetValue<double>();
}
