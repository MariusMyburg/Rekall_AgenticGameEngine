namespace Rekall.Age.Modules.BuiltIns;

[RekallAgeModule("rekall.builtins", "Rekall Built-ins")]
[RekallAgeRequiresCapability("world")]
public sealed class RekallAgeBuiltInModule : RekallAgeModule
{
    public override void Configure(RekallAgeModuleBuilder builder)
    {
        builder.RegisterComponent<RekallAgeTransformComponent>();
        builder.RegisterComponent<RekallAgeInputActionMapComponent>();
        builder.RegisterComponent<RekallAgeCamera2DComponent>();
        builder.RegisterComponent<RekallAgeCamera3DComponent>();
        builder.RegisterComponent<RekallAgeCameraZoomInputComponent>();
        builder.RegisterComponent<RekallAgeCameraTarget3DComponent>();
        builder.RegisterComponent<RekallAgeRenderLayerComponent>();
        builder.RegisterComponent<RekallAgeXrRigComponent>();
        builder.RegisterComponent<RekallAgeXrPoseSourceComponent>();
        builder.RegisterComponent<RekallAgeXrControllerComponent>();
        builder.RegisterComponent<RekallAgeDirectionalLightComponent>();
        builder.RegisterComponent<RekallAgePointLightComponent>();
        builder.RegisterComponent<RekallAgeMultiplayerSessionComponent>();
        builder.RegisterComponent<RekallAgeNetworkIdentityComponent>();
        builder.RegisterComponent<RekallAgeNetworkTransformComponent>();
        builder.RegisterComponent<RekallAgeGeometryPrimitiveComponent>();
        builder.RegisterComponent<RekallAgeGeometryMeshComponent>();
        builder.RegisterComponent<RekallAgeLineSegmentsComponent>();
        builder.RegisterComponent<RekallAgeGeometryExtrusionComponent>();
        builder.RegisterComponent<RekallAgeMaterialComponent>();
        builder.RegisterComponent<RekallAgeLodGroupComponent>();
        builder.RegisterComponent<RekallAgePhysicsWorld3DComponent>();
        builder.RegisterComponent<RekallAgePhysicsMaterial3DComponent>();
        builder.RegisterComponent<RekallAgeRigidbody3DComponent>();
        builder.RegisterComponent<RekallAgeBoxCollider2DComponent>();
        builder.RegisterComponent<RekallAgeCircleCollider2DComponent>();
        builder.RegisterComponent<RekallAgeBoxCollider3DComponent>();
        builder.RegisterComponent<RekallAgeSphereCollider3DComponent>();
        builder.RegisterComponent<RekallAgeCapsuleCollider3DComponent>();
        builder.RegisterComponent<RekallAgeMeshColliderComponent>();
        builder.RegisterComponent<RekallAgePlanetRendererComponent>();
        builder.RegisterComponent<RekallAgeAtmosphereRendererComponent>();
        builder.RegisterComponent<RekallAgeCelestialBodyComponent>();
        builder.RegisterComponent<RekallAgeKeplerOrbitComponent>();
        builder.RegisterComponent<RekallAgeCelestialRotationComponent>();
        builder.RegisterComponent<RekallAgeOrbitPathRendererComponent>();
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

[RekallAgeComponent("Input Action Map")]
public sealed class RekallAgeInputActionMapComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public bool Active { get; init; } = true;

    [RekallAgeProperty(Kind = "inputActions")]
    public RekallAgeInputActionBinding[] Actions { get; init; } =
    [
        new("primary", Key: "Space")
    ];
}

public sealed record RekallAgeInputActionBinding(
    string Name,
    string? Key = null,
    string? Button = null,
    string? PositiveKey = null,
    string? NegativeKey = null,
    string? PositiveButton = null,
    string? NegativeButton = null,
    double MouseWheelScale = 0);

[RekallAgeComponent("Camera 2D")]
public sealed class RekallAgeCamera2DComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public bool Active { get; init; } = true;

    [RekallAgeProperty]
    public string ClearColor { get; init; } = "#102030";

    [RekallAgeProperty]
    public string CullingMask { get; init; } = "*";

    [RekallAgeProperty]
    public double RenderOrder { get; init; }

    [RekallAgeProperty(Minimum = 0, Maximum = 1)]
    public double ViewportX { get; init; }

    [RekallAgeProperty(Minimum = 0, Maximum = 1)]
    public double ViewportY { get; init; }

    [RekallAgeProperty(Minimum = 0.001, Maximum = 1)]
    public double ViewportWidth { get; init; } = 1;

    [RekallAgeProperty(Minimum = 0.001, Maximum = 1)]
    public double ViewportHeight { get; init; } = 1;

    [RekallAgeProperty(Minimum = 0.001)]
    public double OrthographicSize { get; init; } = 10;

    [RekallAgeProperty]
    public double NearClip { get; init; } = -1000;

    [RekallAgeProperty]
    public double FarClip { get; init; } = 1000;
}

[RekallAgeComponent("Camera 3D")]
public sealed class RekallAgeCamera3DComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public string ProjectionMode { get; init; } = "perspective";

    [RekallAgeProperty(Minimum = 1, Maximum = 179)]
    public double FieldOfView { get; init; } = 65;

    [RekallAgeProperty(Minimum = 0.001)]
    public double OrthographicSize { get; init; } = 10;

    [RekallAgeProperty(Minimum = 0.001)]
    public double NearClip { get; init; } = 0.05;

    [RekallAgeProperty(Minimum = 0.001)]
    public double FarClip { get; init; } = 1000;

    [RekallAgeProperty]
    public string ClearColor { get; init; } = "#101820";

    [RekallAgeProperty]
    public string CullingMask { get; init; } = "*";

    [RekallAgeProperty]
    public bool Active { get; init; } = true;

    [RekallAgeProperty]
    public double RenderOrder { get; init; }

    [RekallAgeProperty(Minimum = 0, Maximum = 1)]
    public double ViewportX { get; init; }

    [RekallAgeProperty(Minimum = 0, Maximum = 1)]
    public double ViewportY { get; init; }

    [RekallAgeProperty(Minimum = 0.001, Maximum = 1)]
    public double ViewportWidth { get; init; } = 1;

    [RekallAgeProperty(Minimum = 0.001, Maximum = 1)]
    public double ViewportHeight { get; init; } = 1;

    [RekallAgeProperty]
    public string StereoMode { get; init; } = "mono";

    [RekallAgeProperty]
    public string StereoRenderMode { get; init; } = "single-pass-multiview";

    [RekallAgeProperty(Minimum = 0)]
    public double InterpupillaryDistance { get; init; } = 0.064;

    [RekallAgeProperty(Minimum = 0.001)]
    public double StereoConvergenceDistance { get; init; } = 10;

    [RekallAgeProperty]
    public string XrViewConfiguration { get; init; } = "primary-stereo";

    [RekallAgeProperty]
    public bool FoveatedRendering { get; init; }
}

[RekallAgeComponent("Camera Zoom Input")]
public sealed class RekallAgeCameraZoomInputComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public bool Active { get; init; } = true;

    [RekallAgeProperty(Minimum = 0.0001)]
    public double WheelZoomSpeed { get; init; } = 0.12;

    [RekallAgeProperty(Minimum = 0.001)]
    public double MinimumOrthographicSize { get; init; } = 0.1;

    [RekallAgeProperty(Minimum = 0.001)]
    public double MaximumOrthographicSize { get; init; } = 100000;

    [RekallAgeProperty(Minimum = 1, Maximum = 179)]
    public double MinimumFieldOfView { get; init; } = 15;

    [RekallAgeProperty(Minimum = 1, Maximum = 179)]
    public double MaximumFieldOfView { get; init; } = 120;

    [RekallAgeProperty]
    public bool InvertWheel { get; init; }
}

[RekallAgeComponent("Camera Target 3D")]
public sealed class RekallAgeCameraTarget3DComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public string TargetEntityId { get; init; } = string.Empty;

    [RekallAgeProperty]
    public string TargetName { get; init; } = string.Empty;

    [RekallAgeProperty]
    public string TargetTag { get; init; } = string.Empty;

    [RekallAgeProperty]
    public double OffsetX { get; init; }

    [RekallAgeProperty]
    public double OffsetY { get; init; } = 2;

    [RekallAgeProperty]
    public double OffsetZ { get; init; } = 6;

    [RekallAgeProperty]
    public double TargetOffsetX { get; init; }

    [RekallAgeProperty]
    public double TargetOffsetY { get; init; }

    [RekallAgeProperty]
    public double TargetOffsetZ { get; init; }

    [RekallAgeProperty]
    public bool FollowPosition { get; init; } = true;

    [RekallAgeProperty]
    public bool LookAt { get; init; } = true;

    [RekallAgeProperty]
    public bool Active { get; init; } = true;
}

[RekallAgeComponent("Render Layer")]
public sealed class RekallAgeRenderLayerComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public string Layer { get; init; } = "default";
}

[RekallAgeComponent("XR Rig")]
public sealed class RekallAgeXrRigComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public bool Active { get; init; } = true;

    [RekallAgeProperty]
    public string TrackingSpace { get; init; } = "local-floor";

    [RekallAgeProperty]
    public string ViewConfiguration { get; init; } = "primary-stereo";
}

[RekallAgeComponent("XR Pose Source")]
public sealed class RekallAgeXrPoseSourceComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public bool Active { get; init; } = true;

    [RekallAgeProperty]
    public string Source { get; init; } = "head";

    [RekallAgeProperty]
    public bool ApplyPosition { get; init; } = true;

    [RekallAgeProperty]
    public bool ApplyRotation { get; init; } = true;
}

[RekallAgeComponent("XR Controller")]
public sealed class RekallAgeXrControllerComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public bool Active { get; init; } = true;

    [RekallAgeProperty]
    public string Hand { get; init; } = "left";

    [RekallAgeProperty]
    public string PoseSource { get; init; } = "left-hand";
}

[RekallAgeComponent("Directional Light")]
public sealed class RekallAgeDirectionalLightComponent : RekallAgeComponent
{
    [RekallAgeProperty(Minimum = 0)]
    public double Intensity { get; init; } = 1;

    [RekallAgeProperty(Kind = "color")]
    public string Color { get; init; } = "#ffffff";
}

[RekallAgeComponent("Point Light")]
public sealed class RekallAgePointLightComponent : RekallAgeComponent
{
    [RekallAgeProperty(Minimum = 0)]
    public double Intensity { get; init; } = 1;

    [RekallAgeProperty(Kind = "color")]
    public string Color { get; init; } = "#ffffff";
}

[RekallAgeComponent("Multiplayer Session")]
public sealed class RekallAgeMultiplayerSessionComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public string Role { get; init; } = "server";

    [RekallAgeProperty]
    public string Authority { get; init; } = "server";

    [RekallAgeProperty(Minimum = 1, Maximum = 240)]
    public int TickRate { get; init; } = 60;

    [RekallAgeProperty(Minimum = 1, Maximum = 240)]
    public int SnapshotRate { get; init; } = 20;

    [RekallAgeProperty(Minimum = 1)]
    public int MaxPlayers { get; init; } = 8;

    [RekallAgeProperty]
    public string Transport { get; init; } = "loopback";

    [RekallAgeProperty]
    public string Address { get; init; } = "127.0.0.1";

    [RekallAgeProperty(Minimum = 1, Maximum = 65535)]
    public int Port { get; init; } = 7777;

    [RekallAgeProperty]
    public bool ClientPrediction { get; init; } = true;

    [RekallAgeProperty(Minimum = 0)]
    public int InterpolationDelayMilliseconds { get; init; } = 100;
}

[RekallAgeComponent("Network Identity")]
public sealed class RekallAgeNetworkIdentityComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public string NetworkId { get; init; } = string.Empty;

    [RekallAgeProperty]
    public string OwnerClientId { get; init; } = string.Empty;

    [RekallAgeProperty]
    public string Authority { get; init; } = "server";
}

[RekallAgeComponent("Network Transform")]
public sealed class RekallAgeNetworkTransformComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public bool ReplicatePosition { get; init; } = true;

    [RekallAgeProperty]
    public bool ReplicateRotation { get; init; } = true;

    [RekallAgeProperty]
    public bool ReplicateScale { get; init; } = true;

    [RekallAgeProperty]
    public string Prediction { get; init; } = "interpolated";

    [RekallAgeProperty(Minimum = 0)]
    public int Priority { get; init; } = 0;
}

[RekallAgeComponent("Geometry Primitive")]
public sealed class RekallAgeGeometryPrimitiveComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public string Primitive { get; init; } = "cube";

    [RekallAgeProperty(Kind = "color")]
    public string Color { get; init; } = "#8ab4f8";
}

[RekallAgeComponent("Geometry Mesh")]
public sealed class RekallAgeGeometryMeshComponent : RekallAgeComponent
{
    [RekallAgeProperty(Kind = "geometryVertices")]
    public RekallAgeGeometryMeshVertex[] Vertices { get; init; } =
    [
        new(0, 0, 0),
        new(1, 0, 0),
        new(0, 1, 0)
    ];

    [RekallAgeProperty(Kind = "geometryIndices")]
    public ushort[] Indices { get; init; } = [0, 1, 2];

    [RekallAgeProperty(Kind = "color")]
    public string Color { get; init; } = "#8ab4f8";
}

public sealed record RekallAgeGeometryMeshVertex(
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

[RekallAgeComponent("Line Segments")]
public sealed class RekallAgeLineSegmentsComponent : RekallAgeComponent
{
    [RekallAgeProperty(Kind = "lineSegments")]
    public RekallAgeLineSegment[] Segments { get; init; } =
    [
        new(0, 0, 0, 1, 0, 0)
    ];

    [RekallAgeProperty(Minimum = 0.0001)]
    public double Thickness { get; init; } = 0.02;

    [RekallAgeProperty(Kind = "color")]
    public string Color { get; init; } = "#33ddff";
}

public sealed record RekallAgeLineSegment(
    double FromX,
    double FromY,
    double FromZ,
    double ToX,
    double ToY,
    double ToZ);

[RekallAgeComponent("Geometry Extrusion")]
public sealed class RekallAgeGeometryExtrusionComponent : RekallAgeComponent
{
    [RekallAgeProperty(Kind = "geometryProfile")]
    public RekallAgeGeometryProfilePoint[] Profile { get; init; } =
    [
        new(-0.5, -0.5),
        new(0.5, -0.5),
        new(0.5, 0.5),
        new(-0.5, 0.5)
    ];

    [RekallAgeProperty(Minimum = 0.001)]
    public double Depth { get; init; } = 1;

    [RekallAgeProperty(Kind = "color")]
    public string Color { get; init; } = "#8ab4f8";
}

public sealed record RekallAgeGeometryProfilePoint(double X, double Y);

[RekallAgeComponent("Material")]
public sealed class RekallAgeMaterialComponent : RekallAgeComponent
{
    [RekallAgeProperty(Kind = "color")]
    public string BaseColor { get; init; } = "#ffffff";

    [RekallAgeProperty(Kind = "assetRef", AssetKind = "texture")]
    public string? BaseColorTexture { get; init; }

    [RekallAgeProperty(Minimum = 0, Maximum = 1)]
    public double MetallicFactor { get; init; }

    [RekallAgeProperty(Minimum = 0.04, Maximum = 1)]
    public double RoughnessFactor { get; init; } = 1;

    [RekallAgeProperty(Kind = "assetRef", AssetKind = "texture")]
    public string? MetallicRoughnessTexture { get; init; }

    [RekallAgeProperty(Kind = "assetRef", AssetKind = "texture")]
    public string? NormalTexture { get; init; }

    [RekallAgeProperty(Minimum = 0, Maximum = 4)]
    public double NormalScale { get; init; } = 1;

    [RekallAgeProperty(Kind = "assetRef", AssetKind = "texture")]
    public string? OcclusionTexture { get; init; }

    [RekallAgeProperty(Minimum = 0, Maximum = 1)]
    public double OcclusionStrength { get; init; } = 1;

    [RekallAgeProperty(Kind = "color")]
    public string EmissiveColor { get; init; } = "#000000";

    [RekallAgeProperty(Kind = "assetRef", AssetKind = "texture")]
    public string? EmissiveTexture { get; init; }

    [RekallAgeProperty(Minimum = 0)]
    public double EmissiveStrength { get; init; }
}

[RekallAgeComponent("LOD Group")]
public sealed class RekallAgeLodGroupComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public bool Active { get; init; } = true;

    [RekallAgeProperty(Kind = "lodLevels")]
    public RekallAgeLodLevel[] Levels { get; init; } =
    [
        new(0, 50, Primitive: "cube"),
        new(50, null, Primitive: "plane")
    ];
}

public sealed record RekallAgeLodLevel(
    double MinDistance = 0,
    double? MaxDistance = null,
    string? Mesh = null,
    string? AssetId = null,
    string? Primitive = null,
    string? TextureAssetId = null,
    string? MaterialColor = null,
    double ScaleMultiplier = 1);

[RekallAgeComponent("Physics World 3D")]
public sealed class RekallAgePhysicsWorld3DComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public double GravityX { get; init; }

    [RekallAgeProperty]
    public double GravityY { get; init; } = -9.81;

    [RekallAgeProperty]
    public double GravityZ { get; init; }
}

[RekallAgeComponent("Physics Material 3D")]
public sealed class RekallAgePhysicsMaterial3DComponent : RekallAgeComponent
{
    [RekallAgeProperty(Minimum = 0)]
    public double Friction { get; init; } = 1;

    [RekallAgeProperty(Minimum = 0, Maximum = 1)]
    public double Restitution { get; init; }

    [RekallAgeProperty(Minimum = 0)]
    public double MinimumBounceSpeed { get; init; } = 0.5;

    [RekallAgeProperty(Minimum = 0)]
    public double MaximumRecoveryVelocity { get; init; } = 2;

    [RekallAgeProperty(Minimum = 0.0001)]
    public double SpringFrequency { get; init; } = 30;

    [RekallAgeProperty(Minimum = 0)]
    public double DampingRatio { get; init; } = 1;
}

[RekallAgeComponent("Rigidbody 3D")]
public sealed class RekallAgeRigidbody3DComponent : RekallAgeComponent
{
    [RekallAgeProperty(Minimum = 0.0001)]
    public double Mass { get; init; } = 1;
}

[RekallAgeComponent("Box Collider 2D")]
public sealed class RekallAgeBoxCollider2DComponent : RekallAgeComponent
{
    [RekallAgeProperty(Minimum = 0.0001)]
    public double Width { get; init; } = 1;

    [RekallAgeProperty(Minimum = 0.0001)]
    public double Height { get; init; } = 1;
}

[RekallAgeComponent("Circle Collider 2D")]
public sealed class RekallAgeCircleCollider2DComponent : RekallAgeComponent
{
    [RekallAgeProperty(Minimum = 0.0001)]
    public double Radius { get; init; } = 0.5;
}

[RekallAgeComponent("Box Collider 3D")]
public sealed class RekallAgeBoxCollider3DComponent : RekallAgeComponent
{
    [RekallAgeProperty(Minimum = 0.0001)]
    public double Width { get; init; } = 1;

    [RekallAgeProperty(Minimum = 0.0001)]
    public double Height { get; init; } = 1;

    [RekallAgeProperty(Minimum = 0.0001)]
    public double Depth { get; init; } = 1;
}

[RekallAgeComponent("Sphere Collider 3D")]
public sealed class RekallAgeSphereCollider3DComponent : RekallAgeComponent
{
    [RekallAgeProperty(Minimum = 0.0001)]
    public double Radius { get; init; } = 0.5;
}

[RekallAgeComponent("Capsule Collider 3D")]
public sealed class RekallAgeCapsuleCollider3DComponent : RekallAgeComponent
{
    [RekallAgeProperty(Minimum = 0.0001)]
    public double Radius { get; init; } = 0.5;

    [RekallAgeProperty(Minimum = 0.0001)]
    public double Length { get; init; } = 1;
}

[RekallAgeComponent("Mesh Collider")]
public sealed class RekallAgeMeshColliderComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public bool Convex { get; init; }
}

[RekallAgeComponent("Planet Renderer")]
public sealed class RekallAgePlanetRendererComponent : RekallAgeComponent
{
    [RekallAgeProperty(Minimum = 0.0001)]
    public double Radius { get; init; } = 1;

    [RekallAgeProperty(Kind = "assetRef", AssetKind = "texture")]
    public string? SurfaceTexture { get; init; }

    [RekallAgeProperty(Kind = "assetRef", AssetKind = "texture")]
    public string? HeightTexture { get; init; }

    [RekallAgeProperty(Kind = "assetRef", AssetKind = "texture")]
    public string? NormalTexture { get; init; }

    [RekallAgeProperty(Kind = "color")]
    public string Color { get; init; } = "#4b86d8";
}

[RekallAgeComponent("Atmosphere Renderer")]
public sealed class RekallAgeAtmosphereRendererComponent : RekallAgeComponent
{
    [RekallAgeProperty(Minimum = 0)]
    public double Height { get; init; } = 0.08;

    [RekallAgeProperty(Kind = "color")]
    public string RayleighColor { get; init; } = "#7fb6ff";

    [RekallAgeProperty(Minimum = 0)]
    public double Density { get; init; } = 1;
}

[RekallAgeComponent("Celestial Body")]
public sealed class RekallAgeCelestialBodyComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public string BodyId { get; init; } = string.Empty;

    [RekallAgeProperty]
    public string Type { get; init; } = "PlanetaryBody";

    [RekallAgeProperty]
    public string? ParentBodyId { get; init; }

    [RekallAgeProperty(Minimum = 0)]
    public double MeanRadiusKm { get; init; }

    [RekallAgeProperty(Minimum = 0)]
    public double MassKg { get; init; }

    [RekallAgeProperty(Kind = "color")]
    public string Color { get; init; } = "#8f98a8";
}

[RekallAgeComponent("Kepler Orbit")]
public sealed class RekallAgeKeplerOrbitComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public string ParentBodyId { get; init; } = string.Empty;

    [RekallAgeProperty(Minimum = 0)]
    public double SemiMajorAxisKm { get; init; }

    [RekallAgeProperty(Minimum = 0, Maximum = 0.999999)]
    public double Eccentricity { get; init; }

    [RekallAgeProperty]
    public double InclinationDegrees { get; init; }

    [RekallAgeProperty]
    public double LongitudeOfAscendingNodeDegrees { get; init; }

    [RekallAgeProperty]
    public double ArgumentOfPeriapsisDegrees { get; init; }

    [RekallAgeProperty]
    public double TimeAtPeriapsisSeconds { get; init; }

    [RekallAgeProperty(Minimum = 0)]
    public double PeriodSeconds { get; init; }

    [RekallAgeProperty(Minimum = 0)]
    public double DistanceScale { get; init; } = 1;

    [RekallAgeProperty(Minimum = 0)]
    public double TimeScale { get; init; } = 1;
}

[RekallAgeComponent("Celestial Rotation")]
public sealed class RekallAgeCelestialRotationComponent : RekallAgeComponent
{
    [RekallAgeProperty(Minimum = 0)]
    public double SiderealPeriodSeconds { get; init; }

    [RekallAgeProperty]
    public bool TidallyLocked { get; init; }

    [RekallAgeProperty]
    public double TiltDegrees { get; init; }

    [RekallAgeProperty]
    public double AzimuthDegrees { get; init; }

    [RekallAgeProperty]
    public double InitialLongitudeDegrees { get; init; }

    [RekallAgeProperty]
    public bool Retrograde { get; init; }

    [RekallAgeProperty(Minimum = 0)]
    public double TimeScale { get; init; } = 1;

    [RekallAgeProperty]
    public bool Active { get; init; } = true;
}

[RekallAgeComponent("Orbit Path Renderer")]
public sealed class RekallAgeOrbitPathRendererComponent : RekallAgeComponent
{
    [RekallAgeProperty(Minimum = 8, Maximum = 512)]
    public int Segments { get; init; } = 128;

    [RekallAgeProperty(Minimum = 0.001)]
    public double Thickness { get; init; } = 0.035;

    [RekallAgeProperty]
    public double VerticalOffset { get; init; } = -0.05;

    [RekallAgeProperty(Kind = "color")]
    public string Color { get; init; } = "#88aaff";

    [RekallAgeProperty(Minimum = 0)]
    public double EmissiveStrength { get; init; } = 1.4;

    [RekallAgeProperty]
    public bool Active { get; init; } = true;
}
