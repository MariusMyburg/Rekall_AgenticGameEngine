using System.Text.Json;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed partial class RekallAgeMeshOperationExecutor
{
    private static RekallAgeMeshOperationResult MarkUvSeams(RekallAgeMeshAsset source, RekallAgeMeshOperationRequest request)
    {
        RequireDomain(request, RekallAgeGeometryDomain.Edge);
        var edgeIndices = ResolveIndices(source.Topology.EdgeIds, request.ElementIds, "edge");
        var attributeName = ReadBoundedString(request.Parameters, "attribute", "uv.seam");
        var marked = ReadBoolean(request.Parameters, "marked", true);
        var existing = source.Attributes.FirstOrDefault(item => item.Name == attributeName);
        if (existing is not null && (existing.Domain != RekallAgeGeometryDomain.Edge || existing.ValueType != RekallAgeGeometryValueType.Bool))
            throw Failure("REKALL_MESH_OPERATION_ATTRIBUTE_CONFLICT", $"Attribute '{attributeName}' exists with an incompatible domain or type.");
        var values = existing?.Values.ToArray() ?? Enumerable.Repeat(JsonSerializer.SerializeToElement(false), source.Topology.EdgeIds.Count).ToArray();
        foreach (var index in edgeIndices) values[index] = JsonSerializer.SerializeToElement(marked);
        var attribute = new RekallAgeGeometryAttribute(attributeName, RekallAgeGeometryDomain.Edge, RekallAgeGeometryValueType.Bool, values, "uv-seam", RekallAgeGeometryInterpolation.Nearest);
        var mesh = source with
        {
            Revision = checked(source.Revision + 1),
            Attributes = source.Attributes.Where(item => item.Name != attributeName).Append(attribute).OrderBy(item => item.Name, StringComparer.Ordinal).ToArray()
        };
        var ids = request.ElementIds.Order().ToArray();
        return Result(source, mesh, ChangeSet(RekallAgeMeshChangeKind.Attributes, modifiedEdges: ids, changedAttributes: [attributeName]), ids.Select(id => Preserve(RekallAgeGeometryDomain.Edge, id)).ToArray());
    }

    private static RekallAgeMeshOperationResult UnwrapPackUv(RekallAgeMeshAsset source, RekallAgeMeshOperationRequest request)
    {
        RequireDomain(request, RekallAgeGeometryDomain.Face);
        var selected = ResolveIndices(source.Topology.FaceIds, request.ElementIds, "face").ToHashSet();
        if (selected.Count != source.Topology.FaceIds.Count)
            throw Failure("REKALL_MESH_OPERATION_SELECTION_UNSUPPORTED", "UV unwrap currently requires the complete face set so chart boundaries remain explicit and deterministic.");
        var attributeName = ReadBoundedString(request.Parameters, "attribute", "uv.lightmap");
        var seamAttribute = ReadBoundedString(request.Parameters, "seamAttribute", "uv.seam");
        var semantic = ReadBoundedString(request.Parameters, "semantic", "texcoord-1");
        var margin = ReadFiniteDouble(request.Parameters, "margin", 0.01);
        if (margin < 0 || margin >= 0.25) throw Failure("REKALL_MESH_OPERATION_PARAMETER_INVALID", "UV pack margin must be finite and in [0, 0.25).");
        var existing = source.Attributes.FirstOrDefault(item => item.Name == attributeName);
        if (existing is not null && (existing.Domain != RekallAgeGeometryDomain.Corner || existing.ValueType != RekallAgeGeometryValueType.Float2))
            throw Failure("REKALL_MESH_OPERATION_ATTRIBUTE_CONFLICT", $"Attribute '{attributeName}' exists with an incompatible domain or type.");

        var topology = source.Topology;
        var faceIndexById = topology.FaceIds.Select((id, index) => (id, index)).ToDictionary(item => item.id, item => item.index);
        var cornerIndexById = topology.CornerIds.Select((id, index) => (id, index)).ToDictionary(item => item.id, item => item.index);
        var islands = new RekallAgeUvIslandInspector().Inspect(source, seamAttribute);
        var raw = new Dictionary<int, RekallAgeGeometryVector2>();
        var islandCorners = new List<IReadOnlyList<int>>(islands.Count);
        foreach (var island in islands)
        {
            var faceIndices = island.FaceIds.Select(id => faceIndexById[id]).ToArray();
            var aggregate = faceIndices.Select(index => UvFaceNormal(topology, index)).Aggregate(new RekallAgeGeometryVector3(0, 0, 0), (sum, normal) => new(sum.X + normal.X, sum.Y + normal.Y, sum.Z + normal.Z));
            var axis = RekallAgeUvProjection.DominantPlane(aggregate);
            var corners = island.CornerIds.Select(id => cornerIndexById[id]).Order().ToArray();
            foreach (var corner in corners) raw[corner] = RekallAgeUvProjection.Planar(topology.Positions[topology.CornerPointIndices[corner]], axis);
            islandCorners.Add(corners);
        }
        IReadOnlyDictionary<int, RekallAgeGeometryVector2> packed;
        try { packed = new RekallAgeUvPacker().Pack(raw, islandCorners, margin); }
        catch (InvalidDataException exception) { throw Failure("REKALL_MESH_OPERATION_UV_CHART_DEGENERATE", exception.Message); }
        var values = Enumerable.Range(0, topology.CornerIds.Count)
            .Select(index => { var uv = packed[index]; return JsonSerializer.SerializeToElement(new[] { uv.X, uv.Y }); }).ToArray();
        var attribute = new RekallAgeGeometryAttribute(attributeName, RekallAgeGeometryDomain.Corner, RekallAgeGeometryValueType.Float2, values, semantic);
        var mesh = source with
        {
            Revision = checked(source.Revision + 1),
            Attributes = source.Attributes.Where(item => item.Name != attributeName).Append(attribute).OrderBy(item => item.Name, StringComparer.Ordinal).ToArray()
        };
        var cornerIds = topology.CornerIds.Order().ToArray();
        var faceIds = topology.FaceIds.Order().ToArray();
        return Result(source, mesh, ChangeSet(RekallAgeMeshChangeKind.Attributes, modifiedFaces: faceIds, modifiedCorners: cornerIds, changedAttributes: [attributeName], affectedBounds: Bounds(topology.Positions)), faceIds.Select(id => Preserve(RekallAgeGeometryDomain.Face, id)).ToArray());
    }

    private static RekallAgeGeometryVector3 UvFaceNormal(RekallAgeMeshTopology topology, int faceIndex)
    {
        var start = topology.FaceOffsets[faceIndex]; var end = topology.FaceOffsets[faceIndex + 1];
        double x = 0, y = 0, z = 0;
        for (var corner = start; corner < end; corner++)
        {
            var next = corner + 1 == end ? start : corner + 1;
            var a = topology.Positions[topology.CornerPointIndices[corner]];
            var b = topology.Positions[topology.CornerPointIndices[next]];
            x += (a.Y - b.Y) * (a.Z + b.Z);
            y += (a.Z - b.Z) * (a.X + b.X);
            z += (a.X - b.X) * (a.Y + b.Y);
        }
        var length = Math.Sqrt(x * x + y * y + z * z);
        return length <= 1e-12 ? new(0, 0, 1) : new(x / length, y / length, z / length);
    }
}
