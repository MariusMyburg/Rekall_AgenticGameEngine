namespace Rekall.Age.Modules.BuiltIns;

[RekallAgeModule("rekall.builtins", "Rekall Built-ins")]
[RekallAgeRequiresCapability("world")]
public sealed class RekallAgeBuiltInModule : RekallAgeModule
{
    public override void Configure(RekallAgeModuleBuilder builder)
    {
        builder.RegisterComponent<RekallAgeTransformComponent>();
        builder.RegisterComponent<RekallAgeInputActionMapComponent>();
        builder.RegisterComponent<RekallAgeEventBindingsComponent>();
        builder.RegisterComponent<RekallAgePointerRayComponent>();
        builder.RegisterComponent<RekallAgeTimerComponent>();
        builder.RegisterComponent<RekallAgeCamera2DComponent>();
        builder.RegisterComponent<RekallAgeCamera3DComponent>();
        builder.RegisterComponent<RekallAgeCameraZoomInputComponent>();
        builder.RegisterComponent<RekallAgeCameraTarget3DComponent>();
        builder.RegisterComponent<RekallAgeCameraTargetCycleInputComponent>();
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
        builder.RegisterComponent<RekallAgeProceduralMaterialComponent>();
        builder.RegisterComponent<RekallAgeLodGroupComponent>();
        builder.RegisterComponent<RekallAgeVirtualGeometryComponent>();
        builder.RegisterComponent<RekallAgePhysicsWorld3DComponent>();
        builder.RegisterComponent<RekallAgePhysicsMaterial3DComponent>();
        builder.RegisterComponent<RekallAgeRigidbody3DComponent>();
        builder.RegisterComponent<RekallAgeTriggerComponent>();
        builder.RegisterComponent<RekallAgeBoxCollider2DComponent>();
        builder.RegisterComponent<RekallAgeCircleCollider2DComponent>();
        builder.RegisterComponent<RekallAgeBoxCollider3DComponent>();
        builder.RegisterComponent<RekallAgeSphereCollider3DComponent>();
        builder.RegisterComponent<RekallAgeCapsuleCollider3DComponent>();
        builder.RegisterComponent<RekallAgeMeshColliderComponent>();
        builder.RegisterComponent<RekallAgePlanetRendererComponent>();
        builder.RegisterComponent<RekallAgeCloudLayerRendererComponent>();
        builder.RegisterComponent<RekallAgeAtmosphereRendererComponent>();
        builder.RegisterComponent<RekallAgeCelestialBodyComponent>();
        builder.RegisterComponent<RekallAgeKeplerOrbitComponent>();
        builder.RegisterComponent<RekallAgeCelestialRotationComponent>();
        builder.RegisterComponent<RekallAgeOrbitPathRendererComponent>();
        builder.RegisterComponent<RekallAgeRingRendererComponent>();
        builder.RegisterComponent<RekallAgeStarfieldRendererComponent>();
        builder.RegisterComponent<RekallAgeMarkerRendererComponent>();
        builder.RegisterComponent<RekallAgeHaloRendererComponent>();
        builder.RegisterComponent<RekallAgePostProcessStackComponent>();
        builder.RegisterComponent<RekallAgeTextLabelRendererComponent>();
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
    double MouseWheelScale = 0,
    string? MouseAxis = null,
    double MouseScale = 1);

[RekallAgeComponent("Event Bindings")]
public sealed class RekallAgeEventBindingsComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public bool Active { get; init; } = true;

    [RekallAgeProperty(Kind = "runtimeEvents")]
    public RekallAgeEventBinding[] Events { get; init; } =
    [
        new("entity.tick")
    ];
}

public sealed record RekallAgeEventBinding(
    string Event,
    string? Handler = null,
    bool Active = true);

[RekallAgeComponent("Pointer Ray")]
public sealed class RekallAgePointerRayComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public bool Active { get; init; } = true;

    [RekallAgeProperty]
    public string PointerId { get; init; } = "primary";

    [RekallAgeProperty]
    public double OriginX { get; init; }

    [RekallAgeProperty]
    public double OriginY { get; init; }

    [RekallAgeProperty]
    public double OriginZ { get; init; }

    [RekallAgeProperty]
    public double DirectionX { get; init; }

    [RekallAgeProperty]
    public double DirectionY { get; init; }

    [RekallAgeProperty]
    public double DirectionZ { get; init; } = 1;

    [RekallAgeProperty(Minimum = 0)]
    public double Range { get; init; } = 100;

    [RekallAgeProperty]
    public string Button { get; init; } = "Left";

    [RekallAgeProperty]
    public string TargetTag { get; init; } = string.Empty;

    [RekallAgeProperty]
    public string TargetComponentType { get; init; } = string.Empty;
}

[RekallAgeComponent("Timer")]
public sealed class RekallAgeTimerComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public bool Active { get; init; } = true;

    [RekallAgeProperty]
    public string TimerId { get; init; } = "timer";

    [RekallAgeProperty(Minimum = 0.000001)]
    public double DurationSeconds { get; init; } = 1;

    [RekallAgeProperty]
    public bool Repeat { get; init; }
}

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
    public string OffsetReferenceEntityId { get; init; } = string.Empty;

    [RekallAgeProperty]
    public string OffsetReferenceName { get; init; } = string.Empty;

    [RekallAgeProperty]
    public string OffsetReferenceTag { get; init; } = string.Empty;

    [RekallAgeProperty]
    public string OffsetReferenceMode { get; init; } = "toward";

    [RekallAgeProperty(Minimum = 0)]
    public double OffsetDistance { get; init; }

    [RekallAgeProperty]
    public double OffsetVertical { get; init; }

    [RekallAgeProperty]
    public double OffsetLateral { get; init; }

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

[RekallAgeComponent("Camera Target Cycle Input")]
public sealed class RekallAgeCameraTargetCycleInputComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public bool Active { get; init; } = true;

    [RekallAgeProperty]
    public string NextAction { get; init; } = "nextTarget";

    [RekallAgeProperty]
    public string PreviousAction { get; init; } = "previousTarget";

    [RekallAgeProperty(Minimum = 0)]
    public int CurrentIndex { get; init; }

    [RekallAgeProperty]
    public object[] Targets { get; init; } = [];
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

[RekallAgeComponent("Procedural Material")]
public sealed class RekallAgeProceduralMaterialComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public string Generator { get; init; } = "checker";

    [RekallAgeProperty(Minimum = 2, Maximum = 2048)]
    public int Resolution { get; init; } = 128;

    [RekallAgeProperty(Minimum = 0.0001)]
    public double Scale { get; init; } = 8;

    [RekallAgeProperty]
    public int Seed { get; init; }

    [RekallAgeProperty(Kind = "color")]
    public string BaseColorA { get; init; } = "#ffffff";

    [RekallAgeProperty(Kind = "color")]
    public string BaseColorB { get; init; } = "#202020";

    [RekallAgeProperty(Minimum = 0, Maximum = 1)]
    public double MetallicFactor { get; init; }

    [RekallAgeProperty(Minimum = 0.04, Maximum = 1)]
    public double RoughnessA { get; init; } = 1;

    [RekallAgeProperty(Minimum = 0.04, Maximum = 1)]
    public double RoughnessB { get; init; } = 1;

    [RekallAgeProperty(Minimum = 0, Maximum = 4)]
    public double NormalStrength { get; init; }

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

[RekallAgeComponent("Virtual Geometry")]
public sealed class RekallAgeVirtualGeometryComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public bool Enabled { get; init; } = true;

    [RekallAgeProperty(Minimum = 0.001)]
    public double TargetPixelError { get; init; } = 1;

    [RekallAgeProperty(Minimum = 1)]
    public int ClusterTriangleCount { get; init; } = 128;

    [RekallAgeProperty(Minimum = 0)]
    public int MaxSelectedTriangles { get; init; }

    [RekallAgeProperty(Minimum = 0)]
    public int MaxLodLevel { get; init; } = 8;

    [RekallAgeProperty]
    public string DebugMode { get; init; } = "off";
}

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

[RekallAgeComponent("Trigger")]
public sealed class RekallAgeTriggerComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public bool Active { get; init; } = true;

    [RekallAgeProperty]
    public string Shape { get; init; } = "sphere";

    [RekallAgeProperty(Minimum = 0.0001)]
    public double Radius { get; init; } = 1;

    [RekallAgeProperty(Minimum = 0.0001)]
    public double Width { get; init; } = 1;

    [RekallAgeProperty(Minimum = 0.0001)]
    public double Height { get; init; } = 1;

    [RekallAgeProperty(Minimum = 0.0001)]
    public double Depth { get; init; } = 1;

    [RekallAgeProperty]
    public string TargetTag { get; init; } = string.Empty;

    [RekallAgeProperty]
    public string TargetComponentType { get; init; } = string.Empty;
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

    [RekallAgeProperty(Kind = "assetRef", AssetKind = "texture")]
    public string? WaterTexture { get; init; }

    [RekallAgeProperty(Minimum = 0, Maximum = 4)]
    public double WaterCoverage { get; init; } = 1;

    [RekallAgeProperty(Minimum = 0, Maximum = 8)]
    public double WaterSpecularStrength { get; init; } = 2.5;

    [RekallAgeProperty(Minimum = 0.01, Maximum = 1)]
    public double WaterRoughness { get; init; } = 0.06;

    [RekallAgeProperty(Minimum = 0, Maximum = 512)]
    public int MeshSlices { get; init; }

    [RekallAgeProperty(Minimum = 0, Maximum = 256)]
    public int MeshStacks { get; init; }

    [RekallAgeProperty(Kind = "color")]
    public string Color { get; init; } = "#4b86d8";
}

[RekallAgeComponent("Cloud Layer Renderer")]
public sealed class RekallAgeCloudLayerRendererComponent : RekallAgeComponent
{
    [RekallAgeProperty(Kind = "cloudLayers")]
    public object? Layers { get; init; }

    [RekallAgeProperty(Minimum = 0)]
    public double Height { get; init; } = 0.02;

    [RekallAgeProperty(Kind = "assetRef", AssetKind = "texture")]
    public string? Texture { get; init; }

    [RekallAgeProperty(Kind = "color")]
    public string Color { get; init; } = "#ffffff";

    [RekallAgeProperty(Kind = "boolean")]
    public bool AlphaFromTextureOnly { get; init; } = true;

    [RekallAgeProperty(Minimum = 0, Maximum = 4)]
    public double Coverage { get; init; } = 1;

    [RekallAgeProperty(Minimum = 0, Maximum = 1)]
    public double LambertianStrength { get; init; } = 0.45;

    [RekallAgeProperty(Minimum = 0, Maximum = 2)]
    public double AmbientStrength { get; init; } = 0.18;

    [RekallAgeProperty(Kind = "boolean")]
    public bool CastShadows { get; init; } = true;

    [RekallAgeProperty(Minimum = 0, Maximum = 1)]
    public double ShadowStrength { get; init; } = 0.35;
}

[RekallAgeComponent("Atmosphere Renderer")]
public sealed class RekallAgeAtmosphereRendererComponent : RekallAgeComponent
{
    [RekallAgeProperty(Minimum = 0)]
    public double Height { get; init; } = 0.08;

    [RekallAgeProperty(Kind = "boolean")]
    public bool RenderShell { get; init; } = true;

    [RekallAgeProperty(Kind = "color")]
    public string RayleighColor { get; init; } = "#7fb6ff";

    [RekallAgeProperty(Minimum = 0)]
    public double Density { get; init; } = 1;

    [RekallAgeProperty(Minimum = 0.001)]
    public double DensityFalloff { get; init; } = 0.18;

    [RekallAgeProperty(Minimum = 0)]
    public double RayleighScattering { get; init; } = 0.006;

    [RekallAgeProperty(Minimum = 0)]
    public double MieScattering { get; init; } = 0.002;

    [RekallAgeProperty(Minimum = -0.99, Maximum = 0.99)]
    public double MieAnisotropy { get; init; } = 0.76;

    [RekallAgeProperty(Kind = "color")]
    public string MieColor { get; init; } = "#ffffff";

    [RekallAgeProperty(Kind = "color")]
    public string OzoneAbsorptionColor { get; init; } = "#ffd199";

    [RekallAgeProperty(Minimum = 0)]
    public double OzoneAbsorption { get; init; } = 0;

    [RekallAgeProperty(Minimum = 0, Maximum = 2)]
    public double AerialPerspectiveStrength { get; init; } = 0.38;

    [RekallAgeProperty(Minimum = 0)]
    public double SunIntensity { get; init; } = 22;

    [RekallAgeProperty(Minimum = 0)]
    public double Exposure { get; init; } = 1.2;

    [RekallAgeProperty(Minimum = 4, Maximum = 32)]
    public int ViewSampleCount { get; init; } = 16;

    [RekallAgeProperty(Minimum = 2, Maximum = 16)]
    public int LightSampleCount { get; init; } = 8;
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
    public string Layer { get; init; } = string.Empty;

    [RekallAgeProperty]
    public bool Active { get; init; } = true;
}

[RekallAgeComponent("Ring Renderer")]
public sealed class RekallAgeRingRendererComponent : RekallAgeComponent
{
    [RekallAgeProperty(Minimum = 0.0001)]
    public double InnerRadius { get; init; } = 1;

    [RekallAgeProperty(Minimum = 0.0001)]
    public double OuterRadius { get; init; } = 2;

    [RekallAgeProperty(Kind = "assetRef", AssetKind = "texture")]
    public string? Texture { get; init; }

    [RekallAgeProperty(Kind = "color")]
    public string Color { get; init; } = "#ffffffcc";

    [RekallAgeProperty(Minimum = 16, Maximum = 512)]
    public int Segments { get; init; } = 192;
}

[RekallAgeComponent("Starfield Renderer")]
public sealed class RekallAgeStarfieldRendererComponent : RekallAgeComponent
{
    [RekallAgeProperty(Minimum = 1, Maximum = 8000)]
    public int Count { get; init; } = 1200;

    [RekallAgeProperty(Minimum = 1)]
    public double Radius { get; init; } = 18000;

    [RekallAgeProperty(Minimum = 0.0001)]
    public double Size { get; init; } = 2.5;

    [RekallAgeProperty]
    public int Seed { get; init; } = 1337;

    [RekallAgeProperty(Kind = "color")]
    public string Color { get; init; } = "#dce8ffff";

    [RekallAgeProperty(Minimum = 0, Maximum = 16)]
    public double Brightness { get; init; } = 2.2;

    [RekallAgeProperty(Minimum = 0, Maximum = 1)]
    public double MilkyWayStrength { get; init; } = 0.35;

    [RekallAgeProperty]
    public bool Active { get; init; } = true;
}

[RekallAgeComponent("Marker Renderer")]
public sealed class RekallAgeMarkerRendererComponent : RekallAgeComponent
{
    [RekallAgeProperty(Minimum = 0.0001)]
    public double Size { get; init; } = 1;

    [RekallAgeProperty(Kind = "color")]
    public string Color { get; init; } = "#ffffffcc";

    [RekallAgeProperty(Minimum = 0)]
    public double EmissiveStrength { get; init; } = 2;

    [RekallAgeProperty]
    public double VerticalOffset { get; init; }

    [RekallAgeProperty]
    public string Layer { get; init; } = string.Empty;

    [RekallAgeProperty]
    public bool Active { get; init; } = true;
}

[RekallAgeComponent("Halo Renderer")]
public sealed class RekallAgeHaloRendererComponent : RekallAgeComponent
{
    [RekallAgeProperty(Minimum = 0.0001)]
    public double Radius { get; init; } = 1;

    [RekallAgeProperty(Minimum = 8, Maximum = 256)]
    public int Segments { get; init; } = 48;

    [RekallAgeProperty(Minimum = 1, Maximum = 16)]
    public int Rings { get; init; } = 1;

    [RekallAgeProperty(Minimum = 0.1, Maximum = 8)]
    public double Falloff { get; init; } = 1;

    [RekallAgeProperty(Kind = "color")]
    public string Color { get; init; } = "#ffffff88";

    [RekallAgeProperty(Minimum = 0)]
    public double Intensity { get; init; } = 1;

    [RekallAgeProperty]
    public double VerticalOffset { get; init; }

    [RekallAgeProperty]
    public string FacingMode { get; init; } = "world";

    [RekallAgeProperty]
    public string Layer { get; init; } = string.Empty;

    [RekallAgeProperty]
    public bool Active { get; init; } = true;
}

[RekallAgeComponent("Post Process Stack")]
public sealed class RekallAgePostProcessStackComponent : RekallAgeComponent
{
    [RekallAgeProperty(Kind = "boolean")]
    public bool Enabled { get; init; } = true;

    [RekallAgeProperty(Kind = "postProcessPasses")]
    public object? Passes { get; init; }
}

[RekallAgeComponent("Text Label Renderer")]
public sealed class RekallAgeTextLabelRendererComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public string Text { get; init; } = "Label";

    [RekallAgeProperty(Minimum = 0.0001)]
    public double Size { get; init; } = 1;

    [RekallAgeProperty(Minimum = 0.0001)]
    public double Thickness { get; init; } = 0.02;

    [RekallAgeProperty(Minimum = 0)]
    public double MinimumScreenHeightPixels { get; init; }

    [RekallAgeProperty(Kind = "color")]
    public string Color { get; init; } = "#dce8ffff";

    [RekallAgeProperty]
    public double OffsetX { get; init; }

    [RekallAgeProperty]
    public double OffsetY { get; init; }

    [RekallAgeProperty]
    public double OffsetZ { get; init; }

    [RekallAgeProperty]
    public string FacingMode { get; init; } = "world";

    [RekallAgeProperty]
    public string Layer { get; init; } = string.Empty;

    [RekallAgeProperty]
    public bool Active { get; init; } = true;
}
