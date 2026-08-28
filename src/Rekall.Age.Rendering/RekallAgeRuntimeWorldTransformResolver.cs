using System.Numerics;
using System.Text.Json.Nodes;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Rendering;

internal sealed class RekallAgeRuntimeWorldTransformResolver
{
    private const int MaximumObservations = 256;
    private readonly IReadOnlyDictionary<string, RekallAgeRuntimeEntity> _entities;
    private readonly string? _projectRoot;
    private readonly RekallAgeRigPoseResolver _rigPoseResolver;
    private readonly Dictionary<string, RekallAgeRigPoseResolution> _rigResolutions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Resolution> _cache = new(StringComparer.Ordinal);
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
    private readonly List<RekallAgeRuntimeViewportObservation> _observations = [];
    private readonly HashSet<string> _resolving = new(StringComparer.Ordinal);

    public RekallAgeRuntimeWorldTransformResolver(
        RekallAgeRuntimeWorld world,
        RekallAgeRigPoseResolver? rigPoseResolver = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        _entities = world.Entities.ToDictionary(entity => entity.Id, StringComparer.Ordinal);
        _projectRoot = world.ProjectRoot;
        _rigPoseResolver = rigPoseResolver ?? new RekallAgeRigPoseResolver();
    }

    public IReadOnlyList<RekallAgeRuntimeViewportObservation> Observations => _observations;

    /// <summary>
    /// Entities indexed by id. Exposed so callers that already hold a resolver can look an
    /// entity up in O(1) instead of rescanning <c>world.Entities</c>: a linear scan per
    /// renderable is O(entities x renderables), which dominates the frame on large scenes.
    /// </summary>
    public IReadOnlyDictionary<string, RekallAgeRuntimeEntity> EntitiesById => _entities;

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

            var localMatrix = ToMatrix(entity.Transform);
            var attachment = entity.Components.FirstOrDefault(component =>
                component.Type.Equals("Rekall.RigAttachment", StringComparison.Ordinal));
            if (attachment is not null
                && ReadBoolean(attachment.Properties, "enabled", true)
                && TryResolveJointPose(entity, parent, attachment, out var jointPose))
            {
                localMatrix *= jointPose;
            }
            var worldMatrix = localMatrix * ToMatrix(parentResolution.Transform);
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

    private bool TryResolveJointPose(
        RekallAgeRuntimeEntity entity,
        RekallAgeRuntimeEntity parent,
        RekallAgeRuntimeComponent attachment,
        out Matrix4x4 jointPose)
    {
        jointPose = Matrix4x4.Identity;
        var jointId = ReadString(attachment.Properties, "jointId");
        if (string.IsNullOrWhiteSpace(jointId))
        {
            Report(
                "runtime.transform.rig_attachment_joint_missing",
                entity,
                $"Entity '{entity.Name}' has an enabled rig attachment without a jointId; ordinary parent composition is used.");
            return false;
        }

        var pose = parent.Components.FirstOrDefault(component =>
            component.Type.Equals("Rekall.RigPose", StringComparison.Ordinal));
        if (pose is null)
        {
            Report(
                "runtime.transform.rig_attachment_pose_missing",
                entity,
                $"Parent entity '{parent.Name}' has no Rekall.RigPose for attached joint '{jointId}'; ordinary parent composition is used.");
            return false;
        }

        if (!_rigResolutions.TryGetValue(parent.Id, out var resolution))
        {
            resolution = _rigPoseResolver.Resolve(_projectRoot, pose);
            _rigResolutions[parent.Id] = resolution;
        }
        if (resolution.IssueCode is not null)
        {
            Report(
                "runtime.transform.rig_attachment_pose_invalid",
                entity,
                $"Parent rig pose for '{parent.Name}' could not resolve attached joint '{jointId}': {resolution.IssueMessage}");
            return false;
        }
        if (!resolution.JointPoseMatrices.TryGetValue(jointId, out var values) || !TryMatrix(values, out jointPose))
        {
            Report(
                "runtime.transform.rig_attachment_joint_unknown",
                entity,
                $"Parent rig pose for '{parent.Name}' has no finite joint '{jointId}'; ordinary parent composition is used.");
            return false;
        }
        return true;
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

    private static bool TryMatrix(IReadOnlyList<double> values, out Matrix4x4 matrix)
    {
        if (values.Count != 16 || values.Any(value => !double.IsFinite(value)))
        {
            matrix = default;
            return false;
        }
        matrix = new Matrix4x4(
            (float)values[0], (float)values[1], (float)values[2], (float)values[3],
            (float)values[4], (float)values[5], (float)values[6], (float)values[7],
            (float)values[8], (float)values[9], (float)values[10], (float)values[11],
            (float)values[12], (float)values[13], (float)values[14], (float)values[15]);
        return true;
    }

    private static string? ReadString(JsonObject properties, string name) =>
        TryGet(properties, name, out var node)
        && node is JsonValue value
        && value.TryGetValue<string>(out var text)
            ? text?.Trim()
            : null;

    private static bool ReadBoolean(JsonObject properties, string name, bool fallback) =>
        TryGet(properties, name, out var node)
        && node is JsonValue value
        && value.TryGetValue<bool>(out var result)
            ? result
            : fallback;

    private static bool TryGet(JsonObject properties, string name, out JsonNode? value)
    {
        var match = properties.FirstOrDefault(item => item.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
        value = match.Value;
        return match.Key is not null;
    }

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
