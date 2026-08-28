using System.Text.Json.Nodes;
using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Game.Modules.FleetRules;

[RekallAgeComponent("Faction", Description =
    "Which side a vessel belongs to. Weapons only acquire targets of a different side, and " +
    "'civilian' is never targeted by anyone.")]
public sealed class Faction : RekallAgeComponent
{
    [RekallAgeProperty(AllowedValues = ["compact", "choir", "civilian"])]
    public string Side { get; init; } = "compact";

    /// <summary>
    /// Losing this vessel ends the campaign outright rather than leaving a run that cannot be
    /// completed. Story-critical hulls are the ones later missions need.
    /// </summary>
    [RekallAgeProperty]
    public bool StoryCritical { get; init; }

    [RekallAgeProperty]
    public bool Destroyed { get; init; }
}

[RekallAgeComponent("Weapon", Description =
    "A vessel's main battery: reach, damage per shot and reload time. Fires automatically at " +
    "the vessel's current target when one is inside range.")]
public sealed class Weapon : RekallAgeComponent
{
    [RekallAgeProperty]
    public bool Enabled { get; init; } = true;

    [RekallAgeProperty(Minimum = 1, Maximum = 5000)]
    public double Range { get; init; } = 70;

    [RekallAgeProperty(Minimum = 0, Maximum = 100000)]
    public double Damage { get; init; } = 90;

    [RekallAgeProperty(Minimum = 0.05, Maximum = 60)]
    public double CycleSeconds { get; init; } = 1.6;

    [RekallAgeProperty(Minimum = 0, Maximum = 60)]
    public double Cooldown { get; init; }
}

[RekallAgeComponent("Order", Description =
    "A standing order for a vessel: hold station, move to a point, or engage a target. Issued " +
    "by right-clicking with the vessel selected.")]
public sealed class Order : RekallAgeComponent
{
    [RekallAgeProperty(AllowedValues = ["hold", "move", "attack"])]
    public string Kind { get; init; } = "hold";

    [RekallAgeProperty]
    public string TargetId { get; init; } = string.Empty;

    [RekallAgeProperty(Minimum = -100000, Maximum = 100000)]
    public double X { get; init; }

    [RekallAgeProperty(Minimum = -100000, Maximum = 100000)]
    public double Y { get; init; }

    [RekallAgeProperty(Minimum = -100000, Maximum = 100000)]
    public double Z { get; init; }

    [RekallAgeProperty(Minimum = 0, Maximum = 500)]
    public double Speed { get; init; } = 14;
}

[RekallAgeComponent("Mission State", Description =
    "Objectives and outcome for the current mission. The mission ends when every hostile is " +
    "destroyed, or when a story-critical vessel is lost.")]
public sealed class MissionState : RekallAgeComponent
{
    [RekallAgeProperty]
    public string Title { get; init; } = string.Empty;

    [RekallAgeProperty]
    public string Objective { get; init; } = string.Empty;

    [RekallAgeProperty(AllowedValues = ["active", "victory", "defeat"])]
    public string Outcome { get; init; } = "active";

    [RekallAgeProperty]
    public string PanelEntityName { get; init; } = string.Empty;

    [RekallAgeProperty]
    public string DebriefScene { get; init; } = "Debrief";

    [RekallAgeProperty(Minimum = 0, Maximum = 60)]
    public double EndDelaySeconds { get; init; } = 3.0;

    [RekallAgeProperty(Minimum = 0, Maximum = 120)]
    public double Elapsed { get; init; }

    /// <summary>
    /// Set the first step a live hostile is seen. Victory is "every hostile destroyed", and
    /// without this latch a scene that simply has no hostiles in it - a menu backdrop reusing
    /// the fleet, say - would satisfy that on its first step and declare itself won.
    /// </summary>
    [RekallAgeProperty]
    public bool Engaged { get; init; }
}

/// <summary>
/// Moves vessels under a standing order.
///
/// Steering is deliberately simple - turn toward the goal and advance - because the Choir's
/// predictability is the point of the fiction, and a player has to be able to look at two ships
/// and know what both will do next.
/// </summary>
public sealed class OrderSystem : IRekallAgeRuntimeModuleSystem
{
    internal const string OrderType = "Game.Modules.FleetRules.Order";
    internal const string FactionType = "Game.Modules.FleetRules.Faction";
    internal const string SelectableType = "Game.Modules.FleetRules.Selectable";

    public string Id => "game.orders";

    // Before game.fleet (0), so a hull that moved under orders this step is the position its
    // drive block and its fighter wing are then attached to.
    public int Priority => -10;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeModuleFrameContext context)
    {
        var delta = context.DeltaTime.TotalSeconds;
        var byId = world.Entities.ToDictionary(entity => entity.Id, StringComparer.Ordinal);
        var entities = new List<RekallAgeRuntimeEntity>(world.Entities.Count);

        foreach (var entity in world.Entities)
        {
            var order = entity.FindComponent(OrderType);
            if (order is null || CombatRules.IsDestroyed(entity))
            {
                entities.Add(entity);
                continue;
            }

            var kind = entity.ComponentString(OrderType, "kind", "hold") ?? "hold";
            double goalX, goalY, goalZ;
            if (kind == "attack")
            {
                var targetId = entity.ComponentString(OrderType, "targetId", string.Empty) ?? string.Empty;
                if (!byId.TryGetValue(targetId, out var target) || CombatRules.IsDestroyed(target))
                {
                    entities.Add(entity);
                    continue;
                }

                goalX = target.Transform.Position3D.X;
                goalY = target.Transform.Position3D.Y;
                goalZ = target.Transform.Position3D.Z;
            }
            else if (kind == "move")
            {
                goalX = entity.ComponentNumber(OrderType, "x");
                goalY = entity.ComponentNumber(OrderType, "y");
                goalZ = entity.ComponentNumber(OrderType, "z");
            }
            else
            {
                entities.Add(entity);
                continue;
            }

            var position = entity.Transform.Position3D;
            var dx = goalX - position.X;
            var dy = goalY - position.Y;
            var dz = goalZ - position.Z;
            var distance = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

            // Attackers stop at weapon range instead of ramming their target.
            var standOff = kind == "attack"
                ? Math.Max(8.0, entity.ComponentNumber(CombatSystem.WeaponType, "range", 70) * 0.75)
                : 2.0;
            if (distance <= standOff || distance <= 0.0001)
            {
                var arrived = entity.WithRotation3D(new RekallAgeRuntimeVector3(
                    entity.Transform.Rotation3D.X,
                    SlewToward(dx, dz, entity.Transform.Rotation3D.Y, distance, delta),
                    entity.Transform.Rotation3D.Z));

                // A move order is finished once the ship is there. Leaving it standing would
                // pin the panel on "Moving to ..." forever and keep Drift suppressed.
                entities.Add(kind == "move"
                    ? arrived
                        .WithComponentString(OrderType, "kind", "hold")
                        .WithComponentString(OrderType, "targetId", string.Empty)
                    : arrived);
                continue;
            }

            var speed = Math.Max(0, entity.ComponentNumber(OrderType, "speed", 14));
            var step = Math.Min(speed * delta, distance - standOff);
            var next = entity.WithPosition3D(new RekallAgeRuntimeVector3(
                position.X + (dx / distance * step),
                position.Y + (dy / distance * step),
                position.Z + (dz / distance * step)));

            entities.Add(next.WithRotation3D(new RekallAgeRuntimeVector3(
                next.Transform.Rotation3D.X,
                SlewToward(dx, dz, next.Transform.Rotation3D.Y, distance, delta),
                next.Transform.Rotation3D.Z)));
        }

        return ValueTask.FromResult(world with { Entities = entities });
    }

    /// <summary>How fast a hull can come about. Capital ships do not pivot on the spot.</summary>
    private const double SlewDegreesPerSecond = 40.0;

    /// <summary>
    /// Turns the hull toward the goal, no faster than it can physically come about. Yaw 0 faces
    /// +Z, matching how the hulls are modelled.
    /// </summary>
    private static double SlewToward(double dx, double dz, double current, double distance, double delta)
    {
        if (distance <= 0.0001)
        {
            return current;
        }

        var desired = Math.Atan2(dx, dz) * 180.0 / Math.PI;
        var difference = ((desired - current + 540.0) % 360.0) - 180.0;
        var limit = SlewDegreesPerSecond * delta;
        return current + Math.Clamp(difference, -limit, limit);
    }
}

/// <summary>
/// Weapons fire, shields absorb, hulls do not heal, and losses are permanent.
/// </summary>
public sealed class CombatSystem : IRekallAgeRuntimeModuleSystem
{
    internal const string WeaponType = "Game.Modules.FleetRules.Weapon";

    /// <summary>Shields come back between engagements; hull damage is for good.</summary>
    private const double ShieldRegenPerSecond = 12.0;

    public string Id => "game.combat";

    public int Priority => 33;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeModuleFrameContext context)
    {
        var delta = context.DeltaTime.TotalSeconds;
        var byId = world.Entities.ToDictionary(entity => entity.Id, StringComparer.Ordinal);
        var damage = new Dictionary<string, double>(StringComparer.Ordinal);
        var cooldowns = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var entity in world.Entities)
        {
            if (entity.FindComponent(WeaponType) is null
                || !entity.ComponentBoolean(WeaponType, "enabled", true)
                || CombatRules.IsDestroyed(entity))
            {
                continue;
            }

            var cooldown = Math.Max(0, entity.ComponentNumber(WeaponType, "cooldown") - delta);
            var targetId = entity.ComponentString(OrderSystem.OrderType, "targetId", string.Empty) ?? string.Empty;
            if (targetId.Length > 0
                && byId.TryGetValue(targetId, out var target)
                && !CombatRules.IsDestroyed(target)
                && cooldown <= 0)
            {
                var range = entity.ComponentNumber(WeaponType, "range", 70);
                if (CombatRules.Distance(entity, target) <= range)
                {
                    damage[targetId] = damage.GetValueOrDefault(targetId)
                        + entity.ComponentNumber(WeaponType, "damage", 90);
                    cooldown = Math.Max(0.05, entity.ComponentNumber(WeaponType, "cycleSeconds", 1.6));
                }
            }

            cooldowns[entity.Id] = cooldown;
        }

        var entities = new List<RekallAgeRuntimeEntity>(world.Entities.Count);
        var wrecked = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entity in world.Entities)
        {
            var next = entity;

            if (cooldowns.TryGetValue(entity.Id, out var cooldown))
            {
                next = next.WithComponentNumber(WeaponType, "cooldown", cooldown);
            }

            if (next.FindComponent(OrderSystem.SelectableType) is not null && !CombatRules.IsDestroyed(next))
            {
                var shields = next.ComponentNumber(OrderSystem.SelectableType, "shields");
                var shieldsMax = next.ComponentNumber(OrderSystem.SelectableType, "shieldsMax", 100);
                var hull = next.ComponentNumber(OrderSystem.SelectableType, "hull");

                if (damage.TryGetValue(entity.Id, out var incoming))
                {
                    var absorbed = Math.Min(shields, incoming);
                    shields -= absorbed;
                    hull = Math.Max(0, hull - (incoming - absorbed));
                }
                else
                {
                    shields = Math.Min(shieldsMax, shields + (ShieldRegenPerSecond * delta));
                }

                next = next
                    .WithComponentNumber(OrderSystem.SelectableType, "shields", Math.Round(shields, 2))
                    .WithComponentNumber(OrderSystem.SelectableType, "hull", Math.Round(hull, 2));

                if (hull <= 0)
                {
                    // A destroyed hull stops being a unit: no longer selectable, no longer
                    // targetable, and no longer drawn.
                    next = (next
                        .WithComponentBoolean(OrderSystem.FactionType, "destroyed", true)
                        .WithComponentBoolean(OrderSystem.SelectableType, "enabled", false))
                        with { Visible = false };
                    wrecked.Add(next.Name);
                }
            }

            entities.Add(next);
        }

        // A lost hull takes its drive glow with it. Without this the engine block hangs in
        // space burning brightly with no ship attached to it.
        if (wrecked.Count > 0)
        {
            for (var index = 0; index < entities.Count; index++)
            {
                var candidate = entities[index];
                if (candidate.Visible
                    && candidate.Name.EndsWith(" Drive", StringComparison.Ordinal)
                    && wrecked.Contains(candidate.Name[..^" Drive".Length]))
                {
                    entities[index] = candidate with { Visible = false };
                }
            }
        }

        return ValueTask.FromResult(world with { Entities = entities });
    }
}

/// <summary>
/// The Hollow Choir: acquire the nearest Compact warship and close to weapons range. It never
/// retreats, never regroups and never picks a civilian. Its predictability is deliberate - it
/// is a process running to completion, and that is what makes it beatable.
/// </summary>
public sealed class ChoirAiSystem : IRekallAgeRuntimeModuleSystem
{
    public string Id => "game.ai.choir";

    public int Priority => 31;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeModuleFrameContext context)
    {
        var hostiles = world.Entities
            .Where(entity => entity.ComponentString(OrderSystem.FactionType, "side", string.Empty) == "choir"
                && !CombatRules.IsDestroyed(entity))
            .ToArray();
        if (hostiles.Length == 0)
        {
            return ValueTask.FromResult(world);
        }

        var targets = world.Entities
            .Where(entity => entity.ComponentString(OrderSystem.FactionType, "side", string.Empty) == "compact"
                && !CombatRules.IsDestroyed(entity))
            .ToArray();

        var assignments = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var hostile in hostiles)
        {
            var nearest = targets
                .OrderBy(target => CombatRules.Distance(hostile, target))
                .FirstOrDefault();
            if (nearest is not null)
            {
                assignments[hostile.Id] = nearest.Id;
            }
        }

        if (assignments.Count == 0)
        {
            return ValueTask.FromResult(world);
        }

        var entities = new List<RekallAgeRuntimeEntity>(world.Entities.Count);
        foreach (var entity in world.Entities)
        {
            if (assignments.TryGetValue(entity.Id, out var targetId)
                && entity.ComponentString(OrderSystem.OrderType, "targetId", string.Empty) != targetId)
            {
                entities.Add(entity
                    .WithComponentString(OrderSystem.OrderType, "kind", "attack")
                    .WithComponentString(OrderSystem.OrderType, "targetId", targetId));
                continue;
            }

            entities.Add(entity);
        }

        return ValueTask.FromResult(world with { Entities = entities });
    }
}

/// <summary>
/// Watches for the mission ending and writes the objective readout.
/// </summary>
public sealed class MissionSystem : IRekallAgeRuntimeModuleSystem
{
    private const string MissionType = "Game.Modules.FleetRules.MissionState";
    private const string UiElementType = "Rekall.UiElement";
    private const string ShellTransitionType = "Game.Modules.FleetRules.ShellTransition";
    private const string PersistentStateType = "Rekall.PersistentState";

    public string Id => "game.mission";

    public int Priority => 34;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeModuleFrameContext context)
    {
        var host = world.Entities.FirstOrDefault(entity => entity.FindComponent(MissionType) is not null);
        if (host is null)
        {
            return ValueTask.FromResult(world);
        }

        var outcome = host.ComponentString(MissionType, "outcome", "active") ?? "active";
        var elapsed = host.ComponentNumber(MissionType, "elapsed");

        var hostilesLeft = world.Entities.Count(entity =>
            entity.ComponentString(OrderSystem.FactionType, "side", string.Empty) == "choir"
            && !CombatRules.IsDestroyed(entity));
        var criticalLost = world.Entities.Any(entity =>
            entity.ComponentBoolean(OrderSystem.FactionType, "storyCritical")
            && CombatRules.IsDestroyed(entity));
        var engaged = host.ComponentBoolean(MissionType, "engaged") || hostilesLeft > 0;

        var friendly = world.Entities
            .Where(entity => entity.ComponentString(OrderSystem.FactionType, "side", string.Empty) == "compact")
            .ToArray();
        var losses = friendly.Count(CombatRules.IsDestroyed);
        var wipedOut = friendly.Length > 0 && losses == friendly.Length;

        if (outcome == "active")
        {
            // A story-critical loss ends the run outright. Continuing would leave a campaign
            // that cannot be completed, which is worse than losing now. Losing the whole
            // squadron ends it too - otherwise a wipe with no story hull among the dead leaves
            // the mission running with nobody left to finish it.
            if (criticalLost || wipedOut)
            {
                outcome = "defeat";
                elapsed = 0;
            }
            else if (engaged && hostilesLeft == 0)
            {
                outcome = "victory";
                elapsed = 0;
            }
        }
        else
        {
            elapsed += context.DeltaTime.TotalSeconds;
        }

        var title = host.ComponentString(MissionType, "title", string.Empty) ?? string.Empty;
        var objective = host.ComponentString(MissionType, "objective", string.Empty) ?? string.Empty;
        var readout = outcome switch
        {
            "victory" => string.Join("\n", title, "", "OBJECTIVE COMPLETE",
                $"Hostiles destroyed. Losses: {losses}."),
            "defeat" => string.Join("\n", title, "", "MISSION FAILED", criticalLost
                ? "A vessel the fleet cannot replace was lost."
                : "The squadron was destroyed."),
            _ => string.Join("\n", title, "", objective,
                $"Hostiles remaining: {hostilesLeft}    Losses: {losses}"),
        };

        var panelName = host.ComponentString(MissionType, "panelEntityName", string.Empty) ?? string.Empty;
        var debrief = host.ComponentString(MissionType, "debriefScene", "Debrief") ?? "Debrief";
        var handOver = outcome != "active"
            && elapsed >= host.ComponentNumber(MissionType, "endDelaySeconds", 3.0);

        var entities = new List<RekallAgeRuntimeEntity>(world.Entities.Count);
        foreach (var entity in world.Entities)
        {
            var next = entity;

            if (next.Id.Equals(host.Id, StringComparison.Ordinal))
            {
                next = next
                    .WithComponentString(MissionType, "outcome", outcome)
                    .WithComponentNumber(MissionType, "elapsed", elapsed)
                    .WithComponentBoolean(MissionType, "engaged", engaged);
                if (handOver && next.FindComponent(ShellTransitionType) is not null
                    && next.ComponentString(ShellTransitionType, "phase", string.Empty) != "fadingOut")
                {
                    next = next
                        .WithComponentString(ShellTransitionType, "targetScene", debrief)
                        .WithComponentString(ShellTransitionType, "phase", "fadingOut")
                        .WithComponentNumber(ShellTransitionType, "elapsed", 0);
                }

                // The result outlives the scene. Writing it into the campaign's persistent
                // state is what lets the debrief - a separate scene, with none of this world
                // still loaded - say what actually happened.
                if (outcome != "active" && next.FindComponent(PersistentStateType) is not null)
                {
                    var document = (next.FindComponent(PersistentStateType)!.Properties["document"]
                        as JsonObject)?.DeepClone().AsObject() ?? new JsonObject();
                    document["lastMission"] = title;
                    document["lastOutcome"] = outcome;
                    document["lastLosses"] = losses;
                    document["lastCriticalLoss"] = criticalLost;
                    next = next.UpdateComponent(PersistentStateType, properties =>
                    {
                        properties["document"] = document;
                        return properties;
                    });
                }
            }

            if (panelName.Length > 0
                && next.Name.Equals(panelName, StringComparison.Ordinal)
                && next.FindComponent(UiElementType) is not null
                && !string.Equals(
                    next.ComponentString(UiElementType, "text", string.Empty),
                    readout,
                    StringComparison.Ordinal))
            {
                next = next.WithComponentString(UiElementType, "text", readout);
            }

            entities.Add(next);
        }

        return ValueTask.FromResult(world with { Entities = entities });
    }
}

internal static class CombatRules
{
    public static bool IsDestroyed(RekallAgeRuntimeEntity entity) =>
        entity.ComponentBoolean(OrderSystem.FactionType, "destroyed");

    public static double Distance(RekallAgeRuntimeEntity left, RekallAgeRuntimeEntity right)
    {
        var dx = left.Transform.Position3D.X - right.Transform.Position3D.X;
        var dy = left.Transform.Position3D.Y - right.Transform.Position3D.Y;
        var dz = left.Transform.Position3D.Z - right.Transform.Position3D.Z;
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }
}
