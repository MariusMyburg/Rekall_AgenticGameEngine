using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;
using System.Text.Json.Nodes;

namespace Game.Modules.BouncingBall;

[RekallAgeModule("example.bouncing_ball", "Bouncing Ball")]
[RekallAgeRequiresCapability("world")]
[RekallAgeRequiresCapability("rendering3d")]
public sealed class BouncingBallModule : RekallAgeModule
{
    public override void Configure(RekallAgeModuleBuilder builder)
    {
        builder.RegisterComponent<BouncingBallMotion>();
        builder.RegisterRuntimeSystem<BouncingBallMotionSystem>();
    }
}

[RekallAgeComponent("Bouncing Ball Motion")]
public sealed class BouncingBallMotion : RekallAgeComponent
{
    [RekallAgeProperty]
    public bool Enabled { get; init; } = true;

    [RekallAgeProperty(Minimum = 0.0001)]
    public double Radius { get; init; } = 0.6;

    [RekallAgeProperty]
    public double FloorY { get; init; } = 0;

    [RekallAgeProperty(Minimum = 0)]
    public double BounceHeight { get; init; } = 3.0;

    [RekallAgeProperty(Minimum = 0)]
    public double BouncesPerSecond { get; init; } = 0.8;

    [RekallAgeProperty(Minimum = 0)]
    public double DriftRadius { get; init; } = 1.4;

    [RekallAgeProperty(Minimum = 0)]
    public double SpinDegreesPerSecond { get; init; } = 210;
}

public sealed class BouncingBallMotionSystem : IRekallAgeRuntimeModuleSystem
{
    private const string ComponentType = "Game.Modules.BouncingBall.BouncingBallMotion";

    public string Id => nameof(BouncingBallMotionSystem);

    public int Priority => -20;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeModuleFrameContext context)
    {
        var elapsed = context.ElapsedTime.TotalSeconds;
        var entities = world.Entities.Select(entity =>
        {
            var component = entity.Components.FirstOrDefault(item => item.Type == ComponentType);
            if (component is null || !ReadBoolean(component.Properties, "enabled", true))
            {
                return entity;
            }

            var radius = Math.Max(0.0001, ReadNumber(component.Properties, "radius", 0.6));
            var floorY = ReadNumber(component.Properties, "floorY", 0);
            var bounceHeight = Math.Max(0, ReadNumber(component.Properties, "bounceHeight", 3.0));
            var bouncesPerSecond = Math.Max(0, ReadNumber(component.Properties, "bouncesPerSecond", 0.8));
            var driftRadius = Math.Max(0, ReadNumber(component.Properties, "driftRadius", 1.4));
            var spinDegreesPerSecond = Math.Max(0, ReadNumber(component.Properties, "spinDegreesPerSecond", 210));
            var phase = elapsed * Math.PI * bouncesPerSecond;
            var bounce = Math.Abs(Math.Sin(phase));
            var transform = entity.Transform;

            return entity with
            {
                Transform = transform with
                {
                    Position3D = new RekallAgeRuntimeVector3(
                        Math.Sin(phase * 0.5) * driftRadius,
                        floorY + radius + bounce * bounceHeight,
                        transform.Position3D.Z),
                    Rotation3D = new RekallAgeRuntimeVector3(
                        transform.Rotation3D.X,
                        elapsed * spinDegreesPerSecond,
                        transform.Rotation3D.Z + elapsed * spinDegreesPerSecond * 0.45),
                    Scale3D = new RekallAgeRuntimeVector3(radius * 2, radius * 2, radius * 2)
                }
            };
        }).ToArray();

        return ValueTask.FromResult(world with { Entities = entities });
    }

    private static bool ReadBoolean(JsonObject properties, string name, bool fallback)
    {
        return properties.TryGetPropertyValue(name, out var node)
            && node is JsonValue value
            && value.TryGetValue<bool>(out var boolean)
            ? boolean
            : fallback;
    }

    private static double ReadNumber(JsonObject properties, string name, double fallback)
    {
        if (!properties.TryGetPropertyValue(name, out var node) || node is not JsonValue value)
        {
            return fallback;
        }

        if (value.TryGetValue<double>(out var number))
        {
            return number;
        }

        return value.TryGetValue<int>(out var integer) ? integer : fallback;
    }
}
