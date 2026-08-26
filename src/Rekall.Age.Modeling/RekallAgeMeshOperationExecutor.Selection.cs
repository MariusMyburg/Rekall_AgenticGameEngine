using System.Text.Json.Nodes;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed partial class RekallAgeMeshOperationExecutor
{
    private static RekallAgeMeshOperationResult SelectEdgesByAngle(
        RekallAgeMeshAsset source,
        RekallAgeMeshOperationRequest request)
    {
        RequireDomain(request, RekallAgeGeometryDomain.Edge);
        var candidateIndices = ResolveIndices(source.Topology.EdgeIds, request.ElementIds, "edge");
        var selectionName = ReadBoundedString(request.Parameters, "name", "angle-edges");
        var minimum = ReadFiniteDouble(request.Parameters, "minimumAngleDegrees", 30);
        var maximum = ReadFiniteDouble(request.Parameters, "maximumAngleDegrees", 180);
        var includeBoundary = ReadBoolean(request.Parameters, "includeBoundary", false);
        if (minimum is < 0 or > 180 || maximum is < 0 or > 180 || minimum > maximum)
            throw Failure("REKALL_MESH_SELECTION_ANGLE_INVALID", "Edge angle bounds must satisfy 0 <= minimum <= maximum <= 180 degrees.");

        var edgeFaces = new Dictionary<int, List<int>>();
        for (var face = 0; face < source.Topology.FaceIds.Count; face++)
        {
            for (var corner = source.Topology.FaceOffsets[face]; corner < source.Topology.FaceOffsets[face + 1]; corner++)
            {
                var edge = source.Topology.CornerEdgeIndices[corner];
                if (!edgeFaces.TryGetValue(edge, out var faces))
                    edgeFaces[edge] = faces = [];
                if (!faces.Contains(face))
                    faces.Add(face);
            }
        }

        var selected = new List<ulong>();
        foreach (var edgeIndex in candidateIndices.Order())
        {
            var uses = edgeFaces.GetValueOrDefault(edgeIndex) ?? [];
            if (uses.Count == 1)
            {
                if (includeBoundary)
                    selected.Add(source.Topology.EdgeIds[edgeIndex]);
                continue;
            }
            if (uses.Count != 2)
                continue;
            var first = UvFaceNormal(source.Topology, uses[0]);
            var second = UvFaceNormal(source.Topology, uses[1]);
            var cosine = Math.Clamp(first.X * second.X + first.Y * second.Y + first.Z * second.Z, -1, 1);
            var angle = Math.Acos(cosine) * 180 / Math.PI;
            if (angle + 1e-9 >= minimum && angle - 1e-9 <= maximum)
                selected.Add(source.Topology.EdgeIds[edgeIndex]);
        }

        var selections = source.SelectionSets
            .Where(item => !item.Name.Equals(selectionName, StringComparison.Ordinal))
            .Append(new RekallAgeMeshSelection(selectionName, RekallAgeGeometryDomain.Edge, selected))
            .OrderBy(item => item.Name, StringComparer.Ordinal)
            .ToArray();
        var mesh = source with
        {
            Revision = checked(source.Revision + 1),
            SelectionSets = selections
        };
        return Result(
            source,
            mesh,
            ChangeSet(
                RekallAgeMeshChangeKind.Selection,
                modifiedEdges: selected,
                affectedBounds: Bounds(source.Topology.Positions)),
            request.ElementIds.Order().Select(id => Preserve(RekallAgeGeometryDomain.Edge, id)).ToArray());
    }
}
