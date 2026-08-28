using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Game.Modules.FleetRules;

[RekallAgeComponent("Tactical Camera", Description =
    "Orbit camera for a tactical scene. Holds a pivot on the battle plane and derives the " +
    "camera's pose from yaw, pitch and distance. Middle-drag orbits, WASD pans, the wheel " +
    "zooms, and Space or Home frames every surviving vessel.")]
public sealed class TacticalCamera : RekallAgeComponent
{
    [RekallAgeProperty]
    public bool Enabled { get; init; } = true;

    [RekallAgeProperty(Minimum = -100000, Maximum = 100000)]
    public double PivotX { get; init; }

    [RekallAgeProperty(Minimum = -100000, Maximum = 100000)]
    public double PivotY { get; init; }

    [RekallAgeProperty(Minimum = -100000, Maximum = 100000)]
    public double PivotZ { get; init; }

    [RekallAgeProperty(Minimum = 1, Maximum = 100000)]
    public double Distance { get; init; } = 400;

    [RekallAgeProperty(Minimum = -720, Maximum = 720)]
    public double Yaw { get; init; } = 85;

    [RekallAgeProperty(Minimum = -89, Maximum = 89)]
    public double Pitch { get; init; } = 22;

    [RekallAgeProperty(Minimum = 1, Maximum = 100000)]
    public double MinimumDistance { get; init; } = 40;

    [RekallAgeProperty(Minimum = 1, Maximum = 100000)]
    public double MaximumDistance { get; init; } = 2400;

    [RekallAgeProperty(Minimum = -89, Maximum = 89)]
    public double MinimumPitch { get; init; } = -12;

    [RekallAgeProperty(Minimum = -89, Maximum = 89)]
    public double MaximumPitch { get; init; } = 82;

    [RekallAgeProperty(Minimum = 0.001, Maximum = 10)]
    public double OrbitDegreesPerPixel { get; init; } = 0.35;

    [RekallAgeProperty(Minimum = 1, Maximum = 10000)]
    public double PanUnitsPerSecond { get; init; } = 260;

    /// <summary>Fraction of the current distance one wheel notch adds or removes.</summary>
    [RekallAgeProperty(Minimum = 0.01, Maximum = 0.9)]
    public double ZoomStep { get; init; } = 0.12;

    /// <summary>
    /// Frame everything on the first step. A mission that opens already looking at its own
    /// fleet is worth more than any amount of hand-solved camera placement in the blueprint.
    /// </summary>
    [RekallAgeProperty]
    public bool FrameOnStart { get; init; } = true;
}

/// <summary>
/// Lets the player look where they want.
///
/// The camera's pose is derived, not authored: the component holds a pivot, a yaw, a pitch and
/// a distance, and the transform is recomputed from those every step. That is what makes
/// "frame everything" a two-line operation rather than an inverse problem, and it keeps orbit
/// and pan from fighting each other over the same three numbers.
///
/// Middle-drag orbits because left and right are already spoken for - left selects, right
/// orders - and a camera control that stole either would break the game to move the view.
/// </summary>
public sealed class CameraSystem : IRekallAgeRuntimeModuleSystem
{
    private const string CameraType = "Game.Modules.FleetRules.TacticalCamera";
    private const string Camera3DType = "Rekall.Camera3D";

    /// <summary>Leaves the framed fleet a comfortable margin inside the viewport.</summary>
    private const double FramingMargin = 1.45;

    public string Id => "game.camera";

    // Before selection at 10: picking projects through the camera, so the camera has to be
    // where it will be drawn this step or the cursor and the image disagree while orbiting.
    public int Priority => 5;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeModuleFrameContext context)
    {
        var camera = world.Entities.FirstOrDefault(entity => entity.FindComponent(CameraType) is not null);
        if (camera is null || !camera.ComponentBoolean(CameraType, "enabled", true))
        {
            return ValueTask.FromResult(world);
        }

        var input = context.Input;
        var delta = context.DeltaTime.TotalSeconds;

        var pivotX = camera.ComponentNumber(CameraType, "pivotX");
        var pivotY = camera.ComponentNumber(CameraType, "pivotY");
        var pivotZ = camera.ComponentNumber(CameraType, "pivotZ");
        var distance = camera.ComponentNumber(CameraType, "distance", 400);
        var yaw = camera.ComponentNumber(CameraType, "yaw", 85);
        var pitch = camera.ComponentNumber(CameraType, "pitch", 22);

        var minimumDistance = camera.ComponentNumber(CameraType, "minimumDistance", 40);
        var maximumDistance = Math.Max(minimumDistance + 1, camera.ComponentNumber(CameraType, "maximumDistance", 2400));
        var minimumPitch = camera.ComponentNumber(CameraType, "minimumPitch", -12);
        var maximumPitch = Math.Max(minimumPitch + 1, camera.ComponentNumber(CameraType, "maximumPitch", 82));

        var framed = false;
        var frameRequested = camera.ComponentBoolean(CameraType, "frameOnStart")
            || Pressed(input, "Space")
            || Pressed(input, "Home");

        if (frameRequested && TryFrameAll(world, context, pitch, out var focus, out var span))
        {
            pivotX = focus.X;
            pivotY = focus.Y;
            pivotZ = focus.Z;
            distance = Math.Clamp(span, minimumDistance, maximumDistance);
            framed = true;
        }

        if (!framed)
        {
            // Orbit. Vertical drag pushes the camera up over the plane, which is the direction
            // people expect from dragging "away" from themselves.
            if (Held(input, "Middle"))
            {
                var sensitivity = camera.ComponentNumber(CameraType, "orbitDegreesPerPixel", 0.35);
                yaw -= input.MouseDeltaX * sensitivity;
                pitch += input.MouseDeltaY * sensitivity;
            }

            // Zoom is proportional: a notch moves you the same fraction of the way in whether
            // you are looking at a whole squadron or at one fighter.
            if (Math.Abs(input.MouseWheelDelta) > 0.0001)
            {
                var step = camera.ComponentNumber(CameraType, "zoomStep", 0.12);
                distance *= Math.Pow(1.0 - step, input.MouseWheelDelta);
            }

            // Pan across the battle plane, camera-relative, so "right" is always screen-right.
            var forwardX = Math.Sin(yaw * Math.PI / 180.0);
            var forwardZ = Math.Cos(yaw * Math.PI / 180.0);
            var pan = camera.ComponentNumber(CameraType, "panUnitsPerSecond", 260) * delta
                * Math.Clamp(distance / 400.0, 0.25, 4.0);

            var forwardInput = Axis(input, "W", "S");
            var strafeInput = Axis(input, "D", "A");
            if (forwardInput != 0 || strafeInput != 0)
            {
                // Screen right is cross(forward, up) for a Y-up world, the same convention the
                // renderer uses; here that reduces to (-forwardZ, 0, forwardX).
                pivotX += ((forwardX * forwardInput) + (-forwardZ * strafeInput)) * pan;
                pivotZ += ((forwardZ * forwardInput) + (forwardX * strafeInput)) * pan;
            }
        }

        pitch = Math.Clamp(pitch, minimumPitch, maximumPitch);
        distance = Math.Clamp(distance, minimumDistance, maximumDistance);
        yaw = ((yaw % 360) + 360) % 360;

        // Derive the pose. The camera sits one distance back along its own view direction from
        // the pivot, which is what makes orbit and zoom independent of each other.
        var pitchRadians = pitch * Math.PI / 180.0;
        var yawRadians = yaw * Math.PI / 180.0;
        var dirX = Math.Cos(pitchRadians) * Math.Sin(yawRadians);
        var dirY = -Math.Sin(pitchRadians);
        var dirZ = Math.Cos(pitchRadians) * Math.Cos(yawRadians);

        var posed = camera
            .WithPosition3D(new RekallAgeRuntimeVector3(
                pivotX - (dirX * distance),
                pivotY - (dirY * distance),
                pivotZ - (dirZ * distance)))
            .WithRotation3D(new RekallAgeRuntimeVector3(pitch, yaw, 0))
            .WithComponentNumber(CameraType, "pivotX", pivotX)
            .WithComponentNumber(CameraType, "pivotY", pivotY)
            .WithComponentNumber(CameraType, "pivotZ", pivotZ)
            .WithComponentNumber(CameraType, "distance", distance)
            .WithComponentNumber(CameraType, "yaw", yaw)
            .WithComponentNumber(CameraType, "pitch", pitch)
            .WithComponentBoolean(CameraType, "frameOnStart", false);

        return ValueTask.FromResult(world.ReplaceEntity(posed));
    }

    /// <summary>
    /// Centre on everything still flying, and work out how far back that has to be seen from.
    /// </summary>
    private static bool TryFrameAll(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeModuleFrameContext context,
        double pitch,
        out RekallAgeRuntimeVector3 focus,
        out double distance)
    {
        focus = new RekallAgeRuntimeVector3(0, 0, 0);
        distance = 400;

        var ships = world.Entities
            .Where(entity => entity.FindComponent(OrderSystem.SelectableType) is not null
                && !CombatRules.IsDestroyed(entity))
            .Select(entity => entity.Transform.Position3D)
            .ToArray();
        if (ships.Length == 0)
        {
            return false;
        }

        var minimum = new RekallAgeRuntimeVector3(
            ships.Min(p => p.X), ships.Min(p => p.Y), ships.Min(p => p.Z));
        var maximum = new RekallAgeRuntimeVector3(
            ships.Max(p => p.X), ships.Max(p => p.Y), ships.Max(p => p.Z));
        var centre = new RekallAgeRuntimeVector3(
            (minimum.X + maximum.X) * 0.5,
            (minimum.Y + maximum.Y) * 0.5,
            (minimum.Z + maximum.Z) * 0.5);
        focus = centre;

        var radius = Math.Max(20.0, ships.Max(p =>
        {
            var dx = p.X - centre.X;
            var dy = p.Y - centre.Y;
            var dz = p.Z - centre.Z;
            return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        }));

        var camera = world.Subsystems.Rendering.Cameras.FirstOrDefault(item => item.Active)
            ?? world.Subsystems.Rendering.Cameras.FirstOrDefault();
        var fieldOfView = Math.Max(10.0, camera?.FieldOfViewDegrees ?? 62.0);

        // Fit the vertical field, then widen for a viewport narrower than it is tall.
        var halfVertical = Math.Tan(fieldOfView * Math.PI / 360.0);
        var aspect = context.Input.ViewportWidth > 0 && context.Input.ViewportHeight > 0
            ? context.Input.ViewportWidth / context.Input.ViewportHeight
            : 16.0 / 9.0;
        var half = aspect < 1 ? halfVertical * aspect : halfVertical;

        distance = radius / Math.Max(0.05, half) * FramingMargin;
        return true;
    }

    private static bool Held(RekallAgeRuntimeInputState input, string button) =>
        input.PressedButtons?.Contains(button) == true;

    private static bool Pressed(RekallAgeRuntimeInputState input, string key) =>
        input.PressedKeysThisFrame?.Contains(key) == true;

    private static bool Down(RekallAgeRuntimeInputState input, string key) =>
        input.PressedKeys?.Contains(key) == true;

    private static double Axis(RekallAgeRuntimeInputState input, string positive, string negative) =>
        (Down(input, positive) ? 1 : 0) - (Down(input, negative) ? 1 : 0);
}
