using System.Numerics;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Rendering;

internal sealed class RekallAgeRuntimeWorldTransformResolver
{
    private const int MaximumObservations = 256;
    private readonly IReadOnlyDictionary<string, RekallAgeRuntimeEntity> _entities;
    private readonly Dictionary<string, Resolution> _cache = new(StringComparer.Ordinal);
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
    private readonly List<RekallAgeRuntimeViewportObservation> _observations = [];
    private readonly HashSet<string> _resolving = new(StringComparer.Ordinal);

    public RekallAgeRuntimeWorldTransformResolver(RekallAgeRuntimeWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _entities = world.Entities.ToDictionary(entity => entity.Id, StringComparer.Ordinal);
    }

    public IReadOnlyList<RekallAgeRuntimeViewportObservation> Observations => _observations;

    public RekallAgeRuntimeTransform Resolve(string entityId)
    {
        if (!_entities.TryGetValue(entityId, out var entity))
        {
            return RekallAgeRuntimeTransform.Identity;
        }

        return Resolve(entity).Transform;
    }

    private Resolution Resolve(RekallAgeRuntimeEntity entity)
    {
        if (_cache.TryGetValue(entity.Id, out var cached))
        {
            return cached;
        }

        if (!_resolving.Add(entity.Id))
        {
            Report(
                "runtime.transform.parent_cycle",
                entity,
                $"Entity '{entity.Name}' participates in a parent cycle; its local 3D transform is used.");
            return new Resolution(entity.Transform, false);
        }

        try
        {
            if (string.IsNullOrWhiteSpace(entity.ParentId))
            {
                return Cache(entity.Id, new Resolution(entity.Transform, true));
            }

            if (!_entities.TryGetValue(entity.ParentId, out var parent))
            {
                Report(
                    "runtime.transform.parent_missing",
                    entity,
                    $"Entity '{entity.Name}' references missing parent '{entity.ParentId}'; its local 3D transform is used.");
                return Cache(entity.Id, new Resolution(entity.Transform, false));
            }

            var parentResolution = Resolve(parent);
            if (!parentResolution.ValidHierarchy)
            {
                return Cache(entity.Id, new Resolution(entity.Transform, false));
            }

            var worldMatrix = ToMatrix(entity.Transform) * ToMatrix(parentResolution.Transform);
            if (!Matrix4x4.Decompose(worldMatrix, out var scale, out var rotation, out var translation)
                || !Finite(scale)
                || !Finite(rotation)
                || !Finite(translation))
            {
                Report(
                    "runtime.transform.compose_failed",
                    entity,
                    $"Entity '{entity.Name}' parent transform could not be decomposed; its local 3D transform is used.");
                return Cache(entity.Id, new Resolution(entity.Transform, false));
            }

            var euler = ToEulerDegrees(rotation);
            if (!Finite(euler))
            {
                Report(
                    "runtime.transform.compose_failed",
                    entity,
                    $"Entity '{entity.Name}' parent rotation was not finite; its local 3D transform is used.");
                return Cache(entity.Id, new Resolution(entity.Transform, false));
            }

            return Cache(entity.Id, new Resolution(entity.Transform with
            {
                Position3D = new RekallAgeRuntimeVector3(translation.X, translation.Y, translation.Z),
                Rotation3D = new RekallAgeRuntimeVector3(euler.X, euler.Y, euler.Z),
                Scale3D = new RekallAgeRuntimeVector3(scale.X, scale.Y, scale.Z)
            }, true));
        }
        finally
        {
            _resolving.Remove(entity.Id);
        }
    }

    private Resolution Cache(string entityId, Resolution resolution)
    {
        _cache[entityId] = resolution;
        return resolution;
    }

    private void Report(string code, RekallAgeRuntimeEntity entity, string message)
    {
        var key = $"{code}:{entity.Id}";
        if (_observations.Count >= MaximumObservations || !_reported.Add(key))
        {
            return;
        }

        _observations.Add(new RekallAgeRuntimeViewportObservation(
            code,
            "warning",
            "transform",
            entity.Name.Length > 0 ? entity.Name : entity.Id,
            message));
    }

    private static Matrix4x4 ToMatrix(RekallAgeRuntimeTransform transform) =>
        Matrix4x4.CreateScale(
            (float)transform.Scale3D.X,
            (float)transform.Scale3D.Y,
            (float)transform.Scale3D.Z)
        * Matrix4x4.CreateRotationX(ToRadians(transform.Rotation3D.X))
        * Matrix4x4.CreateRotationY(ToRadians(transform.Rotation3D.Y))
        * Matrix4x4.CreateRotationZ(ToRadians(transform.Rotation3D.Z))
        * Matrix4x4.CreateTranslation(
            (float)transform.Position3D.X,
            (float)transform.Position3D.Y,
            (float)transform.Position3D.Z);

    private static Vector3 ToEulerDegrees(Quaternion quaternion)
    {
        var matrix = Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(quaternion));
        var y = MathF.Asin(Math.Clamp(-matrix.M13, -1f, 1f));
        float x;
        float z;
        if (MathF.Abs(MathF.Cos(y)) > 0.00001f)
        {
            x = MathF.Atan2(matrix.M23, matrix.M33);
            z = MathF.Atan2(matrix.M12, matrix.M11);
        }
        else
        {
            x = MathF.Atan2(-matrix.M32, matrix.M22);
            z = 0;
        }

        return new Vector3(ToDegrees(x), ToDegrees(y), ToDegrees(z));
    }

    private static float ToRadians(double degrees) => (float)(Math.PI / 180d * degrees);

    private static float ToDegrees(float radians) => 180f / MathF.PI * radians;

    private static bool Finite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool Finite(Quaternion value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) && float.IsFinite(value.W);

    private sealed record Resolution(RekallAgeRuntimeTransform Transform, bool ValidHierarchy);
}
