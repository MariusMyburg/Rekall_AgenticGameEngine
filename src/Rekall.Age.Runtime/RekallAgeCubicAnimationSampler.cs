using System.Globalization;
using System.Text.Json.Nodes;

namespace Rekall.Age.Runtime;

internal enum RekallAgeCubicAnimationValueKind { Scalar, Vector, Color }

internal sealed record RekallAgeCubicAnimationKey(
    double Time,
    RekallAgeCubicAnimationValueKind Kind,
    double[] Value,
    double[] InTangent,
    double[] OutTangent,
    JsonNode AuthoredValue);

internal static class RekallAgeCubicAnimationSampler
{
    private const double Epsilon = 1e-9;
    private const int MaximumVectorComponents = 16;

    public static bool TryCreateKeys(JsonArray nodes, out RekallAgeCubicAnimationKey[] keys, out string issue)
    {
        var parsed = new List<RekallAgeCubicAnimationKey>(nodes.Count);
        foreach (var node in nodes)
        {
            if (node is not JsonObject item
                || !TryReadFiniteNumber(Property(item, "time"), out var time)
                || !TryReadValue(Property(item, "value"), out var kind, out var values)
                || !TryReadTangent(Property(item, "inTangent"), kind, values.Length, out var inTangent)
                || !TryReadTangent(Property(item, "outTangent"), kind, values.Length, out var outTangent))
            {
                keys = [];
                issue = "Cubic keys require finite time/value data and shape-matched finite inTangent/outTangent data.";
                return false;
            }
            if (parsed.Count > 0 && time <= parsed[^1].Time)
            {
                keys = [];
                issue = "Cubic key times must be strictly increasing in authored order.";
                return false;
            }
            if (parsed.Count > 0 && (kind != parsed[0].Kind || values.Length != parsed[0].Value.Length))
            {
                keys = [];
                issue = "Cubic key values must use one consistent scalar, vector, or color shape.";
                return false;
            }
            parsed.Add(new RekallAgeCubicAnimationKey(
                time, kind, values, inTangent, outTangent, Property(item, "value")!.DeepClone()));
        }
        if (parsed.Count == 0)
        {
            keys = [];
            issue = "Cubic tracks require at least one key.";
            return false;
        }

        keys = [.. parsed];
        issue = string.Empty;
        return true;
    }

    public static JsonNode? Sample(IReadOnlyList<RekallAgeCubicAnimationKey> keys, double time)
    {
        if (time <= keys[0].Time + Epsilon) return keys[0].AuthoredValue.DeepClone();
        var right = 1;
        while (right < keys.Count && keys[right].Time + Epsilon < time) right++;
        if (right >= keys.Count) return keys[^1].AuthoredValue.DeepClone();
        if (Math.Abs(time - keys[right].Time) <= Epsilon) return keys[right].AuthoredValue.DeepClone();

        var left = keys[right - 1];
        var next = keys[right];
        var duration = next.Time - left.Time;
        var amount = Math.Round(Math.Clamp((time - left.Time) / duration, 0, 1), 5, MidpointRounding.AwayFromZero);
        var amount2 = amount * amount;
        var amount3 = amount2 * amount;
        var sampled = new double[left.Value.Length];
        for (var index = 0; index < sampled.Length; index++)
        {
            sampled[index] = (2 * amount3 - 3 * amount2 + 1) * left.Value[index]
                + (amount3 - 2 * amount2 + amount) * duration * left.OutTangent[index]
                + (-2 * amount3 + 3 * amount2) * next.Value[index]
                + (amount3 - amount2) * duration * next.InTangent[index];
            if (!double.IsFinite(sampled[index])) return null;
        }

        return left.Kind switch
        {
            RekallAgeCubicAnimationValueKind.Scalar => JsonValue.Create(sampled[0]),
            RekallAgeCubicAnimationValueKind.Vector => new JsonArray(sampled.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()),
            RekallAgeCubicAnimationValueKind.Color => JsonValue.Create(FormatColor(sampled)),
            _ => null
        };
    }

    private static bool TryReadValue(JsonNode? node, out RekallAgeCubicAnimationValueKind kind, out double[] components)
    {
        if (TryReadFiniteNumber(node, out var scalar))
        {
            kind = RekallAgeCubicAnimationValueKind.Scalar;
            components = [scalar];
            return true;
        }
        if (node is JsonArray array && TryReadFlatVector(array, null, out components))
        {
            kind = RekallAgeCubicAnimationValueKind.Vector;
            return true;
        }
        if (TryReadColor(node, out components))
        {
            kind = RekallAgeCubicAnimationValueKind.Color;
            return true;
        }
        kind = default;
        components = [];
        return false;
    }

    private static bool TryReadTangent(JsonNode? node, RekallAgeCubicAnimationValueKind kind, int count, out double[] tangent)
    {
        if (kind == RekallAgeCubicAnimationValueKind.Scalar && TryReadFiniteNumber(node, out var scalar))
        {
            tangent = [scalar];
            return true;
        }
        if (kind is RekallAgeCubicAnimationValueKind.Vector or RekallAgeCubicAnimationValueKind.Color
            && node is JsonArray array && TryReadFlatVector(array, count, out tangent)) return true;
        tangent = [];
        return false;
    }

    private static bool TryReadFlatVector(JsonArray array, int? exactCount, out double[] values)
    {
        if (array.Count == 0 || array.Count > MaximumVectorComponents || (exactCount.HasValue && array.Count != exactCount.Value))
        {
            values = [];
            return false;
        }
        var parsed = new double[array.Count];
        for (var index = 0; index < array.Count; index++)
        {
            if (!TryReadFiniteNumber(array[index], out parsed[index]))
            {
                values = [];
                return false;
            }
        }
        values = parsed;
        return true;
    }

    private static bool TryReadColor(JsonNode? node, out double[] channels)
    {
        channels = [];
        if (node is not JsonValue value || !value.TryGetValue<string>(out var text)
            || text.Length is not (7 or 9) || text[0] != '#') return false;
        var count = text.Length == 9 ? 4 : 3;
        var parsed = new double[count];
        for (var index = 0; index < count; index++)
        {
            if (!byte.TryParse(text.AsSpan(1 + index * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var channel))
                return false;
            parsed[index] = channel;
        }
        channels = parsed;
        return true;
    }

    private static string FormatColor(IReadOnlyList<double> channels)
    {
        static byte Channel(double value) => (byte)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0, 255);
        var color = $"#{Channel(channels[0]):x2}{Channel(channels[1]):x2}{Channel(channels[2]):x2}";
        return channels.Count == 4 ? $"{color}{Channel(channels[3]):x2}" : color;
    }

    private static bool TryReadFiniteNumber(JsonNode? node, out double number)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue<double>(out number)) return double.IsFinite(number);
            if (value.TryGetValue<int>(out var integer)) { number = integer; return true; }
            if (value.TryGetValue<long>(out var longInteger)) { number = longInteger; return true; }
        }
        number = 0;
        return false;
    }

    private static JsonNode? Property(JsonObject item, string name)
    {
        if (item.TryGetPropertyValue(name, out var exact)) return exact;
        return item.FirstOrDefault(pair => pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
    }
}
