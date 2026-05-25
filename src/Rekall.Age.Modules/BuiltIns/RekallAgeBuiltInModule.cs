namespace Rekall.Age.Modules.BuiltIns;

[RekallAgeModule("rekall.builtins", "Rekall Built-ins")]
[RekallAgeRequiresCapability("world")]
public sealed class RekallAgeBuiltInModule : RekallAgeModule
{
    public override void Configure(RekallAgeModuleBuilder builder)
    {
        builder.RegisterComponent<RekallAgeTransformComponent>();
        builder.RegisterComponent<RekallAgeCamera2DComponent>();
        builder.RegisterComponent<RekallAgeCamera3DComponent>();
        builder.RegisterComponent<RekallAgeGeometryPrimitiveComponent>();
        builder.RegisterComponent<RekallAgeGeometryMeshComponent>();
        builder.RegisterComponent<RekallAgeGeometryExtrusionComponent>();
        builder.RegisterComponent<RekallAgePhysicsWorld3DComponent>();
        builder.RegisterComponent<RekallAgePhysicsMaterial3DComponent>();
        builder.RegisterComponent<RekallAgeRigidbody3DComponent>();
        builder.RegisterComponent<RekallAgeBoxCollider3DComponent>();
        builder.RegisterComponent<RekallAgeSphereCollider3DComponent>();
        builder.RegisterComponent<RekallAgeCapsuleCollider3DComponent>();
        builder.RegisterComponent<RekallAgeMeshColliderComponent>();
        builder.RegisterComponent<RekallAgePlanetRendererComponent>();
        builder.RegisterComponent<RekallAgeAtmosphereRendererComponent>();
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

[RekallAgeComponent("Camera 2D")]
public sealed class RekallAgeCamera2DComponent : RekallAgeComponent
{
    [RekallAgeProperty]
    public bool Active { get; init; } = true;

    [RekallAgeProperty]
    public string ClearColor { get; init; } = "#102030";

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
    public bool Active { get; init; } = true;
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
