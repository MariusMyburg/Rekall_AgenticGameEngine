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
    RekallAgeRuntimeViewportStereoSettings? Stereo = null,
    RekallAgeRuntimeViewportPostProcessStack? PostProcessStack = null)
{
    public RekallAgeRuntimeViewportCulling Culling { get; init; } = RekallAgeRuntimeViewportCulling.Empty;

    public IReadOnlyList<RekallAgeRuntimeViewportCameraView> CameraViews { get; init; } =
        Array.Empty<RekallAgeRuntimeViewportCameraView>();

    public RekallAgeRuntimeViewportCamera? HeadsetCamera { get; init; }

    public RekallAgeRuntimeViewportFrame ForHeadsetOutput()
    {
        return HeadsetCamera is null ? this : ForCameraView(HeadsetCamera);
    }

    public RekallAgeRuntimeViewportFrame ForCameraView(RekallAgeRuntimeViewportCamera camera)
    {
        var view = CameraViews.FirstOrDefault(item =>
            item.Camera.EntityId.Equals(camera.EntityId, StringComparison.Ordinal));
        if (view is null)
        {
            return this with { ActiveCamera = camera };
        }

        return this with
        {
            ActiveCamera = camera,
            Renderables = view.Renderables,
            Culling = new RekallAgeRuntimeViewportCulling(view.CulledRenderables.Count, view.CulledRenderables)
        };
    }
}

public sealed record RekallAgeRuntimeViewportCameraView(
    RekallAgeRuntimeViewportCamera Camera,
    RekallAgeRuntimeViewportCameraRect PixelRect,
    IReadOnlyList<RekallAgeRuntimeViewportRenderable> Renderables,
    IReadOnlyList<RekallAgeRuntimeViewportCulledRenderable> CulledRenderables);

public readonly record struct RekallAgeRuntimeViewportCameraRect(
    int X,
    int Y,
    int Width,
    int Height)
{
    public static RekallAgeRuntimeViewportCameraRect FromFrame(RekallAgeRuntimeViewportFrame frame)
    {
        if (frame.Width <= 0 || frame.Height <= 0)
        {
            return new RekallAgeRuntimeViewportCameraRect(0, 0, 0, 0);
        }

        var camera = frame.ActiveCamera;
        if (camera is null)
        {
            return new RekallAgeRuntimeViewportCameraRect(0, 0, frame.Width, frame.Height);
        }

        return FromCamera(frame.Width, frame.Height, camera);
    }

    public static RekallAgeRuntimeViewportCameraRect FromCamera(
        int frameWidth,
        int frameHeight,
        RekallAgeRuntimeViewportCamera camera)
    {
        if (frameWidth <= 0 || frameHeight <= 0)
        {
            return new RekallAgeRuntimeViewportCameraRect(0, 0, 0, 0);
        }

        var x = (int)Math.Round(Math.Clamp(camera.ViewportX, 0, 1) * frameWidth);
        var y = (int)Math.Round(Math.Clamp(camera.ViewportY, 0, 1) * frameHeight);
        x = Math.Clamp(x, 0, frameWidth - 1);
        y = Math.Clamp(y, 0, frameHeight - 1);
        var requestedWidth = (int)Math.Round(Math.Clamp(camera.ViewportWidth, 0.001, 1) * frameWidth);
        var requestedHeight = (int)Math.Round(Math.Clamp(camera.ViewportHeight, 0.001, 1) * frameHeight);
        var width = Math.Clamp(requestedWidth, 1, frameWidth - x);
        var height = Math.Clamp(requestedHeight, 1, frameHeight - y);
        return new RekallAgeRuntimeViewportCameraRect(x, y, width, height);
    }
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
    string CullingMask = "*",
    double RenderOrder = 0,
    double ViewportX = 0,
    double ViewportY = 0,
    double ViewportWidth = 1,
    double ViewportHeight = 1);

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

public sealed record RekallAgeRuntimeViewportPostProcessStack(
    string EntityId,
    string EntityName,
    bool Enabled,
    IReadOnlyList<RekallAgeRuntimeViewportPostProcessPass> Passes);

public sealed record RekallAgeRuntimeViewportPostProcessPass(
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
    string Layer = "default",
    RekallAgeRuntimeViewportProceduralMaterial? ProceduralMaterial = null,
    RekallAgeRuntimeViewportAtmosphereMaterial? Atmosphere = null,
    RekallAgeRuntimeViewportCloudLayerMaterial? CloudLayer = null,
    RekallAgeRuntimeViewportCloudShadowMaterial? CloudShadow = null,
    RekallAgeRuntimeViewportSurfaceWaterMaterial? SurfaceWater = null,
    int MeshSlices = 0,
    int MeshStacks = 0,
    string FacingMode = "world");

public sealed record RekallAgeRuntimeViewportAtmosphereMaterial(
    double PlanetRadius,
    double AtmosphereRadius,
    string RayleighColor = "#7fb6ff",
    string MieColor = "#ffffff",
    double Density = 1,
    double DensityFalloff = 0.18,
    double RayleighScattering = 0.006,
    double MieScattering = 0.002,
    double MieAnisotropy = 0.76,
    double SunIntensity = 22,
    double Exposure = 1.2,
    int ViewSampleCount = 16,
    int LightSampleCount = 8,
    string OzoneAbsorptionColor = "#ffd199",
    double OzoneAbsorption = 0,
    double AerialPerspectiveStrength = 0.38);

public sealed record RekallAgeRuntimeViewportCloudLayerMaterial(
    double Radius,
    string Color = "#ffffff",
    bool AlphaFromTextureOnly = true,
    double Coverage = 1,
    double LambertianStrength = 0.45,
    double AmbientStrength = 0.18);

public sealed record RekallAgeRuntimeViewportCloudShadowMaterial(
    string TextureAssetId,
    double CloudRadius,
    double Strength = 0.35);

public sealed record RekallAgeRuntimeViewportSurfaceWaterMaterial(
    string TextureAssetId,
    double Coverage = 1,
    double SpecularStrength = 2.5,
    double Roughness = 0.06);

public sealed record RekallAgeRuntimeViewportProceduralMaterial(
    string Generator = "checker",
    int Resolution = 128,
    double Scale = 8,
    int Seed = 0,
    string BaseColorA = "#ffffff",
    string BaseColorB = "#202020",
    double MetallicFactor = 0,
    double RoughnessA = 1,
    double RoughnessB = 1,
    double NormalStrength = 0,
    double EmissiveStrength = 0);

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
