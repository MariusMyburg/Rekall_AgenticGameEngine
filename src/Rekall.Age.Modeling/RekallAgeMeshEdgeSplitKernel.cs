using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public static class RekallAgeMeshEdgeSplitKernel
{
    public static IReadOnlyList<ulong> ResolveQuadRing(RekallAgeMeshAsset mesh, ulong seedEdgeId)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        var topology = mesh.Topology;
        var seedIndex = topology.EdgeIds.ToList().IndexOf(seedEdgeId);
        if (seedIndex < 0)
            throw new RekallAgeMeshOperationException("REKALL_MESH_OPERATION_SELECTION_INVALID", $"Selected edge ID '{seedEdgeId}' does not exist.");

        var edgeFaces = Enumerable.Range(0, topology.EdgeIds.Count).ToDictionary(index => index, _ => new List<int>());
        for (var face = 0; face < topology.FaceIds.Count; face++)
        {
            var cornerCount = topology.FaceOffsets[face + 1] - topology.FaceOffsets[face];
            if (cornerCount != 4) continue;
            for (var corner = topology.FaceOffsets[face]; corner < topology.FaceOffsets[face + 1]; corner++)
                edgeFaces[topology.CornerEdgeIndices[corner]].Add(face);
        }

        var ring = new HashSet<int> { seedIndex };
        var pending = new Queue<int>();
        pending.Enqueue(seedIndex);
        while (pending.Count > 0)
        {
            var edge = pending.Dequeue();
            foreach (var face in edgeFaces[edge].Order())
            {
                var start = topology.FaceOffsets[face];
                var local = Enumerable.Range(0, 4).Single(index => topology.CornerEdgeIndices[start + index] == edge);
                var opposite = topology.CornerEdgeIndices[start + ((local + 2) % 4)];
                if (ring.Add(opposite)) pending.Enqueue(opposite);
            }
        }

        return ring.Select(index => topology.EdgeIds[index]).Order().ToArray();
    }

    internal static RekallAgeGeometryVector3 Interpolate(
        RekallAgeGeometryVector3 first,
        RekallAgeGeometryVector3 second,
        double factor) => new(
            first.X + (second.X - first.X) * factor,
            first.Y + (second.Y - first.Y) * factor,
            first.Z + (second.Z - first.Z) * factor);
}
