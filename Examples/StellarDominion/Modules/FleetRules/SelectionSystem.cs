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
/// unprojects the pointer itself: it builds the camera basis from the SDK's Forward/Right/Up
/// helpers rather than re-deriving Euler signs by hand, and intersects the resulting ray
/// against each unit's pick sphere. Picking against a sphere rather than a collider keeps
/// selection independent of the physics subsystem - these ships carry no rigidbodies and should
/// not need any in order to be clickable.
/// </summary>
public sealed class SelectionSystem : IRekallAgeRuntimeModuleSystem
{
    internal const string SelectableType = "Game.Modules.FleetRules.Selectable";
    internal const string CommandType = "Game.Modules.FleetRules.FleetCommand";
    private const string UiElementType = "Rekall.UiElement";

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

        if (context.Input.PressedButtonsThisFrame?.Contains("Left") == true
            && TryPick(world, context, out var picked))
        {
            selectedId = picked?.Id ?? string.Empty;
            selectedName = picked?.Name ?? string.Empty;
        }

        var panelName = command.ComponentString(CommandType, "panelEntityName", string.Empty) ?? string.Empty;
        var readout = BuildReadout(world, selectedId);

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

    private static string BuildReadout(RekallAgeRuntimeWorld world, string selectedId)
    {
        var unit = selectedId.Length == 0
            ? null
            : world.Entities.FirstOrDefault(entity => entity.Id.Equals(selectedId, StringComparison.Ordinal));
        if (unit is null)
        {
            return "NO UNIT SELECTED" + "\n" + "Click a vessel to inspect it.";
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
        var right = transform.Right3D();
        var up = transform.Up3D();

        // Normalised device coordinates, y flipped: pointer y grows downward.
        var ndcX = (2.0 * (input.MouseX / input.ViewportWidth)) - 1.0;
        var ndcY = 1.0 - (2.0 * (input.MouseY / input.ViewportHeight));
        var aspect = input.ViewportWidth / input.ViewportHeight;
        var tanHalfFov = Math.Tan(Math.Max(1.0, camera.FieldOfViewDegrees) * Math.PI / 360.0);

        var dirX = forward.X + (right.X * ndcX * tanHalfFov * aspect) + (up.X * ndcY * tanHalfFov);
        var dirY = forward.Y + (right.Y * ndcX * tanHalfFov * aspect) + (up.Y * ndcY * tanHalfFov);
        var dirZ = forward.Z + (right.Z * ndcX * tanHalfFov * aspect) + (up.Z * ndcY * tanHalfFov);
        var length = Math.Sqrt((dirX * dirX) + (dirY * dirY) + (dirZ * dirZ));
        if (length <= 0.000001)
        {
            return false;
        }

        dirX /= length;
        dirY /= length;
        dirZ /= length;

        var origin = transform.Position3D;
        var nearest = double.MaxValue;
        foreach (var entity in world.Entities)
        {
            if (entity.FindComponent(SelectableType) is null
                || !entity.ComponentBoolean(SelectableType, "enabled", true))
            {
                continue;
            }

            var radius = Math.Max(0.01, entity.ComponentNumber(SelectableType, "selectRadius", 5));
            var ox = entity.Transform.Position3D.X - origin.X;
            var oy = entity.Transform.Position3D.Y - origin.Y;
            var oz = entity.Transform.Position3D.Z - origin.Z;

            // Project the sphere centre onto the ray, then measure how far off-axis it sits.
            var along = (ox * dirX) + (oy * dirY) + (oz * dirZ);
            if (along <= 0)
            {
                continue;                                   // behind the camera
            }

            var perpX = ox - (dirX * along);
            var perpY = oy - (dirY * along);
            var perpZ = oz - (dirZ * along);
            if ((perpX * perpX) + (perpY * perpY) + (perpZ * perpZ) > radius * radius)
            {
                continue;
            }

            if (along < nearest)
            {
                nearest = along;
                picked = entity;
            }
        }

        // A click that hits nothing clears the selection, which is why this reports success
        // even when nothing was picked.
        return true;
    }
}
