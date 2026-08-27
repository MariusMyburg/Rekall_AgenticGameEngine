using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuUtilities;
using BepuUtilities.Memory;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Runtime;

public sealed class RekallAgeBepuPhysicsSystem : IRekallAgeRuntimeWorldSystem, IDisposable
{
    private const float DefaultGravityY = -9.81f;
    private PersistentPhysicsWorld? _physicsWorld;
    private readonly RekallAgeCompiledMeshAssetResolver _meshResolver = new();
    private readonly RekallAgeCompiledModelAssetResolver _modelAssetResolver = new();

    public string Id => "runtime.physics.bepu";

    public int Priority => 0;

    public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeWorldFrameContext context)
    {
        var observations = new List<RekallAgeRuntimeObservation>();
        var physicsEntities = world.Entities
            .Select(entity => CreatePhysicsEntity(world.ProjectRoot, entity, context.FrameIndex, observations))
            .ToArray();
        var dynamicBodies = physicsEntities
            .Where(item => item.Rigidbody is not null && item.Collider is not null)
            .ToArray();
        var staticBodies = physicsEntities
            .Where(item => item.Rigidbody is null && item.Collider is not null)
            .ToArray();
        if (dynamicBodies.Length == 0 && staticBodies.Length == 0)
        {
            return ValueTask.FromResult(world with
            {
                Observations = world.Observations.Concat(observations).ToArray()
            });
        }

        var configuration = ReadPhysicsWorldConfiguration(world);
        if (_physicsWorld is null
            || !_physicsWorld.SceneId.Equals(world.SceneId, StringComparison.Ordinal)
            || _physicsWorld.Configuration != configuration)
        {
            _physicsWorld?.Dispose();
            _physicsWorld = new PersistentPhysicsWorld(world.SceneId, configuration);
        }

        _physicsWorld.Reconcile(dynamicBodies, staticBodies);
        _physicsWorld.SynchronizeAuthoredChanges(dynamicBodies);
        var preStepBodies = _physicsWorld.CapturePreStepBodies(dynamicBodies);
        _physicsWorld.Simulation.Timestep((float)context.DeltaTime.TotalSeconds);

        var updated = world.Entities
            .Select(entity => preStepBodies.TryGetValue(entity.Id, out var body)
                ? ApplyBodyState(
                    entity,
                    _physicsWorld.Simulation.Bodies[body.Handle],
                    body.CenterOffset,
                    body.Entity?.Is2D == true)
                : entity)
            .ToArray();
        _physicsWorld.RecordOutputs(updated);
        return ValueTask.FromResult(world with
        {
            Entities = updated,
            Observations = world.Observations.Concat(observations).ToArray()
        });
    }

    public void Dispose()
    {
        _physicsWorld?.Dispose();
        _physicsWorld = null;
    }

    private static RekallAgeRuntimeEntity ApplyBodyState(
        RekallAgeRuntimeEntity entity,
        BodyReference body,
        Vector3 centerOffset,
        bool is2D)
    {
        var pose = body.Pose;
        var velocity = body.Velocity;
        var position = pose.Position - centerOffset;
        var angularDegrees = velocity.Angular * (180 / MathF.PI);
        return entity with
        {
            Transform = entity.Transform with
            {
                Position2D = is2D
                    ? new RekallAgeRuntimeVector2(position.X, position.Y)
                    : entity.Transform.Position2D,
                Position3D = is2D
                    ? new RekallAgeRuntimeVector3(
                        entity.Transform.Position3D.X,
                        entity.Transform.Position3D.Y,
                        entity.Transform.Position3D.Z)
                    : new RekallAgeRuntimeVector3(position.X, position.Y, position.Z),
                Rotation2D = is2D
                    ? QuaternionToPlanarDegrees(pose.Orientation)
                    : entity.Transform.Rotation2D,
                Rotation3D = is2D
                    ? entity.Transform.Rotation3D
                    : QuaternionToRendererEulerDegrees(pose.Orientation)
            },
            Components = UpsertPhysicsState(
                entity.Components,
                velocity.Linear,
                angularDegrees,
                pose.Orientation,
                body.Awake,
                is2D)
        };
    }

    private static IReadOnlyList<RekallAgeRuntimeComponent> UpsertPhysicsState(
        IReadOnlyList<RekallAgeRuntimeComponent> components,
        Vector3 linearVelocity,
        Vector3 angularVelocity,
        Quaternion orientation,
        bool awake,
        bool is2D)
    {
        var state = new JsonObject
        {
            ["backend"] = "bepu",
            ["awake"] = awake,
            ["linearVelocity"] = new JsonObject
            {
                ["x"] = linearVelocity.X,
                ["y"] = linearVelocity.Y,
                ["z"] = linearVelocity.Z
            },
            ["angularVelocity"] = new JsonObject
            {
                ["x"] = angularVelocity.X,
                ["y"] = angularVelocity.Y,
                ["z"] = angularVelocity.Z
            },
            ["orientation"] = new JsonObject
            {
                ["x"] = orientation.X,
                ["y"] = orientation.Y,
                ["z"] = orientation.Z,
                ["w"] = orientation.W
            }
        };
        var stateType = is2D ? "Rekall.PhysicsState2D" : "Rekall.PhysicsState3D";
        var replaced = false;
        var updated = components.Select(component =>
        {
            if (!component.Type.Equals(stateType, StringComparison.Ordinal))
            {
                return component;
            }

            replaced = true;
            return new RekallAgeRuntimeComponent(component.Type, state.DeepClone().AsObject());
        }).ToList();
        if (!replaced)
        {
            updated.Add(new RekallAgeRuntimeComponent(stateType, state));
        }

        return updated
            .OrderBy(component => component.Type, StringComparer.Ordinal)
            .ToArray();
    }

    private PhysicsEntity CreatePhysicsEntity(
        string? projectRoot,
        RekallAgeRuntimeEntity entity,
        int frame,
        ICollection<RekallAgeRuntimeObservation> observations)
    {
        var reference = FindComponent(entity, "Rekall.MeshAssetReference");
        RekallAgeCompiledMeshSnapshot? compiledMesh = null;
        string? compiledMeshRevision = null;
        if (reference is not null)
        {
            var resolved = _meshResolver.Resolve(
                projectRoot,
                ReadString(reference, "assetId", string.Empty),
                ReadString(reference, "expectedRevision", string.Empty));
            compiledMesh = resolved.Snapshot;
            compiledMeshRevision = resolved.FileRevision;
            if (resolved.IssueCode is not null)
            {
                observations.Add(new RekallAgeRuntimeObservation(
                    frame,
                    resolved.IssueCode,
                    "error",
                    "physics",
                    entity.Id,
                    entity.Name,
                    Id,
                    resolved.IssueMessage ?? "Editable mesh collider could not be resolved.",
                    ["rekall.mesh.inspect", "rekall.mesh.validate"]));
            }
        }
        else
        {
            // Rekall.ModelAssetReference is the newer published Model Asset placement shape
            // (rekall.scene.instantiate_asset); a same-entity Rekall.MeshCollider must resolve its
            // compiled geometry the same way an older Rekall.MeshAssetReference entity's collider
            // does, or a placed Model Asset silently has no physical shape at all.
            var modelReference = FindComponent(entity, "Rekall.ModelAssetReference");
            if (modelReference is not null)
            {
                var resolved = _modelAssetResolver.Resolve(
                    projectRoot,
                    ReadString(modelReference, "assetId", string.Empty));
                compiledMesh = resolved.Snapshot;
                compiledMeshRevision = resolved.Revision;
                if (resolved.IssueCode is not null)
                {
                    observations.Add(new RekallAgeRuntimeObservation(
                        frame,
                        resolved.IssueCode,
                        "error",
                        "physics",
                        entity.Id,
                        entity.Name,
                        Id,
                        resolved.IssueMessage ?? "Model Asset collider could not be resolved.",
                        ["rekall.asset.model.inspect", "rekall.asset.model.rebuild"]));
                }
            }
        }
        return new PhysicsEntity(
            entity,
            FindComponent(entity, "Rekall.Rigidbody3D") ?? FindComponent(entity, "Rekall.Rigidbody2D"),
            FindCollider(entity),
            FindComponent(entity, "Rekall.GeometryMesh"),
            compiledMesh,
            compiledMeshRevision,
            ReadPhysicsMaterial(entity),
            FindComponent(entity, "Rekall.Rigidbody2D") is not null
                || FindComponent(entity, "Rekall.BoxCollider2D") is not null
                || FindComponent(entity, "Rekall.CircleCollider2D") is not null);
    }

    private static RekallAgeRuntimeComponent? FindCollider(RekallAgeRuntimeEntity entity)
    {
        return entity.Components.FirstOrDefault(component =>
            component.Type is
                "Rekall.BoxCollider2D" or
                "Rekall.CircleCollider2D" or
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
            case "Rekall.BoxCollider2D":
                created = new DynamicBodyState(
                    default,
                    BodyDescription.CreateConvexDynamic(pose, velocity, mass, simulation.Shapes, CreateBox2D(collider)),
                    Vector3.Zero);
                return true;
            case "Rekall.CircleCollider2D":
                created = new DynamicBodyState(
                    default,
                    BodyDescription.CreateConvexDynamic(pose, velocity, mass, simulation.Shapes, CreateSphere(collider)),
                    Vector3.Zero);
                return true;
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
                && TryCreateConvexHull(pool, item.GeometryMesh, item.CompiledMesh, out var hull, out var center):
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
            "Rekall.BoxCollider2D" => new StaticShape(true, simulation.Shapes.Add(CreateBox2D(item.Collider))),
            "Rekall.CircleCollider2D" => new StaticShape(true, simulation.Shapes.Add(CreateSphere(item.Collider))),
            "Rekall.BoxCollider3D" => new StaticShape(true, simulation.Shapes.Add(CreateBox(item.Collider))),
            "Rekall.SphereCollider3D" => new StaticShape(true, simulation.Shapes.Add(CreateSphere(item.Collider))),
            "Rekall.CapsuleCollider3D" => new StaticShape(true, simulation.Shapes.Add(CreateCapsule(item.Collider))),
            "Rekall.MeshCollider" => TryCreateStaticMesh(pool, item.GeometryMesh, item.CompiledMesh, out var mesh)
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

    private static Box CreateBox2D(RekallAgeRuntimeComponent collider)
    {
        return new Box(
            Math.Max(0.0001f, ReadSingle(collider, "width", 1)),
            Math.Max(0.0001f, ReadSingle(collider, "height", 1)),
            0.1f);
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
        RekallAgeCompiledMeshSnapshot? compiledMesh,
        out Mesh mesh)
    {
        mesh = default;
        if (compiledMesh is not null)
        {
            pool.Take<Triangle>((compiledMesh.Indices.Count / 3) * 2, out var compiledTriangles);
            for (var i = 0; i + 2 < compiledMesh.Indices.Count; i += 3)
            {
                var a = compiledMesh.Vertices[checked((int)compiledMesh.Indices[i])].Position;
                var b = compiledMesh.Vertices[checked((int)compiledMesh.Indices[i + 1])].Position;
                var c = compiledMesh.Vertices[checked((int)compiledMesh.Indices[i + 2])].Position;
                var va = new Vector3((float)a.X, (float)a.Y, (float)a.Z);
                var vb = new Vector3((float)b.X, (float)b.Y, (float)b.Z);
                var vc = new Vector3((float)c.X, (float)c.Y, (float)c.Z);
                var triangleIndex = (i / 3) * 2;
                compiledTriangles[triangleIndex] = new Triangle(in va, in vb, in vc);
                compiledTriangles[triangleIndex + 1] = new Triangle(in vc, in vb, in va);
            }
            var compiledScale = Vector3.One;
            mesh = new Mesh(compiledTriangles, in compiledScale, pool);
            return true;
        }
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
        RekallAgeCompiledMeshSnapshot? compiledMesh,
        out ConvexHull hull,
        out Vector3 center)
    {
        hull = default;
        center = default;
        var points = compiledMesh is null
            ? TryReadMeshPoints(geometryMesh, out var legacyPoints) ? legacyPoints : []
            : compiledMesh.Vertices
                .GroupBy(vertex => vertex.SourcePointId)
                .Select(group => group.First().Position)
                .Select(point => new Vector3((float)point.X, (float)point.Y, (float)point.Z))
                .ToArray();
        if (points.Length < 4)
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

    private static PhysicsWorldConfiguration ReadPhysicsWorldConfiguration(RekallAgeRuntimeWorld world)
    {
        var settings = world.Entities
            .Select(entity => FindComponent(entity, "Rekall.PhysicsWorld3D"))
            .FirstOrDefault(component => component is not null);
        return new PhysicsWorldConfiguration(
            new Vector3(
                ReadSingle(settings, "gravityX", 0),
                ReadSingle(settings, "gravityY", DefaultGravityY),
                ReadSingle(settings, "gravityZ", 0)),
            Math.Clamp((int)ReadSingle(settings, "velocityIterationCount", 4), 1, 32),
            Math.Clamp((int)ReadSingle(settings, "substepCount", 4), 1, 16));
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

    private static Vector3 ToVector3(RekallAgeRuntimeVector3 value)
    {
        return new Vector3((float)value.X, (float)value.Y, (float)value.Z);
    }

    private static Vector3 ToPhysicsPosition(PhysicsEntity item)
    {
        return item.Is2D
            ? new Vector3(
                (float)item.Entity.Transform.Position2D.X,
                (float)item.Entity.Transform.Position2D.Y,
                0)
            : ToVector3(item.Entity.Transform.Position3D);
    }

    private static Quaternion ToPhysicsOrientation(PhysicsEntity item)
    {
        var stateType = item.Is2D ? "Rekall.PhysicsState2D" : "Rekall.PhysicsState3D";
        if (TryReadQuaternion(FindComponent(item.Entity, stateType), "orientation", out var persisted))
        {
            return persisted;
        }

        return ToAuthoredOrientation(item);
    }

    private static Quaternion ToAuthoredOrientation(PhysicsEntity item)
    {

        if (item.Is2D)
        {
            return Quaternion.CreateFromAxisAngle(
                Vector3.UnitZ,
                (float)(item.Entity.Transform.Rotation2D * Math.PI / 180));
        }

        var rotation = item.Entity.Transform.Rotation3D;
        var radiansX = (float)(rotation.X * Math.PI / 180);
        var radiansY = (float)(rotation.Y * Math.PI / 180);
        var radiansZ = (float)(rotation.Z * Math.PI / 180);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(
            Matrix4x4.CreateRotationX(radiansX)
            * Matrix4x4.CreateRotationY(radiansY)
            * Matrix4x4.CreateRotationZ(radiansZ)));
    }

    private static Vector3 ReadAuthoredLinearVelocity(RekallAgeRuntimeComponent rigidbody)
    {
        return new Vector3(
            ReadSingle(rigidbody, "linearVelocityX", 0),
            ReadSingle(rigidbody, "linearVelocityY", 0),
            ReadSingle(rigidbody, "linearVelocityZ", 0));
    }

    private static Vector3 ReadAuthoredAngularVelocity(RekallAgeRuntimeComponent rigidbody)
    {
        return new Vector3(
            ReadSingle(rigidbody, "angularVelocityX", 0),
            ReadSingle(rigidbody, "angularVelocityY", 0),
            ReadSingle(rigidbody, "angularVelocityZ", 0));
    }

    private static bool TryReadQuaternion(
        RekallAgeRuntimeComponent? component,
        string name,
        out Quaternion orientation)
    {
        orientation = Quaternion.Identity;
        if (component is null
            || !component.Properties.TryGetPropertyValue(name, out var node)
            || node is not JsonObject value)
        {
            return false;
        }

        orientation = Quaternion.Normalize(new Quaternion(
            ReadSingle(value, "x", 0),
            ReadSingle(value, "y", 0),
            ReadSingle(value, "z", 0),
            ReadSingle(value, "w", 1)));
        return true;
    }

    private static double QuaternionToPlanarDegrees(Quaternion orientation)
    {
        return Math.Atan2(
            2 * ((orientation.W * orientation.Z) + (orientation.X * orientation.Y)),
            1 - (2 * ((orientation.Y * orientation.Y) + (orientation.Z * orientation.Z))))
            * 180 / Math.PI;
    }

    private static RekallAgeRuntimeVector3 QuaternionToRendererEulerDegrees(Quaternion orientation)
    {
        orientation = Quaternion.Normalize(orientation);
        var matrix = Matrix4x4.CreateFromQuaternion(orientation);
        var yaw = Math.Asin(Math.Clamp(-matrix.M13, -1, 1));
        var cosineYaw = Math.Cos(yaw);
        double pitch;
        double roll;
        if (Math.Abs(cosineYaw) > 0.000001)
        {
            pitch = Math.Atan2(matrix.M23, matrix.M33);
            roll = Math.Atan2(matrix.M12, matrix.M11);
        }
        else
        {
            pitch = -matrix.M13 > 0
                ? Math.Atan2(matrix.M21, matrix.M22)
                : Math.Atan2(-matrix.M21, matrix.M22);
            roll = 0;
        }
        const double radiansToDegrees = 180 / Math.PI;
        return new RekallAgeRuntimeVector3(
            pitch * radiansToDegrees,
            yaw * radiansToDegrees,
            roll * radiansToDegrees);
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

    private static string ReadString(RekallAgeRuntimeComponent component, string name, string fallback)
    {
        if (!TryGetPropertyValue(component.Properties, name, out var node) || node is not JsonValue value)
        {
            return fallback;
        }

        return value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
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

    private sealed class PersistentPhysicsWorld : IDisposable
    {
        private readonly BufferPool _pool = new();
        private readonly CollidableProperty<PhysicsMaterial> _materials;
        // RekallAgeCollisionFilter.Rule holds reference-type fields (string/IReadOnlySet<string>),
        // so it cannot live in BEPU's CollidableProperty<T> (requires an unmanaged T). Plain
        // handle-keyed dictionaries instead; entries are removed in RemoveDynamic/RemoveStatic so a
        // recycled BEPU handle never resolves to a stale entity's rule.
        private readonly Dictionary<BodyHandle, RekallAgeCollisionFilter.Rule> _dynamicFilters = new();
        private readonly Dictionary<StaticHandle, RekallAgeCollisionFilter.Rule> _staticFilters = new();
        private readonly Dictionary<string, PersistentDynamicBody> _dynamicBodies = new(StringComparer.Ordinal);
        private readonly Dictionary<string, PersistentStaticBody> _staticBodies = new(StringComparer.Ordinal);
        private readonly Dictionary<string, BodyOutputSnapshot> _lastOutputs = new(StringComparer.Ordinal);

        public PersistentPhysicsWorld(string sceneId, PhysicsWorldConfiguration configuration)
        {
            SceneId = sceneId;
            Configuration = configuration;
            _materials = new CollidableProperty<PhysicsMaterial>(_pool);
            Simulation = Simulation.Create(
                _pool,
                new RekallAgeBepuNarrowPhaseCallbacks(_materials, LookupFilter),
                new RekallAgeBepuPoseIntegratorCallbacks(configuration.Gravity),
                new SolveDescription(configuration.VelocityIterationCount, configuration.SubstepCount));
        }

        public string SceneId { get; }

        public PhysicsWorldConfiguration Configuration { get; }

        public Simulation Simulation { get; }

        public void Reconcile(
            IReadOnlyList<PhysicsEntity> dynamicBodies,
            IReadOnlyList<PhysicsEntity> staticBodies)
        {
            var desiredDynamic = dynamicBodies.ToDictionary(item => item.Entity.Id, StringComparer.Ordinal);
            foreach (var existing in _dynamicBodies.ToArray())
            {
                if (!desiredDynamic.TryGetValue(existing.Key, out var desired)
                    || !existing.Value.Signature.Equals(ConfigurationSignature(desired, includeTransform: false), StringComparison.Ordinal))
                {
                    RemoveDynamic(existing.Key, existing.Value);
                }
            }

            foreach (var item in dynamicBodies)
            {
                if (!_dynamicBodies.ContainsKey(item.Entity.Id))
                {
                    AddDynamic(item);
                }
            }

            var desiredStatic = staticBodies.ToDictionary(item => item.Entity.Id, StringComparer.Ordinal);
            foreach (var existing in _staticBodies.ToArray())
            {
                if (!desiredStatic.TryGetValue(existing.Key, out var desired)
                    || !existing.Value.Signature.Equals(ConfigurationSignature(desired, includeTransform: true), StringComparison.Ordinal))
                {
                    RemoveStatic(existing.Key, existing.Value);
                }
            }

            foreach (var item in staticBodies)
            {
                if (!_staticBodies.ContainsKey(item.Entity.Id))
                {
                    AddStatic(item);
                }
            }
        }

        public void SynchronizeAuthoredChanges(IReadOnlyList<PhysicsEntity> dynamicBodies)
        {
            foreach (var item in dynamicBodies)
            {
                if (!_dynamicBodies.TryGetValue(item.Entity.Id, out var persistent)
                    || !_lastOutputs.TryGetValue(item.Entity.Id, out var previous)
                    || previous.Matches(item.Entity))
                {
                    continue;
                }

                var body = Simulation.Bodies[persistent.Handle];
                body.Pose = new RigidPose(
                    ToPhysicsPosition(item) + persistent.CenterOffset,
                    ToAuthoredOrientation(item));
                body.Velocity = CreateVelocity(item);
                body.Awake = true;
                Simulation.Bodies.UpdateBounds(persistent.Handle);
            }
        }

        public Dictionary<string, DynamicBodyState> CapturePreStepBodies(
            IReadOnlyList<PhysicsEntity> dynamicBodies)
        {
            var current = dynamicBodies.ToDictionary(item => item.Entity.Id, StringComparer.Ordinal);
            return _dynamicBodies
                .Where(pair => current.ContainsKey(pair.Key))
                .ToDictionary(
                    pair => pair.Key,
                    pair => new DynamicBodyState(
                        pair.Value.Handle,
                        default,
                        pair.Value.CenterOffset,
                        current[pair.Key],
                        Simulation.Bodies[pair.Value.Handle].Velocity.Linear),
                    StringComparer.Ordinal);
        }

        public void RecordOutputs(IReadOnlyList<RekallAgeRuntimeEntity> entities)
        {
            _lastOutputs.Clear();
            foreach (var entity in entities)
            {
                if (_dynamicBodies.ContainsKey(entity.Id))
                {
                    _lastOutputs[entity.Id] = BodyOutputSnapshot.Create(entity);
                }
            }
        }

        public void Dispose()
        {
            Simulation.Dispose();
            _pool.Clear();
        }

        private void AddDynamic(PhysicsEntity item)
        {
            var pose = CreatePose(item, Vector3.Zero);
            var velocity = CreateVelocity(item);
            var mass = Math.Max(0.0001f, ReadSingle(item.Rigidbody!, "mass", 1));
            if (!TryCreateDynamicDescription(Simulation, _pool, item, pose, velocity, mass, out var created))
            {
                return;
            }

            var handle = Simulation.Bodies.Add(created.Description);
            _materials.Allocate(handle) = item.Material;
            _dynamicFilters[handle] = RekallAgeCollisionFilter.Rule.From(item.Entity);
            _dynamicBodies[item.Entity.Id] = new PersistentDynamicBody(
                handle,
                created.Description.Collidable.Shape,
                created.CenterOffset,
                ConfigurationSignature(item, includeTransform: false));
        }

        private void AddStatic(PhysicsEntity item)
        {
            var shape = CreateStaticShape(Simulation, _pool, item);
            if (!shape.Created)
            {
                return;
            }

            var handle = Simulation.Statics.Add(new StaticDescription(
                CreatePose(item, Vector3.Zero),
                shape.Shape));
            _materials.Allocate(handle) = item.Material;
            _staticFilters[handle] = RekallAgeCollisionFilter.Rule.From(item.Entity);
            _staticBodies[item.Entity.Id] = new PersistentStaticBody(
                handle,
                shape.Shape,
                ConfigurationSignature(item, includeTransform: true));
        }

        private void RemoveDynamic(string id, PersistentDynamicBody body)
        {
            Simulation.Bodies.Remove(body.Handle);
            Simulation.Shapes.RemoveAndDispose(body.Shape, _pool);
            _dynamicFilters.Remove(body.Handle);
            _dynamicBodies.Remove(id);
            _lastOutputs.Remove(id);
        }

        private void RemoveStatic(string id, PersistentStaticBody body)
        {
            Simulation.Statics.Remove(body.Handle);
            Simulation.Shapes.RemoveAndDispose(body.Shape, _pool);
            _staticFilters.Remove(body.Handle);
            _staticBodies.Remove(id);
        }

        private RekallAgeCollisionFilter.Rule LookupFilter(CollidableReference collidable)
        {
            return collidable.Mobility == CollidableMobility.Static
                ? _staticFilters.GetValueOrDefault(collidable.StaticHandle, RekallAgeCollisionFilter.Rule.Default)
                : _dynamicFilters.GetValueOrDefault(collidable.BodyHandle, RekallAgeCollisionFilter.Rule.Default);
        }

        private static RigidPose CreatePose(PhysicsEntity item, Vector3 centerOffset)
        {
            return new RigidPose(ToPhysicsPosition(item) + centerOffset, ToPhysicsOrientation(item));
        }

        private static BodyVelocity CreateVelocity(PhysicsEntity item)
        {
            var stateType = item.Is2D ? "Rekall.PhysicsState2D" : "Rekall.PhysicsState3D";
            var state = FindComponent(item.Entity, stateType);
            var linearVelocity = state is null
                ? ReadAuthoredLinearVelocity(item.Rigidbody!)
                : ReadVector3(state, "linearVelocity");
            var angularDegrees = state is null
                ? ReadAuthoredAngularVelocity(item.Rigidbody!)
                : ReadVector3(state, "angularVelocity");
            var velocity = new BodyVelocity(linearVelocity)
            {
                Angular = angularDegrees * (MathF.PI / 180)
            };
            if (item.Is2D)
            {
                velocity.Linear.Z = 0;
                velocity.Angular.X = 0;
                velocity.Angular.Y = 0;
            }

            return velocity;
        }

        private static string ConfigurationSignature(PhysicsEntity item, bool includeTransform)
        {
            return string.Join(
                "|",
                item.Is2D,
                item.Rigidbody?.Properties.ToJsonString() ?? string.Empty,
                item.Collider?.Type ?? string.Empty,
                item.Collider?.Properties.ToJsonString() ?? string.Empty,
                item.GeometryMesh?.Properties.ToJsonString() ?? string.Empty,
                item.CompiledMeshRevision ?? string.Empty,
                item.Material,
                includeTransform ? item.Entity.Transform : string.Empty);
        }
    }

    private readonly record struct PersistentDynamicBody(
        BodyHandle Handle,
        TypedIndex Shape,
        Vector3 CenterOffset,
        string Signature);

    private readonly record struct PersistentStaticBody(
        StaticHandle Handle,
        TypedIndex Shape,
        string Signature);

    private readonly record struct PhysicsWorldConfiguration(
        Vector3 Gravity,
        int VelocityIterationCount,
        int SubstepCount);

    private readonly record struct BodyOutputSnapshot(
        RekallAgeRuntimeTransform Transform,
        string PhysicsState)
    {
        public static BodyOutputSnapshot Create(RekallAgeRuntimeEntity entity)
        {
            var state = entity.Components.FirstOrDefault(component =>
                component.Type is "Rekall.PhysicsState2D" or "Rekall.PhysicsState3D");
            return new BodyOutputSnapshot(
                entity.Transform,
                state?.Properties.ToJsonString() ?? string.Empty);
        }

        public bool Matches(RekallAgeRuntimeEntity entity)
        {
            var current = Create(entity);
            return Transform == current.Transform
                && PhysicsState.Equals(current.PhysicsState, StringComparison.Ordinal);
        }
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
        RekallAgeCompiledMeshSnapshot? CompiledMesh,
        string? CompiledMeshRevision,
        PhysicsMaterial Material,
        bool Is2D);

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

    private struct RekallAgeBepuNarrowPhaseCallbacks(
        CollidableProperty<PhysicsMaterial> materials,
        Func<CollidableReference, RekallAgeCollisionFilter.Rule> lookupFilter) : INarrowPhaseCallbacks
    {
        private Simulation? _simulation;

        public void Initialize(Simulation simulation)
        {
            _simulation = simulation;
            materials.Initialize(simulation);
        }

        public bool AllowContactGeneration(
            int workerIndex,
            CollidableReference a,
            CollidableReference b,
            ref float speculativeMargin)
        {
            var left = lookupFilter(a);
            var right = lookupFilter(b);
            return left.Accepts(right.Layer) && right.Accepts(left.Layer);
        }

        public bool ConfigureContactManifold<TManifold>(
            int workerIndex,
            CollidablePair pair,
            ref TManifold manifold,
            out PairMaterialProperties pairMaterial)
            where TManifold : unmanaged, IContactManifold<TManifold>
        {
            var material = CombineMaterials([materials[pair.A], materials[pair.B]]);
            var dampingRatio = material.DampingRatio;
            var springFrequency = material.SpringFrequency;
            if (material.Restitution > 0
                && HasBounceImpact(pair, ref manifold, material.MinimumBounceSpeed))
            {
                dampingRatio = Math.Min(dampingRatio, RestitutionToDampingRatio(material.Restitution));
                springFrequency = Math.Min(springFrequency, 10);
            }
            pairMaterial = new PairMaterialProperties
            {
                FrictionCoefficient = material.Friction,
                MaximumRecoveryVelocity = material.MaximumRecoveryVelocity,
                SpringSettings = new SpringSettings(springFrequency, dampingRatio)
            };
            return true;
        }

        private bool HasBounceImpact<TManifold>(
            CollidablePair pair,
            ref TManifold manifold,
            float minimumBounceSpeed)
            where TManifold : unmanaged, IContactManifold<TManifold>
        {
            if (_simulation is null)
            {
                return false;
            }

            for (var contactIndex = 0; contactIndex < manifold.Count; contactIndex++)
            {
                manifold.GetContact(contactIndex, out var offsetFromA, out var normal, out _, out _);
                var positionA = CollidablePosition(pair.A);
                var contactPosition = positionA + offsetFromA;
                var velocityA = PointVelocity(pair.A, contactPosition);
                var velocityB = PointVelocity(pair.B, contactPosition);
                if (Vector3.Dot(velocityA - velocityB, normal) <= -minimumBounceSpeed)
                {
                    return true;
                }
            }

            return false;
        }

        private Vector3 CollidablePosition(CollidableReference collidable)
        {
            return collidable.Mobility == CollidableMobility.Static
                ? _simulation!.Statics[collidable.StaticHandle].Pose.Position
                : _simulation!.Bodies[collidable.BodyHandle].Pose.Position;
        }

        private Vector3 PointVelocity(CollidableReference collidable, Vector3 contactPosition)
        {
            if (collidable.Mobility == CollidableMobility.Static)
            {
                return Vector3.Zero;
            }

            var body = _simulation!.Bodies[collidable.BodyHandle];
            return body.Velocity.Linear
                + Vector3.Cross(body.Velocity.Angular, contactPosition - body.Pose.Position);
        }

        private static float RestitutionToDampingRatio(float restitution)
        {
            return 1 - Math.Clamp(restitution, 0, 1);
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
            materials.Dispose();
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
