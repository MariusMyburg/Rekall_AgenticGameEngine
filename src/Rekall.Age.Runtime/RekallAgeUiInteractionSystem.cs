using System.Text.Json.Nodes;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Runtime;

public sealed class RekallAgeUiInteractionSystem : IRekallAgeRuntimeWorldSystem
{
    private const string CanvasType = "Rekall.UiCanvas";
    private const string LayoutType = "Rekall.UiLayoutState";
    private const string InputStateType = "Rekall.UiInputState";
    private static readonly string[] ElementTypes = ["Rekall.UiElement", "Rekall.Button", "Rekall.Label", "Rekall.Panel"];

    public string Id => "runtime.ui.interaction";

    public int Priority => 20;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeWorldFrameContext context)
    {
        if (context.Input.ViewportWidth <= 0 || context.Input.ViewportHeight <= 0)
        {
            return ValueTask.FromResult(world);
        }

        var canvas = world.Entities
            .Where(entity => entity.Components.Any(component => component.Type == CanvasType))
            .OrderByDescending(entity => ReadNumber(
                entity.Components.First(component => component.Type == CanvasType).Properties,
                "layer",
                0))
            .ThenBy(entity => entity.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (canvas is null)
        {
            return ValueTask.FromResult(world);
        }

        var canvasComponent = canvas.Components.First(component => component.Type == CanvasType);
        var referenceWidth = Math.Max(1, ReadNumber(canvasComponent.Properties, "referenceWidth", 1920));
        var referenceHeight = Math.Max(1, ReadNumber(canvasComponent.Properties, "referenceHeight", 1080));
        var pointerX = context.Input.MouseX * referenceWidth / context.Input.ViewportWidth;
        var pointerY = context.Input.MouseY * referenceHeight / context.Input.ViewportHeight;
        var canvasLayers = world.Entities
            .Where(entity => entity.Components.Any(component => component.Type == CanvasType))
            .ToDictionary(
                entity => entity.Id,
                entity => ReadNumber(entity.Components.First(component => component.Type == CanvasType).Properties, "layer", 0),
                StringComparer.Ordinal);
        var hit = world.Entities
            .Where(entity => entity.Visible && IsInteractive(entity))
            .Select(entity => (Entity: entity, Layout: ReadLayout(entity)))
            .Where(item => item.Layout is not null && Contains(item.Layout, context.Input))
            .OrderByDescending(item => canvasLayers.GetValueOrDefault(item.Layout!.CanvasEntityId))
            .ThenByDescending(item => Depth(world, item.Entity))
            .ThenByDescending(item => item.Entity.Id, StringComparer.Ordinal)
            .Select(item => item.Entity)
            .FirstOrDefault();
        var previousState = canvas.Components.FirstOrDefault(component => component.Type == InputStateType)?.Properties;
        var previousHovered = ReadString(previousState, "hoveredEntityId");
        var previousPressed = ReadString(previousState, "pressedEntityId");
        var previousFocused = ReadString(previousState, "focusedEntityId");
        var events = new List<RekallAgeRuntimeEvent>();

        if (!string.Equals(previousHovered, hit?.Id, StringComparison.Ordinal))
        {
            if (previousHovered is not null && world.Entities.FirstOrDefault(entity => entity.Id == previousHovered) is { } left)
            {
                AddBoundEvents(events, "pointer.leave", left, context, pointerX, pointerY);
            }

            if (hit is not null)
            {
                AddBoundEvents(events, "pointer.enter", hit, context, pointerX, pointerY);
            }
        }

        if (hit is not null)
        {
            AddBoundEvents(events, "pointer.hit", hit, context, pointerX, pointerY);
        }

        var pressed = previousPressed;
        var focused = previousFocused;
        if (Contains(context.Input.PressedButtonsThisFrame, "Left"))
        {
            pressed = hit?.Id;
            if (hit is not null)
            {
                AddBoundEvents(events, "pointer.down", hit, context, pointerX, pointerY);
            }
        }

        if (Contains(context.Input.ReleasedButtonsThisFrame, "Left"))
        {
            if (hit is not null)
            {
                AddBoundEvents(events, "pointer.up", hit, context, pointerX, pointerY);
                if (previousPressed == hit.Id)
                {
                    AddBoundEvents(events, "pointer.click", hit, context, pointerX, pointerY);
                    if (focused != hit.Id)
                    {
                        focused = hit.Id;
                        AddBoundEvents(events, "ui.focus", hit, context, pointerX, pointerY);
                    }
                }
            }

            pressed = null;
        }

        var state = new JsonObject
        {
            ["pointerX"] = pointerX,
            ["pointerY"] = pointerY
        };
        SetOptional(state, "hoveredEntityId", hit?.Id);
        SetOptional(state, "pressedEntityId", pressed);
        SetOptional(state, "focusedEntityId", focused);
        var updatedCanvas = canvas with
        {
            Components = canvas.Components
                .Where(component => component.Type != InputStateType)
                .Append(new RekallAgeRuntimeComponent(InputStateType, state))
                .OrderBy(component => component.Type, StringComparer.Ordinal)
                .ToArray()
        };
        return ValueTask.FromResult(world with
        {
            Entities = world.Entities
                .Select(entity => entity.Id == canvas.Id ? updatedCanvas : entity)
                .ToArray(),
            Subsystems = world.Subsystems with
            {
                Events = new RekallAgeRuntimeEventView(
                    world.Subsystems.Events.Events
                        .Concat(events)
                        .OrderBy(item => item.Frame)
                        .ThenBy(item => item.EntityName, StringComparer.Ordinal)
                        .ThenBy(item => item.Type, StringComparer.Ordinal)
                        .ToArray())
            }
        });
    }

    private static bool IsInteractive(RekallAgeRuntimeEntity entity)
    {
        var component = entity.Components.FirstOrDefault(item => ElementTypes.Contains(item.Type, StringComparer.Ordinal));
        return component is not null &&
            ReadBoolean(component.Properties, "active", true) &&
            ReadBoolean(component.Properties, "interactive", component.Type == "Rekall.Button");
    }

    private static LayoutRect? ReadLayout(RekallAgeRuntimeEntity entity)
    {
        var component = entity.Components.FirstOrDefault(item => item.Type == LayoutType);
        return component is null
            ? null
            : new LayoutRect(
                ReadString(component.Properties, "canvasEntityId") ?? string.Empty,
                ReadNumber(component.Properties, "referenceWidth", 1920),
                ReadNumber(component.Properties, "referenceHeight", 1080),
                ReadNumber(component.Properties, "clipX", 0),
                ReadNumber(component.Properties, "clipY", 0),
                ReadNumber(component.Properties, "clipWidth", 0),
                ReadNumber(component.Properties, "clipHeight", 0));
    }

    private static bool Contains(LayoutRect? rect, RekallAgeRuntimeInputState input)
    {
        if (rect is null)
        {
            return false;
        }

        var x = input.MouseX * rect.ReferenceWidth / input.ViewportWidth;
        var y = input.MouseY * rect.ReferenceHeight / input.ViewportHeight;
        return x >= rect.X && x < rect.X + rect.Width && y >= rect.Y && y < rect.Y + rect.Height;
    }

    private static int Depth(RekallAgeRuntimeWorld world, RekallAgeRuntimeEntity entity)
    {
        var depth = 0;
        var parent = entity.ParentId;
        while (parent is not null && depth < world.Entities.Count)
        {
            depth++;
            parent = world.Entities.FirstOrDefault(item => item.Id == parent)?.ParentId;
        }

        return depth;
    }

    private static void AddBoundEvents(
        List<RekallAgeRuntimeEvent> events,
        string eventType,
        RekallAgeRuntimeEntity entity,
        RekallAgeRuntimeWorldFrameContext context,
        double x,
        double y)
    {
        foreach (var binding in entity.Components
                     .Where(component => component.Type == "Rekall.EventBindings")
                     .SelectMany(component => component.Properties["events"] is JsonArray array ? array.OfType<JsonObject>() : []))
        {
            var type = ReadString(binding, "event") ?? ReadString(binding, "type");
            var handler = ReadString(binding, "handler");
            if (!ReadBoolean(binding, "active", true) || type?.Equals(eventType, StringComparison.OrdinalIgnoreCase) != true)
            {
                continue;
            }

            events.Add(new RekallAgeRuntimeEvent(
                context.FrameIndex,
                eventType,
                entity.Id,
                entity.Name,
                "runtime.ui",
                handler,
                new JsonObject { ["x"] = x, ["y"] = y, ["button"] = "Left" }));
        }
    }

    private static bool Contains(IReadOnlySet<string>? values, string value) =>
        values is not null && values.Contains(value);

    private static void SetOptional(JsonObject target, string name, string? value)
    {
        if (value is not null)
        {
            target[name] = value;
        }
    }

    private static string? ReadString(JsonObject? properties, string name) =>
        properties?[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : null;

    private static bool ReadBoolean(JsonObject properties, string name, bool fallback) =>
        properties[name] is JsonValue value && value.TryGetValue<bool>(out var boolean) ? boolean : fallback;

    private static double ReadNumber(JsonObject properties, string name, double fallback)
    {
        if (properties[name] is not JsonValue value)
        {
            return fallback;
        }

        if (value.TryGetValue<double>(out var number)) return number;
        if (value.TryGetValue<int>(out var integer)) return integer;
        if (value.TryGetValue<long>(out var longInteger)) return longInteger;
        return fallback;
    }

    private sealed record LayoutRect(
        string CanvasEntityId,
        double ReferenceWidth,
        double ReferenceHeight,
        double X,
        double Y,
        double Width,
        double Height);
}
