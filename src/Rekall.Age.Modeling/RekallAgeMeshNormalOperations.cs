using System.Text.Json;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed partial class RekallAgeMeshOperationExecutor
{
    private static RekallAgeMeshOperationResult WeightedNormals(RekallAgeMeshAsset source, RekallAgeMeshOperationRequest request)
    {
        RequireDomain(request, RekallAgeGeometryDomain.Face);
        if (!request.ElementIds.Order().SequenceEqual(source.Topology.FaceIds.Order()))
            throw Failure("REKALL_MESH_NORMAL_PARTIAL_UNSUPPORTED", "Weighted normals currently require the complete face selection.");
        var name = ReadBoundedString(request.Parameters, "attribute", "normal.weighted");
        var exponent = ReadFiniteDouble(request.Parameters, "faceAreaWeight", 1);
        if (exponent is < 0 or > 4)
            throw Failure("REKALL_MESH_OPERATION_PARAMETER_INVALID", "faceAreaWeight must be between 0 and 4.");
        var topology = source.Topology;
        var weightedFaces = Enumerable.Range(0, topology.FaceIds.Count).Select(face =>
        {
            var corners = FaceCornerSourceIndices(face, topology);
            var points = corners.Select(corner => topology.Positions[topology.CornerPointIndices[corner]]).ToArray();
            var vector = new RekallAgeGeometryVector3(0, 0, 0);
            for (var index = 0; index < points.Length; index++)
            {
                var a = points[index]; var b = points[(index + 1) % points.Length];
                vector = new(vector.X + (a.Y - b.Y) * (a.Z + b.Z), vector.Y + (a.Z - b.Z) * (a.X + b.X), vector.Z + (a.X - b.X) * (a.Y + b.Y));
            }
            var length = Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y + vector.Z * vector.Z);
            if (length <= 1e-12) throw Failure("REKALL_MESH_NORMAL_FACE_DEGENERATE", $"Face '{topology.FaceIds[face]}' has no finite normal.");
            var weight = Math.Pow(length * 0.5, exponent) / length;
            return new RekallAgeGeometryVector3(vector.X * weight, vector.Y * weight, vector.Z * weight);
        }).ToArray();
        var incidentFaces = Enumerable.Range(0, topology.FaceIds.Count)
            .SelectMany(face => FaceCornerSourceIndices(face, topology).Select(corner => (Point: topology.CornerPointIndices[corner], Face: face)))
            .GroupBy(item => item.Point).ToDictionary(group => group.Key, group => group.Select(item => item.Face).Distinct().ToArray());
        var values = topology.CornerPointIndices.Select(point =>
        {
            var sum = incidentFaces[point].Aggregate(new RekallAgeGeometryVector3(0, 0, 0), (value, face) => new(value.X + weightedFaces[face].X, value.Y + weightedFaces[face].Y, value.Z + weightedFaces[face].Z));
            var length = Math.Sqrt(sum.X * sum.X + sum.Y * sum.Y + sum.Z * sum.Z);
            if (length <= 1e-12) throw Failure("REKALL_MESH_NORMAL_VERTEX_DEGENERATE", $"Point '{topology.PointIds[point]}' has cancelling incident normals.");
            return JsonSerializer.SerializeToElement(new[] { sum.X / length, sum.Y / length, sum.Z / length });
        }).ToArray();
        var attribute = new RekallAgeGeometryAttribute(name, RekallAgeGeometryDomain.Corner, RekallAgeGeometryValueType.Float3, values, "normal", RekallAgeGeometryInterpolation.NormalizedLinear);
        var attributes = source.Attributes.Where(item => !item.Name.Equals(name, StringComparison.Ordinal)).Append(attribute).OrderBy(item => item.Name, StringComparer.Ordinal).ToArray();
        var mesh = source with { Revision = checked(source.Revision + 1), Attributes = attributes };
        return Result(source, mesh, ChangeSet(RekallAgeMeshChangeKind.Attributes, modifiedCorners: topology.CornerIds, changedAttributes: [name], affectedBounds: Bounds(topology.Positions)), []);
    }
}
