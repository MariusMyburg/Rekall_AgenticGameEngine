using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuUtilities;
using BepuUtilities.Memory;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Runtime;

public sealed class RekallAgeBepuPhysicsSystem : IRekallAgeRuntimeWorldSystem
{
    private const float DefaultGravityY = -9.81f;

    public string Id => "runtime.physics.bepu";

    public int Priority => 0;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeWorldFrameContext context)
    {
        var dynamicBodies = world.Entities
            .Select(CreatePhysicsEntity)
            .Where(item => item.Rigidbody is not null && item.Collider is not null)
            .ToArray();
        var staticBodies = world.Entities
            .Select(CreatePhysicsEntity)
            .Where(item => item.Rigidbody is null && item.Collider is not null)
            .ToArray();
        if (dynamicBodies.Length == 0 && staticBodies.Length == 0)
        {
            return ValueTask.FromResult(world);
        }

        using var pool = new BufferPool();
        var gravity = ReadGravity(world);
        var simulation = Simulation.Create(
            pool,
            new RekallAgeBepuNarrowPhaseCallbacks(CombineMaterials(dynamicBodies.Concat(staticBodies).Select(item => item.Material))),
            new RekallAgeBepuPoseIntegratorCallbacks(gravity),
            new SolveDescription(velocityIterationCount: 8, substepCount: 1));
        try
        {
            foreach (var item in staticBodies)
            {
                var shape = CreateStaticShape(simulation, pool, item);
                if (!shape.Created)
                {
                    continue;
                }

                var description = new StaticDescription(
                    new RigidPose(ToVector3(item.Entity.Transform.Position3D)),
                    shape.Shape);
                simulation.Statics.Add(description);
            }

            var handles = new Dictionary<string, DynamicBodyState>(StringComparer.Ordinal);
            foreach (var item in dynamicBodies)
            {
                var pose = new RigidPose(ToVector3(item.Entity.Transform.Position3D));
                var velocity = new BodyVelocity(ReadVector3(FindComponent(item.Entity, "Rekall.PhysicsState3D"), "linearVelocity"));
                var mass = Math.Max(0.0001f, ReadSingle(item.Rigidbody!, "mass", 1));
                if (TryCreateDynamicDescription(simulation, pool, item, pose, velocity, mass, out var created))
                {
                    handles[item.Entity.Id] = created with
                    {
                        Entity = item,
                        InitialVelocity = velocity.Linear,
                        Handle = simulation.Bodies.Add(created.Description)
                    };
                }
            }

            simulation.Timestep((float)context.DeltaTime.TotalSeconds);

            var updated = world.Entities
                .Select(entity => handles.TryGetValue(entity.Id, out var body)
                    ? ApplyBodyState(
                        entity,
                        simulation.Bodies[body.Handle],
                        body.CenterOffset,
                        ApplyRestitution(body, simulation.Bodies[body.Handle], staticBodies))
                    : entity)
                .ToArray();

            return ValueTask.FromResult(world with { Entities = updated });
        }
        finally
        {
            simulation.Dispose();
        }
    }

    private static RekallAgeRuntimeEntity ApplyBodyState(
        RekallAgeRuntimeEntity entity,
        BodyReference body,
        Vector3 centerOffset,
        RestitutionAdjustment adjustment)
    {
        var pose = body.Pose;
        var velocity = body.Velocity;
        if (adjustment.Applied)
        {
            velocity.Linear = adjustment.LinearVelocity;
        }
        var position = adjustment.Applied
            ? adjustment.Position
            : pose.Position - centerOffset;
        return entity with
        {
            Transform = entity.Transform with
            {
                Position3D = new RekallAgeRuntimeVector3(position.X, position.Y, position.Z)
            },
            Components = UpsertPhysicsState(entity.Components, velocity.Linear)
        };
    }

    private static RestitutionAdjustment ApplyRestitution(
        DynamicBodyState dynamicBody,
        BodyReference body,
        IReadOnlyList<PhysicsEntity> staticBodies)
    {
        var entity = dynamicBody.Entity;
        if (entity is null)
        {
            return RestitutionAdjustment.None;
        }

        var material = entity.Material;
        if (material.Restitution <= 0 || dynamicBody.InitialVelocity.Y >= -material.MinimumBounceSpeed)
        {
            return RestitutionAdjustment.None;
        }

        var pose = body.Pose;
        var position = pose.Position - dynamicBody.CenterOffset;
        var extent = EstimateVerticalExtent(entity);
        if (extent <= 0)
        {
            return RestitutionAdjustment.None;
        }

        foreach (var support in staticBodies)
        {
            if (!TryGetSupportSurface(support, out var surface)
                || position.X < surface.MinX
                || position.X > surface.MaxX
                || position.Z < surface.MinZ
                || position.Z > surface.MaxZ)
            {
                continue;
            }

            var bottom = position.Y - extent;
            if (bottom > surface.TopY + 0.1f)
            {
                continue;
            }

            var combined = CombineMaterials([material, support.Material]);
            if (combined.Restitution <= 0)
            {
                return RestitutionAdjustment.None;
            }

            var velocity = body.Velocity.Linear;
            velocity.Y = Math.Max(Math.Abs(dynamicBody.InitialVelocity.Y) * combined.Restitution, combined.MinimumBounceSpeed);
            position.Y = surface.TopY + extent + 0.001f;
            return new RestitutionAdjustment(true, position, velocity);
        }

        return RestitutionAdjustment.None;
    }

    private static IReadOnlyList<RekallAgeRuntimeComponent> UpsertPhysicsState(
        IReadOnlyList<RekallAgeRuntimeComponent> components,
        Vector3 linearVelocity)
    {
        var state = new JsonObject
        {
            ["backend"] = "bepu",
            ["linearVelocity"] = new JsonObject
            {
                ["x"] = linearVelocity.X,
                ["y"] = linearVelocity.Y,
                ["z"] = linearVelocity.Z
            }
        };
        var replaced = false;
        var updated = components.Select(component =>
        {
            if (!component.Type.Equals("Rekall.PhysicsState3D", StringComparison.Ordinal))
            {
                return component;
            }

            replaced = true;
            return new RekallAgeRuntimeComponent(component.Type, state.DeepClone().AsObject());
        }).ToList();
        if (!replaced)
        {
            updated.Add(new RekallAgeRuntimeComponent("Rekall.PhysicsState3D", state));
        }

        return updated
            .OrderBy(component => component.Type, StringComparer.Ordinal)
            .ToArray();
    }

    private static PhysicsEntity CreatePhysicsEntity(RekallAgeRuntimeEntity entity)
    {
        return new PhysicsEntity(
            entity,
            FindComponent(entity, "Rekall.Rigidbody3D"),
            FindCollider(entity),
            FindComponent(entity, "Rekall.GeometryMesh"),
            ReadPhysicsMaterial(entity));
    }

    private static RekallAgeRuntimeComponent? FindCollider(RekallAgeRuntimeEntity entity)
    {
        return entity.Components.FirstOrDefault(component =>
            component.Type is
                "Rekall.BoxCollider3D" or
                "Rekall.SphereCollider3D" or
                "Rekall.CapsuleCollider3D" or
                "Rekall.MeshCollider");
    }

    private static bool TryCreateDynamicDescription(
        Simulation simulation,
        BufferPool pool,
        PhysicsEntity item,
        RigidPose pose,
        BodyVelocity velocity,
        float mass,
        out DynamicBodyState created)
    {
        var collider = item.Collider!;
        switch (collider.Type)
        {
            case "Rekall.BoxCollider3D":
                created = new DynamicBodyState(
                    default,
                    BodyDescription.CreateConvexDynamic(pose, velocity, mass, simulation.Shapes, CreateBox(collider)),
                    Vector3.Zero);
                return true;
            case "Rekall.SphereCollider3D":
                created = new DynamicBodyState(
                    default,
                    BodyDescription.CreateConvexDynamic(pose, velocity, mass, simulation.Shapes, CreateSphere(collider)),
                    Vector3.Zero);
                return true;
            case "Rekall.CapsuleCollider3D":
                created = new DynamicBodyState(
                    default,
                    BodyDescription.CreateConvexDynamic(pose, velocity, mass, simulation.Shapes, CreateCapsule(collider)),
                    Vector3.Zero);
                return true;
            case "Rekall.MeshCollider" when ReadBoolean(collider, "convex", false)
                && TryCreateConvexHull(pool, item.GeometryMesh, out var hull, out var center):
                pose.Position += center;
                created = new DynamicBodyState(
                    default,
                    BodyDescription.CreateConvexDynamic(pose, velocity, mass, simulation.Shapes, hull),
                    center);
                return true;
            default:
                created = default;
                return false;
        }
    }

    private static StaticShape CreateStaticShape(
        Simulation simulation,
        BufferPool pool,
        PhysicsEntity item)
    {
        return item.Collider!.Type switch
        {
            "Rekall.BoxCollider3D" => new StaticShape(true, simulation.Shapes.Add(CreateBox(item.Collider))),
            "Rekall.SphereCollider3D" => new StaticShape(true, simulation.Shapes.Add(CreateSphere(item.Collider))),
            "Rekall.CapsuleCollider3D" => new StaticShape(true, simulation.Shapes.Add(CreateCapsule(item.Collider))),
            "Rekall.MeshCollider" => TryCreateStaticMesh(pool, item.GeometryMesh, out var mesh)
                ? new StaticShape(true, simulation.Shapes.Add(mesh))
                : default,
            _ => default
        };
    }

    private static Box CreateBox(RekallAgeRuntimeComponent collider)
    {
        return new Box(
            Math.Max(0.0001f, ReadSingle(collider, "width", 1)),
            Math.Max(0.0001f, ReadSingle(collider, "height", 1)),
            Math.Max(0.0001f, ReadSingle(collider, "depth", 1)));
    }

    private static Sphere CreateSphere(RekallAgeRuntimeComponent collider)
    {
        return new Sphere(Math.Max(0.0001f, ReadSingle(collider, "radius", 0.5f)));
    }

    private static Capsule CreateCapsule(RekallAgeRuntimeComponent collider)
    {
        return new Capsule(
            Math.Max(0.0001f, ReadSingle(collider, "radius", 0.5f)),
            Math.Max(0.0001f, ReadSingle(collider, "length", 1)));
    }

    private static bool TryCreateStaticMesh(
        BufferPool pool,
        RekallAgeRuntimeComponent? geometryMesh,
        out Mesh mesh)
    {
        mesh = default;
        if (geometryMesh is null
            || !TryGetPropertyValue(geometryMesh.Properties, "vertices", out var verticesNode)
            || verticesNode is not JsonArray vertices
            || !TryGetPropertyValue(geometryMesh.Properties, "indices", out var indicesNode)
            || indicesNode is not JsonArray indices
            || vertices.Count == 0
            || indices.Count < 3
            || indices.Count % 3 != 0)
        {
            return false;
        }

        pool.Take<Triangle>((indices.Count / 3) * 2, out var triangles);
        for (var i = 0; i + 2 < indices.Count; i += 3)
        {
            if (!TryReadIndex(indices[i], vertices.Count, out var a)
                || !TryReadIndex(indices[i + 1], vertices.Count, out var b)
                || !TryReadIndex(indices[i + 2], vertices.Count, out var c)
                || !TryReadVertex(vertices[a], out var va)
                || !TryReadVertex(vertices[b], out var vb)
                || !TryReadVertex(vertices[c], out var vc))
            {
                pool.Return(ref triangles);
                return false;
            }

            var triangleIndex = (i / 3) * 2;
            triangles[triangleIndex] = new Triangle(in va, in vb, in vc);
            triangles[triangleIndex + 1] = new Triangle(in vc, in vb, in va);
        }

        var scale = Vector3.One;
        mesh = new Mesh(triangles, in scale, pool);
        return true;
    }

    private static bool TryCreateConvexHull(
        BufferPool pool,
        RekallAgeRuntimeComponent? geometryMesh,
        out ConvexHull hull,
        out Vector3 center)
    {
        hull = default;
        center = default;
        if (!TryReadMeshPoints(geometryMesh, out var points) || points.Length < 4)
        {
            return false;
        }

        ConvexHullHelper.CreateShape(points.AsSpan(), pool, out center, out hull);
        return true;
    }

    private static bool TryReadMeshPoints(
        RekallAgeRuntimeComponent? geometryMesh,
        out Vector3[] points)
    {
        points = [];
        if (geometryMesh is null
            || !TryGetPropertyValue(geometryMesh.Properties, "vertices", out var verticesNode)
            || verticesNode is not JsonArray vertices
            || vertices.Count == 0)
        {
            return false;
        }

        var parsed = new List<Vector3>(vertices.Count);
        foreach (var vertexNode in vertices)
        {
            if (!TryReadVertex(vertexNode, out var vertex))
            {
                return false;
            }

            parsed.Add(vertex);
        }

        points = parsed.ToArray();
        return true;
    }

    private static bool TryReadIndex(JsonNode? node, int vertexCount, out int index)
    {
        index = 0;
        if (node is not JsonValue value)
        {
            return false;
        }

        if (!value.TryGetValue<int>(out index))
        {
            if (value.TryGetValue<double>(out var doubleValue))
            {
                index = (int)doubleValue;
            }
            else if (!value.TryGetValue<string>(out var text)
                     || !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
            {
                return false;
            }
        }

        return index >= 0 && index < vertexCount;
    }

    private static bool TryReadVertex(JsonNode? node, out Vector3 vertex)
    {
        vertex = default;
        if (node is JsonArray array && array.Count >= 3)
        {
            vertex = new Vector3(
                ReadSingle(array[0], 0),
                ReadSingle(array[1], 0),
                ReadSingle(array[2], 0));
            return true;
        }

        if (node is not JsonObject obj)
        {
            return false;
        }

        vertex = new Vector3(
            ReadSingle(obj, "x", 0),
            ReadSingle(obj, "y", 0),
            ReadSingle(obj, "z", 0));
        return true;
    }

    private static Vector3 ReadGravity(RekallAgeRuntimeWorld world)
    {
        var settings = world.Entities
            .Select(entity => FindComponent(entity, "Rekall.PhysicsWorld3D"))
            .FirstOrDefault(component => component is not null);
        return new Vector3(
            ReadSingle(settings, "gravityX", 0),
            ReadSingle(settings, "gravityY", DefaultGravityY),
            ReadSingle(settings, "gravityZ", 0));
    }

    private static RekallAgeRuntimeComponent? FindComponent(RekallAgeRuntimeEntity entity, string type)
    {
        return entity.Components.FirstOrDefault(component => component.Type.Equals(type, StringComparison.Ordinal));
    }

    private static PhysicsMaterial ReadPhysicsMaterial(RekallAgeRuntimeEntity entity)
    {
        var component = FindComponent(entity, "Rekall.PhysicsMaterial3D");
        return new PhysicsMaterial(
            Math.Max(0, ReadSingle(component, "friction", 1)),
            Math.Clamp(ReadSingle(component, "restitution", 0), 0, 1),
            Math.Max(0, ReadSingle(component, "minimumBounceSpeed", 0.5f)),
            Math.Max(0, ReadSingle(component, "maximumRecoveryVelocity", 2)),
            Math.Max(0.0001f, ReadSingle(component, "springFrequency", 30)),
            Math.Max(0, ReadSingle(component, "dampingRatio", 1)));
    }

    private static PhysicsMaterial CombineMaterials(IEnumerable<PhysicsMaterial> materials)
    {
        var items = materials.ToArray();
        if (items.Length == 0)
        {
            return PhysicsMaterial.Default;
        }

        return new PhysicsMaterial(
            items.Select(item => item.Friction).DefaultIfEmpty(1).Average(),
            items.Select(item => item.Restitution).DefaultIfEmpty(0).Max(),
            items.Select(item => item.MinimumBounceSpeed).DefaultIfEmpty(0.5f).Min(),
            items.Select(item => item.MaximumRecoveryVelocity).DefaultIfEmpty(2).Max(),
            items.Select(item => item.SpringFrequency).DefaultIfEmpty(30).Max(),
            items.Select(item => item.DampingRatio).DefaultIfEmpty(1).Average());
    }

    private static float EstimateVerticalExtent(PhysicsEntity item)
    {
        return item.Collider?.Type switch
        {
            "Rekall.BoxCollider3D" => Math.Max(0.0001f, ReadSingle(item.Collider, "height", 1)) * 0.5f,
            "Rekall.SphereCollider3D" => Math.Max(0.0001f, ReadSingle(item.Collider, "radius", 0.5f)),
            "Rekall.CapsuleCollider3D" => Math.Max(0.0001f, ReadSingle(item.Collider, "radius", 0.5f))
                + Math.Max(0.0001f, ReadSingle(item.Collider, "length", 1)) * 0.5f,
            _ => 0
        };
    }

    private static bool TryGetSupportSurface(PhysicsEntity item, out SupportSurface surface)
    {
        surface = default;
        if (item.Collider?.Type != "Rekall.BoxCollider3D")
        {
            return false;
        }

        var transform = item.Entity.Transform;
        var width = Math.Max(0.0001f, ReadSingle(item.Collider, "width", 1));
        var height = Math.Max(0.0001f, ReadSingle(item.Collider, "height", 1));
        var depth = Math.Max(0.0001f, ReadSingle(item.Collider, "depth", 1));
        var x = (float)transform.Position3D.X;
        var y = (float)transform.Position3D.Y;
        var z = (float)transform.Position3D.Z;
        surface = new SupportSurface(
            y + height * 0.5f,
            x - width * 0.5f,
            x + width * 0.5f,
            z - depth * 0.5f,
            z + depth * 0.5f);
        return true;
    }

    private static Vector3 ToVector3(RekallAgeRuntimeVector3 value)
    {
        return new Vector3((float)value.X, (float)value.Y, (float)value.Z);
    }

    private static Vector3 ReadVector3(RekallAgeRuntimeComponent? component, string name)
    {
        if (component is null
            || !component.Properties.TryGetPropertyValue(name, out var node)
            || node is not JsonObject vector)
        {
            return Vector3.Zero;
        }

        return new Vector3(
            ReadSingle(vector, "x", 0),
            ReadSingle(vector, "y", 0),
            ReadSingle(vector, "z", 0));
    }

    private static float ReadSingle(RekallAgeRuntimeComponent? component, string name, float fallback)
    {
        return component is null ? fallback : ReadSingle(component.Properties, name, fallback);
    }

    private static bool ReadBoolean(RekallAgeRuntimeComponent component, string name, bool fallback)
    {
        if (!TryGetPropertyValue(component.Properties, name, out var node) || node is not JsonValue value)
        {
            return fallback;
        }

        if (value.TryGetValue<bool>(out var boolean))
        {
            return boolean;
        }

        return value.TryGetValue<string>(out var text)
            && bool.TryParse(text, out var parsed)
            ? parsed
            : fallback;
    }

    private static float ReadSingle(JsonObject properties, string name, float fallback)
    {
        if (!TryGetPropertyValue(properties, name, out var node) || node is not JsonValue value)
        {
            return fallback;
        }

        if (value.TryGetValue<float>(out var single))
        {
            return single;
        }

        if (value.TryGetValue<double>(out var doubleValue))
        {
            return (float)doubleValue;
        }

        if (value.TryGetValue<int>(out var intValue))
        {
            return intValue;
        }

        return value.TryGetValue<string>(out var text)
            && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    private static float ReadSingle(JsonNode? node, float fallback)
    {
        if (node is not JsonValue value)
        {
            return fallback;
        }

        if (value.TryGetValue<float>(out var single))
        {
            return single;
        }

        if (value.TryGetValue<double>(out var doubleValue))
        {
            return (float)doubleValue;
        }

        if (value.TryGetValue<int>(out var intValue))
        {
            return intValue;
        }

        return value.TryGetValue<string>(out var text)
            && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    private static bool TryGetPropertyValue(JsonObject properties, string name, out JsonNode? node)
    {
        if (properties.TryGetPropertyValue(name, out node))
        {
            return true;
        }

        if (name.Length > 0)
        {
            var pascalName = char.ToUpperInvariant(name[0]) + name[1..];
            if (properties.TryGetPropertyValue(pascalName, out node))
            {
                return true;
            }
        }

        node = null;
        return false;
    }

    private readonly record struct StaticShape(bool Created, TypedIndex Shape);

    private readonly record struct DynamicBodyState(
        BodyHandle Handle,
        BodyDescription Description,
        Vector3 CenterOffset,
        PhysicsEntity? Entity = null,
        Vector3 InitialVelocity = default);

    private sealed record PhysicsEntity(
        RekallAgeRuntimeEntity Entity,
        RekallAgeRuntimeComponent? Rigidbody,
        RekallAgeRuntimeComponent? Collider,
        RekallAgeRuntimeComponent? GeometryMesh,
        PhysicsMaterial Material);

    private readonly record struct PhysicsMaterial(
        float Friction,
        float Restitution,
        float MinimumBounceSpeed,
        float MaximumRecoveryVelocity,
        float SpringFrequency,
        float DampingRatio)
    {
        public static PhysicsMaterial Default { get; } = new(1, 0, 0.5f, 2, 30, 1);
    }

    private readonly record struct SupportSurface(
        float TopY,
        float MinX,
        float MaxX,
        float MinZ,
        float MaxZ);

    private readonly record struct RestitutionAdjustment(
        bool Applied,
        Vector3 Position,
        Vector3 LinearVelocity)
    {
        public static RestitutionAdjustment None { get; } = new(false, default, default);
    }

    private struct RekallAgeBepuNarrowPhaseCallbacks(PhysicsMaterial material) : INarrowPhaseCallbacks
    {
        public void Initialize(Simulation simulation)
        {
        }

        public bool AllowContactGeneration(
            int workerIndex,
            CollidableReference a,
            CollidableReference b,
            ref float speculativeMargin)
        {
            return true;
        }

        public bool ConfigureContactManifold<TManifold>(
            int workerIndex,
            CollidablePair pair,
            ref TManifold manifold,
            out PairMaterialProperties pairMaterial)
            where TManifold : unmanaged, IContactManifold<TManifold>
        {
            pairMaterial = new PairMaterialProperties
            {
                FrictionCoefficient = material.Friction,
                MaximumRecoveryVelocity = material.MaximumRecoveryVelocity,
                SpringSettings = new SpringSettings(material.SpringFrequency, material.DampingRatio)
            };
            return true;
        }

        public bool AllowContactGeneration(
            int workerIndex,
            CollidablePair pair,
            int childIndexA,
            int childIndexB)
        {
            return true;
        }

        public bool ConfigureContactManifold(
            int workerIndex,
            CollidablePair pair,
            int childIndexA,
            int childIndexB,
            ref ConvexContactManifold manifold)
        {
            return true;
        }

        public void Dispose()
        {
        }
    }

    private struct RekallAgeBepuPoseIntegratorCallbacks(Vector3 gravity) : IPoseIntegratorCallbacks
    {
        private Vector3Wide _gravityDt;

        public AngularIntegrationMode AngularIntegrationMode => AngularIntegrationMode.Nonconserving;

        public bool AllowSubstepsForUnconstrainedBodies => false;

        public bool IntegrateVelocityForKinematics => false;

        public void Initialize(Simulation simulation)
        {
        }

        public void PrepareForIntegration(float dt)
        {
            _gravityDt = Vector3Wide.Broadcast(gravity * dt);
        }

        public void IntegrateVelocity(
            Vector<int> bodyIndices,
            Vector3Wide position,
            QuaternionWide orientation,
            BodyInertiaWide localInertia,
            Vector<int> integrationMask,
            int workerIndex,
            Vector<float> dt,
            ref BodyVelocityWide velocity)
        {
            velocity.Linear += _gravityDt;
        }
    }
}
