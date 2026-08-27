using System.Text.Json.Nodes;
using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Runtime;

/// <summary>
/// The one shared collision-layer matching rule, consumed identically by
/// <see cref="RekallAgeBepuPhysicsSystem"/>, <see cref="RekallAgeCollisionEventSystem"/>,
/// and <see cref="RekallAgeTriggerEventSystem"/> so physical response and event facts can
/// never drift from each other.
/// </summary>
public static class RekallAgeCollisionFilter
{
    private const string ComponentType = "Rekall.CollisionFilter";
    private const string DefaultLayer = "default";

    public static bool Allows(RekallAgeRuntimeEntity a, RekallAgeRuntimeEntity b)
    {
        var left = Rule.From(a);
        var right = Rule.From(b);
        return left.Accepts(right.Layer) && right.Accepts(left.Layer);
    }

    public readonly record struct Rule(string Layer, IReadOnlySet<string>? CollidesWith)
    {
        /// <summary>The "no filter" rule: collides with every layer. Used as the fallback for
        /// any collidable that never had a <see cref="Rule"/> explicitly recorded for it.</summary>
        public static Rule Default { get; } = new(DefaultLayer, null);

        public static Rule From(RekallAgeRuntimeEntity entity)
        {
            var component = entity.FindComponent(ComponentType);
            if (component is null)
            {
                return new Rule(DefaultLayer, null);
            }

            var layer = ReadString(component.Properties, "layer") is { Length: > 0 } value
                ? value
                : DefaultLayer;
            var collidesWith = ReadStringArray(component.Properties, "collidesWith");
            return new Rule(layer, collidesWith is { Count: > 0 } ? collidesWith : null);
        }

        public bool Accepts(string otherLayer) =>
            CollidesWith is null || CollidesWith.Contains(otherLayer);
    }

    private static string? ReadString(JsonObject properties, string name)
    {
        return TryGetPropertyValue(properties, name, out var node)
            && node is JsonValue value
            && value.TryGetValue<string>(out var text)
            ? text
            : null;
    }

    private static IReadOnlySet<string>? ReadStringArray(JsonObject properties, string name)
    {
        if (!TryGetPropertyValue(properties, name, out var node) || node is not JsonArray array)
        {
            return null;
        }

        return array
            .OfType<JsonValue>()
            .Select(value => value.TryGetValue<string>(out var text) ? text : null)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text!)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool TryGetPropertyValue(JsonObject properties, string name, out JsonNode? node)
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
}
