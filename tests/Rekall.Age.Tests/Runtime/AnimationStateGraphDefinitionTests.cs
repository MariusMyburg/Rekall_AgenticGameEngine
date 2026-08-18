using System.Text.Json.Nodes;
using Rekall.Age.Runtime;

namespace Rekall.Age.Tests.Runtime;

public sealed class AnimationStateGraphDefinitionTests
{
    [Fact]
    public void ValidGraphParsesIntoImmutableBoundedDefinition()
    {
        var result = RekallAgeAnimationStateGraphDefinition.TryParse(Graph(), out var graph, out var issue);

        Assert.True(result);
        Assert.Null(issue);
        Assert.NotNull(graph);
        Assert.Equal("idle", graph.InitialState);
        Assert.Equal(["idle", "active"], graph.States.Select(state => state.Name));
        Assert.Equal(2, graph.Transitions.Count);
        Assert.Equal(2, graph.Parameters.Count);
    }

    [Fact]
    public void ExactStateTransitionPrecedesEarlierAnyStateTransition()
    {
        var authored = Graph();
        authored["transitions"] = new JsonArray
        {
            Transition("*", "idle", Condition("enabled", "equals", false)),
            Transition("idle", "active", Condition("speed", "greaterOrEqual", 2))
        };
        authored["parameters"] = new JsonObject { ["enabled"] = false, ["speed"] = 2 };
        Assert.True(RekallAgeAnimationStateGraphDefinition.TryParse(authored, out var graph, out _));

        var selected = graph!.SelectTransition("idle");

        Assert.NotNull(selected);
        Assert.Equal("active", selected.To);
    }

    [Theory]
    [InlineData("equals", 2, true)]
    [InlineData("notEquals", 3, true)]
    [InlineData("greater", 1, true)]
    [InlineData("greaterOrEqual", 2, true)]
    [InlineData("less", 3, true)]
    [InlineData("lessOrEqual", 2, true)]
    [InlineData("greater", 2, false)]
    public void TypedConditionOperatorsSelectDeterministically(string operation, double comparison, bool expected)
    {
        var authored = Graph();
        authored["transitions"] = new JsonArray
        {
            Transition("idle", "active", Condition("speed", operation, comparison))
        };
        Assert.True(RekallAgeAnimationStateGraphDefinition.TryParse(authored, out var graph, out _));

        Assert.Equal(expected, graph!.SelectTransition("idle") is not null);
    }

    [Fact]
    public void UnconditionalTransitionIsValidAndSelfTransitionRequiresReset()
    {
        var authored = Graph();
        authored["transitions"] = new JsonArray
        {
            new JsonObject { ["from"] = "idle", ["to"] = "idle", ["resetTime"] = false, ["conditions"] = new JsonArray() },
            new JsonObject { ["from"] = "idle", ["to"] = "active", ["conditions"] = new JsonArray() }
        };
        Assert.True(RekallAgeAnimationStateGraphDefinition.TryParse(authored, out var graph, out _));

        Assert.Equal("active", graph!.SelectTransition("idle")!.To);

        ((JsonObject)((JsonArray)authored["transitions"]!)[0]!)["resetTime"] = true;
        Assert.True(RekallAgeAnimationStateGraphDefinition.TryParse(authored, out graph, out _));
        Assert.Equal("idle", graph!.SelectTransition("idle")!.To);
    }

    [Theory]
    [InlineData(2, "runtime.animation.graph_version_unsupported")]
    [InlineData(1, "runtime.animation.graph_initial_state_invalid")]
    public void UnsupportedVersionAndMissingInitialStateFailClosed(int version, string expectedCode)
    {
        var authored = Graph();
        authored["version"] = version;
        if (version == 1)
        {
            authored["initialState"] = "missing";
        }

        var result = RekallAgeAnimationStateGraphDefinition.TryParse(authored, out var graph, out var issue);

        Assert.False(result);
        Assert.Null(graph);
        Assert.Equal(expectedCode, issue!.Code);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("missing-target")]
    [InlineData("unknown-operator")]
    [InlineData("object-parameter")]
    [InlineData("non-finite")]
    public void InvalidGraphFactsReturnStableIssue(string mutation)
    {
        var authored = Graph();
        switch (mutation)
        {
            case "duplicate":
                ((JsonArray)authored["states"]!).Add(State("idle", "clip-other"));
                break;
            case "missing-target":
                ((JsonObject)((JsonArray)authored["transitions"]!)[0]!)["to"] = "missing";
                break;
            case "unknown-operator":
                ((JsonObject)((JsonArray)((JsonObject)((JsonArray)authored["transitions"]!)[0]!)["conditions"]!)[0]!)["operator"] = "approximately";
                break;
            case "object-parameter":
                ((JsonObject)authored["parameters"]!)["speed"] = new JsonObject { ["value"] = 2 };
                break;
            case "non-finite":
                ((JsonObject)authored["parameters"]!)["speed"] = double.PositiveInfinity;
                break;
        }

        Assert.False(RekallAgeAnimationStateGraphDefinition.TryParse(authored, out _, out var issue));
        Assert.StartsWith("runtime.animation.graph_", issue!.Code, StringComparison.Ordinal);
    }

    [Fact]
    public void EqualityConditionRejectsMismatchedPrimitiveTypes()
    {
        var authored = Graph();
        authored["transitions"] = new JsonArray
        {
            Transition("idle", "active", Condition("speed", "equals", "2"))
        };

        Assert.False(RekallAgeAnimationStateGraphDefinition.TryParse(authored, out _, out var issue));
        Assert.Equal("runtime.animation.graph_condition_invalid", issue!.Code);
    }

    [Theory]
    [InlineData("states", 65)]
    [InlineData("transitions", 257)]
    [InlineData("parameters", 129)]
    [InlineData("conditions", 17)]
    public void AuthoredWorkIsBounded(string kind, int count)
    {
        var authored = Graph();
        if (kind == "states")
        {
            var states = (JsonArray)authored["states"]!;
            for (var index = states.Count; index < count; index++) states.Add(State($"state-{index}", "clip"));
        }
        else if (kind == "transitions")
        {
            var transitions = (JsonArray)authored["transitions"]!;
            for (var index = transitions.Count; index < count; index++) transitions.Add(Transition("idle", "active"));
        }
        else if (kind == "parameters")
        {
            var parameters = (JsonObject)authored["parameters"]!;
            for (var index = parameters.Count; index < count; index++) parameters[$"p-{index}"] = index;
        }
        else
        {
            var conditions = (JsonArray)((JsonObject)((JsonArray)authored["transitions"]!)[0]!)["conditions"]!;
            for (var index = conditions.Count; index < count; index++) conditions.Add(Condition("speed", "greater", index));
        }

        Assert.False(RekallAgeAnimationStateGraphDefinition.TryParse(authored, out _, out var issue));
        Assert.Equal("runtime.animation.graph_limit_exceeded", issue!.Code);
    }

    private static JsonObject Graph() => new()
    {
        ["version"] = 1,
        ["playing"] = true,
        ["initialState"] = "idle",
        ["parameters"] = new JsonObject { ["speed"] = 2, ["enabled"] = true },
        ["states"] = new JsonArray { State("idle", "clip-idle"), State("active", "clip-active") },
        ["transitions"] = new JsonArray
        {
            Transition("idle", "active", Condition("speed", "greater", 1)),
            Transition("active", "idle", Condition("enabled", "equals", false))
        }
    };

    private static JsonObject State(string name, string clip) => new()
    {
        ["name"] = name, ["clip"] = clip, ["speed"] = 1, ["loopMode"] = "loop", ["startTimeSeconds"] = 0
    };

    private static JsonObject Transition(string from, string to, params JsonObject[] conditions) => new()
    {
        ["from"] = from, ["to"] = to, ["durationSeconds"] = 0.25, ["resetTime"] = true,
        ["conditions"] = new JsonArray(conditions.Cast<JsonNode?>().ToArray())
    };

    private static JsonObject Condition(string parameter, string operation, object value) => new()
    {
        ["parameter"] = parameter,
        ["operator"] = operation,
        ["value"] = JsonValue.Create(value)
    };
}
