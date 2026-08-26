using System.Numerics;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed class RekallAgeRigEvaluator
{
    public RekallAgeEvaluatedRig Evaluate(
        RekallAgeRigAsset rig,
        IReadOnlyDictionary<string, IReadOnlyList<double>>? jointDeltas = null)
    {
        ArgumentNullException.ThrowIfNull(rig);
        var report = new RekallAgeRigValidator().Validate(rig);
        if (!report.IsValid)
            throw new InvalidDataException("Rig asset failed strict validation: " + string.Join(", ", report.Diagnostics.Where(item => item.Severity == RekallAgeRigDiagnosticSeverity.Error).Select(item => item.Code).Distinct(StringComparer.Ordinal)));

        var normalizedDeltas = new Dictionary<string, IReadOnlyList<double>>(StringComparer.OrdinalIgnoreCase);
        foreach (var delta in jointDeltas ?? new Dictionary<string, IReadOnlyList<double>>())
        {
            if (!normalizedDeltas.TryAdd(delta.Key, delta.Value))
                throw new InvalidDataException($"REKALL_RIG_POSE_JOINT_DUPLICATE: Pose contains duplicate joint '{delta.Key}'.");
        }
        var known = rig.Joints.Select(joint => joint.JointId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknown = normalizedDeltas.Keys.FirstOrDefault(id => !known.Contains(id));
        if (unknown is not null)
            throw new InvalidDataException($"REKALL_RIG_POSE_JOINT_UNKNOWN: Pose delta targets unknown joint '{unknown}'.");

        var bindGlobals = new Matrix4x4[rig.Joints.Count];
        var poseGlobals = new Matrix4x4[rig.Joints.Count];
        IReadOnlyList<double>[] skinMatrices = new IReadOnlyList<double>[rig.Joints.Count];
        for (var index = 0; index < rig.Joints.Count; index++)
        {
            var joint = rig.Joints[index];
            _ = RekallAgeRigValidator.TryMatrix(joint.BindLocalMatrix, out var bindLocal);
            var poseLocal = bindLocal;
            if (normalizedDeltas.TryGetValue(joint.JointId, out var values))
            {
                if (!RekallAgeRigValidator.TryMatrix(values, out var delta))
                    throw new InvalidDataException($"REKALL_RIG_POSE_MATRIX_INVALID: Pose delta for joint '{joint.JointId}' must contain 16 finite values.");
                poseLocal = delta * bindLocal;
            }
            bindGlobals[index] = joint.ParentIndex is { } parent ? bindLocal * bindGlobals[parent] : bindLocal;
            poseGlobals[index] = joint.ParentIndex is { } poseParent ? poseLocal * poseGlobals[poseParent] : poseLocal;
            _ = Matrix4x4.Invert(bindGlobals[index], out var inverseBind);
            skinMatrices[index] = Values(inverseBind * poseGlobals[index]);
        }
        return new(rig.Joints.Select(joint => joint.JointId).ToArray(), skinMatrices)
        {
            PoseGlobalMatrices = poseGlobals.Select(Values).ToArray()
        };
    }

    internal static IReadOnlyList<double> Values(Matrix4x4 value) =>
    [
        value.M11, value.M12, value.M13, value.M14,
        value.M21, value.M22, value.M23, value.M24,
        value.M31, value.M32, value.M33, value.M34,
        value.M41, value.M42, value.M43, value.M44
    ];
}
