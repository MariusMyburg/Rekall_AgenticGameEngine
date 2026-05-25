using System.Text.Json.Nodes;
using Rekall.Age.World;

namespace Rekall.Age.Runtime.Abstractions;

public sealed record RekallAgeRuntimeWorld(
    string SceneId,
    string SceneName,
    int FrameIndex,
    TimeSpan ElapsedTime,
    IReadOnlyList<RekallAgeRuntimeEntity> Entities,
    RekallAgeRuntimeSubsystemViews Subsystems,
    IReadOnlyList<RekallAgeRuntimeObservation> Observations);

public sealed record RekallAgeRuntimeEntity(
    string Id,
    string Name,
    IReadOnlyList<string> Tags,
    string? ParentId,
    string? PrefabSourceId,
    bool Visible,
    bool Locked,
    RekallAgeRuntimeTransform Transform,
    IReadOnlyList<RekallAgeRuntimeComponent> Components);

public sealed record RekallAgeRuntimeComponent(string Type, JsonObject Properties);

public sealed record RekallAgeRuntimeTransform(
    RekallAgeRuntimeVector2 Position2D,
    double Rotation2D,
    RekallAgeRuntimeVector2 Scale2D,
    RekallAgeRuntimeVector3 Position3D,
    RekallAgeRuntimeVector3 Rotation3D,
    RekallAgeRuntimeVector3 Scale3D)
{
    public static RekallAgeRuntimeTransform Identity { get; } = new(
        new RekallAgeRuntimeVector2(0, 0),
        0,
        new RekallAgeRuntimeVector2(1, 1),
        new RekallAgeRuntimeVector3(0, 0, 0),
        new RekallAgeRuntimeVector3(0, 0, 0),
        new RekallAgeRuntimeVector3(1, 1, 1));
}

public sealed record RekallAgeRuntimeVector2(double X, double Y);

public sealed record RekallAgeRuntimeVector3(double X, double Y, double Z);

public sealed record RekallAgeRuntimeSubsystemViews(
    RekallAgeRuntimeRenderView Rendering,
    RekallAgeRuntimePhysicsView Physics,
    RekallAgeRuntimeAudioView Audio,
    RekallAgeRuntimeAnimationView Animation,
    RekallAgeRuntimeUiView Ui)
{
    public static RekallAgeRuntimeSubsystemViews Empty { get; } = new(
        RekallAgeRuntimeRenderView.Empty,
        RekallAgeRuntimePhysicsView.Empty,
        RekallAgeRuntimeAudioView.Empty,
        RekallAgeRuntimeAnimationView.Empty,
        RekallAgeRuntimeUiView.Empty);
}

public sealed record RekallAgeRuntimeRenderView
{
    public static RekallAgeRuntimeRenderView Empty { get; } = new();
}

public sealed record RekallAgeRuntimePhysicsView
{
    public static RekallAgeRuntimePhysicsView Empty { get; } = new();
}

public sealed record RekallAgeRuntimeAudioView
{
    public static RekallAgeRuntimeAudioView Empty { get; } = new();
}

public sealed record RekallAgeRuntimeAnimationView
{
    public static RekallAgeRuntimeAnimationView Empty { get; } = new();
}

public sealed record RekallAgeRuntimeUiView
{
    public static RekallAgeRuntimeUiView Empty { get; } = new();
}

public sealed record RekallAgeRuntimeObservation(
    int Frame,
    string Code,
    string Severity,
    string Subsystem,
    string TargetId,
    string TargetName,
    string System,
    string Message,
    IReadOnlyList<string> SuggestedCommands)
{
    public string EntityId => TargetId;

    public string EntityName => TargetName;
}

public sealed record RekallAgeFrameContext(
    int FrameIndex,
    TimeSpan DeltaTime,
    TimeSpan ElapsedTime,
    CancellationToken CancellationToken);

public interface IRekallAgeRuntimeSystem
{
    string Id { get; }

    ValueTask UpdateAsync(
        RekallAgeSceneDocument scene,
        RekallAgeFrameContext context);
}

public sealed record RekallAgeSubsystemDescriptor(
    string Id,
    string Kind,
    string Status,
    IReadOnlyList<string> Capabilities);

public sealed class RekallAgeSubsystemRegistry
{
    private readonly List<RekallAgeSubsystemDescriptor> _subsystems = [];

    public IReadOnlyList<RekallAgeSubsystemDescriptor> Subsystems => _subsystems;

    public void Register(RekallAgeSubsystemDescriptor descriptor)
    {
        if (_subsystems.Any(item => item.Id.Equals(descriptor.Id, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Subsystem '{descriptor.Id}' is already registered.");
        }

        _subsystems.Add(descriptor);
    }
}
