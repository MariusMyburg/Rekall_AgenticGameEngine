using System.Globalization;
using System.Text.Json.Nodes;
using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Game.Modules.FleetRules;

[RekallAgeComponent("Debrief Panel", Description =
    "Reads the result the last mission wrote into the campaign's Rekall.PersistentState " +
    "document and renders it as the after-action report.")]
public sealed class DebriefPanel : RekallAgeComponent
{
    [RekallAgeProperty]
    public bool Enabled { get; init; } = true;

    /// <summary>Entity carrying the Rekall.UiElement the report is written into.</summary>
    [RekallAgeProperty]
    public string TextEntityName { get; init; } = string.Empty;

    /// <summary>Entity carrying the Rekall.UiElement that names the outcome in large type.</summary>
    [RekallAgeProperty]
    public string HeadlineEntityName { get; init; } = string.Empty;
}

/// <summary>
/// The after-action report.
///
/// The mission scene is gone by the time this runs, so nothing about the battle is still in
/// the world to inspect. The result travels between the two scenes the only way it can: through
/// the campaign's persistent state document, written by MissionSystem as the mission ended.
/// </summary>
public sealed class DebriefSystem : IRekallAgeRuntimeModuleSystem
{
    private const string PanelType = "Game.Modules.FleetRules.DebriefPanel";
    private const string PersistentStateType = "Rekall.PersistentState";
    private const string UiElementType = "Rekall.UiElement";

    public string Id => "game.debrief";

    public int Priority => 29;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeModuleFrameContext context)
    {
        var panel = world.Entities.FirstOrDefault(entity => entity.FindComponent(PanelType) is not null);
        var store = world.Entities.FirstOrDefault(entity => entity.FindComponent(PersistentStateType) is not null);
        if (panel is null || store is null || !panel.ComponentBoolean(PanelType, "enabled", true))
        {
            return ValueTask.FromResult(world);
        }

        var document = store.FindComponent(PersistentStateType)!.Properties["document"] as JsonObject
            ?? new JsonObject();
        var mission = document["lastMission"]?.GetValue<string>() ?? "Unrecorded operation";
        var outcome = document["lastOutcome"]?.GetValue<string>() ?? "victory";
        var losses = (int)(document["lastLosses"]?.GetValue<double>() ?? 0);
        var criticalLoss = document["lastCriticalLoss"]?.GetValue<bool>() ?? false;

        var headline = outcome == "victory" ? "OPERATION COMPLETE" : "OPERATION FAILED";
        var report = string.Join("\n", BuildReport(mission, outcome, losses, criticalLoss));

        var textName = panel.ComponentString(PanelType, "textEntityName", string.Empty) ?? string.Empty;
        var headlineName = panel.ComponentString(PanelType, "headlineEntityName", string.Empty) ?? string.Empty;

        var entities = new List<RekallAgeRuntimeEntity>(world.Entities.Count);
        foreach (var entity in world.Entities)
        {
            entities.Add(WriteText(WriteText(entity, textName, report), headlineName, headline));
        }

        return ValueTask.FromResult(world with { Entities = entities });
    }

    private static RekallAgeRuntimeEntity WriteText(
        RekallAgeRuntimeEntity entity,
        string entityName,
        string text)
    {
        if (entityName.Length == 0
            || !entity.Name.Equals(entityName, StringComparison.Ordinal)
            || entity.FindComponent(UiElementType) is null
            || string.Equals(
                entity.ComponentString(UiElementType, "text", string.Empty),
                text,
                StringComparison.Ordinal))
        {
            return entity;
        }

        return entity.WithComponentString(UiElementType, "text", text);
    }

    private static IEnumerable<string> BuildReport(
        string mission,
        string outcome,
        int losses,
        bool criticalLoss)
    {
        yield return mission.ToUpperInvariant();
        yield return "";

        if (outcome == "victory")
        {
            yield return "The picket is gone. The lane is open, and the tankers are";
            yield return "moving again on their own schedule.";
            yield return "";
            yield return losses == 0
                ? "Every hull came home. Command has noted it."
                : "Vessels lost: " + losses.ToString(CultureInfo.InvariantCulture)
                    + ". They will not be replaced.";
            yield return "";
            yield return "Nothing about the Choir has changed. It has simply lost";
            yield return "three units, and it does not appear to have noticed.";
        }
        else if (criticalLoss)
        {
            yield return "The flagship is gone.";
            yield return "";
            yield return "There is no second Dominion-class hull in the Reach and no";
            yield return "yard left that could lay one down. Whatever the Compact was";
            yield return "still holding together, it was holding together with that ship.";
            yield return "";
            yield return "The campaign ends here.";
        }
        else
        {
            yield return "The squadron was destroyed in the transit lane.";
            yield return "";
            yield return "Vessels lost: " + losses.ToString(CultureInfo.InvariantCulture) + ".";
            yield return "";
            yield return "The picket held its station throughout, and holds it still.";
        }
    }
}
