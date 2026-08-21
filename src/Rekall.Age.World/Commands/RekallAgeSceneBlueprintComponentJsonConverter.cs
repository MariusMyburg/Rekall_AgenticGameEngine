using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rekall.Age.World.Commands;

public sealed class RekallAgeSceneBlueprintComponentJsonConverter
    : JsonConverter<RekallAgeSceneBlueprintComponent>
{
    private const int MaxProperties = 1_024;

    public override RekallAgeSceneBlueprintComponent Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (JsonNode.Parse(ref reader) is not JsonObject value)
        {
            throw new JsonException("Scene blueprint components must be JSON objects.");
        }

        var typeEntry = FindDiscriminator(value);
        if (typeEntry is null)
        {
            return new RekallAgeSceneBlueprintComponent(string.Empty, new JsonObject());
        }

        if (typeEntry.Value.Value is not JsonValue typeValue
            || !typeValue.TryGetValue<string>(out var componentType)
            || string.IsNullOrWhiteSpace(componentType))
        {
            throw new JsonException("Every scene blueprint component requires a non-empty string type.");
        }

        var propertiesEntry = FindOptionalEntry(value, "properties");
        var properties = propertiesEntry is null
            ? new JsonObject()
            : ReadProperties(propertiesEntry.Value.Value);

        foreach (var entry in value)
        {
            if (entry.Key.Equals(typeEntry.Value.Key, StringComparison.Ordinal)
                || propertiesEntry is not null
                && entry.Key.Equals(propertiesEntry.Value.Key, StringComparison.Ordinal))
            {
                continue;
            }

            AddProperty(properties, entry.Key, entry.Value);
        }

        if (properties.Count > MaxProperties)
        {
            throw new JsonException(
                $"Scene blueprint components cannot contain more than {MaxProperties} properties.");
        }

        return new RekallAgeSceneBlueprintComponent(componentType.Trim(), properties);
    }

    public override void Write(
        Utf8JsonWriter writer,
        RekallAgeSceneBlueprintComponent value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("type", value.Type);
        writer.WritePropertyName("properties");
        JsonSerializer.Serialize(writer, value.Properties ?? new JsonObject(), options);
        writer.WriteEndObject();
    }

    private static KeyValuePair<string, JsonNode?>? FindDiscriminator(JsonObject value)
    {
        var typeNameMatches = value
            .Where(entry => entry.Key.Equals("typeName", StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        if (typeNameMatches.Length > 1)
        {
            throw new JsonException("Scene blueprint component contains ambiguous typeName fields.");
        }

        var exact = value.FirstOrDefault(entry => entry.Key.Equals("type", StringComparison.Ordinal));
        if (!string.IsNullOrEmpty(exact.Key))
        {
            if (typeNameMatches.Length > 0)
            {
                throw new JsonException(
                    "Scene blueprint component cannot contain both type and typeName fields.");
            }
            return exact;
        }

        var matches = value
            .Where(entry => entry.Key.Equals("type", StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        if (matches.Length > 0 && typeNameMatches.Length > 0)
        {
            throw new JsonException(
                "Scene blueprint component cannot contain both type and typeName fields.");
        }

        return matches.Length switch
        {
            1 => matches[0],
            0 when typeNameMatches.Length == 1 => typeNameMatches[0],
            0 => null,
            _ => throw new JsonException("Scene blueprint component contains ambiguous type fields.")
        };
    }

    private static KeyValuePair<string, JsonNode?>? FindOptionalEntry(JsonObject value, string name)
    {
        var exact = value.FirstOrDefault(entry => entry.Key.Equals(name, StringComparison.Ordinal));
        if (!string.IsNullOrEmpty(exact.Key))
        {
            return exact;
        }

        var matches = value
            .Where(entry => entry.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        return matches.Length switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new JsonException($"Scene blueprint component contains ambiguous {name} fields.")
        };
    }

    private static JsonObject ReadProperties(JsonNode? value)
    {
        if (value is null)
        {
            return new JsonObject();
        }

        if (value is JsonObject objectProperties)
        {
            var result = new JsonObject();
            foreach (var property in objectProperties)
            {
                AddProperty(result, property.Key, property.Value);
            }
            return result;
        }

        if (value is not JsonArray entries)
        {
            throw new JsonException(
                "Scene blueprint component properties must be an object or a name/value array.");
        }

        var properties = new JsonObject();
        for (var index = 0; index < entries.Count; index++)
        {
            if (entries[index] is not JsonObject entry)
            {
                throw new JsonException($"Component properties[{index}] must be a name/value object.");
            }

            var nameEntry = FindOptionalEntry(entry, "name");
            var valueEntry = FindOptionalEntry(entry, "value");
            if (nameEntry?.Value is not JsonValue nameValue
                || !nameValue.TryGetValue<string>(out var name)
                || string.IsNullOrWhiteSpace(name)
                || valueEntry is null)
            {
                throw new JsonException(
                    $"Component properties[{index}] requires one non-empty name and a value.");
            }

            if (entry.Count != 2)
            {
                throw new JsonException(
                    $"Component properties[{index}] contains fields other than name and value.");
            }

            AddProperty(properties, name.Trim(), valueEntry.Value.Value);
        }

        return properties;
    }

    private static void AddProperty(JsonObject properties, string name, JsonNode? value)
    {
        var existing = properties.FirstOrDefault(entry =>
            entry.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(existing.Key))
        {
            throw new JsonException(
                $"Scene blueprint component contains conflicting property '{name}'.");
        }

        properties[name] = value?.DeepClone();
    }
}
