using System.Globalization;
using System.Text.Json.Nodes;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Runtime;

public sealed class RekallAgeInputActionSystem : IRekallAgeRuntimeWorldSystem
{
    public string Id => "runtime.input.actions";

    public int Priority => -1000;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeWorldFrameContext context)
    {
        var actions = new List<RekallAgeRuntimeInputAction>();
        foreach (var entity in world.Entities)
        {
            if (!entity.Visible)
            {
                continue;
            }

            foreach (var component in entity.Components.Where(component =>
                         component.Type.Equals("Rekall.InputActionMap", StringComparison.Ordinal)))
            {
                if (!ReadBoolean(component.Properties, "active", true)
                    || !TryGetPropertyValue(component.Properties, "actions", out var node)
                    || node is not JsonArray actionNodes)
                {
                    continue;
                }

                foreach (var actionNode in actionNodes.OfType<JsonObject>())
                {
                    if (MapAction(entity, actionNode, context.Input) is { } action)
                    {
                        actions.Add(action);
                    }
                }
            }
        }

        return ValueTask.FromResult(world with
        {
            Subsystems = world.Subsystems with
            {
                Input = new RekallAgeRuntimeInputView(
                    actions
                        .OrderBy(action => action.Name, StringComparer.Ordinal)
                        .ThenBy(action => action.SourceEntityName, StringComparer.Ordinal)
                        .ToArray())
            }
        });
    }

    private static RekallAgeRuntimeInputAction? MapAction(
        RekallAgeRuntimeEntity entity,
        JsonObject definition,
        RekallAgeRuntimeInputState input)
    {
        var name = ReadString(definition, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var value = 0.0;
        var isDown = false;
        var wasPressed = false;
        var wasReleased = false;

        ApplyDigitalBinding(
            ReadString(definition, "key"),
            input.PressedKeys,
            input.PressedKeysThisFrame,
            input.ReleasedKeysThisFrame,
            1,
            ref value,
            ref isDown,
            ref wasPressed,
            ref wasReleased);
        ApplyDigitalBinding(
            ReadString(definition, "button") ?? ReadString(definition, "mouseButton"),
            input.PressedButtons,
            input.PressedButtonsThisFrame,
            input.ReleasedButtonsThisFrame,
            1,
            ref value,
            ref isDown,
            ref wasPressed,
            ref wasReleased);
        ApplyDigitalBinding(
            ReadString(definition, "positiveKey"),
            input.PressedKeys,
            input.PressedKeysThisFrame,
            input.ReleasedKeysThisFrame,
            1,
            ref value,
            ref isDown,
            ref wasPressed,
            ref wasReleased);
        ApplyDigitalBinding(
            ReadString(definition, "negativeKey"),
            input.PressedKeys,
            input.PressedKeysThisFrame,
            input.ReleasedKeysThisFrame,
            -1,
            ref value,
            ref isDown,
            ref wasPressed,
            ref wasReleased);
        ApplyDigitalBinding(
            ReadString(definition, "positiveButton"),
            input.PressedButtons,
            input.PressedButtonsThisFrame,
            input.ReleasedButtonsThisFrame,
            1,
            ref value,
            ref isDown,
            ref wasPressed,
            ref wasReleased);
        ApplyDigitalBinding(
            ReadString(definition, "negativeButton"),
            input.PressedButtons,
            input.PressedButtonsThisFrame,
            input.ReleasedButtonsThisFrame,
            -1,
            ref value,
            ref isDown,
            ref wasPressed,
            ref wasReleased);

        var mouseWheelScale = ReadNumber(definition, "mouseWheelScale", 0);
        if (Math.Abs(mouseWheelScale) > 0.000001 && Math.Abs(input.MouseWheelDelta) > 0.000001)
        {
            value += input.MouseWheelDelta * mouseWheelScale;
            isDown = true;
            wasPressed = true;
        }

        var mouseAxis = ReadString(definition, "mouseAxis") ?? ReadString(definition, "mouseDeltaAxis");
        if (!string.IsNullOrWhiteSpace(mouseAxis))
        {
            var mouseValue = ReadMouseAxis(input, mouseAxis);
            var mouseScale = ReadNumber(definition, "mouseScale", 1);
            if (Math.Abs(mouseScale) > 0.000001 && Math.Abs(mouseValue) > 0.000001)
            {
                value += mouseValue * mouseScale;
                isDown = true;
                wasPressed = true;
            }
        }

        return new RekallAgeRuntimeInputAction(
            name.Trim(),
            value,
            isDown,
            wasPressed,
            wasReleased,
            entity.Id,
            entity.Name);
    }

    private static void ApplyDigitalBinding(
        string? binding,
        IReadOnlySet<string>? pressed,
        IReadOnlySet<string>? pressedThisFrame,
        IReadOnlySet<string>? releasedThisFrame,
        double contribution,
        ref double value,
        ref bool isDown,
        ref bool wasPressed,
        ref bool wasReleased)
    {
        if (string.IsNullOrWhiteSpace(binding))
        {
            return;
        }

        if (Contains(pressed, binding))
        {
            value += contribution;
            isDown = true;
        }

        wasPressed |= Contains(pressedThisFrame, binding);
        wasReleased |= Contains(releasedThisFrame, binding);
    }

    private static double ReadMouseAxis(RekallAgeRuntimeInputState input, string mouseAxis)
    {
        return mouseAxis.Trim().ToLowerInvariant() switch
        {
            "x" or "horizontal" or "deltax" or "mousex" => input.MouseDeltaX,
            "y" or "vertical" or "deltay" or "mousey" => input.MouseDeltaY,
            _ => 0
        };
    }

    private static bool Contains(IReadOnlySet<string>? values, string value)
    {
        return values is not null && values.Contains(value.Trim());
    }

    private static string? ReadString(JsonObject properties, string name)
    {
        return TryGetPropertyValue(properties, name, out var node)
            && node is JsonValue value
            && value.TryGetValue<string>(out var text)
            ? text
            : null;
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

        return value.TryGetValue<string>(out var text) && bool.TryParse(text, out var parsed)
            ? parsed
            : fallback;
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
}
