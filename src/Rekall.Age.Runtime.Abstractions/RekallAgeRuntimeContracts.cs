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
    IReadOnlyList<RekallAgeRuntimeObservation> Observations)
{
    public IReadOnlyList<string> SystemsRun { get; init; } = Array.Empty<string>();
}

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

public sealed record RekallAgeRuntimeInputState(
    double MouseX = 0,
    double MouseY = 0,
    double MouseDeltaX = 0,
    double MouseDeltaY = 0,
    double MouseWheelDelta = 0,
    IReadOnlySet<string>? PressedKeys = null,
    IReadOnlySet<string>? PressedKeysThisFrame = null,
    IReadOnlySet<string>? ReleasedKeysThisFrame = null,
    IReadOnlySet<string>? PressedButtons = null,
    IReadOnlySet<string>? PressedButtonsThisFrame = null,
    IReadOnlySet<string>? ReleasedButtonsThisFrame = null,
    IReadOnlyList<RekallAgeRuntimeXrPose>? XrPoses = null,
    IReadOnlyList<RekallAgeRuntimeXrAction>? XrActions = null)
{
    public static RekallAgeRuntimeInputState Empty { get; } = new();
}

public sealed record RekallAgeRuntimeInputFrame(
    double MouseX = 0,
    double MouseY = 0,
    double MouseDeltaX = 0,
    double MouseDeltaY = 0,
    double MouseWheelDelta = 0,
    IReadOnlyList<string>? PressedKeys = null,
    IReadOnlyList<string>? PressedKeysThisFrame = null,
    IReadOnlyList<string>? ReleasedKeysThisFrame = null,
    IReadOnlyList<string>? PressedButtons = null,
    IReadOnlyList<string>? PressedButtonsThisFrame = null,
    IReadOnlyList<string>? ReleasedButtonsThisFrame = null,
    IReadOnlyList<RekallAgeRuntimeXrPose>? XrPoses = null,
    IReadOnlyList<RekallAgeRuntimeXrAction>? XrActions = null)
{
    public RekallAgeRuntimeInputState ToState()
    {
        return new RekallAgeRuntimeInputState(
            MouseX,
            MouseY,
            MouseDeltaX,
            MouseDeltaY,
            MouseWheelDelta,
            ToSet(PressedKeys),
            ToSet(PressedKeysThisFrame),
            ToSet(ReleasedKeysThisFrame),
            ToSet(PressedButtons),
            ToSet(PressedButtonsThisFrame),
            ToSet(ReleasedButtonsThisFrame),
            XrPoses,
            XrActions);
    }

    private static IReadOnlySet<string>? ToSet(IReadOnlyList<string>? values)
    {
        return values is null
            ? null
            : values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}

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

public sealed record RekallAgeRuntimeXrPose(
    string Source,
    bool IsTracked,
    double X = 0,
    double Y = 0,
    double Z = 0,
    double Pitch = 0,
    double Yaw = 0,
    double Roll = 0);

public sealed record RekallAgeRuntimeXrAction(
    string Hand,
    string Name,
    double Value,
    bool IsDown,
    bool WasPressed,
    bool WasReleased);

public sealed record RekallAgeRuntimeSubsystemViews(
    RekallAgeRuntimeRenderView Rendering,
    RekallAgeRuntimePhysicsView Physics,
    RekallAgeRuntimeAudioView Audio,
    RekallAgeRuntimeAnimationView Animation,
    RekallAgeRuntimeUiView Ui)
{
    public RekallAgeRuntimeInputView Input { get; init; } = RekallAgeRuntimeInputView.Empty;

    public RekallAgeRuntimeEventView Events { get; init; } = RekallAgeRuntimeEventView.Empty;

    public RekallAgeRuntimeMultiplayerView Multiplayer { get; init; } =
        RekallAgeRuntimeMultiplayerView.Empty;

    public RekallAgeRuntimeXrView Xr { get; init; } = RekallAgeRuntimeXrView.Empty;

    public static RekallAgeRuntimeSubsystemViews Empty { get; } = new(
        RekallAgeRuntimeRenderView.Empty,
        RekallAgeRuntimePhysicsView.Empty,
        RekallAgeRuntimeAudioView.Empty,
        RekallAgeRuntimeAnimationView.Empty,
        RekallAgeRuntimeUiView.Empty);
}

public sealed record RekallAgeRuntimeInputView(
    IReadOnlyList<RekallAgeRuntimeInputAction> Actions)
{
    public static RekallAgeRuntimeInputView Empty { get; } = new(
        Array.Empty<RekallAgeRuntimeInputAction>());
}

public sealed record RekallAgeRuntimeInputAction(
    string Name,
    double Value,
    bool IsDown,
    bool WasPressed,
    bool WasReleased,
    string SourceEntityId,
    string SourceEntityName);

public sealed record RekallAgeRuntimeEventView(
    IReadOnlyList<RekallAgeRuntimeEvent> Events)
{
    public static RekallAgeRuntimeEventView Empty { get; } = new(
        Array.Empty<RekallAgeRuntimeEvent>());
}

public sealed record RekallAgeRuntimeEvent(
    int Frame,
    string Type,
    string EntityId,
    string EntityName,
    string Source,
    string? Handler,
    JsonObject Payload);

public sealed record RekallAgeRuntimeXrView(
    IReadOnlyList<RekallAgeRuntimeXrRig> Rigs,
    IReadOnlyList<RekallAgeRuntimeXrController> Controllers,
    IReadOnlyList<RekallAgeRuntimeXrTrackedPose> Poses,
    IReadOnlyList<RekallAgeRuntimeXrAction> Actions)
{
    public static RekallAgeRuntimeXrView Empty { get; } = new(
        Array.Empty<RekallAgeRuntimeXrRig>(),
        Array.Empty<RekallAgeRuntimeXrController>(),
        Array.Empty<RekallAgeRuntimeXrTrackedPose>(),
        Array.Empty<RekallAgeRuntimeXrAction>());
}

public sealed record RekallAgeRuntimeXrRig(
    string EntityId,
    string EntityName,
    string TrackingSpace,
    string ViewConfiguration,
    bool Active);

public sealed record RekallAgeRuntimeXrController(
    string EntityId,
    string EntityName,
    string Hand,
    string PoseSource,
    bool Active);

public sealed record RekallAgeRuntimeXrTrackedPose(
    string EntityId,
    string EntityName,
    string Source,
    bool IsTracked,
    double X,
    double Y,
    double Z,
    double Pitch,
    double Yaw,
    double Roll);

public sealed record RekallAgeRuntimeMultiplayerView(
    IReadOnlyList<RekallAgeRuntimeNetworkSession> Sessions,
    IReadOnlyList<RekallAgeRuntimeNetworkEntity> Entities)
{
    public static RekallAgeRuntimeMultiplayerView Empty { get; } = new(
        Array.Empty<RekallAgeRuntimeNetworkSession>(),
        Array.Empty<RekallAgeRuntimeNetworkEntity>());
}

public sealed record RekallAgeRuntimeNetworkSession(
    string EntityId,
    string EntityName,
    string Role,
    string Authority,
    int TickRate,
    int SnapshotRate,
    int MaxPlayers,
    string Transport,
    string Address,
    int Port,
    bool ClientPrediction,
    int InterpolationDelayMilliseconds);

public sealed record RekallAgeRuntimeNetworkEntity(
    string EntityId,
    string EntityName,
    string NetworkId,
    string? OwnerClientId,
    string Authority,
    bool ReplicatePosition,
    bool ReplicateRotation,
    bool ReplicateScale,
    string Prediction,
    int Priority);

public sealed record RekallAgeRuntimeRenderView(
    IReadOnlyList<RekallAgeRuntimeRenderCamera> Cameras,
    IReadOnlyList<RekallAgeRuntimeRenderSprite> Sprites,
    IReadOnlyList<RekallAgeRuntimeRenderMesh> Meshes,
    IReadOnlyList<RekallAgeRuntimeRenderLight> Lights,
    IReadOnlyList<RekallAgeRuntimeRenderUiLayer> UiLayers)
{
    public static RekallAgeRuntimeRenderView Empty { get; } = new(
        Array.Empty<RekallAgeRuntimeRenderCamera>(),
        Array.Empty<RekallAgeRuntimeRenderSprite>(),
        Array.Empty<RekallAgeRuntimeRenderMesh>(),
        Array.Empty<RekallAgeRuntimeRenderLight>(),
        Array.Empty<RekallAgeRuntimeRenderUiLayer>());
}

public sealed record RekallAgeRuntimeRenderCamera(
    string EntityId,
    string EntityName,
    string Kind,
    bool Active,
    string ProjectionSource = RekallAgeRuntimeProjectionSources.Authored,
    string ProjectionMode = "perspective",
    double FieldOfViewDegrees = 65,
    double OrthographicSize = 10,
    double NearClip = 0.05,
    double FarClip = 1000,
    string ClearColor = "#101820",
    string StereoMode = "mono",
    string StereoRenderMode = "single-pass-multiview",
    double InterpupillaryDistance = 0.064,
    double StereoConvergenceDistance = 10,
    string XrViewConfiguration = "primary-stereo",
    bool FoveatedRendering = false,
    string CullingMask = "*",
    double RenderOrder = 0,
    double ViewportX = 0,
    double ViewportY = 0,
    double ViewportWidth = 1,
    double ViewportHeight = 1);

public sealed record RekallAgeRuntimeRenderSprite(
    string EntityId,
    string EntityName,
    string? AssetId,
    string ProjectionSource = RekallAgeRuntimeProjectionSources.Authored,
    string Layer = "default");

public sealed record RekallAgeRuntimeRenderMesh(
    string EntityId,
    string EntityName,
    string? AssetId,
    string? Variant = null,
    string? TextureAssetId = null,
    string? MaterialColor = null,
    string Kind = "mesh",
    int SortKey = 200,
    RekallAgeRuntimeRenderShaderPipeline? ShaderPipeline = null,
    string ProjectionSource = RekallAgeRuntimeProjectionSources.Authored,
    string Layer = "default");

public sealed record RekallAgeRuntimeRenderShaderPipeline(
    string VertexShader,
    string FragmentShader);

public sealed record RekallAgeRuntimeRenderLight(
    string EntityId,
    string EntityName,
    string Kind,
    double Intensity,
    string ProjectionSource = RekallAgeRuntimeProjectionSources.Authored,
    string? Color = null,
    string Layer = "default");

public sealed record RekallAgeRuntimeRenderUiLayer(
    string EntityId,
    string EntityName,
    int Layer,
    string ProjectionSource = RekallAgeRuntimeProjectionSources.Authored);

public static class RekallAgeRuntimeProjectionSources
{
    public const string Authored = "authored";
    public const string BuiltIn = "built-in";
}

public sealed record RekallAgeRuntimePhysicsView(
    IReadOnlyList<RekallAgeRuntimePhysicsBody> RigidBodies,
    IReadOnlyList<RekallAgeRuntimePhysicsCollider> Colliders,
    IReadOnlyList<RekallAgeRuntimePhysicsCollider> Triggers)
{
    public static RekallAgeRuntimePhysicsView Empty { get; } = new(
        Array.Empty<RekallAgeRuntimePhysicsBody>(),
        Array.Empty<RekallAgeRuntimePhysicsCollider>(),
        Array.Empty<RekallAgeRuntimePhysicsCollider>());
}

public sealed record RekallAgeRuntimePhysicsBody(
    string EntityId,
    string EntityName,
    string Kind);

public sealed record RekallAgeRuntimePhysicsCollider(
    string EntityId,
    string EntityName,
    string Kind);

public sealed record RekallAgeRuntimeAudioView(
    IReadOnlyList<RekallAgeRuntimeAudioListener> Listeners,
    IReadOnlyList<RekallAgeRuntimeAudioEmitter> Emitters)
{
    public static RekallAgeRuntimeAudioView Empty { get; } = new(
        Array.Empty<RekallAgeRuntimeAudioListener>(),
        Array.Empty<RekallAgeRuntimeAudioEmitter>());
}

public sealed record RekallAgeRuntimeAudioListener(
    string EntityId,
    string EntityName);

public sealed record RekallAgeRuntimeAudioEmitter(
    string EntityId,
    string EntityName,
    string? ClipAssetId,
    string? Bus);

public sealed record RekallAgeRuntimeAnimationView(
    IReadOnlyList<RekallAgeRuntimeAnimationPlayer> Players)
{
    public static RekallAgeRuntimeAnimationView Empty { get; } = new(
        Array.Empty<RekallAgeRuntimeAnimationPlayer>());
}

public sealed record RekallAgeRuntimeAnimationPlayer(
    string EntityId,
    string EntityName,
    string Kind,
    string? ClipAssetId);

public sealed record RekallAgeRuntimeUiView(
    IReadOnlyList<RekallAgeRuntimeUiCanvas> Canvases,
    IReadOnlyList<RekallAgeRuntimeUiElement> Elements,
    int InteractiveElementCount)
{
    public static RekallAgeRuntimeUiView Empty { get; } = new(
        Array.Empty<RekallAgeRuntimeUiCanvas>(),
        Array.Empty<RekallAgeRuntimeUiElement>(),
        0);
}

public sealed record RekallAgeRuntimeUiCanvas(
    string EntityId,
    string EntityName,
    int Layer);

public sealed record RekallAgeRuntimeUiElement(
    string EntityId,
    string EntityName,
    string Kind,
    bool Interactive);

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
