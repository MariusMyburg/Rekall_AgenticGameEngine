namespace Rekall.Age.Modules.BuiltIns;

[RekallAgeModule("rekall.builtins", "Rekall Built-ins")]
[RekallAgeRequiresCapability("world")]
public sealed class RekallAgeBuiltInModule : RekallAgeModule
{
    public override void Configure(RekallAgeModuleBuilder builder)
    {
        builder.RegisterComponent<RekallAgeTransformComponent>();
        builder.RegisterComponent<RekallAgePlayableLoopComponent>();
        builder.RegisterComponent<RekallAgeCamera2DComponent>();
        builder.RegisterComponent<RekallAgeCamera3DComponent>();
    }
}

[RekallAgeComponent("Transform")]
public sealed class RekallAgeTransformComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public double X { get; init; }

    [RekallAgeProperty]
    public double Y { get; init; }

    [RekallAgeProperty]
    public double Z { get; init; }
}

[RekallAgeComponent("Playable Loop")]
public sealed class RekallAgePlayableLoopComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public string Kind { get; init; } = "game";

    [RekallAgeProperty]
    public string State { get; init; } = "ready";
}

[RekallAgeComponent("Camera 2D")]
public sealed class RekallAgeCamera2DComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public bool Active { get; init; } = true;

    [RekallAgeProperty]
    public string ClearColor { get; init; } = "#102030";
}

[RekallAgeComponent("Camera 3D")]
public sealed class RekallAgeCamera3DComponent : RekallAgeComponent
{
    [RekallAgeProperty(Minimum = 1, Maximum = 179)]
    public double FieldOfView { get; init; } = 65;

    [RekallAgeProperty]
    public bool Active { get; init; } = true;
}
