using System.Globalization;
using System.Text.Json.Nodes;
using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Game.Modules.FleetRules;

[RekallAgeComponent("Setting Binding", Description =
    "Binds a menu row to one key inside the scene's Rekall.PersistentState document. Clicking " +
    "the row toggles a boolean or advances a numeric value, wrapping at its maximum.")]
public sealed class SettingBinding : RekallAgeComponent
{
    [RekallAgeProperty]
    public bool Enabled { get; init; } = true;

    /// <summary>Key inside the persistent state document.</summary>
    [RekallAgeProperty]
    public string Key { get; init; } = string.Empty;

    [RekallAgeProperty]
    public string Label { get; init; } = string.Empty;

    [RekallAgeProperty(AllowedValues = ["toggle", "range"])]
    public string Kind { get; init; } = "toggle";

    [RekallAgeProperty(Minimum = -1000, Maximum = 1000)]
    public double Minimum { get; init; }

    [RekallAgeProperty(Minimum = -1000, Maximum = 1000)]
    public double Maximum { get; init; } = 1;

    [RekallAgeProperty(Minimum = 0.001, Maximum = 1000)]
    public double Step { get; init; } = 0.1;
}

/// <summary>
/// The settings screen: rows that read and write the scene's persistent state document.
///
/// Nothing here writes a file. A row edits the Rekall.PersistentState document as ordinary
/// component state and the runtime persists it, which is the whole point of that contract -
/// authored content never needs file access, and the same document is already loaded and
/// waiting when the game next starts.
/// </summary>
public sealed class SettingsSystem : IRekallAgeRuntimeModuleSystem
{
    private const string BindingType = "Game.Modules.FleetRules.SettingBinding";
    private const string PersistentStateType = "Rekall.PersistentState";
    private const string UiElementType = "Rekall.UiElement";

    public string Id => "game.settings";

    // Runs after the engine's UI interaction system (priority 20), which is what emits
    // pointer.click. Event facts do not survive the frame, so a consumer ordered before the
    // emitter never sees them at all.
    public int Priority => 30;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeModuleFrameContext context)
    {
        var store = world.Entities.FirstOrDefault(entity => entity.FindComponent(PersistentStateType) is not null);
        if (store is null)
        {
            return ValueTask.FromResult(world);
        }

        var rows = world.Entities
            .Where(entity => entity.FindComponent(BindingType) is not null
                && entity.ComponentBoolean(BindingType, "enabled", true))
            .ToArray();
        if (rows.Length == 0)
        {
            return ValueTask.FromResult(world);
        }

        var document = (store.FindComponent(PersistentStateType)!.Properties["document"] as JsonObject)?.DeepClone().AsObject()
            ?? new JsonObject();
        var changed = false;

        // A click on a row advances that row's value.
        foreach (var runtimeEvent in world.Subsystems.Events.Events)
        {
            if (!runtimeEvent.Type.Equals("pointer.click", StringComparison.Ordinal))
            {
                continue;
            }

            var row = rows.FirstOrDefault(entity => entity.Id.Equals(runtimeEvent.EntityId, StringComparison.Ordinal));
            if (row is null)
            {
                continue;
            }

            var key = row.ComponentString(BindingType, "key", string.Empty) ?? string.Empty;
            if (key.Length == 0)
            {
                continue;
            }

            if (row.ComponentString(BindingType, "kind", "toggle") == "toggle")
            {
                var current = document[key]?.GetValue<bool>() ?? false;
                document[key] = !current;
            }
            else
            {
                var minimum = row.ComponentNumber(BindingType, "minimum", 0);
                var maximum = row.ComponentNumber(BindingType, "maximum", 1);
                var step = Math.Max(0.001, row.ComponentNumber(BindingType, "step", 0.1));
                var current = document[key]?.GetValue<double>() ?? minimum;
                var next = current + step;
                // Wrap rather than clamp: a single click target has to be able to come back
                // down again without a second control.
                document[key] = next > maximum + 0.0001 ? minimum : Math.Round(next, 3);
            }

            changed = true;
        }

        var entities = new List<RekallAgeRuntimeEntity>(world.Entities.Count);
        foreach (var entity in world.Entities)
        {
            var next = entity;

            if (changed && next.Id.Equals(store.Id, StringComparison.Ordinal))
            {
                var properties = next.FindComponent(PersistentStateType)!.Properties.DeepClone().AsObject();
                properties["document"] = document.DeepClone();
                next = next with
                {
                    Components = next.Components
                        .Select(item => item.Type.Equals(PersistentStateType, StringComparison.Ordinal)
                            ? item with { Properties = properties }
                            : item)
                        .ToArray()
                };
            }

            var binding = next.FindComponent(BindingType);
            if (binding is not null && next.FindComponent(UiElementType) is not null)
            {
                var key = next.ComponentString(BindingType, "key", string.Empty) ?? string.Empty;
                var label = next.ComponentString(BindingType, "label", key) ?? key;
                var text = "  " + label.PadRight(22) + FormatValue(next, document, key);
                if (!string.Equals(
                        next.ComponentString(UiElementType, "text", string.Empty),
                        text,
                        StringComparison.Ordinal))
                {
                    next = next.WithComponentString(UiElementType, "text", text);
                }
            }

            entities.Add(next);
        }

        return ValueTask.FromResult(world with { Entities = entities });
    }

    private static string FormatValue(RekallAgeRuntimeEntity row, JsonObject document, string key)
    {
        if (row.ComponentString(BindingType, "kind", "toggle") == "toggle")
        {
            return (document[key]?.GetValue<bool>() ?? false) ? "ON" : "OFF";
        }

        var minimum = row.ComponentNumber(BindingType, "minimum", 0);
        var maximum = Math.Max(minimum + 0.0001, row.ComponentNumber(BindingType, "maximum", 1));
        var value = document[key]?.GetValue<double>() ?? minimum;
        var filled = (int)Math.Round(Math.Clamp((value - minimum) / (maximum - minimum), 0, 1) * 10);
        return new string('#', filled) + new string('.', 10 - filled)
            + "  " + value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
