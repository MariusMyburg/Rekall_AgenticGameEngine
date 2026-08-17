using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rekall.Age.Core.Commands;

internal static class RekallAgeCommandJsonArgumentNormalizer
{
    private const int MaxDepth = 32;
    private const int MaxEncodedJsonCharacters = 1_000_000;

    public static string Normalize(string json, Type requestType)
    {
        var root = JsonNode.Parse(json)
            ?? throw new JsonException("Command arguments cannot be JSON null.");
        var normalized = NormalizeNode(root, requestType, 0);
        return normalized.ToJsonString();
    }

    private static JsonNode NormalizeNode(JsonNode node, Type expectedType, int depth)
    {
        if (depth > MaxDepth)
        {
            throw new JsonException($"Command arguments exceed the normalization depth limit of {MaxDepth}.");
        }

        var type = Nullable.GetUnderlyingType(expectedType) ?? expectedType;
        if (type == typeof(string) || typeof(JsonNode).IsAssignableFrom(type))
        {
            return node;
        }

        if (node is JsonValue scalar && scalar.TryGetValue<string>(out var text))
        {
            if (TryNormalizeScalar(text, type, out var normalizedScalar))
            {
                return normalizedScalar;
            }

            if (type != typeof(string)
                && (IsEnumerable(type) || IsObjectContract(type))
                && text.Length <= MaxEncodedJsonCharacters)
            {
                try
                {
                    if (JsonNode.Parse(text) is { } decoded)
                    {
                        return NormalizeNode(decoded, type, depth + 1);
                    }
                }
                catch (JsonException)
                {
                    // Preserve the original value so normal deserialization reports its exact path.
                }
            }
        }

        if (node is JsonArray array && IsEnumerable(type))
        {
            var itemType = GetEnumerableItemType(type) ?? typeof(object);
            for (var index = 0; index < array.Count; index++)
            {
                if (array[index] is not { } child)
                {
                    continue;
                }

                var normalized = NormalizeNode(child, itemType, depth + 1);
                if (!ReferenceEquals(child, normalized))
                {
                    array[index] = normalized;
                }
            }

            return array;
        }

        if (node is JsonObject value && IsObjectContract(type))
        {
            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                         .Where(property => property.GetMethod is not null && property.GetIndexParameters().Length == 0))
            {
                var match = value.FirstOrDefault(item => item.Key.Equals(property.Name, StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrEmpty(match.Key) || match.Value is null)
                {
                    continue;
                }

                var normalized = NormalizeNode(match.Value, property.PropertyType, depth + 1);
                if (!ReferenceEquals(match.Value, normalized))
                {
                    value[match.Key] = normalized;
                }
            }

            return value;
        }

        return node;
    }

    private static bool TryNormalizeScalar(string text, Type type, out JsonNode value)
    {
        if (type == typeof(bool) && bool.TryParse(text, out var boolean))
        {
            value = JsonValue.Create(boolean);
            return true;
        }
        if (type == typeof(int) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var int32))
        {
            value = JsonValue.Create(int32);
            return true;
        }
        if (type == typeof(long) && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var int64))
        {
            value = JsonValue.Create(int64);
            return true;
        }
        if (type == typeof(short) && short.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var int16))
        {
            value = JsonValue.Create(int16);
            return true;
        }
        if (type == typeof(uint) && uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var uint32))
        {
            value = JsonValue.Create(uint32);
            return true;
        }
        if (type == typeof(ulong) && ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var uint64))
        {
            value = JsonValue.Create(uint64);
            return true;
        }
        if (type == typeof(ushort) && ushort.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var uint16))
        {
            value = JsonValue.Create(uint16);
            return true;
        }
        if (type == typeof(double)
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue)
            && double.IsFinite(doubleValue))
        {
            value = JsonValue.Create(doubleValue);
            return true;
        }
        if (type == typeof(float)
            && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var singleValue)
            && float.IsFinite(singleValue))
        {
            value = JsonValue.Create(singleValue);
            return true;
        }
        if (type == typeof(decimal)
            && decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var decimalValue))
        {
            value = JsonValue.Create(decimalValue);
            return true;
        }

        value = null!;
        return false;
    }

    private static bool IsEnumerable(Type type) =>
        type != typeof(string) && typeof(System.Collections.IEnumerable).IsAssignableFrom(type);

    private static bool IsObjectContract(Type type) =>
        type.IsClass || (type.IsValueType && !type.IsPrimitive && !type.IsEnum);

    private static Type? GetEnumerableItemType(Type type)
    {
        if (type.IsArray)
        {
            return type.GetElementType();
        }

        return type.GetInterfaces()
            .Prepend(type)
            .FirstOrDefault(candidate =>
                candidate.IsGenericType
                && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            ?.GetGenericArguments()[0];
    }
}
