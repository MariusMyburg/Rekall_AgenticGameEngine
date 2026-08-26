using System.Text.Json.Nodes;
using Rekall.Age.Rendering.Abstractions;
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
    public double DeltaSeconds { get; init; } = 1.0 / 60.0;

    public IReadOnlyList<string> SystemsRun { get; init; } = Array.Empty<string>();

    public string? ProjectRoot { get; init; }
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

public sealed record RekallAgeRuntimeSemanticActionSample(
    string Name,
    double Value = 1,
    bool IsDown = true,
    bool WasPressed = false,
    bool WasReleased = false);

public sealed record RekallAgeRuntimeControllerAxis(string Name, double Value);

public sealed record RekallAgeRuntimeControllerHat(string Name, int X, int Y);

public sealed record RekallAgeRuntimeControllerState(
    string DeviceId,
    string Kind,
    int PlayerIndex,
    IReadOnlyList<RekallAgeRuntimeControllerAxis> Axes,
    IReadOnlyList<string> PressedButtons,
    IReadOnlyList<string> PressedButtonsThisFrame,
    IReadOnlyList<string> ReleasedButtonsThisFrame,
    IReadOnlyList<RekallAgeRuntimeControllerHat> Hats);

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
    IReadOnlyList<RekallAgeRuntimeXrAction>? XrActions = null,
    double ViewportWidth = 0,
    double ViewportHeight = 0,
    IReadOnlyList<RekallAgeRuntimeSemanticActionSample>? SemanticActions = null,
    IReadOnlyList<RekallAgeRuntimeControllerState>? Controllers = null)
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
    IReadOnlyList<RekallAgeRuntimeXrAction>? XrActions = null,
    double ViewportWidth = 0,
    double ViewportHeight = 0,
    IReadOnlyList<RekallAgeRuntimeSemanticActionSample>? SemanticActions = null,
    IReadOnlyList<RekallAgeRuntimeControllerState>? Controllers = null)
{
    // Capture and inspection clients use this explicit per-frame duration when a
    // playable module consumes DeltaSeconds. Runtime simulation itself remains
    // fixed-step; authored realtime systems receive that fixed engine timestep.
    public double DeltaSeconds { get; init; } = 1.0 / 60.0;

    public int VerticalAxis { get; init; }

    public bool PrimaryAction { get; init; }

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
            XrActions,
            ViewportWidth,
            ViewportHeight,
            SemanticActions,
            Controllers);
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
    public IReadOnlyList<RekallAgeRuntimeControllerState> Controllers { get; init; } =
        Array.Empty<RekallAgeRuntimeControllerState>();

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
    string SourceEntityName)
{
    public string? PhysicalDeviceId { get; init; }

    public string? PhysicalDeviceKind { get; init; }
}

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
    IReadOnlyList<RekallAgeRuntimeRenderUiLayer> UiLayers,
    IReadOnlyList<RekallAgeRuntimeRenderPostProcessStack> PostProcessStacks)
{
    public IReadOnlyList<RekallAgeRuntimeGpuWorkload> GpuWorkloads { get; init; } =
        Array.Empty<RekallAgeRuntimeGpuWorkload>();

    public IReadOnlyList<RekallAgeRuntimeRenderQualityProfile> QualityProfiles { get; init; } =
        Array.Empty<RekallAgeRuntimeRenderQualityProfile>();

    public IReadOnlyList<RekallAgeRuntimeEnvironment3D> Environments { get; init; } =
        Array.Empty<RekallAgeRuntimeEnvironment3D>();

    public IReadOnlyList<RekallAgeRuntimeShadowSettings> ShadowSettings { get; init; } =
        Array.Empty<RekallAgeRuntimeShadowSettings>();

    public IReadOnlyList<RekallAgeRuntimeFogVolume> FogVolumes { get; init; } =
        Array.Empty<RekallAgeRuntimeFogVolume>();

    public IReadOnlyList<RekallAgeRuntimeParticleEmitter> ParticleEmitters { get; init; } =
        Array.Empty<RekallAgeRuntimeParticleEmitter>();

    public static RekallAgeRuntimeRenderView Empty { get; } = new(
        Array.Empty<RekallAgeRuntimeRenderCamera>(),
        Array.Empty<RekallAgeRuntimeRenderSprite>(),
        Array.Empty<RekallAgeRuntimeRenderMesh>(),
        Array.Empty<RekallAgeRuntimeRenderLight>(),
        Array.Empty<RekallAgeRuntimeRenderUiLayer>(),
        Array.Empty<RekallAgeRuntimeRenderPostProcessStack>());
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
    string Layer = "default",
    double Range = 10,
    int Priority = 0);

public sealed record RekallAgeRuntimeRenderUiLayer(
    string EntityId,
    string EntityName,
    int Layer,
    string ProjectionSource = RekallAgeRuntimeProjectionSources.Authored);

public sealed record RekallAgeRuntimeRenderPostProcessStack(
    string EntityId,
    string EntityName,
    bool Enabled,
    IReadOnlyList<RekallAgeRuntimeRenderPostProcessPass> Passes,
    string ProjectionSource = RekallAgeRuntimeProjectionSources.Authored);

public sealed record RekallAgeRuntimeRenderPostProcessPass(
    string Name,
    string Type,
    string Input = "sceneColor",
    string? Source = null,
    string Output = "sceneColor",
    double Scale = 1,
    int Iterations = 1,
    double Threshold = 1,
    double Intensity = 1,
    double Radius = 1,
    string BlendMode = "add");

public sealed record RekallAgeRuntimeRenderQualityProfile(
    string EntityId,
    string EntityName,
    RekallAgeRenderQualityIntent Intent)
{
    public string ProjectionSource { get; init; } = RekallAgeRuntimeProjectionSources.Authored;
}

public sealed record RekallAgeRuntimeEnvironment3D(
    string EntityId,
    string EntityName,
    string? SkyAssetId,
    double AmbientEnergy,
    double Exposure,
    string ToneMapper,
    double WhitePoint,
    string? ColorGradeAssetId,
    string BackgroundPolicy)
{
    public string ProjectionSource { get; init; } = RekallAgeRuntimeProjectionSources.Authored;

    public string AmbientSkyColor { get; init; } = "#ffffff";

    public string AmbientGroundColor { get; init; } = "#ffffff";
}

public sealed record RekallAgeRuntimeShadowSettings(
    string EntityId,
    string EntityName,
    int CascadeCount,
    int AtlasResolution,
    double MaximumDistance,
    string SplitPolicy,
    double Bias,
    double NormalBias,
    string Filter,
    bool Stabilization)
{
    public string ProjectionSource { get; init; } = RekallAgeRuntimeProjectionSources.Authored;
}

public sealed record RekallAgeRuntimeFogVolume(
    string EntityId,
    string EntityName,
    string Shape,
    double Density,
    string Albedo,
    string Emission,
    double Anisotropy,
    double HeightFalloff,
    double BlendDistance,
    int Priority)
{
    public string ProjectionSource { get; init; } = RekallAgeRuntimeProjectionSources.Authored;

    public RekallAgeRuntimeTransform Transform { get; init; } = RekallAgeRuntimeTransform.Identity;
}

public sealed record RekallAgeRuntimeParticleBurst(double TimeSeconds, int Count);

public sealed record RekallAgeRuntimeParticleScalarKey(double NormalizedAge, double Value);

public sealed record RekallAgeRuntimeParticleColorKey(double NormalizedAge, string Color);

public sealed record RekallAgeRuntimeParticleEmitter(
    string EntityId,
    string EntityName,
    bool Enabled,
    string SimulationSpace,
    int Capacity,
    double SpawnRate,
    IReadOnlyList<RekallAgeRuntimeParticleBurst> Bursts,
    double LifetimeSeconds,
    uint DeterministicSeed,
    RekallAgeRuntimeVector3 VelocityDirection,
    double VelocityConeDegrees,
    double MinimumSpeed,
    double MaximumSpeed,
    RekallAgeRuntimeVector3 Gravity,
    double Drag,
    IReadOnlyList<RekallAgeRuntimeParticleScalarKey> SizeCurve,
    IReadOnlyList<RekallAgeRuntimeParticleColorKey> ColorCurve,
    string DrawMode,
    bool Lit,
    double EmissiveIntensity,
    double SoftParticleFade,
    string? TextureAssetId,
    int FlipbookColumns,
    int FlipbookRows,
    double FlipbookFramesPerSecond,
    string BlendMode,
    int Priority,
    double VisibilityDistance,
    string Layer)
{
    public string ProjectionSource { get; init; } = RekallAgeRuntimeProjectionSources.Authored;

    public RekallAgeRuntimeTransform Transform { get; init; } = RekallAgeRuntimeTransform.Identity;
}

[Flags]
public enum RekallAgeRuntimeGpuBufferUsage
{
    None = 0,
    CopySource = 1 << 0,
    CopyDestination = 1 << 1,
    Vertex = 1 << 2,
    Index = 1 << 3,
    Uniform = 1 << 4,
    Storage = 1 << 5,
    Indirect = 1 << 6,
    Readback = 1 << 7
}

[Flags]
public enum RekallAgeRuntimeGpuTextureUsage
{
    None = 0,
    CopySource = 1 << 0,
    CopyDestination = 1 << 1,
    Sampled = 1 << 2,
    Storage = 1 << 3,
    ColorAttachment = 1 << 4,
    DepthStencilAttachment = 1 << 5,
    Present = 1 << 6
}

public enum RekallAgeRuntimeGpuTextureDimension { Texture1D, Texture2D, Texture3D, Cube }
public enum RekallAgeRuntimeGpuShaderStage { Vertex, Fragment, Compute }
public enum RekallAgeRuntimeGpuShaderLanguage { Glsl, SpirV, Wgsl }
public enum RekallAgeRuntimeGpuBindingType
{
    UniformBuffer,
    ReadOnlyStorageBuffer,
    StorageBuffer,
    Sampler,
    ComparisonSampler,
    SampledTexture,
    ReadOnlyStorageTexture,
    StorageTexture
}
public enum RekallAgeRuntimeGpuPipelineKind { Render, Compute }
public enum RekallAgeRuntimeGpuIndexFormat { UInt16, UInt32 }
public enum RekallAgeRuntimeGpuVertexStepMode { Vertex, Instance }
public enum RekallAgeRuntimeGpuVertexFormat
{
    Float32,
    Float32x2,
    Float32x3,
    Float32x4,
    Uint32,
    Uint32x2,
    Uint32x3,
    Uint32x4,
    Sint32,
    Sint32x2,
    Sint32x3,
    Sint32x4
}
public enum RekallAgeRuntimeGpuCommandKind
{
    CopyBuffer = 0,
    BeginRenderPass = 1,
    SetRenderPipeline = 2,
    SetComputePipeline = 3,
    SetBindingSet = 4,
    SetVertexBuffer = 5,
    SetIndexBuffer = 6,
    Draw = 7,
    DrawIndexed = 8,
    EndRenderPass = 9,
    BeginComputePass = 10,
    Dispatch = 11,
    EndComputePass = 12,
    DrawIndirect = 13,
    DrawIndexedIndirect = 14,
    DispatchIndirect = 15
}

public enum RekallAgeRuntimeGpuStorageAccess { ReadOnly, ReadWrite }

public sealed record RekallAgeRuntimeGpuBuffer(
    string Id,
    ulong SizeBytes,
    RekallAgeRuntimeGpuBufferUsage Usage)
{
    public string MemoryAccess { get; init; } = "device-local";
    public string? InitialDataAsset { get; init; }
    public IReadOnlyList<uint> InitialDataUInt32 { get; init; } = [];
    public uint StructureByteStride { get; init; }
    public RekallAgeRuntimeGpuStorageAccess StorageAccess { get; init; } = RekallAgeRuntimeGpuStorageAccess.ReadWrite;
}

public sealed record RekallAgeRuntimeGpuTexture(
    string Id,
    RekallAgeRuntimeGpuTextureDimension Dimension,
    int Width,
    int Height,
    int Depth,
    string Format,
    RekallAgeRuntimeGpuTextureUsage Usage)
{
    public int MipLevels { get; init; } = 1;
    public int ArrayLayers { get; init; } = 1;
    public int SampleCount { get; init; } = 1;
    public string? InitialDataAsset { get; init; }
}

public sealed record RekallAgeRuntimeGpuSampler(string Id)
{
    public string MinFilter { get; init; } = "linear";
    public string MagFilter { get; init; } = "linear";
    public string MipmapFilter { get; init; } = "linear";
    public string AddressU { get; init; } = "repeat";
    public string AddressV { get; init; } = "repeat";
    public string AddressW { get; init; } = "repeat";
    public int MaximumAnisotropy { get; init; } = 1;
}

public sealed record RekallAgeRuntimeGpuShader(
    string Id,
    RekallAgeRuntimeGpuShaderStage Stage,
    RekallAgeRuntimeGpuShaderLanguage Language,
    string Source)
{
    public string EntryPoint { get; init; } = "main";
}

public sealed record RekallAgeRuntimeGpuBindingLayoutEntry(
    int Binding,
    RekallAgeRuntimeGpuBindingType Type,
    IReadOnlyList<RekallAgeRuntimeGpuShaderStage> Visibility)
{
    public ulong MinimumBindingSize { get; init; }
}

public sealed record RekallAgeRuntimeGpuBindingLayout(
    string Id,
    IReadOnlyList<RekallAgeRuntimeGpuBindingLayoutEntry> Entries);

public sealed record RekallAgeRuntimeGpuBinding(
    int Binding,
    string Resource)
{
    public ulong Offset { get; init; }
    public ulong SizeBytes { get; init; }
}

public sealed record RekallAgeRuntimeGpuBindingSet(
    string Id,
    string Layout,
    IReadOnlyList<RekallAgeRuntimeGpuBinding> Bindings);

public sealed record RekallAgeRuntimeGpuVertexAttribute(
    string Name,
    int Location,
    RekallAgeRuntimeGpuVertexFormat Format,
    int OffsetBytes);

public sealed record RekallAgeRuntimeGpuVertexBufferLayout(
    int StrideBytes,
    RekallAgeRuntimeGpuVertexStepMode StepMode,
    IReadOnlyList<RekallAgeRuntimeGpuVertexAttribute> Attributes);

public sealed record RekallAgeRuntimeGpuPipeline(
    string Id,
    RekallAgeRuntimeGpuPipelineKind Kind)
{
    public string? VertexShader { get; init; }
    public string? FragmentShader { get; init; }
    public string? ComputeShader { get; init; }
    public IReadOnlyList<string> BindingLayouts { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ColorFormats { get; init; } = Array.Empty<string>();
    public string? DepthStencilFormat { get; init; }
    public string PrimitiveTopology { get; init; } = "triangle-list";
    public string CullMode { get; init; } = "back";
    public IReadOnlyList<RekallAgeRuntimeGpuVertexBufferLayout> VertexBuffers { get; init; } =
        Array.Empty<RekallAgeRuntimeGpuVertexBufferLayout>();
}

public sealed record RekallAgeRuntimeGpuRenderTarget(
    string Id,
    IReadOnlyList<string> ColorAttachments,
    int Width,
    int Height)
{
    public string? DepthStencilAttachment { get; init; }
}

public sealed record RekallAgeRuntimeGpuClearColor(float Red, float Green, float Blue, float Alpha);

public sealed record RekallAgeRuntimeGpuCommand(RekallAgeRuntimeGpuCommandKind Kind)
{
    public string? Resource { get; init; }
    public string? Source { get; init; }
    public string? Destination { get; init; }
    public string? Label { get; init; }
    public int Slot { get; init; }
    public int BindingSetIndex { get; init; }
    public ulong SourceOffset { get; init; }
    public ulong DestinationOffset { get; init; }
    public ulong SizeBytes { get; init; }
    public RekallAgeRuntimeGpuIndexFormat IndexFormat { get; init; } = RekallAgeRuntimeGpuIndexFormat.UInt32;
    public uint VertexCount { get; init; }
    public uint IndexCount { get; init; }
    public uint InstanceCount { get; init; } = 1;
    public uint FirstVertex { get; init; }
    public uint FirstIndex { get; init; }
    public int BaseVertex { get; init; }
    public uint FirstInstance { get; init; }
    public uint GroupCountX { get; init; } = 1;
    public uint GroupCountY { get; init; } = 1;
    public uint GroupCountZ { get; init; } = 1;
    public uint IndirectCount { get; init; } = 1;
    public uint IndirectStrideBytes { get; init; }
    public IReadOnlyList<RekallAgeRuntimeGpuClearColor> ClearColors { get; init; } =
        Array.Empty<RekallAgeRuntimeGpuClearColor>();
    public float? ClearDepth { get; init; }
}

public sealed record RekallAgeRuntimeGpuWorkload(string Id)
{
    public bool Enabled { get; init; } = true;
    public IReadOnlyList<RekallAgeRuntimeGpuBuffer> Buffers { get; init; } = Array.Empty<RekallAgeRuntimeGpuBuffer>();
    public IReadOnlyList<RekallAgeRuntimeGpuTexture> Textures { get; init; } = Array.Empty<RekallAgeRuntimeGpuTexture>();
    public IReadOnlyList<RekallAgeRuntimeGpuSampler> Samplers { get; init; } = Array.Empty<RekallAgeRuntimeGpuSampler>();
    public IReadOnlyList<RekallAgeRuntimeGpuShader> Shaders { get; init; } = Array.Empty<RekallAgeRuntimeGpuShader>();
    public IReadOnlyList<RekallAgeRuntimeGpuBindingLayout> BindingLayouts { get; init; } = Array.Empty<RekallAgeRuntimeGpuBindingLayout>();
    public IReadOnlyList<RekallAgeRuntimeGpuBindingSet> BindingSets { get; init; } = Array.Empty<RekallAgeRuntimeGpuBindingSet>();
    public IReadOnlyList<RekallAgeRuntimeGpuPipeline> Pipelines { get; init; } = Array.Empty<RekallAgeRuntimeGpuPipeline>();
    public IReadOnlyList<RekallAgeRuntimeGpuRenderTarget> RenderTargets { get; init; } = Array.Empty<RekallAgeRuntimeGpuRenderTarget>();
    public IReadOnlyList<RekallAgeRuntimeGpuCommand> Commands { get; init; } = Array.Empty<RekallAgeRuntimeGpuCommand>();
}

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
    public IReadOnlyList<RekallAgeRuntimeAudioVoice> Voices { get; init; } =
        Array.Empty<RekallAgeRuntimeAudioVoice>();

    public IReadOnlyList<RekallAgeRuntimeAudioBus> Buses { get; init; } =
        Array.Empty<RekallAgeRuntimeAudioBus>();

    public RekallAgeRuntimeAudioMixFrame MixFrame { get; init; } =
        RekallAgeRuntimeAudioMixFrame.Silent;

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

public sealed record RekallAgeRuntimeAudioVoice(
    string EntityId,
    string EntityName,
    string ClipAssetId,
    string Bus,
    string State,
    bool Loop,
    double PlaybackSeconds,
    double DurationSeconds,
    double Gain,
    double Pitch,
    double LeftGain,
    double RightGain);

public sealed record RekallAgeRuntimeAudioBus(
    string Name,
    double Gain,
    bool Muted);

public sealed record RekallAgeRuntimeAudioMixFrame(
    int FrameIndex,
    int ActiveVoiceCount,
    double PeakGain,
    int SampleRate = 48_000,
    int Channels = 2,
    IReadOnlyList<float>? Samples = null)
{
    public static RekallAgeRuntimeAudioMixFrame Silent { get; } = new(0, 0, 0, Samples: Array.Empty<float>());
}

public sealed record RekallAgeRuntimeAnimationView(
    IReadOnlyList<RekallAgeRuntimeAnimationPlayer> Players)
{
    public IReadOnlyList<RekallAgeRuntimeMorphState> MorphStates { get; init; } =
        Array.Empty<RekallAgeRuntimeMorphState>();

    public static RekallAgeRuntimeAnimationView Empty { get; } = new(
        Array.Empty<RekallAgeRuntimeAnimationPlayer>());
}

public sealed record RekallAgeRuntimeMorphState(
    string EntityId,
    string EntityName,
    IReadOnlyList<double> Weights);

public sealed record RekallAgeRuntimeAnimationPlayer(
    string EntityId,
    string EntityName,
    string Kind,
    string? ClipAssetId)
{
    public bool InlineClip { get; init; }

    public bool Playing { get; init; }

    public double TimeSeconds { get; init; }

    public double DurationSeconds { get; init; }

    public string LoopMode { get; init; } = "loop";

    public int LayerCount { get; init; }

    public int ActiveLayerCount { get; init; }

    public IReadOnlyList<RekallAgeRuntimeAnimationLayer> Layers { get; init; } =
        Array.Empty<RekallAgeRuntimeAnimationLayer>();

    public string? AnimationName { get; init; }

    public string? SkinName { get; init; }

    public int JointCount { get; init; }

    public string? StateName { get; init; }

    public string? PreviousStateName { get; init; }

    public double TransitionProgress { get; init; } = 1;
}

public sealed record RekallAgeRuntimeAnimationLayer(
    string Name,
    string? ClipAssetId,
    double Weight,
    double TargetWeight,
    double TimeSeconds,
    double DurationSeconds,
    bool Playing);

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
    int Layer)
{
    public double ReferenceWidth { get; init; } = 1920;

    public double ReferenceHeight { get; init; } = 1080;
}

public sealed record RekallAgeRuntimeUiElement(
    string EntityId,
    string EntityName,
    string Kind,
    bool Interactive)
{
    public RekallAgeRuntimeUiLayout? Layout { get; init; }

    public string Text { get; init; } = string.Empty;

    public string BackgroundColor { get; init; } = "#00000000";

    public string ForegroundColor { get; init; } = "#ffffff";

    public string BorderColor { get; init; } = "#00000000";

    public double BorderWidth { get; init; }

    public double FontSize { get; init; } = 16;

    public string FontFamily { get; init; } = "Segoe UI";

    public string FontWeight { get; init; } = "normal";

    public string FontStyle { get; init; } = "normal";

    public string? FontAssetId { get; init; }

    public string? AssetId { get; init; }
}

public sealed record RekallAgeRuntimeUiLayout(
    string CanvasEntityId,
    double ReferenceWidth,
    double ReferenceHeight,
    double X,
    double Y,
    double Width,
    double Height,
    double ClipX,
    double ClipY,
    double ClipWidth,
    double ClipHeight);

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
