using System.Text.Json;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed partial class RekallAgeMeshOperationExecutor
{
    private static RekallAgeMeshOperationResult AssignLinearSkinWeights(
        RekallAgeMeshAsset source,
        RekallAgeMeshOperationRequest request)
    {
        RequireDomain(request, RekallAgeGeometryDomain.Point);
        var selectedIndices = ResolveIndices(source.Topology.PointIds, request.ElementIds, "point");
        var axis = ReadBoundedString(request.Parameters, "axis", "y").ToLowerInvariant();
        if (axis is not ("x" or "y" or "z"))
            throw Failure("REKALL_MESH_SKIN_AXIS_INVALID", "Skin weight axis must be x, y, or z.");
        var minimum = ReadFiniteDouble(request.Parameters, "minimum");
        var maximum = ReadFiniteDouble(request.Parameters, "maximum", 1);
        if (maximum <= minimum)
            throw Failure("REKALL_MESH_SKIN_RANGE_INVALID", "Skin weight maximum must be greater than minimum.");
        var jointA = ReadBoundedInt(request.Parameters, "jointA", 0, 0, int.MaxValue);
        var jointB = ReadBoundedInt(request.Parameters, "jointB", 1, 0, int.MaxValue);

        var existingJoints = FindSkinAttribute("joint-indices-0", RekallAgeGeometryValueType.Int4);
        var existingWeights = FindSkinAttribute("joint-weights-0", RekallAgeGeometryValueType.Float4);
        if ((existingJoints is null) != (existingWeights is null))
            throw Failure(
                "REKALL_MESH_OPERATION_ATTRIBUTE_CONFLICT",
                "Skin joint indices and weights must either both exist or both be absent.");
        EnsureCanonicalOutputNameIsAvailable("skin.joints", existingJoints);
        EnsureCanonicalOutputNameIsAvailable("skin.weights", existingWeights);
        var jointValues = existingJoints?.Values.ToArray()
            ?? source.Topology.PointIds.Select(_ => JsonSerializer.SerializeToElement(new[] { jointA, jointB, 0, 0 })).ToArray();
        var weightValues = existingWeights?.Values.ToArray()
            ?? source.Topology.PointIds.Select(_ => JsonSerializer.SerializeToElement(new[] { 1d, 0d, 0d, 0d })).ToArray();
        foreach (var index in selectedIndices)
        {
            var position = source.Topology.Positions[index];
            var coordinate = axis switch { "x" => position.X, "y" => position.Y, _ => position.Z };
            var blend = Math.Clamp((coordinate - minimum) / (maximum - minimum), 0, 1);
            jointValues[index] = JsonSerializer.SerializeToElement(new[] { jointA, jointB, 0, 0 });
            weightValues[index] = JsonSerializer.SerializeToElement(new[] { 1 - blend, blend, 0d, 0d });
        }

        var joints = new RekallAgeGeometryAttribute(
            existingJoints?.Name ?? "skin.joints", RekallAgeGeometryDomain.Point,
            RekallAgeGeometryValueType.Int4, jointValues, "joint-indices-0",
            RekallAgeGeometryInterpolation.Nearest);
        var weights = new RekallAgeGeometryAttribute(
            existingWeights?.Name ?? "skin.weights", RekallAgeGeometryDomain.Point,
            RekallAgeGeometryValueType.Float4, weightValues, "joint-weights-0",
            RekallAgeGeometryInterpolation.NormalizedLinear);
        var replacedNames = new HashSet<string>([joints.Name, weights.Name], StringComparer.Ordinal);
        var mesh = source with
        {
            Revision = checked(source.Revision + 1),
            Attributes = source.Attributes.Where(item => !replacedNames.Contains(item.Name))
                .Append(joints).Append(weights).OrderBy(item => item.Name, StringComparer.Ordinal).ToArray()
        };
        return Result(source, mesh,
            ChangeSet(RekallAgeMeshChangeKind.Attributes,
                modifiedPoints: request.ElementIds.Order().ToArray(),
                changedAttributes: [joints.Name, weights.Name],
                affectedBounds: Bounds(selectedIndices.Select(index => source.Topology.Positions[index]))),
            request.ElementIds.Order().Select(id => Preserve(RekallAgeGeometryDomain.Point, id)).ToArray());

        RekallAgeGeometryAttribute? FindSkinAttribute(string semantic, RekallAgeGeometryValueType type)
        {
            var matches = source.Attributes.Where(item =>
                string.Equals(item.Semantic, semantic, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length > 1)
                throw Failure("REKALL_MESH_OPERATION_ATTRIBUTE_CONFLICT", $"Multiple attributes publish skin semantic '{semantic}'.");
            var found = matches.SingleOrDefault();
            if (found is not null && (found.Domain != RekallAgeGeometryDomain.Point || found.ValueType != type))
                throw Failure("REKALL_MESH_OPERATION_ATTRIBUTE_CONFLICT", $"Skin semantic '{semantic}' has an incompatible domain or type.");
            return found;
        }

        void EnsureCanonicalOutputNameIsAvailable(
            string canonicalName,
            RekallAgeGeometryAttribute? existingSemanticAttribute)
        {
            if (existingSemanticAttribute is not null)
                return;
            if (source.Attributes.Any(item => string.Equals(item.Name, canonicalName, StringComparison.OrdinalIgnoreCase)))
                throw Failure(
                    "REKALL_MESH_OPERATION_ATTRIBUTE_CONFLICT",
                    $"Attribute '{canonicalName}' is already used by an unrelated attribute.");
        }
    }
}
