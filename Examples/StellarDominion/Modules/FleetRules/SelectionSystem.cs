using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Game.Modules.FleetRules;

[RekallAgeComponent("Selectable", Description =
    "Marks a unit the player can click to select, and carries the tactical readout shown in " +
    "the unit panel. SelectRadius is the pick sphere, in world units, around the entity origin.")]
public sealed class Selectable : RekallAgeComponent
{
    [RekallAgeProperty]
    public bool Enabled { get; init; } = true;

    [RekallAgeProperty]
    public bool Selected { get; init; }

    [RekallAgeProperty]
    public string UnitClass { get; init; } = "Unknown";

    [RekallAgeProperty]
    public string Role { get; init; } = "Unassigned";

    [RekallAgeProperty(Minimum = 0, Maximum = 100000)]
    public double Hull { get; init; } = 100;

    [RekallAgeProperty(Minimum = 0, Maximum = 100000)]
    public double HullMax { get; init; } = 100;

    [RekallAgeProperty(Minimum = 0, Maximum = 100000)]
    public double Shields { get; init; } = 100;

    [RekallAgeProperty(Minimum = 0, Maximum = 100000)]
    public double ShieldsMax { get; init; } = 100;

    [RekallAgeProperty(Minimum = 0, Maximum = 1000000)]
    public double Crew { get; init; }

    [RekallAgeProperty(Minimum = 0.01, Maximum = 1000)]
    public double SelectRadius { get; init; } = 5;
}

[RekallAgeComponent("Fleet Command", Description =
    "Singleton tactical state: which unit is currently selected. Attach to the entity that " +
    "owns the unit-info panel.")]
public sealed class FleetCommand : RekallAgeComponent
{
    [RekallAgeProperty]
    public bool Enabled { get; init; } = true;

    [RekallAgeProperty]
    public string SelectedEntityId { get; init; } = string.Empty;

    [RekallAgeProperty]
    public string SelectedName { get; init; } = string.Empty;

    [RekallAgeProperty]
    public string PanelEntityName { get; init; } = string.Empty;
}

/// <summary>
/// Click-to-select, and the unit readout that follows from it.
///
/// The engine's built-in Rekall.PointerRay casts a *fixed* direction from an entity, which is
/// the right primitive for a gun or a gaze ray but not for a mouse cursor, so this system
/// resolves the pointer itself. It builds the camera basis from the SDK's Forward/Right/Up
/// helpers rather than re-deriving Euler signs by hand.
///
/// Picking is done in screen space: each unit is projected to pixels and compared against the
/// cursor, with a minimum pixel radius. That is what the player is actually aiming with, it
/// does not depend on authoring a correct world-space radius per hull size, and small or
/// distant craft stay clickable instead of shrinking below a ray. It also keeps selection
/// independent of the physics subsystem - these ships carry no colliders and should not need
/// any in order to be clickable.
/// </summary>
public sealed class SelectionSystem : IRekallAgeRuntimeModuleSystem
{
    internal const string SelectableType = "Game.Modules.FleetRules.Selectable";
    internal const string CommandType = "Game.Modules.FleetRules.FleetCommand";
    private const string UiElementType = "Rekall.UiElement";

    /// <summary>Pixel slack around a unit so small or distant craft stay clickable.</summary>
    private const double MinimumPickRadiusPixels = 22.0;

    public string Id => "game.fleet.selection";

    // Runs after game.fleet so picking tests this step's positions, not last step's.
    public int Priority => 10;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeModuleFrameContext context)
    {
        var command = world.Entities.FirstOrDefault(entity => entity.FindComponent(CommandType) is not null);
        if (command is null)
        {
            return ValueTask.FromResult(world);
        }

        var selectedId = command.ComponentString(CommandType, "selectedEntityId", string.Empty) ?? string.Empty;
        var selectedName = command.ComponentString(CommandType, "selectedName", string.Empty) ?? string.Empty;

        // Hover is resolved every step, not just on click. It drives the "under cursor" line
        // in the panel, and it is what makes a selection failure diagnosable: if hover tracks
        // the pointer then picking works and only the click edge is at fault.
        var hovered = TryPick(world, context, out var underCursor) ? underCursor : null;

        var clicked = context.Input.PressedButtonsThisFrame?.Contains("Left") == true
            || context.Input.PressedButtons?.Contains("Left") == true;
        if (clicked)
        {
            selectedId = hovered?.Id ?? string.Empty;
            selectedName = hovered?.Name ?? string.Empty;
        }

        var panelName = command.ComponentString(CommandType, "panelEntityName", string.Empty) ?? string.Empty;
        var readout = BuildReadout(world, selectedId, hovered);

        var entities = new List<RekallAgeRuntimeEntity>(world.Entities.Count);
        foreach (var entity in world.Entities)
        {
            var next = entity;

            if (next.FindComponent(SelectableType) is not null)
            {
                var isSelected = next.Id.Equals(selectedId, StringComparison.Ordinal);
                if (next.ComponentBoolean(SelectableType, "selected") != isSelected)
                {
                    next = next.WithComponentBoolean(SelectableType, "selected", isSelected);
                }
            }

            if (next.Id.Equals(command.Id, StringComparison.Ordinal))
            {
                next = next
                    .WithComponentString(CommandType, "selectedEntityId", selectedId)
                    .WithComponentString(CommandType, "selectedName", selectedName);
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

    private static string BuildReadout(
        RekallAgeRuntimeWorld world,
        string selectedId,
        RekallAgeRuntimeEntity? hovered)
    {
        var unit = selectedId.Length == 0
            ? null
            : world.Entities.FirstOrDefault(entity => entity.Id.Equals(selectedId, StringComparison.Ordinal));
        if (unit is null)
        {
            var under = hovered is null
                ? "Click a vessel to inspect it."
                : "UNDER CURSOR: " + hovered.Name;
            return string.Join("\n", "NO UNIT SELECTED", under);
        }

        var hull = unit.ComponentNumber(SelectableType, "hull");
        var hullMax = Math.Max(1, unit.ComponentNumber(SelectableType, "hullMax", 100));
        var shields = unit.ComponentNumber(SelectableType, "shields");
        var shieldsMax = Math.Max(1, unit.ComponentNumber(SelectableType, "shieldsMax", 100));
        var position = unit.Transform.Position3D;

        var lines = new[]
        {
            unit.Name.ToUpperInvariant(),
            unit.ComponentString(SelectableType, "unitClass", "Unknown")
                + "  -  " + unit.ComponentString(SelectableType, "role", "Unassigned"),
            "HULL    " + Bar(hull / hullMax) + " " + hull.ToString("F0") + "/" + hullMax.ToString("F0"),
            "SHIELDS " + Bar(shields / shieldsMax) + " " + shields.ToString("F0") + "/" + shieldsMax.ToString("F0"),
            "CREW    " + unit.ComponentNumber(SelectableType, "crew").ToString("N0"),
            "POS     " + position.X.ToString("F0") + ", " + position.Y.ToString("F0") + ", " + position.Z.ToString("F0"),
        };
        return string.Join("\n", lines);
    }

    private static string Bar(double fraction)
    {
        var filled = (int)Math.Round(Math.Clamp(fraction, 0, 1) * 10);
        return new string('#', filled) + new string('.', 10 - filled);
    }

    private static bool TryPick(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeModuleFrameContext context,
        out RekallAgeRuntimeEntity? picked)
    {
        picked = null;
        var input = context.Input;
        if (input.ViewportWidth <= 0 || input.ViewportHeight <= 0)
        {
            return false;
        }

        var camera = world.Subsystems.Rendering.Cameras.FirstOrDefault(item => item.Active)
            ?? world.Subsystems.Rendering.Cameras.FirstOrDefault();
        if (camera is null)
        {
            return false;
        }

        var cameraEntity = world.Entities.FirstOrDefault(entity =>
            entity.Id.Equals(camera.EntityId, StringComparison.Ordinal));
        if (cameraEntity is null)
        {
            return false;
        }

        var transform = cameraEntity.Transform;
        var forward = transform.Forward3D();
        var rightAxis = transform.Right3D();
        var upAxis = transform.Up3D();

        var aspect = input.ViewportWidth / input.ViewportHeight;
        var tanHalfFov = Math.Tan(Math.Max(1.0, camera.FieldOfViewDegrees) * Math.PI / 360.0);

        var origin = transform.Position3D;
        var nearestDepth = double.MaxValue;
        foreach (var entity in world.Entities)
        {
            if (entity.FindComponent(SelectableType) is null
                || !entity.ComponentBoolean(SelectableType, "enabled", true))
            {
                continue;
            }

            var ox = entity.Transform.Position3D.X - origin.X;
            var oy = entity.Transform.Position3D.Y - origin.Y;
            var oz = entity.Transform.Position3D.Z - origin.Z;

            var depth = (ox * forward.X) + (oy * forward.Y) + (oz * forward.Z);
            if (depth <= 0.001)
            {
                continue;                                   // behind the camera
            }

            // Project the unit to screen and compare in pixels rather than intersecting a
            // world-space sphere. Pixel proximity is what the player is actually aiming with,
            // it does not depend on getting an authored world radius right for every hull
            // size, and it keeps distant units clickable instead of shrinking below the ray.
            var right = (ox * rightAxis.X) + (oy * rightAxis.Y) + (oz * rightAxis.Z);
            var upAmount = (ox * upAxis.X) + (oy * upAxis.Y) + (oz * upAxis.Z);
            var screenX = (((right / depth / (tanHalfFov * aspect)) + 1.0) * 0.5) * input.ViewportWidth;
            var screenY = ((1.0 - (upAmount / depth / tanHalfFov)) * 0.5) * input.ViewportHeight;

            // The unit's own extent in pixels, with a floor so small craft stay clickable.
            var worldRadius = Math.Max(0.01, entity.ComponentNumber(SelectableType, "selectRadius", 5));
            var radiusPixels = Math.Max(
                MinimumPickRadiusPixels,
                worldRadius / depth / tanHalfFov * (input.ViewportHeight * 0.5));

            var dx = screenX - input.MouseX;
            var dy = screenY - input.MouseY;
            if ((dx * dx) + (dy * dy) > radiusPixels * radiusPixels)
            {
                continue;
            }

            if (depth < nearestDepth)
            {
                nearestDepth = depth;
                picked = entity;
            }
        }

        // A click that hits nothing clears the selection, which is why this reports success
        // even when nothing was picked.
        return true;
    }
}
