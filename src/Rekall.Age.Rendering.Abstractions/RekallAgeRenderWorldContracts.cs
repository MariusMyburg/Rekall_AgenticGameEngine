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
    IReadOnlyList<RekallAgeRuntimeViewportObservation> Observations);

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
    string ClearColor = "#101820");

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
    RekallAgeRuntimeViewportShaderPipeline? ShaderPipeline = null);

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
