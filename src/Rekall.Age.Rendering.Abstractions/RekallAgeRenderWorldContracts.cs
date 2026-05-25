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
    bool Active);

public sealed record RekallAgeRuntimeViewportRenderable(
    string EntityId,
    string EntityName,
    string Kind,
    string? AssetId,
    double X,
    double Y,
    double Z,
    int SortKey);

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
    int ObservationCount);
