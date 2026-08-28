using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Game.Modules.FleetRules;

[RekallAgeComponent("Tactical Abilities", Description =
    "Agent-authored fleet ability state. Cooldowns live on the command singleton so they are " +
    "inspectable, saveable, and independent of keyboard layout.")]
public sealed class TacticalAbilities : RekallAgeComponent
{
    [RekallAgeProperty] public bool Enabled { get; init; } = true;
    [RekallAgeProperty(Minimum = 0, Maximum = 600)] public double ShieldPulseCooldown { get; init; }
    [RekallAgeProperty(Minimum = 0.1, Maximum = 600)] public double ShieldPulseCooldownSeconds { get; init; } = 12;
    [RekallAgeProperty(Minimum = 0, Maximum = 600)] public double OverchargeCooldown { get; init; }
    [RekallAgeProperty(Minimum = 0.1, Maximum = 600)] public double OverchargeCooldownSeconds { get; init; } = 16;
    [RekallAgeProperty] public string PanelEntityName { get; init; } = string.Empty;
    [RekallAgeProperty] public string LastResult { get; init; } = "Tactical grid ready";
}

[RekallAgeComponent("Tactical Status", Description =
    "Per-vessel durations produced by tactical abilities. Combat consumes this generic, " +
    "agent-owned state; all timers advance with the runtime delta time.")]
public sealed class TacticalStatus : RekallAgeComponent
{
    [RekallAgeProperty(Minimum = 0, Maximum = 60)] public double ShieldPulseVisualSeconds { get; init; }
    [RekallAgeProperty(Minimum = 0, Maximum = 60)] public double OverchargeRemaining { get; init; }
}

/// <summary>
/// Turns semantic fleet actions into inspectable state changes and ordinary AGE effect entities.
/// The engine supplies input projection and render/audio primitives; this game module decides
/// targeting, balance, timing and presentation.
/// </summary>
public sealed class TacticalAbilitySystem : IRekallAgeRuntimeModuleSystem
{
    internal const string AbilityType = "Game.Modules.FleetRules.TacticalAbilities";
    internal const string StatusType = "Game.Modules.FleetRules.TacticalStatus";
    private const string CommandType = "Game.Modules.FleetRules.FleetCommand";
    private const string SelectableType = "Game.Modules.FleetRules.Selectable";
    private const string FactionType = "Game.Modules.FleetRules.Faction";
    private const string UiElementType = "Rekall.UiElement";

    public string Id => "game.tactical-abilities";
    public int Priority => 30;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeModuleFrameContext context)
    {
        var host = world.Entities.FirstOrDefault(entity => entity.FindComponent(AbilityType) is not null);
        if (host is null || !host.ComponentBoolean(AbilityType, "enabled", true))
        {
            return ValueTask.FromResult(world);
        }

        var delta = context.DeltaTime.TotalSeconds;
        var pulseCooldown = Math.Max(0, host.ComponentNumber(AbilityType, "shieldPulseCooldown") - delta);
        var overchargeCooldown = Math.Max(0, host.ComponentNumber(AbilityType, "overchargeCooldown") - delta);
        var lastResult = host.ComponentString(AbilityType, "lastResult", "Tactical grid ready")
            ?? "Tactical grid ready";
        var selectedId = host.ComponentString(CommandType, "selectedEntityId", string.Empty) ?? string.Empty;
        var selected = world.Entities.FirstOrDefault(entity => entity.Id.Equals(selectedId, StringComparison.Ordinal));
        var valid = selected is not null
            && selected.FindComponent(StatusType) is not null
            && !CombatRules.IsDestroyed(selected)
            && selected.ComponentString(FactionType, "side", string.Empty) == "compact";
        var spawned = new List<RekallAgeRuntimeEntity>();
        var stamp = context.ElapsedTime.Ticks;

        var pulsePressed = world.WasInputActionPressed("fleet.shield-pulse");
        var overchargePressed = world.WasInputActionPressed("fleet.overcharge");
        if (pulsePressed)
        {
            if (!valid)
            {
                lastResult = "SHIELD PULSE: select a surviving Compact vessel";
            }
            else if (pulseCooldown > 0)
            {
                lastResult = $"SHIELD PULSE recharging ({Math.Ceiling(pulseCooldown):0}s)";
            }
            else
            {
                var activeSelected = selected!;
                var shields = activeSelected.ComponentNumber(SelectableType, "shields");
                var maximum = activeSelected.ComponentNumber(SelectableType, "shieldsMax", 100);
                selected = activeSelected
                    .WithComponentNumber(SelectableType, "shields", Math.Min(maximum, shields + Math.Max(90, maximum * 0.18)))
                    .WithComponentNumber(StatusType, "shieldPulseVisualSeconds", 1.25);
                pulseCooldown = host.ComponentNumber(AbilityType, "shieldPulseCooldownSeconds", 12);
                lastResult = $"SHIELD PULSE: {selected.Name} reinforced";
                spawned.Add(OrdnanceFactory.AbilityPulse(
                    $"ability_shield_{stamp}", selected.Transform.Position3D, "#58cfff", false));
            }
        }

        if (overchargePressed)
        {
            if (!valid)
            {
                lastResult = "OVERCHARGE: select a surviving Compact vessel";
            }
            else if (overchargeCooldown > 0)
            {
                lastResult = $"OVERCHARGE recharging ({Math.Ceiling(overchargeCooldown):0}s)";
            }
            else
            {
                var activeSelected = selected!;
                selected = activeSelected.WithComponentNumber(StatusType, "overchargeRemaining", 7.0);
                overchargeCooldown = host.ComponentNumber(AbilityType, "overchargeCooldownSeconds", 16);
                lastResult = $"OVERCHARGE: {selected.Name} weapons unbound";
                spawned.Add(OrdnanceFactory.AbilityPulse(
                    $"ability_overcharge_{stamp}", selected.Transform.Position3D, "#d7f3ff", true));
            }
        }

        var panelName = host.ComponentString(AbilityType, "panelEntityName", string.Empty) ?? string.Empty;
        var hud = BuildHud(pulseCooldown, overchargeCooldown, selected, lastResult);
        var entities = new List<RekallAgeRuntimeEntity>(world.Entities.Count + spawned.Count);
        foreach (var entity in world.Entities)
        {
            var next = selected is not null && entity.Id.Equals(selected.Id, StringComparison.Ordinal)
                ? selected
                : entity;

            if (next.FindComponent(StatusType) is not null)
            {
                next = next
                    .WithComponentNumber(StatusType, "shieldPulseVisualSeconds",
                        Math.Max(0, next.ComponentNumber(StatusType, "shieldPulseVisualSeconds") - delta))
                    .WithComponentNumber(StatusType, "overchargeRemaining",
                        Math.Max(0, next.ComponentNumber(StatusType, "overchargeRemaining") - delta));
            }

            if (next.Id.Equals(host.Id, StringComparison.Ordinal))
            {
                next = next
                    .WithComponentNumber(AbilityType, "shieldPulseCooldown", pulseCooldown)
                    .WithComponentNumber(AbilityType, "overchargeCooldown", overchargeCooldown)
                    .WithComponentString(AbilityType, "lastResult", lastResult);
            }
            else if (panelName.Length > 0
                     && next.Name.Equals(panelName, StringComparison.Ordinal)
                     && next.FindComponent(UiElementType) is not null)
            {
                next = next.WithComponentString(UiElementType, "text", hud);
            }

            entities.Add(next);
        }

        entities.AddRange(spawned);
        return ValueTask.FromResult(world with { Entities = entities });
    }

    private static string BuildHud(
        double pulseCooldown,
        double overchargeCooldown,
        RekallAgeRuntimeEntity? selected,
        string result)
    {
        var pulse = pulseCooldown <= 0 ? "READY" : $"{pulseCooldown:0.0}s";
        var charge = overchargeCooldown <= 0 ? "READY" : $"{overchargeCooldown:0.0}s";
        var active = selected is not null && selected.ComponentNumber(StatusType, "overchargeRemaining") > 0
            ? $"  OVERCHARGED {selected.ComponentNumber(StatusType, "overchargeRemaining"):0.0}s"
            : string.Empty;
        return $"[Q] SHIELD PULSE  {pulse}    [E] OVERCHARGE  {charge}{active}\n{result}";
    }
}
