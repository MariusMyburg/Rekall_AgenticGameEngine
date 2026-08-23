using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed partial class RekallAgeModelingGraphEvaluator
{
    private static RekallAgeMeshAsset CreateTorus(
        RekallAgeModelingGraphAsset graph,
        RekallAgeModelingGraphNode node)
    {
        var majorRadius = ReadPositive(node, "majorRadius", 1);
        var minorRadius = ReadPositive(node, "minorRadius", 0.25);
        if (minorRadius >= majorRadius)
            throw new EvaluationException("REKALL_MODELING_EVALUATION_PARAMETER_INVALID", "Torus minor radius must be less than its major radius to produce a regular non-self-intersecting surface.", node.NodeId);
        var majorSegments = ReadInteger(node, "majorSegments", 24, 3, 4_096);
        var minorSegments = ReadInteger(node, "minorSegments", 12, 3, 4_096);
        var pointCount = checked(majorSegments * minorSegments);
        if (pointCount > 2_000_000)
            throw new EvaluationException("REKALL_MODELING_EVALUATION_ELEMENT_BUDGET_EXCEEDED", "Torus parameters exceed the hard element ceiling.", node.NodeId);

        var positions = new RekallAgeGeometryVector3[pointCount];
        for (var major = 0; major < majorSegments; major++)
        for (var minor = 0; minor < minorSegments; minor++)
        {
            var u = major * Math.PI * 2 / majorSegments;
            var v = minor * Math.PI * 2 / minorSegments;
            var ringRadius = majorRadius + minorRadius * Math.Cos(v);
            positions[Point(major, minor)] = new(
                ringRadius * Math.Cos(u),
                minorRadius * Math.Sin(v),
                ringRadius * Math.Sin(u));
        }

        var faces = new List<int[]>(pointCount);
        for (var major = 0; major < majorSegments; major++)
        for (var minor = 0; minor < minorSegments; minor++)
        {
            var nextMajor = (major + 1) % majorSegments;
            var nextMinor = (minor + 1) % minorSegments;
            faces.Add([
                Point(major, minor),
                Point(major, nextMinor),
                Point(nextMajor, nextMinor),
                Point(nextMajor, minor)]);
        }

        var edgeIndices = new Dictionary<(int A, int B), int>();
        var edges = new List<RekallAgeMeshEdgePointIndices>(pointCount * 2);
        var cornerPoints = new List<int>(pointCount * 4);
        var cornerEdges = new List<int>(pointCount * 4);
        var faceOffsets = new List<int>(pointCount + 1) { 0 };
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
            Enumerable.Range(1, pointCount).Select(value => (ulong)value).ToArray(),
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
            throw new EvaluationException("REKALL_MODELING_EVALUATION_OUTPUT_INVALID", "Torus evaluator produced invalid topology.", node.NodeId);
        return mesh;

        int Point(int major, int minor) => major * minorSegments + minor;
    }
}
