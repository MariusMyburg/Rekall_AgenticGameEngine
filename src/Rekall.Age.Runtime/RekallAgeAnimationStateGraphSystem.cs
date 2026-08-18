using System.Text.Json.Nodes;
using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Runtime;

public sealed class RekallAgeAnimationStateGraphSystem : IRekallAgeRuntimeWorldSystem
{
    private const string GraphComponent = "Rekall.AnimationStateGraph";
    private const string GraphMixerComponent = "Rekall.AnimationGraphMixer";
    private const string GraphStateComponent = "Rekall.AnimationGraphState";
    private const double Epsilon = 0.000000001;
    private const double CompletionRelativeTolerance = 0.00001;

    public string Id => "runtime.animation.graph";

    public int Priority => -10;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeWorldFrameContext context)
    {
        var observations = new List<RekallAgeRuntimeObservation>();
        var events = new List<RekallAgeRuntimeEvent>();
        var entities = world.Entities
            .Select(entity => Apply(entity, context, observations, events))
            .ToArray();
        return ValueTask.FromResult(world with
        {
            Entities = entities,
            Observations = world.Observations.Concat(observations).ToArray(),
            Subsystems = world.Subsystems with
            {
                Events = new RekallAgeRuntimeEventView(world.Subsystems.Events.Events.Concat(events).ToArray())
            }
        });
    }

    private static RekallAgeRuntimeEntity Apply(
        RekallAgeRuntimeEntity entity,
        RekallAgeRuntimeWorldFrameContext context,
        List<RekallAgeRuntimeObservation> observations,
        List<RekallAgeRuntimeEvent> events)
    {
        var authored = entity.FindComponent(GraphComponent);
        if (authored is null)
        {
            return entity;
        }

        if (!RekallAgeAnimationStateGraphDefinition.TryParse(authored.Properties, out var graph, out var issue))
        {
            observations.Add(Observation(context.FrameIndex, issue!, entity));
            return RemoveComponent(entity, GraphMixerComponent);
        }

        if (entity.Components.Any(component =>
                component.Type is "Rekall.AnimationMixer" or "Rekall.AnimationPlayer"))
        {
            observations.Add(new RekallAgeRuntimeObservation(
                context.FrameIndex,
                "runtime.animation.graph_driver_conflict",
                "warning",
                "animation",
                entity.Id,
                entity.Name,
                "AnimationStateGraph",
                "Animation state graph takes precedence over other animation drivers on the same entity.",
                []));
        }

        var state = entity.FindComponent(GraphStateComponent)?.Properties;
        var declared = graph!.States.ToDictionary(item => item.Name, StringComparer.Ordinal);
        var active = ReadString(state, "activeState");
        if (active is null || !declared.ContainsKey(active))
        {
            active = graph.InitialState;
        }

        var previous = ReadString(state, "previousState");
        if (previous is not null && !declared.ContainsKey(previous))
        {
            previous = null;
        }

        var clocks = ReadClocks(state, graph.States);
        var elapsed = previous is null ? 0 : Math.Max(0, ReadNumber(state, "transitionElapsedSeconds", 0));
        var duration = previous is null ? 0 : Math.Max(0, ReadNumber(state, "transitionDurationSeconds", 0));
        if (graph.Playing && previous is null)
        {
            var transition = graph.SelectTransition(active);
            if (transition is not null)
            {
                var source = active;
                active = transition.To;
                previous = source;
                elapsed = 0;
                duration = transition.DurationSeconds;
                if (transition.ResetTime)
                {
                    clocks[active] = declared[active].StartTimeSeconds;
                }

                EmitTransitionFacts(entity, context.FrameIndex, source, active, duration, events);
            }
        }

        if (graph.Playing)
        {
            clocks[active] = Math.Max(0, clocks[active] + context.DeltaTime.TotalSeconds * declared[active].Speed);
            if (previous is not null && previous != active)
            {
                clocks[previous] = Math.Max(0, clocks[previous] + context.DeltaTime.TotalSeconds * declared[previous].Speed);
            }

            if (previous is not null)
            {
                elapsed += context.DeltaTime.TotalSeconds;
            }
        }

        var progress = previous is null ? 1 : duration <= Epsilon ? 1 : Math.Clamp(elapsed / duration, 0, 1);
        var completionTolerance = Math.Max(Epsilon, duration * CompletionRelativeTolerance);
        var completedSource = previous is not null && duration - elapsed <= completionTolerance
            ? previous
            : null;
        if (completedSource is not null)
        {
            EmitFact(entity, context.FrameIndex, "animation.transition.end", completedSource, active, duration, 1, events);
            previous = null;
            progress = 1;
        }

        var layers = new JsonArray();
        if (previous is not null)
        {
            layers.Add(Layer(declared[previous], clocks[previous], 1 - progress, graph.Playing));
        }

        layers.Add(Layer(declared[active], clocks[active], previous is null ? 1 : progress, graph.Playing));
        var clockState = new JsonObject();
        foreach (var declaredState in graph.States)
        {
            clockState[declaredState.Name] = clocks[declaredState.Name];
        }

        var updated = entity
            .UpsertComponent(GraphMixerComponent, new JsonObject
            {
                ["playing"] = graph.Playing,
                ["layers"] = layers
            })
            .UpsertComponent(GraphStateComponent, new JsonObject
            {
                ["version"] = 1,
                ["activeState"] = active,
                ["previousState"] = previous,
                ["transitionElapsedSeconds"] = previous is null ? duration : elapsed,
                ["transitionDurationSeconds"] = duration,
                ["transitionProgress"] = progress,
                ["playing"] = graph.Playing,
                ["stateClocks"] = clockState
            });
        return updated;
    }

    private static Dictionary<string, double> ReadClocks(
        JsonObject? runtimeState,
        IReadOnlyList<RekallAgeAnimationGraphStateDefinition> states)
    {
        var authored = Get(runtimeState, "stateClocks") as JsonObject;
        var clocks = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var state in states)
        {
            clocks[state.Name] = Math.Max(0, ReadNumber(authored, state.Name, state.StartTimeSeconds));
        }

        return clocks;
    }

    private static JsonObject Layer(
        RekallAgeAnimationGraphStateDefinition state,
        double time,
        double weight,
        bool playing) => new()
    {
        ["name"] = "graph:" + state.Name,
        ["clip"] = state.Clip,
        ["weight"] = weight,
        ["targetWeight"] = weight,
        ["fadeSeconds"] = 0,
        ["playing"] = playing,
        ["speed"] = state.Speed,
        ["loopMode"] = state.LoopMode,
        ["startTimeSeconds"] = state.StartTimeSeconds,
        ["authoritativeTimeSeconds"] = time
    };

    private static void EmitTransitionFacts(
        RekallAgeRuntimeEntity entity,
        int frame,
        string from,
        string to,
        double duration,
        List<RekallAgeRuntimeEvent> events)
    {
        EmitFact(entity, frame, "animation.state.exit", from, to, duration, 0, events);
        EmitFact(entity, frame, "animation.transition.begin", from, to, duration, 0, events);
        EmitFact(entity, frame, "animation.state.enter", from, to, duration, 0, events);
    }

    private static void EmitFact(
        RekallAgeRuntimeEntity entity,
        int frame,
        string type,
        string from,
        string to,
        double duration,
        double progress,
        List<RekallAgeRuntimeEvent> events)
    {
        foreach (var handler in EventHandlers(entity, type))
        {
            events.Add(new RekallAgeRuntimeEvent(
                frame,
                type,
                entity.Id,
                entity.Name,
                "runtime.animation.graph",
                handler,
                new JsonObject
                {
                    ["from"] = from,
                    ["to"] = to,
                    ["durationSeconds"] = duration,
                    ["progress"] = progress
                }));
        }
    }

    private static IReadOnlyList<string?> EventHandlers(RekallAgeRuntimeEntity entity, string type) =>
        entity.Components
            .Where(component => component.Type == "Rekall.EventBindings")
            .SelectMany(component => Get(component.Properties, "events") is JsonArray bindings
                ? bindings.OfType<JsonObject>()
                : [])
            .Where(binding => ReadBoolean(binding, "active", true) &&
                string.Equals(ReadString(binding, "event") ?? ReadString(binding, "type"), type, StringComparison.OrdinalIgnoreCase))
            .Select(binding => ReadString(binding, "handler"))
            .ToArray();

    private static RekallAgeRuntimeObservation Observation(
        int frame,
        RekallAgeAnimationGraphIssue issue,
        RekallAgeRuntimeEntity entity) => new(
            frame,
            issue.Code,
            "error",
            "animation",
            entity.Id,
            entity.Name,
            "AnimationStateGraph",
            issue.Message,
            [issue.Target]);

    private static RekallAgeRuntimeEntity RemoveComponent(RekallAgeRuntimeEntity entity, string type) =>
        entity with { Components = entity.Components.Where(component => component.Type != type).ToArray() };

    private static JsonNode? Get(JsonObject? source, string name) =>
        source?.FirstOrDefault(pair => pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;

    private static string? ReadString(JsonObject? source, string name) =>
        Get(source, name) is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static bool ReadBoolean(JsonObject? source, string name, bool fallback) =>
        Get(source, name) is JsonValue value && value.TryGetValue<bool>(out var boolean) ? boolean : fallback;

    private static double ReadNumber(JsonObject? source, string name, double fallback)
    {
        if (Get(source, name) is not JsonValue value)
        {
            return fallback;
        }

        if (value.TryGetValue<double>(out var number) && double.IsFinite(number)) return number;
        if (value.TryGetValue<int>(out var integer)) return integer;
        if (value.TryGetValue<long>(out var longInteger)) return longInteger;
        return fallback;
    }
}
