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
        var observations = new List<RekallAgeRuntimeObservation>();
        foreach (var entity in world.Entities)
        {
            foreach (var component in entity.Components.Where(component =>
                         component.Type.Equals("Rekall.InputActionMap", StringComparison.Ordinal)))
            {
                if (!ReadBoolean(component.Properties, "active", true))
                {
                    continue;
                }

                if (!TryGetPropertyValue(component.Properties, "actions", out var node)
                    || node is not JsonArray actionNodes)
                {
                    observations.Add(new RekallAgeRuntimeObservation(
                        context.FrameIndex,
                        "runtime.input.action_map_actions_invalid",
                        "error",
                        "input",
                        entity.Id,
                        entity.Name,
                        Id,
                        "Rekall.InputActionMap.Actions must be a native JSON array, not a JSON-encoded string. Use bindings such as [{\"name\":\"move.horizontal\",\"positiveKey\":\"D\",\"negativeKey\":\"A\"}].",
                        ["rekall.component.set_property", "rekall.module.search_component_schemas"]));
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

        var declaredNames = actions
            .Select(action => action.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        foreach (var sample in context.Input.SemanticActions ?? [])
        {
            if (string.IsNullOrWhiteSpace(sample.Name)
                || declaredNames.Contains(sample.Name.Trim(), StringComparer.Ordinal))
            {
                continue;
            }

            observations.Add(new RekallAgeRuntimeObservation(
                context.FrameIndex,
                "runtime.input.semantic_action_undeclared",
                "error",
                "input",
                sample.Name.Trim(),
                sample.Name.Trim(),
                Id,
                $"Injected semantic action '{sample.Name.Trim()}' has no exact declaration in an active Rekall.InputActionMap. Declared actions: {(declaredNames.Length == 0 ? "(none)" : string.Join(", ", declaredNames))}.",
                ["rekall.entity.inspect", "rekall.component.set_property", "rekall.module.search_component_schemas"]));
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
                {
                    Controllers = (context.Input.Controllers ?? [])
                        .OrderBy(controller => controller.PlayerIndex)
                        .ThenBy(controller => controller.DeviceId, StringComparer.Ordinal)
                        .ToArray()
                }
            },
            Observations = world.Observations.Concat(observations).ToArray()
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
        string? physicalDeviceId = null;
        string? physicalDeviceKind = null;

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

        foreach (var controller in MatchingControllers(definition, input))
        {
            var before = value;
            ApplyControllerBindings(
                definition,
                controller,
                ref value,
                ref isDown,
                ref wasPressed,
                ref wasReleased);
            if (Math.Abs(value - before) > 0.000001 || ControllerHasEdge(definition, controller))
            {
                physicalDeviceId ??= controller.DeviceId;
                physicalDeviceKind ??= controller.Kind;
            }
        }

        var semanticSample = input.SemanticActions?.FirstOrDefault(sample =>
            !string.IsNullOrWhiteSpace(sample.Name)
            && sample.Name.Trim().Equals(name.Trim(), StringComparison.Ordinal));
        if (semanticSample is not null)
        {
            value = semanticSample.Value;
            isDown = semanticSample.IsDown;
            wasPressed = semanticSample.WasPressed;
            wasReleased = semanticSample.WasReleased;
        }

        return new RekallAgeRuntimeInputAction(
            name.Trim(),
            value,
            isDown,
            wasPressed,
            wasReleased,
            entity.Id,
            entity.Name)
        {
            PhysicalDeviceId = physicalDeviceId,
            PhysicalDeviceKind = physicalDeviceKind
        };
    }

    private static IEnumerable<RekallAgeRuntimeControllerState> MatchingControllers(
        JsonObject definition,
        RekallAgeRuntimeInputState input)
    {
        var deviceId = ReadString(definition, "deviceId");
        var deviceKind = ReadString(definition, "deviceKind");
        var playerIndex = ReadNullableInteger(definition, "playerIndex");
        return (input.Controllers ?? [])
            .Where(controller => string.IsNullOrWhiteSpace(deviceId)
                || controller.DeviceId.Equals(deviceId.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(controller => string.IsNullOrWhiteSpace(deviceKind)
                || controller.Kind.Equals(deviceKind.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(controller => playerIndex is null || controller.PlayerIndex == playerIndex)
            .OrderBy(controller => controller.DeviceId, StringComparer.Ordinal);
    }

    private static void ApplyControllerBindings(
        JsonObject definition,
        RekallAgeRuntimeControllerState controller,
        ref double value,
        ref bool isDown,
        ref bool wasPressed,
        ref bool wasReleased)
    {
        ApplyControllerButton(
            ReadFirstString(definition, "controllerButton", "gamepadButton", "joystickButton"),
            controller,
            1,
            ref value,
            ref isDown,
            ref wasPressed,
            ref wasReleased);
        ApplyControllerButton(
            ReadFirstString(definition, "positiveControllerButton", "positiveGamepadButton", "positiveJoystickButton"),
            controller,
            1,
            ref value,
            ref isDown,
            ref wasPressed,
            ref wasReleased);
        ApplyControllerButton(
            ReadFirstString(definition, "negativeControllerButton", "negativeGamepadButton", "negativeJoystickButton"),
            controller,
            -1,
            ref value,
            ref isDown,
            ref wasPressed,
            ref wasReleased);

        var axisName = ReadFirstString(definition, "controllerAxis", "gamepadAxis", "joystickAxis");
        if (!string.IsNullOrWhiteSpace(axisName))
        {
            var axis = controller.Axes.FirstOrDefault(candidate =>
                candidate.Name.Equals(axisName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (axis is not null)
            {
                var deadzone = Math.Clamp(ReadNumber(definition, "deadzone", 0), 0, 0.99);
                var saturation = Math.Clamp(ReadNumber(definition, "saturation", 1), deadzone + 0.000001, 1);
                var exponent = Math.Max(0.000001, ReadNumber(definition, "responseExponent", 1));
                var scale = ReadNumber(
                    definition,
                    "controllerAxisScale",
                    ReadNumber(definition, "axisScale", 1));
                var magnitude = Math.Abs(axis.Value);
                var normalized = magnitude <= deadzone
                    ? 0
                    : Math.Pow(Math.Clamp((magnitude - deadzone) / (saturation - deadzone), 0, 1), exponent);
                var contribution = Math.CopySign(normalized, axis.Value) * scale;
                if (ReadBoolean(definition, "invert", false))
                {
                    contribution = -contribution;
                }

                value += contribution;
                isDown |= Math.Abs(contribution) > 0.000001;
            }
        }

        var hatName = ReadString(definition, "controllerHat");
        var hatDirection = ReadString(definition, "controllerHatDirection");
        if (!string.IsNullOrWhiteSpace(hatName) && !string.IsNullOrWhiteSpace(hatDirection))
        {
            var hat = controller.Hats.FirstOrDefault(candidate =>
                candidate.Name.Equals(hatName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (hat is not null && HatMatches(hat, hatDirection))
            {
                value += 1;
                isDown = true;
            }
        }
    }

    private static bool ControllerHasEdge(
        JsonObject definition,
        RekallAgeRuntimeControllerState controller)
    {
        var names = new[]
        {
            ReadFirstString(definition, "controllerButton", "gamepadButton", "joystickButton"),
            ReadFirstString(definition, "positiveControllerButton", "positiveGamepadButton", "positiveJoystickButton"),
            ReadFirstString(definition, "negativeControllerButton", "negativeGamepadButton", "negativeJoystickButton")
        };
        return names.Where(name => !string.IsNullOrWhiteSpace(name)).Any(name =>
            Contains(controller.PressedButtonsThisFrame, name!)
            || Contains(controller.ReleasedButtonsThisFrame, name!));
    }

    private static void ApplyControllerButton(
        string? binding,
        RekallAgeRuntimeControllerState controller,
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

        if (Contains(controller.PressedButtons, binding))
        {
            value += contribution;
            isDown = true;
        }

        wasPressed |= Contains(controller.PressedButtonsThisFrame, binding);
        wasReleased |= Contains(controller.ReleasedButtonsThisFrame, binding);
    }

    private static bool HatMatches(RekallAgeRuntimeControllerHat hat, string direction) =>
        direction.Trim().ToLowerInvariant() switch
        {
            "up" => hat.Y > 0,
            "down" => hat.Y < 0,
            "left" => hat.X < 0,
            "right" => hat.X > 0,
            "up-left" or "upleft" => hat.X < 0 && hat.Y > 0,
            "up-right" or "upright" => hat.X > 0 && hat.Y > 0,
            "down-left" or "downleft" => hat.X < 0 && hat.Y < 0,
            "down-right" or "downright" => hat.X > 0 && hat.Y < 0,
            "centered" or "center" => hat.X == 0 && hat.Y == 0,
            _ => false
        };

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

    private static bool Contains(IReadOnlyList<string>? values, string value)
    {
        return values is not null && values.Any(candidate =>
            candidate.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static string? ReadFirstString(JsonObject properties, params string[] names) =>
        names.Select(name => ReadString(properties, name)).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static int? ReadNullableInteger(JsonObject properties, string name)
    {
        if (!TryGetPropertyValue(properties, name, out var node) || node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<int>(out var integer))
        {
            return integer;
        }

        return value.TryGetValue<string>(out var text) && int.TryParse(text, out integer)
            ? integer
            : null;
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
