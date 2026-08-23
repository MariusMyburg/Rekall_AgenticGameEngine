using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed partial class RekallAgeModelingGraphEvaluator
{
    private static RekallAgeMeshAsset CreateSphere(
        RekallAgeModelingGraphAsset graph,
        RekallAgeModelingGraphNode node)
    {
        var radius = ReadPositive(node, "radius", 0.5);
        var segments = ReadInteger(node, "segments", 16, 3, 4_096);
        var rings = ReadInteger(node, "rings", 8, 2, 4_096);
        var pointCount = checked(2 + segments * (rings - 1));
        var faceCount = checked(segments * 2 + segments * Math.Max(0, rings - 2));
        if (pointCount > 2_000_000 || faceCount > 2_000_000)
            throw new EvaluationException("REKALL_MODELING_EVALUATION_ELEMENT_BUDGET_EXCEEDED", "Sphere parameters exceed the hard element ceiling.", node.NodeId);

        var positions = new List<RekallAgeGeometryVector3>(pointCount) { new(0, radius, 0) };
        for (var ring = 1; ring < rings; ring++)
        {
            var theta = Math.PI * ring / rings;
            for (var segment = 0; segment < segments; segment++)
            {
                var phi = Math.PI * 2 * segment / segments;
                positions.Add(new(
                    radius * Math.Sin(theta) * Math.Cos(phi),
                    radius * Math.Cos(theta),
                    radius * Math.Sin(theta) * Math.Sin(phi)));
            }
        }
        var bottom = positions.Count;
        positions.Add(new(0, -radius, 0));

        var faces = new List<int[]>(faceCount);
        for (var segment = 0; segment < segments; segment++)
        {
            var next = (segment + 1) % segments;
            faces.Add([0, Point(0, next), Point(0, segment)]);
        }
        for (var ring = 0; ring < rings - 2; ring++)
        for (var segment = 0; segment < segments; segment++)
        {
            var next = (segment + 1) % segments;
            faces.Add([Point(ring, segment), Point(ring, next), Point(ring + 1, next), Point(ring + 1, segment)]);
        }
        var lastRing = rings - 2;
        for (var segment = 0; segment < segments; segment++)
        {
            var next = (segment + 1) % segments;
            faces.Add([Point(lastRing, segment), Point(lastRing, next), bottom]);
        }

        var edgeIndices = new Dictionary<(int A, int B), int>();
        var edges = new List<RekallAgeMeshEdgePointIndices>();
        var cornerPoints = new List<int>();
        var cornerEdges = new List<int>();
        var faceOffsets = new List<int>(faces.Count + 1) { 0 };
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
        if (!validation.IsValid || validation.Summary.BoundaryEdgeCount != 0 || validation.Summary.NonManifoldEdgeCount != 0)
            throw new EvaluationException("REKALL_MODELING_EVALUATION_OUTPUT_INVALID", "Sphere evaluator produced invalid or open topology.", node.NodeId);
        return mesh;

        int Point(int ring, int segment) => 1 + ring * segments + segment;
    }
}
