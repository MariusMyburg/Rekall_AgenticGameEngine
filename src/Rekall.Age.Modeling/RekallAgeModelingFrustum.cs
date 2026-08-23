using System.Text.Json.Nodes;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed partial class RekallAgeModelingGraphEvaluator
{
    private static RekallAgeMeshAsset CreateFrustum(
        RekallAgeModelingGraphAsset graph,
        RekallAgeModelingGraphNode node)
    {
        var radiusBottom = ReadNonNegative(node, "radiusBottom", 0.5);
        var radiusTop = ReadNonNegative(node, "radiusTop", 0.5);
        var depth = ReadPositive(node, "depth", 1);
        var segments = ReadInteger(node, "segments", 16, 3, 4_096);
        var capBottom = ReadBoolean(node, "capBottom", true);
        var capTop = ReadBoolean(node, "capTop", true);
        if (radiusBottom == 0 && radiusTop == 0)
            throw new EvaluationException("REKALL_MODELING_EVALUATION_PARAMETER_INVALID", "At least one frustum radius must be greater than zero.", node.NodeId);

        var positions = new List<RekallAgeGeometryVector3>(segments * 2);
        var bottom = AddRing(radiusBottom, -depth / 2);
        var top = AddRing(radiusTop, depth / 2);
        var faces = new List<int[]>(segments + 2);
        for (var index = 0; index < segments; index++)
        {
            var next = (index + 1) % segments;
            if (radiusBottom == 0)
                faces.Add([bottom[0], top[index], top[next]]);
            else if (radiusTop == 0)
                faces.Add([bottom[index], top[0], bottom[next]]);
            else
                faces.Add([bottom[index], top[index], top[next], bottom[next]]);
        }
        if (capBottom && radiusBottom > 0) faces.Add(bottom.ToArray());
        if (capTop && radiusTop > 0) faces.Add(top.Reverse().ToArray());

        var edgeIndices = new Dictionary<(int A, int B), int>();
        var edges = new List<RekallAgeMeshEdgePointIndices>();
        var cornerPoints = new List<int>();
        var cornerEdges = new List<int>();
        var faceOffsets = new List<int> { 0 };
        foreach (var face in faces)
        {
            for (var corner = 0; corner < face.Length; corner++)
            {
                var a = face[corner];
                var b = face[(corner + 1) % face.Length];
                var key = a < b ? (a, b) : (b, a);
                if (!edgeIndices.TryGetValue(key, out var edgeIndex))
                {
                    edgeIndex = edges.Count;
                    edgeIndices[key] = edgeIndex;
                    edges.Add(new(a, b));
                }
                cornerPoints.Add(a);
                cornerEdges.Add(edgeIndex);
            }
            faceOffsets.Add(cornerPoints.Count);
        }

        var topology = new RekallAgeMeshTopology(
            Enumerable.Range(1, positions.Count).Select(value => (ulong)value).ToArray(),
            positions,
            Enumerable.Range(1, edges.Count).Select(value => (ulong)(10_000 + value)).ToArray(),
            edges,
            Enumerable.Range(1, faces.Count).Select(value => (ulong)(20_000 + value)).ToArray(),
            faceOffsets,
            Enumerable.Range(1, cornerPoints.Count).Select(value => (ulong)(30_000 + value)).ToArray(),
            cornerPoints,
            cornerEdges);
        var mesh = RekallAgeMeshAsset.Create($"{graph.AssetId}.{node.NodeId}", node.NodeId, topology) with { Revision = graph.Revision };
        var validation = new RekallAgeMeshValidator().Validate(mesh);
        if (!validation.IsValid)
            throw new EvaluationException("REKALL_MODELING_EVALUATION_OUTPUT_INVALID", "Frustum evaluator produced invalid topology.", node.NodeId);
        return mesh;

        IReadOnlyList<int> AddRing(double radius, double y)
        {
            if (radius == 0)
            {
                positions.Add(new(0, y, 0));
                return [positions.Count - 1];
            }
            var result = new int[segments];
            for (var index = 0; index < segments; index++)
            {
                var radians = index * Math.PI * 2 / segments;
                result[index] = positions.Count;
                positions.Add(new(radius * Math.Cos(radians), y, radius * Math.Sin(radians)));
            }
            return result;
        }
    }

    private static double ReadNonNegative(RekallAgeModelingGraphNode node, string name, double fallback)
    {
        var value = node.Parameters[name] is JsonValue json && json.TryGetValue<double>(out var number) ? number : fallback;
        if (!double.IsFinite(value) || value < 0)
            throw new EvaluationException("REKALL_MODELING_EVALUATION_PARAMETER_INVALID", $"Parameter '{name}' must be non-negative and finite.", node.NodeId);
        return value;
    }

    private static bool ReadBoolean(RekallAgeModelingGraphNode node, string name, bool fallback)
    {
        if (node.Parameters[name] is null) return fallback;
        if (node.Parameters[name] is JsonValue json && json.TryGetValue<bool>(out var value)) return value;
        throw new EvaluationException("REKALL_MODELING_EVALUATION_PARAMETER_INVALID", $"Parameter '{name}' must be Boolean.", node.NodeId);
    }
}
