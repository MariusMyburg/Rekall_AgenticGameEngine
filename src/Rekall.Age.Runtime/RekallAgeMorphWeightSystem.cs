using System.Text.Json.Nodes;
using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Runtime;

public sealed class RekallAgeMorphWeightSystem : IRekallAgeRuntimeWorldSystem
{
    private const string WeightsComponent = "Rekall.MorphWeights";
    private const string StateComponent = "Rekall.MorphState";
    private const int MaximumWeights = 64;
    private const double MaximumMagnitude = 1_000_000;

    public string Id => "runtime.animation.morph";

    public int Priority => 5;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeWorldFrameContext context)
    {
        var observations = new List<RekallAgeRuntimeObservation>();
        var entities = world.Entities.Select(entity => Apply(entity, context.FrameIndex, observations)).ToArray();
        return ValueTask.FromResult(world with
        {
            Entities = entities,
            Observations = world.Observations.Concat(observations).ToArray()
        });
    }

    private static RekallAgeRuntimeEntity Apply(
        RekallAgeRuntimeEntity entity,
        int frame,
        List<RekallAgeRuntimeObservation> observations)
    {
        var withoutStaleState = entity with
        {
            Components = entity.Components
                .Where(component => !component.Type.Equals(StateComponent, StringComparison.Ordinal))
                .ToArray()
        };
        var authored = entity.FindComponent(WeightsComponent);
        if (authored is null)
        {
            return withoutStaleState;
        }

        if (!TryReadWeights(authored.Properties, out var weights, out var reason))
        {
            observations.Add(new RekallAgeRuntimeObservation(
                frame,
                "runtime.animation.morph_weights_invalid",
                "error",
                "animation",
                entity.Id,
                entity.Name,
                "MorphWeights",
                $"Morph weights are invalid: {reason}",
                [entity.Id]));
            return withoutStaleState;
        }

        return withoutStaleState.UpsertComponent(StateComponent, new JsonObject
        {
            ["version"] = 1,
            ["weights"] = new JsonArray(weights.Select(weight => JsonValue.Create(weight)).ToArray())
        });
    }

    private static bool TryReadWeights(
        JsonObject properties,
        out IReadOnlyList<double> weights,
        out string reason)
    {
        weights = Array.Empty<double>();
        var node = properties.FirstOrDefault(pair =>
            pair.Key.Equals("weights", StringComparison.OrdinalIgnoreCase)).Value;
        if (node is not JsonArray array)
        {
            reason = "Weights must be a numeric array.";
            return false;
        }
        if (array.Count is < 1 or > MaximumWeights)
        {
            reason = $"Weights contains {array.Count} entries; the supported count is 1 to {MaximumWeights}.";
            return false;
        }

        var parsed = new double[array.Count];
        for (var index = 0; index < array.Count; index++)
        {
            if (!TryReadFiniteNumber(array[index], out var value))
            {
                reason = $"Weights entry {index} must be a finite number.";
                return false;
            }
            if (Math.Abs(value) > MaximumMagnitude)
            {
                reason = $"Weights entry {index} exceeds the absolute magnitude limit {MaximumMagnitude:0}.";
                return false;
            }
            parsed[index] = value;
        }

        weights = parsed;
        reason = string.Empty;
        return true;
    }

    private static bool TryReadFiniteNumber(JsonNode? node, out double number)
    {
        number = 0;
        if (node is not JsonValue value)
        {
            return false;
        }
        if (value.TryGetValue<double>(out number)) return double.IsFinite(number);
        if (value.TryGetValue<int>(out var integer))
        {
            number = integer;
            return true;
        }
        if (value.TryGetValue<long>(out var longInteger))
        {
            number = longInteger;
            return true;
        }
        if (value.TryGetValue<decimal>(out var decimalValue))
        {
            number = (double)decimalValue;
            return double.IsFinite(number);
        }
        return false;
    }
}
