using System.Globalization;
using System.Text.Json.Nodes;
using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Runtime;

public sealed class RekallAgeTimerEventSystem : IRekallAgeRuntimeWorldSystem
{
    private const string TimerComponent = "Rekall.Timer";
    private const string TimerStateComponent = "Rekall.TimerState";
    private const double TimerEpsilonSeconds = 0.000001;

    public string Id => "runtime.events.timer";

    public int Priority => -800;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeWorldFrameContext context)
    {
        var emitted = new List<RekallAgeRuntimeEvent>();
        var updatedEntities = world.Entities.Select(entity =>
        {
            if (!entity.Visible || entity.FindComponent(TimerComponent) is not { } timer)
            {
                return entity;
            }

            if (!ReadBoolean(timer.Properties, "active", true))
            {
                return entity;
            }

            var evaluation = EvaluateTimer(entity, timer, context);
            if (evaluation.Elapsed)
            {
                AddEvents(emitted, entity, timer, evaluation, context.FrameIndex);
            }

            return entity.UpsertComponent(TimerStateComponent, CreateState(evaluation));
        }).ToArray();

        return ValueTask.FromResult(world with
        {
            Entities = updatedEntities,
            Subsystems = world.Subsystems with
            {
                Events = new RekallAgeRuntimeEventView(
                    world.Subsystems.Events.Events
                        .Concat(emitted)
                        .OrderBy(runtimeEvent => runtimeEvent.Frame)
                        .ThenBy(runtimeEvent => runtimeEvent.EntityName, StringComparer.Ordinal)
                        .ThenBy(runtimeEvent => runtimeEvent.Type, StringComparer.Ordinal)
                        .ThenBy(runtimeEvent => runtimeEvent.Handler, StringComparer.Ordinal)
                        .ToArray())
            }
        });
    }

    private static TimerEvaluation EvaluateTimer(
        RekallAgeRuntimeEntity entity,
        RekallAgeRuntimeComponent timer,
        RekallAgeRuntimeWorldFrameContext context)
    {
        var timerId = Clean(ReadString(timer.Properties, "timerId")) ?? entity.Id;
        var duration = Math.Max(0.000001, ReadNumber(timer.Properties, "durationSeconds", 1));
        var repeat = ReadBoolean(timer.Properties, "repeat", false);
        var state = entity.FindComponent(TimerStateComponent)?.Properties;
        var completed = ReadBooleanOptional(state, "completed", false);
        var completedCount = Math.Max(0, ReadInt32Optional(state, "completedCount", 0));
        var elapsedSeconds = Math.Max(0, ReadNumberOptional(state, "elapsedSeconds", 0));

        if (completed && !repeat)
        {
            return new TimerEvaluation(
                timerId,
                duration,
                repeat,
                elapsedSeconds,
                Completed: true,
                completedCount,
                IntervalsElapsed: 0,
                Elapsed: false);
        }

        elapsedSeconds += context.DeltaTime.TotalSeconds;
        var intervalsElapsed = 0;
        var elapsed = elapsedSeconds + TimerEpsilonSeconds >= duration;
        if (elapsed)
        {
            if (repeat)
            {
                intervalsElapsed = Math.Max(1, (int)Math.Floor((elapsedSeconds + TimerEpsilonSeconds) / duration));
                elapsedSeconds -= intervalsElapsed * duration;
                if (elapsedSeconds < TimerEpsilonSeconds)
                {
                    elapsedSeconds = 0;
                }
            }
            else
            {
                intervalsElapsed = 1;
                elapsedSeconds = duration;
                completed = true;
            }

            completedCount += intervalsElapsed;
        }

        return new TimerEvaluation(
            timerId,
            duration,
            repeat,
            elapsedSeconds,
            completed && !repeat,
            completedCount,
            intervalsElapsed,
            elapsed);
    }

    private static void AddEvents(
        List<RekallAgeRuntimeEvent> emitted,
        RekallAgeRuntimeEntity entity,
        RekallAgeRuntimeComponent timer,
        TimerEvaluation evaluation,
        int frame)
    {
        foreach (var binding in EventBindingsFor(entity, "timer.elapsed"))
        {
            emitted.Add(new RekallAgeRuntimeEvent(
                frame,
                "timer.elapsed",
                entity.Id,
                entity.Name,
                "runtime.timer",
                binding.Handler,
                new JsonObject
                {
                    ["timerId"] = evaluation.TimerId,
                    ["durationSeconds"] = evaluation.DurationSeconds,
                    ["elapsedSeconds"] = evaluation.ElapsedSeconds,
                    ["repeat"] = evaluation.Repeat,
                    ["completed"] = evaluation.Completed,
                    ["completedCount"] = evaluation.CompletedCount,
                    ["intervalsElapsed"] = evaluation.IntervalsElapsed
                }));
        }
    }

    private static JsonObject CreateState(TimerEvaluation evaluation)
    {
        return new JsonObject
        {
            ["timerId"] = evaluation.TimerId,
            ["elapsedSeconds"] = evaluation.ElapsedSeconds,
            ["completed"] = evaluation.Completed,
            ["completedCount"] = evaluation.CompletedCount
        };
    }

    private static IReadOnlyList<TimerEventBinding> EventBindingsFor(
        RekallAgeRuntimeEntity target,
        string type)
    {
        var bindings = new List<TimerEventBinding>();
        foreach (var component in target.Components.Where(component =>
                     component.Type.Equals("Rekall.EventBindings", StringComparison.Ordinal)))
        {
            if (!ReadBoolean(component.Properties, "active", true)
                || !TryGetPropertyValue(component.Properties, "events", out var eventNodes)
                || eventNodes is not JsonArray eventArray)
            {
                continue;
            }

            foreach (var eventNode in eventArray.OfType<JsonObject>())
            {
                if (!ReadBoolean(eventNode, "active", true))
                {
                    continue;
                }

                var eventType = ReadString(eventNode, "event") ?? ReadString(eventNode, "type");
                if (eventType?.Equals(type, StringComparison.OrdinalIgnoreCase) == true)
                {
                    bindings.Add(new TimerEventBinding(Clean(ReadString(eventNode, "handler"))));
                }
            }
        }

        return bindings;
    }

    private static string? Clean(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? ReadString(JsonObject properties, string name)
    {
        return TryGetPropertyValue(properties, name, out var node)
            && node is JsonValue value
            && value.TryGetValue<string>(out var text)
            ? text
            : null;
    }

    private static bool ReadBooleanOptional(JsonObject? properties, string name, bool fallback)
    {
        return properties is null ? fallback : ReadBoolean(properties, name, fallback);
    }

    private static bool ReadBoolean(JsonObject properties, string name, bool fallback)
    {
        if (!TryGetPropertyValue(properties, name, out var node) || node is not JsonValue value)
        {
            return fallback;
        }

        if (value.TryGetValue<bool>(out var boolean))
        {
            return boolean;
        }

        return value.TryGetValue<string>(out var text)
            && bool.TryParse(text, out var parsed)
            ? parsed
            : fallback;
    }

    private static int ReadInt32Optional(JsonObject? properties, string name, int fallback)
    {
        return properties is null ? fallback : ReadInt32(properties, name, fallback);
    }

    private static int ReadInt32(JsonObject properties, string name, int fallback)
    {
        if (!TryGetPropertyValue(properties, name, out var node) || node is not JsonValue value)
        {
            return fallback;
        }

        if (value.TryGetValue<int>(out var integer))
        {
            return integer;
        }

        if (value.TryGetValue<double>(out var number))
        {
            return (int)number;
        }

        return value.TryGetValue<string>(out var text)
            && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    private static double ReadNumberOptional(JsonObject? properties, string name, double fallback)
    {
        return properties is null ? fallback : ReadNumber(properties, name, fallback);
    }

    private static double ReadNumber(JsonObject properties, string name, double fallback)
    {
        if (!TryGetPropertyValue(properties, name, out var node) || node is not JsonValue value)
        {
            return fallback;
        }

        if (value.TryGetValue<double>(out var doubleValue))
        {
            return doubleValue;
        }

        if (value.TryGetValue<int>(out var intValue))
        {
            return intValue;
        }

        if (value.TryGetValue<long>(out var longValue))
        {
            return longValue;
        }

        return value.TryGetValue<string>(out var text)
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    private static bool TryGetPropertyValue(
        JsonObject properties,
        string name,
        out JsonNode? node)
    {
        if (properties.TryGetPropertyValue(name, out node))
        {
            return true;
        }

        var pascalName = char.ToUpperInvariant(name[0]) + name[1..];
        if (properties.TryGetPropertyValue(pascalName, out node))
        {
            return true;
        }

        var match = properties.FirstOrDefault(property =>
            property.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
        node = match.Value;
        return !string.IsNullOrEmpty(match.Key);
    }

    private sealed record TimerEvaluation(
        string TimerId,
        double DurationSeconds,
        bool Repeat,
        double ElapsedSeconds,
        bool Completed,
        int CompletedCount,
        int IntervalsElapsed,
        bool Elapsed);

    private sealed record TimerEventBinding(string? Handler);
}
