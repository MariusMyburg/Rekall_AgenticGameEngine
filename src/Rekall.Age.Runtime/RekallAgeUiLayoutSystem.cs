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
        // Layout state is only ever written for entities resolved through a Rekall.UiCanvas;
        // with no UI components at all the pass rebuilds every entity to no effect.
        if (!RekallAgeRuntimeComponentPresence.AnyEntityHasPrefixed(world, "Rekall.Ui"))
        {
            return ValueTask.FromResult(world);
        }

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

            if (!entitiesById.TryGetValue(entityId, out var entity) || !resolving.Add(entityId))
            {
                return null;
            }

            var element = entity.Components.FirstOrDefault(component => ElementTypes.Contains(component.Type, StringComparer.Ordinal));
            if (element is null)
            {
                resolving.Remove(entityId);
                return canvasById.GetValueOrDefault(entityId);
            }
            if (defaultCanvas is null)
            {
                resolving.Remove(entityId);
                return null;
            }

            var parent = canvasById.GetValueOrDefault(entityId);
            if (parent is null && entity.ParentId is not null)
            {
                parent = canvasById.GetValueOrDefault(entity.ParentId) ?? Resolve(entity.ParentId);
            }
            parent ??= defaultCanvas;
            var parentEntity = entitiesById.GetValueOrDefault(entity.ParentId ?? parent.CanvasEntityId);
            var container = parentEntity?.Components.FirstOrDefault(component =>
                component.Type == "Rekall.UiCanvas" || ElementTypes.Contains(component.Type, StringComparer.Ordinal));
            var direction = container is null ? "none" : ReadString(container.Properties, "layoutDirection") ?? "none";
            var siblings = direction is "horizontal" or "vertical"
                ? world.Entities
                    .Where(candidate => candidate.ParentId == entity.ParentId)
                    .Select(candidate => new
                    {
                        candidate.Id,
                        Component = candidate.Components.FirstOrDefault(component => ElementTypes.Contains(component.Type, StringComparer.Ordinal))
                    })
                    .Where(candidate => candidate.Component is not null)
                    .OrderBy(candidate => ReadNumber(candidate.Component!.Properties, "layoutOrder", 0))
                    .ThenBy(candidate => candidate.Id, StringComparer.Ordinal)
                    .ToArray()
                : [];
            var preceding = siblings.TakeWhile(candidate => candidate.Id != entity.Id)
                .Select(candidate => candidate.Component!.Properties)
                .ToArray();
            var layout = CreateElementLayout(parent, element.Properties, container?.Properties, direction, preceding);
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
        JsonObject properties,
        JsonObject? containerProperties,
        string layoutDirection,
        IReadOnlyList<JsonObject> precedingSiblings)
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
            var anchorMinX = Math.Clamp(ReadNumber(properties, "anchorMinX", 0), 0, 1);
            var anchorMaxX = Math.Clamp(ReadNumber(properties, "anchorMaxX", 1), 0, 1);
            if (anchorMinX == anchorMaxX)
            {
                x = parent.X + parent.Width * anchorMinX + ReadNumber(properties, "x", 0) - width * pivotX;
            }
            else
            {
                var left = parent.X + parent.Width * anchorMinX + ReadNumber(properties, "offsetLeft", 0);
                var right = parent.X + parent.Width * anchorMaxX + ReadNumber(properties, "offsetRight", 0);
                x = Math.Min(left, right);
                width = Math.Max(0, right - left);
            }
        }

        if (TryGetPropertyValue(properties, "anchorMinY", out _) || TryGetPropertyValue(properties, "anchorMaxY", out _))
        {
            var anchorMinY = Math.Clamp(ReadNumber(properties, "anchorMinY", 0), 0, 1);
            var anchorMaxY = Math.Clamp(ReadNumber(properties, "anchorMaxY", 1), 0, 1);
            if (anchorMinY == anchorMaxY)
            {
                y = parent.Y + parent.Height * anchorMinY + ReadNumber(properties, "y", 0) - height * pivotY;
            }
            else
            {
                var top = parent.Y + parent.Height * anchorMinY + ReadNumber(properties, "offsetTop", 0);
                var bottom = parent.Y + parent.Height * anchorMaxY + ReadNumber(properties, "offsetBottom", 0);
                y = Math.Min(top, bottom);
                height = Math.Max(0, bottom - top);
            }
        }

        if (containerProperties is not null && layoutDirection is "horizontal" or "vertical")
        {
            var paddingLeft = Math.Max(0, ReadNumber(containerProperties, "paddingLeft", 0));
            var paddingTop = Math.Max(0, ReadNumber(containerProperties, "paddingTop", 0));
            var paddingRight = Math.Max(0, ReadNumber(containerProperties, "paddingRight", 0));
            var paddingBottom = Math.Max(0, ReadNumber(containerProperties, "paddingBottom", 0));
            var gap = Math.Max(0, ReadNumber(containerProperties, "gap", 0));
            var contentX = parent.X + paddingLeft;
            var contentY = parent.Y + paddingTop;
            var contentWidth = Math.Max(0, parent.Width - paddingLeft - paddingRight);
            var contentHeight = Math.Max(0, parent.Height - paddingTop - paddingBottom);
            if (layoutDirection == "vertical")
            {
                y = contentY + precedingSiblings.Sum(sibling => Math.Max(0, ReadNumber(sibling, "height", 40)))
                    + gap * precedingSiblings.Count + ReadNumber(properties, "y", 0);
                (x, width) = Align(contentX, contentWidth, width, ReadString(properties, "horizontalAlignment"), ReadNumber(properties, "x", 0));
            }
            else
            {
                x = contentX + precedingSiblings.Sum(sibling => Math.Max(0, ReadNumber(sibling, "width", 100)))
                    + gap * precedingSiblings.Count + ReadNumber(properties, "x", 0);
                (y, height) = Align(contentY, contentHeight, height, ReadString(properties, "verticalAlignment"), ReadNumber(properties, "y", 0));
            }
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

    private static (double Position, double Size) Align(
        double contentPosition,
        double contentSize,
        double size,
        string? alignment,
        double offset) => alignment?.ToLowerInvariant() switch
        {
            "center" => (contentPosition + (contentSize - size) / 2 + offset, size),
            "end" => (contentPosition + contentSize - size + offset, size),
            "stretch" => (contentPosition + offset, Math.Max(0, contentSize - offset)),
            _ => (contentPosition + offset, size)
        };

    private static string? ReadString(JsonObject properties, string name) =>
        TryGetPropertyValue(properties, name, out var node)
            && node is JsonValue value
            && value.TryGetValue<string>(out var text)
            && !string.IsNullOrWhiteSpace(text)
                ? text.Trim().ToLowerInvariant()
                : null;

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
