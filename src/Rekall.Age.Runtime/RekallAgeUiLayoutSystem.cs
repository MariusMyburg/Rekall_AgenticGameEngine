using System.Globalization;
using System.Text.Json.Nodes;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Runtime;

public sealed class RekallAgeUiLayoutSystem : IRekallAgeRuntimeWorldSystem
{
    private const string StateType = "Rekall.UiLayoutState";
    private static readonly string[] ElementTypes = ["Rekall.UiElement", "Rekall.Button", "Rekall.Label", "Rekall.Panel", "Rekall.Image"];

    public string Id => "runtime.ui";

    public int Priority => 0;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeWorldFrameContext context)
    {
        var entitiesById = world.Entities.ToDictionary(entity => entity.Id, StringComparer.Ordinal);
        var canvasById = world.Entities
            .Select(entity => (Entity: entity, Component: entity.Components.FirstOrDefault(component => component.Type == "Rekall.UiCanvas")))
            .Where(item => item.Component is not null)
            .ToDictionary(
                item => item.Entity.Id,
                item => CreateCanvasLayout(item.Entity, item.Component!),
                StringComparer.Ordinal);
        var defaultCanvas = canvasById.Values
            .OrderBy(canvas => canvas.CanvasEntityId, StringComparer.Ordinal)
            .FirstOrDefault();
        var resolved = new Dictionary<string, RekallAgeRuntimeUiLayout>(StringComparer.Ordinal);
        var resolving = new HashSet<string>(StringComparer.Ordinal);

        RekallAgeRuntimeUiLayout? Resolve(string entityId)
        {
            if (resolved.TryGetValue(entityId, out var cached))
            {
                return cached;
            }

            if (canvasById.TryGetValue(entityId, out var canvas))
            {
                return canvas;
            }

            if (!entitiesById.TryGetValue(entityId, out var entity) || !resolving.Add(entityId))
            {
                return null;
            }

            var element = entity.Components.FirstOrDefault(component => ElementTypes.Contains(component.Type, StringComparer.Ordinal));
            if (element is null || defaultCanvas is null)
            {
                resolving.Remove(entityId);
                return null;
            }

            var parent = entity.ParentId is not null ? Resolve(entity.ParentId) : defaultCanvas;
            parent ??= defaultCanvas;
            var layout = CreateElementLayout(parent, element.Properties);
            resolving.Remove(entityId);
            resolved[entityId] = layout;
            return layout;
        }

        var updated = world.Entities.Select(entity =>
        {
            var element = entity.Components.FirstOrDefault(component => ElementTypes.Contains(component.Type, StringComparer.Ordinal));
            var layout = element is null ? null : Resolve(entity.Id);
            if (layout is null)
            {
                return entity;
            }

            var state = new RekallAgeRuntimeComponent(
                StateType,
                new JsonObject
                {
                    ["canvasEntityId"] = layout.CanvasEntityId,
                    ["referenceWidth"] = layout.ReferenceWidth,
                    ["referenceHeight"] = layout.ReferenceHeight,
                    ["x"] = layout.X,
                    ["y"] = layout.Y,
                    ["width"] = layout.Width,
                    ["height"] = layout.Height,
                    ["clipX"] = layout.ClipX,
                    ["clipY"] = layout.ClipY,
                    ["clipWidth"] = layout.ClipWidth,
                    ["clipHeight"] = layout.ClipHeight
                });
            return entity with
            {
                Components = entity.Components
                    .Where(component => component.Type != StateType)
                    .Append(state)
                    .OrderBy(component => component.Type, StringComparer.Ordinal)
                    .ToArray()
            };
        }).ToArray();
        return ValueTask.FromResult(world with { Entities = updated });
    }

    private static RekallAgeRuntimeUiLayout CreateCanvasLayout(
        RekallAgeRuntimeEntity entity,
        RekallAgeRuntimeComponent canvas)
    {
        var width = Math.Max(1, ReadNumber(canvas.Properties, "referenceWidth", 1920));
        var height = Math.Max(1, ReadNumber(canvas.Properties, "referenceHeight", 1080));
        return new RekallAgeRuntimeUiLayout(entity.Id, width, height, 0, 0, width, height, 0, 0, width, height);
    }

    private static RekallAgeRuntimeUiLayout CreateElementLayout(
        RekallAgeRuntimeUiLayout parent,
        JsonObject properties)
    {
        var width = Math.Max(0, ReadNumber(properties, "width", 100));
        var height = Math.Max(0, ReadNumber(properties, "height", 40));
        var anchorX = Math.Clamp(ReadNumber(properties, "anchorX", 0), 0, 1);
        var anchorY = Math.Clamp(ReadNumber(properties, "anchorY", 0), 0, 1);
        var pivotX = Math.Clamp(ReadNumber(properties, "pivotX", 0), 0, 1);
        var pivotY = Math.Clamp(ReadNumber(properties, "pivotY", 0), 0, 1);
        var x = parent.X + parent.Width * anchorX + ReadNumber(properties, "x", 0) - width * pivotX;
        var y = parent.Y + parent.Height * anchorY + ReadNumber(properties, "y", 0) - height * pivotY;

        if (TryGetPropertyValue(properties, "anchorMinX", out _) || TryGetPropertyValue(properties, "anchorMaxX", out _))
        {
            var left = parent.X + parent.Width * Math.Clamp(ReadNumber(properties, "anchorMinX", 0), 0, 1) + ReadNumber(properties, "offsetLeft", 0);
            var right = parent.X + parent.Width * Math.Clamp(ReadNumber(properties, "anchorMaxX", 1), 0, 1) + ReadNumber(properties, "offsetRight", 0);
            x = Math.Min(left, right);
            width = Math.Max(0, right - left);
        }

        if (TryGetPropertyValue(properties, "anchorMinY", out _) || TryGetPropertyValue(properties, "anchorMaxY", out _))
        {
            var top = parent.Y + parent.Height * Math.Clamp(ReadNumber(properties, "anchorMinY", 0), 0, 1) + ReadNumber(properties, "offsetTop", 0);
            var bottom = parent.Y + parent.Height * Math.Clamp(ReadNumber(properties, "anchorMaxY", 1), 0, 1) + ReadNumber(properties, "offsetBottom", 0);
            y = Math.Min(top, bottom);
            height = Math.Max(0, bottom - top);
        }

        var clipX = Math.Max(x, parent.ClipX);
        var clipY = Math.Max(y, parent.ClipY);
        var clipRight = Math.Min(x + width, parent.ClipX + parent.ClipWidth);
        var clipBottom = Math.Min(y + height, parent.ClipY + parent.ClipHeight);
        return new RekallAgeRuntimeUiLayout(
            parent.CanvasEntityId,
            parent.ReferenceWidth,
            parent.ReferenceHeight,
            x,
            y,
            width,
            height,
            clipX,
            clipY,
            Math.Max(0, clipRight - clipX),
            Math.Max(0, clipBottom - clipY));
    }

    private static double ReadNumber(JsonObject properties, string name, double fallback)
    {
        if (!TryGetPropertyValue(properties, name, out var node) || node is not JsonValue value)
        {
            return fallback;
        }

        if (value.TryGetValue<double>(out var number))
        {
            return number;
        }

        if (value.TryGetValue<int>(out var integer))
        {
            return integer;
        }

        if (value.TryGetValue<long>(out var longInteger))
        {
            return longInteger;
        }

        return value.TryGetValue<string>(out var text) &&
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    private static bool TryGetPropertyValue(JsonObject properties, string name, out JsonNode? value)
    {
        foreach (var property in properties)
        {
            if (property.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = null;
        return false;
    }
}
