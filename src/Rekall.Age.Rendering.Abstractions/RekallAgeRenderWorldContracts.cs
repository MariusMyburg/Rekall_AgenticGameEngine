namespace Rekall.Age.Rendering.Abstractions;

public sealed record RekallAgeViewportModel(
    string SceneName,
    string? ActiveCameraName,
    RekallAgeRenderWorld RenderWorld);

public sealed record RekallAgeRenderWorld(
    IReadOnlyList<RekallAgeRenderCamera> Cameras,
    IReadOnlyList<RekallAgeRenderSprite> Sprites,
    IReadOnlyList<RekallAgeRenderMesh> Meshes,
    IReadOnlyList<RekallAgeRenderLight> Lights);

public sealed record RekallAgeRenderCamera(
    string EntityId,
    string EntityName,
    string Kind,
    bool Active);

public sealed record RekallAgeRenderSprite(
    string EntityId,
    string EntityName,
    string? AssetId);

public sealed record RekallAgeRenderMesh(
    string EntityId,
    string EntityName,
    string? AssetId);

public sealed record RekallAgeRenderLight(
    string EntityId,
    string EntityName,
    string Kind);

public sealed record RekallAgeRuntimeViewportFrame(
    string SceneName,
    int FrameIndex,
    double ElapsedSeconds,
    int Width,
    int Height,
    RekallAgeRuntimeViewportCamera? ActiveCamera,
    IReadOnlyList<RekallAgeRuntimeViewportCamera> Cameras,
    IReadOnlyList<RekallAgeRuntimeViewportRenderable> Renderables,
    int UiLayerCount,
    RekallAgeRuntimeViewportOverlay DebugOverlay,
    IReadOnlyList<RekallAgeRuntimeViewportObservation> Observations,
    RekallAgeRuntimeViewportStereoSettings? Stereo = null)
{
    public RekallAgeRuntimeViewportCulling Culling { get; init; } = RekallAgeRuntimeViewportCulling.Empty;
}

public sealed record RekallAgeRuntimeViewportCulling(
    int CulledRenderableCount,
    IReadOnlyList<RekallAgeRuntimeViewportCulledRenderable> CulledRenderables)
{
    public static RekallAgeRuntimeViewportCulling Empty { get; } = new(
        0,
        Array.Empty<RekallAgeRuntimeViewportCulledRenderable>());
}

public sealed record RekallAgeRuntimeViewportCulledRenderable(
    string EntityId,
    string EntityName,
    string Kind,
    string Layer,
    string Reason,
    string? CameraEntityId,
    string? CameraEntityName,
    string CullingMask);

public sealed record RekallAgeRuntimeViewportCamera(
    string EntityId,
    string EntityName,
    string Kind,
    bool Active,
    double X = 0,
    double Y = 0,
    double Z = 0,
    double RotationX = 0,
    double RotationY = 0,
    double RotationZ = 0,
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
    string CullingMask = "*");

public sealed record RekallAgeRuntimeViewportStereoSettings(
    bool Enabled,
    string Mode,
    string RenderMode,
    int EyeCount,
    double InterpupillaryDistance,
    double ConvergenceDistance,
    string XrViewConfiguration,
    bool FoveatedRendering,
    bool PreferSinglePassMultiview,
    IReadOnlyList<RekallAgeRuntimeViewportEye> Eyes);

public sealed record RekallAgeRuntimeViewportEye(
    string Name,
    int Index,
    double OffsetX,
    double OffsetY,
    double OffsetZ,
    double ViewportX,
    double ViewportY,
    double ViewportWidth,
    double ViewportHeight);

public sealed record RekallAgeRuntimeViewportRenderable(
    string EntityId,
    string EntityName,
    string Kind,
    string? AssetId,
    double X,
    double Y,
    double Z,
    int SortKey,
    string? Variant = null,
    double RotationX = 0,
    double RotationY = 0,
    double RotationZ = 0,
    double ScaleX = 1,
    double ScaleY = 1,
    double ScaleZ = 1,
    double Intensity = 1,
    string? MaterialColor = null,
    RekallAgeRuntimeViewportGeometryMesh? GeometryMesh = null,
    string? TextureAssetId = null,
    string? MetallicRoughnessTextureAssetId = null,
    string? NormalTextureAssetId = null,
    string? OcclusionTextureAssetId = null,
    double MetallicFactor = 0,
    double RoughnessFactor = 1,
    double NormalScale = 1,
    double OcclusionStrength = 1,
    string? EmissiveColor = null,
    string? EmissiveTextureAssetId = null,
    double EmissiveStrength = 0,
    RekallAgeRuntimeViewportShaderPipeline? ShaderPipeline = null,
    RekallAgeRuntimeViewportLineSegments? LineSegments = null,
    string Layer = "default");

public sealed record RekallAgeRuntimeViewportShaderPipeline(
    string VertexShader,
    string FragmentShader);

public sealed record RekallAgeRuntimeViewportGeometryMesh(
    IReadOnlyList<RekallAgeRuntimeViewportGeometryVertex> Vertices,
    IReadOnlyList<ushort> Indices);

public sealed record RekallAgeRuntimeViewportGeometryVertex(
    double X,
    double Y,
    double Z,
    double NormalX = 0,
    double NormalY = 1,
    double NormalZ = 0,
    double R = double.NaN,
    double G = double.NaN,
    double B = double.NaN,
    double A = double.NaN,
    double U = 0,
    double V = 0);

public sealed record RekallAgeRuntimeViewportLineSegments(
    IReadOnlyList<RekallAgeRuntimeViewportLineSegment> Segments,
    double Thickness = 0.02);

public sealed record RekallAgeRuntimeViewportLineSegment(
    double FromX,
    double FromY,
    double FromZ,
    double ToX,
    double ToY,
    double ToZ);

public sealed record RekallAgeRuntimeViewportOverlay(
    bool Enabled,
    int ObservationCount);

public sealed record RekallAgeRuntimeViewportObservation(
    string Code,
    string Severity,
    string Subsystem,
    string Target,
    string Message);

public sealed record RekallAgeRuntimeViewportCapture(
    bool Captured,
    string ScreenshotPath,
    bool NonBlank,
    int Width,
    int Height,
    int FrameIndex,
    string? ActiveCamera,
    int RenderableCount,
    int ObservationCount,
    int AssetBackedRenderableCount,
    int FallbackRenderableCount,
    int MissingAssetCount,
    int UnsupportedAssetCount,
    IReadOnlyList<string> AssetIssueCodes);
