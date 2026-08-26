using System.Numerics;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed class RekallAgeRigValidator
{
    public RekallAgeRigValidationReport Validate(RekallAgeRigAsset rig)
    {
        ArgumentNullException.ThrowIfNull(rig);
        var diagnostics = new List<RekallAgeRigDiagnostic>();
        if (rig.SchemaVersion != RekallAgeRigAsset.CurrentSchemaVersion)
            Error("REKALL_RIG_SCHEMA_UNSUPPORTED", $"Rig schema {rig.SchemaVersion} is unsupported.");
        if (string.IsNullOrWhiteSpace(rig.AssetId))
            Error("REKALL_RIG_ASSET_ID_REQUIRED", "Rig asset ID is required.");
        if (string.IsNullOrWhiteSpace(rig.Name) || rig.Name.Length > 256)
            Error("REKALL_RIG_NAME_INVALID", "Rig name must contain 1 to 256 characters.");
        if (rig.Revision < 1)
            Error("REKALL_RIG_REVISION_INVALID", "Rig revision must be at least one.");
        if (rig.Joints.Count is < 1 or > 4_096)
            Error("REKALL_RIG_JOINT_COUNT_INVALID", "Rig must contain between 1 and 4096 joints.");

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < rig.Joints.Count; index++)
        {
            var joint = rig.Joints[index];
            if (string.IsNullOrWhiteSpace(joint.JointId) || joint.JointId.Length > 128)
                Error("REKALL_RIG_JOINT_ID_INVALID", "Joint ID must contain 1 to 128 characters.", joint.JointId);
            else if (!ids.Add(joint.JointId))
                Error("REKALL_RIG_JOINT_ID_DUPLICATE", $"Joint ID '{joint.JointId}' is duplicated.", joint.JointId);
            if (string.IsNullOrWhiteSpace(joint.Name) || joint.Name.Length > 256)
                Error("REKALL_RIG_JOINT_NAME_INVALID", "Joint name must contain 1 to 256 characters.", joint.JointId);
            else if (!names.Add(joint.Name))
                Error("REKALL_RIG_JOINT_NAME_DUPLICATE", $"Joint name '{joint.Name}' is duplicated.", joint.JointId);
            if (joint.ParentIndex is { } parent && (parent < 0 || parent >= index))
                Error("REKALL_RIG_PARENT_ORDER_INVALID", "A joint parent must refer to an earlier joint index.", joint.JointId);
            if (!TryMatrix(joint.BindLocalMatrix, out var bind))
                Error("REKALL_RIG_BIND_MATRIX_INVALID", "Bind-local matrix must contain 16 finite values.", joint.JointId);
            else if (!Matrix4x4.Invert(bind, out _))
                Error("REKALL_RIG_BIND_MATRIX_NON_INVERTIBLE", "Bind-local matrix must be invertible.", joint.JointId);
        }

        return new(!diagnostics.Any(item => item.Severity == RekallAgeRigDiagnosticSeverity.Error), diagnostics);

        void Error(string code, string message, string? jointId = null) =>
            diagnostics.Add(new(code, RekallAgeRigDiagnosticSeverity.Error, message, jointId));
    }

    internal static bool TryMatrix(IReadOnlyList<double>? values, out Matrix4x4 matrix)
    {
        matrix = Matrix4x4.Identity;
        if (values is not { Count: 16 } || values.Any(value => !double.IsFinite(value) || Math.Abs(value) > 1_000_000_000))
            return false;
        matrix = new(
            (float)values[0], (float)values[1], (float)values[2], (float)values[3],
            (float)values[4], (float)values[5], (float)values[6], (float)values[7],
            (float)values[8], (float)values[9], (float)values[10], (float)values[11],
            (float)values[12], (float)values[13], (float)values[14], (float)values[15]);
        return IsFinite(matrix);
    }

    private static bool IsFinite(Matrix4x4 value) =>
        float.IsFinite(value.M11) && float.IsFinite(value.M12) && float.IsFinite(value.M13) && float.IsFinite(value.M14)
        && float.IsFinite(value.M21) && float.IsFinite(value.M22) && float.IsFinite(value.M23) && float.IsFinite(value.M24)
        && float.IsFinite(value.M31) && float.IsFinite(value.M32) && float.IsFinite(value.M33) && float.IsFinite(value.M34)
        && float.IsFinite(value.M41) && float.IsFinite(value.M42) && float.IsFinite(value.M43) && float.IsFinite(value.M44);
}
