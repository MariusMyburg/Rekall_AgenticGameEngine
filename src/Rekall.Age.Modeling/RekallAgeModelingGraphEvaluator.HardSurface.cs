using System.Text.Json.Nodes;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed partial class RekallAgeModelingGraphEvaluator
{
    internal static RekallAgeMeshAsset ExecuteMirrorModifier(RekallAgeMeshAsset source, JsonObject parameters) =>
        ExecuteHardSurfaceModifier(source, "rekall.modeling.mirror", parameters, MirrorGeometry);

    internal static RekallAgeMeshAsset ExecuteArrayModifier(RekallAgeMeshAsset source, JsonObject parameters) =>
        ExecuteHardSurfaceModifier(source, "rekall.modeling.array", parameters, ArrayGeometry);

    private static RekallAgeMeshAsset ExecuteHardSurfaceModifier(
        RekallAgeMeshAsset source,
        string typeId,
        JsonObject parameters,
        Func<RekallAgeModelingGraphAsset, RekallAgeModelingGraphNode, NodeValue, NodeValue> operation)
    {
        var graph = new RekallAgeModelingGraphAsset(1, $"modifier.{source.AssetId}", "Modifier execution", source.Revision, [], [], [], []);
        var node = new RekallAgeModelingGraphNode("modifier", typeId, 1, parameters);
        try { return operation(graph, node, new(source)).Mesh!; }
        catch (EvaluationException error) { throw new RekallAgeMeshOperationException(error.Code, error.Message); }
    }

    private static NodeValue MirrorGeometry(RekallAgeModelingGraphAsset graph, RekallAgeModelingGraphNode node, NodeValue input)
    {
        var source = input.Mesh ?? throw new EvaluationException("REKALL_MODELING_EVALUATION_INPUT_TYPE_INVALID", "Mirror requires geometry input.", node.NodeId);
        var axis = ReadString(node, "axis", "x").ToLowerInvariant();
        var origin = ReadNumber(node, "origin", 0);
        var mergeDistance = ReadNumber(node, "mergeDistance", 0);
        if (axis is not ("x" or "y" or "z") || mergeDistance < 0)
            throw new EvaluationException("REKALL_MODELING_EVALUATION_PARAMETER_INVALID", "Mirror axis must be x/y/z and mergeDistance must be non-negative.", node.NodeId);
        if (ReadBoolean(node, "bisect", false))
            throw new EvaluationException("REKALL_MODELING_MIRROR_BISECT_UNSUPPORTED", "Mirror bisect is not yet implemented; author a half mesh or disable bisect.", node.NodeId);
        var mirroredPositions = source.Topology.Positions.Select(point => axis switch
        {
            "x" => point with { X = 2 * origin - point.X },
            "y" => point with { Y = 2 * origin - point.Y },
            _ => point with { Z = 2 * origin - point.Z }
        }).ToArray();
        var mirrored = source with { Topology = source.Topology with { Positions = mirroredPositions } };
        mirrored = new RekallAgeMeshOperationExecutor().Execute(mirrored, new("reverse_faces", RekallAgeGeometryDomain.Face, mirrored.Topology.FaceIds, new())).Mesh;
        var joined = JoinMeshes(graph, node, [source, mirrored]);
        if (mergeDistance <= 0) return joined;
        var mesh = joined.Mesh!;
        var welded = new RekallAgeMeshOperationExecutor().Execute(mesh, new("merge_by_distance", RekallAgeGeometryDomain.Point, mesh.Topology.PointIds, new() { ["distance"] = mergeDistance })).Mesh;
        return new(welded with { AssetId = $"{graph.AssetId}.{node.NodeId}", Name = node.NodeId, Revision = graph.Revision });
    }

    private static NodeValue ArrayGeometry(RekallAgeModelingGraphAsset graph, RekallAgeModelingGraphNode node, NodeValue input)
    {
        var source = input.Mesh ?? throw new EvaluationException("REKALL_MODELING_EVALUATION_INPUT_TYPE_INVALID", "Array requires geometry input.", node.NodeId);
        var count = ReadInteger(node, "count", 2, 1, 4_096);
        var offset = ReadVector3(node, "offset", new(1, 0, 0));
        if (ReadBoolean(node, "instanceMode", false))
            throw new EvaluationException("REKALL_MODELING_ARRAY_INSTANCE_UNSUPPORTED", "Linked instance output requires the forthcoming instance-geometry contract; use realized array mode.", node.NodeId);
        if (ReadBoolean(node, "relativeOffset", false))
        {
            var min = new RekallAgeGeometryVector3(source.Topology.Positions.Min(point => point.X), source.Topology.Positions.Min(point => point.Y), source.Topology.Positions.Min(point => point.Z));
            var max = new RekallAgeGeometryVector3(source.Topology.Positions.Max(point => point.X), source.Topology.Positions.Max(point => point.Y), source.Topology.Positions.Max(point => point.Z));
            offset = new(offset.X * (max.X - min.X), offset.Y * (max.Y - min.Y), offset.Z * (max.Z - min.Z));
        }
        var copies = Enumerable.Range(0, count).Select(index => TransformMesh(
            graph, node, source,
            new(offset.X * index, offset.Y * index, offset.Z * index),
            new(0, 0, 0), new(1, 1, 1), $"{node.NodeId}-{index}")).ToArray();
        return JoinMeshes(graph, node, copies);
    }
}
