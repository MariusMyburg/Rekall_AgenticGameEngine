using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rekall.Age.Runtime;

internal sealed record RekallAgeAnimationGraphIssue(string Code, string Message, string Target);

internal sealed record RekallAgeAnimationGraphStateDefinition(
    string Name,
    string Clip,
    double Speed,
    string LoopMode,
    double StartTimeSeconds);

internal sealed record RekallAgeAnimationGraphConditionDefinition(
    string Parameter,
    string Operator,
    object Value)
{
    public bool Matches(IReadOnlyDictionary<string, object> parameters)
    {
        if (!parameters.TryGetValue(Parameter, out var actual))
        {
            return false;
        }

        if (Operator is "equals" or "notEquals")
        {
            var equal = actual.GetType() == Value.GetType() && Equals(actual, Value);
            return Operator == "equals" ? equal : !equal;
        }

        if (actual is not double left || Value is not double right)
        {
            return false;
        }

        return Operator switch
        {
            "greater" => left > right,
            "greaterOrEqual" => left >= right,
            "less" => left < right,
            "lessOrEqual" => left <= right,
            _ => false
        };
    }
}

internal sealed record RekallAgeAnimationGraphTransitionDefinition(
    string From,
    string To,
    double DurationSeconds,
    bool ResetTime,
    IReadOnlyList<RekallAgeAnimationGraphConditionDefinition> Conditions);

internal sealed record RekallAgeAnimationStateGraphDefinition(
    bool Playing,
    string InitialState,
    IReadOnlyList<RekallAgeAnimationGraphStateDefinition> States,
    IReadOnlyList<RekallAgeAnimationGraphTransitionDefinition> Transitions,
    IReadOnlyDictionary<string, object> Parameters)
{
    private const int MaximumStates = 64;
    private const int MaximumTransitions = 256;
    private const int MaximumParameters = 128;
    private const int MaximumConditions = 16;
    private const int MaximumIdentifierCharacters = 128;
    private const int MaximumStringCharacters = 1_024;
    private static readonly HashSet<string> Operators = new(StringComparer.Ordinal)
    {
        "equals", "notEquals", "greater", "greaterOrEqual", "less", "lessOrEqual"
    };
    private static readonly HashSet<string> LoopModes = new(StringComparer.Ordinal)
    {
        "clamp", "loop", "pingpong"
    };

    public RekallAgeAnimationGraphTransitionDefinition? SelectTransition(string currentState)
    {
        foreach (var transition in Transitions.Where(item => item.From == currentState))
        {
            if ((transition.To != currentState || transition.ResetTime) &&
                transition.Conditions.All(condition => condition.Matches(Parameters)))
            {
                return transition;
            }
        }

        foreach (var transition in Transitions.Where(item => item.From == "*"))
        {
            if ((transition.To != currentState || transition.ResetTime) &&
                transition.Conditions.All(condition => condition.Matches(Parameters)))
            {
                return transition;
            }
        }

        return null;
    }

    public static bool TryParse(
        JsonObject authored,
        out RekallAgeAnimationStateGraphDefinition? definition,
        out RekallAgeAnimationGraphIssue? issue)
    {
        ArgumentNullException.ThrowIfNull(authored);
        definition = null;
        issue = null;

        if (ReadInt32(authored, "version", 1) != 1)
        {
            return Fail("runtime.animation.graph_version_unsupported", "Animation state graph version must be 1.", "version", out issue);
        }

        if (!TryReadObject(authored, "parameters", out var parameterObject) ||
            parameterObject.Count > MaximumParameters)
        {
            return FailLimitOrShape(parameterObject?.Count, MaximumParameters, "parameters", out issue);
        }

        var parameters = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var pair in parameterObject)
        {
            if (!ValidIdentifier(pair.Key) || !TryReadPrimitive(pair.Value, out var value))
            {
                return Fail("runtime.animation.graph_parameter_invalid", "Graph parameters require bounded names and finite number, boolean, or string values.", pair.Key, out issue);
            }

            parameters.Add(pair.Key, value!);
        }

        if (!TryReadArray(authored, "states", out var stateArray) || stateArray.Count > MaximumStates)
        {
            return FailLimitOrShape(stateArray?.Count, MaximumStates, "states", out issue);
        }

        var states = new List<RekallAgeAnimationGraphStateDefinition>(stateArray.Count);
        var stateNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in stateArray)
        {
            if (node is not JsonObject state ||
                !TryIdentifier(state, "name", out var name) ||
                !TryIdentifier(state, "clip", out var clip) ||
                !stateNames.Add(name))
            {
                return Fail("runtime.animation.graph_state_invalid", "Graph states require unique bounded names and clip ids.", "states", out issue);
            }

            var speed = ReadDouble(state, "speed", 1);
            var start = ReadDouble(state, "startTimeSeconds", 0);
            var loop = ReadString(state, "loopMode") ?? "loop";
            if (!double.IsFinite(speed) || !double.IsFinite(start) || start < 0 || !LoopModes.Contains(loop))
            {
                return Fail("runtime.animation.graph_state_invalid", "Graph state timing and loop mode are invalid.", name, out issue);
            }

            states.Add(new(name, clip, speed, loop, start));
        }

        var initialState = ReadString(authored, "initialState");
        if (!ValidIdentifier(initialState) || !stateNames.Contains(initialState!))
        {
            return Fail("runtime.animation.graph_initial_state_invalid", "Graph initialState must name a declared state.", initialState ?? "initialState", out issue);
        }

        if (!TryReadArray(authored, "transitions", out var transitionArray) || transitionArray.Count > MaximumTransitions)
        {
            return FailLimitOrShape(transitionArray?.Count, MaximumTransitions, "transitions", out issue);
        }

        var transitions = new List<RekallAgeAnimationGraphTransitionDefinition>(transitionArray.Count);
        foreach (var node in transitionArray)
        {
            if (node is not JsonObject transition ||
                !TryIdentifier(transition, "from", out var from, allowWildcard: true) ||
                !TryIdentifier(transition, "to", out var to) ||
                from != "*" && !stateNames.Contains(from) ||
                !stateNames.Contains(to))
            {
                return Fail("runtime.animation.graph_transition_invalid", "Graph transitions must reference declared states or wildcard from '*'.", "transitions", out issue);
            }

            var duration = ReadDouble(transition, "durationSeconds", 0);
            if (!double.IsFinite(duration) || duration < 0)
            {
                return Fail("runtime.animation.graph_transition_invalid", "Graph transition duration is invalid.", $"{from}->{to}", out issue);
            }

            if (!TryReadArray(transition, "conditions", out var conditionArray) ||
                conditionArray.Count > MaximumConditions)
            {
                return FailLimitOrShape(conditionArray?.Count, MaximumConditions, "conditions", out issue);
            }

            var conditions = new List<RekallAgeAnimationGraphConditionDefinition>(conditionArray.Count);
            foreach (var conditionNode in conditionArray)
            {
                if (conditionNode is not JsonObject condition ||
                    !TryIdentifier(condition, "parameter", out var parameter) ||
                    !parameters.ContainsKey(parameter) ||
                    ReadString(condition, "operator") is not { } operation ||
                    !Operators.Contains(operation) ||
                    !TryReadPrimitive(Get(condition, "value"), out var comparison) ||
                    parameters[parameter].GetType() != comparison!.GetType() ||
                    operation is not ("equals" or "notEquals") &&
                    (parameters[parameter] is not double || comparison is not double))
                {
                    return Fail("runtime.animation.graph_condition_invalid", "Graph conditions require declared parameters, supported operators, and compatible primitive values.", $"{from}->{to}", out issue);
                }

                conditions.Add(new(parameter, operation, comparison!));
            }

            transitions.Add(new(
                from,
                to,
                duration,
                ReadBoolean(transition, "resetTime", true),
                conditions.ToArray()));
        }

        definition = new(
            ReadBoolean(authored, "playing", true),
            initialState!,
            states.ToArray(),
            transitions.ToArray(),
            new Dictionary<string, object>(parameters, StringComparer.Ordinal));
        return true;
    }

    private static bool FailLimitOrShape(int? count, int limit, string target, out RekallAgeAnimationGraphIssue? issue) =>
        count > limit
            ? Fail("runtime.animation.graph_limit_exceeded", $"Animation graph {target} count {count} exceeds limit {limit}.", target, out issue)
            : Fail("runtime.animation.graph_shape_invalid", $"Animation graph {target} must be an array or object of the documented shape.", target, out issue);

    private static bool TryReadPrimitive(JsonNode? node, out object? value)
    {
        value = null;
        if (node is not JsonValue)
        {
            return false;
        }

        switch (node.GetValueKind())
        {
            case System.Text.Json.JsonValueKind.Number:
                var jsonValue = (JsonValue)node;
                double number;
                if (jsonValue.TryGetValue<double>(out var doubleValue))
                {
                    number = doubleValue;
                }
                else if (jsonValue.TryGetValue<int>(out var intValue))
                {
                    number = intValue;
                }
                else if (jsonValue.TryGetValue<long>(out var longValue))
                {
                    number = longValue;
                }
                else if (jsonValue.TryGetValue<decimal>(out var decimalValue))
                {
                    number = (double)decimalValue;
                }
                else if (jsonValue.TryGetValue<float>(out var floatValue))
                {
                    number = floatValue;
                }
                else
                {
                    return false;
                }

                if (double.IsFinite(number))
                {
                    value = number;
                    return true;
                }

                return false;
            case JsonValueKind.True:
            case JsonValueKind.False:
                value = node.GetValue<bool>();
                return true;
            case JsonValueKind.String when node.GetValue<string>() is { Length: <= MaximumStringCharacters } text:
                value = text;
                return true;
            default:
                return false;
        }
    }

    private static bool TryIdentifier(JsonObject source, string name, out string value, bool allowWildcard = false)
    {
        value = ReadString(source, name) ?? string.Empty;
        return allowWildcard && value == "*" || ValidIdentifier(value);
    }

    private static bool ValidIdentifier(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= MaximumIdentifierCharacters;

    private static JsonNode? Get(JsonObject source, string name) =>
        source.FirstOrDefault(pair => pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;

    private static string? ReadString(JsonObject source, string name) =>
        Get(source, name) is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static int ReadInt32(JsonObject source, string name, int fallback) =>
        Get(source, name) is JsonValue value && value.TryGetValue<int>(out var number) ? number : fallback;

    private static double ReadDouble(JsonObject source, string name, double fallback)
    {
        var node = Get(source, name);
        return node is null ? fallback : TryReadPrimitive(node, out var value) && value is double number ? number : double.NaN;
    }

    private static bool ReadBoolean(JsonObject source, string name, bool fallback) =>
        Get(source, name) is JsonValue value && value.TryGetValue<bool>(out var boolean) ? boolean : fallback;

    private static bool TryReadArray(JsonObject source, string name, out JsonArray array)
    {
        array = Get(source, name) as JsonArray ?? null!;
        return array is not null;
    }

    private static bool TryReadObject(JsonObject source, string name, out JsonObject value)
    {
        value = Get(source, name) as JsonObject ?? null!;
        return value is not null;
    }

    private static bool Fail(string code, string message, string target, out RekallAgeAnimationGraphIssue? issue)
    {
        issue = new(code, message, target);
        return false;
    }
}
