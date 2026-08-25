using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed record RekallAgeUvIsland(
    int Index,
    IReadOnlyList<ulong> FaceIds,
    IReadOnlyList<ulong> CornerIds);

public sealed class RekallAgeUvIslandInspector
{
    public IReadOnlyList<RekallAgeUvIsland> Inspect(
        RekallAgeMeshAsset mesh,
        string seamAttribute = "uv.seam")
    {
        ArgumentNullException.ThrowIfNull(mesh);
        var validation = new RekallAgeMeshValidator().Validate(mesh);
        if (!validation.IsValid) throw new InvalidDataException("Cannot inspect UV islands on an invalid mesh.");

        var topology = mesh.Topology;
        var seam = mesh.Attributes.FirstOrDefault(item => item.Name == seamAttribute);
        if (seam is not null && (seam.Domain != RekallAgeGeometryDomain.Edge || seam.ValueType != RekallAgeGeometryValueType.Bool))
            throw new InvalidDataException($"UV seam attribute '{seamAttribute}' must be an edge-domain Bool attribute.");
        var seamEdges = seam is null
            ? new HashSet<int>()
            : seam.Values.Select((value, index) => (value, index)).Where(item => item.value.GetBoolean()).Select(item => item.index).ToHashSet();

        var edgeFaces = Enumerable.Range(0, topology.EdgeIds.Count).Select(_ => new List<int>()).ToArray();
        for (var face = 0; face < topology.FaceIds.Count; face++)
            for (var corner = topology.FaceOffsets[face]; corner < topology.FaceOffsets[face + 1]; corner++)
                edgeFaces[topology.CornerEdgeIndices[corner]].Add(face);

        var visited = new bool[topology.FaceIds.Count];
        var islands = new List<RekallAgeUvIsland>();
        foreach (var seed in Enumerable.Range(0, topology.FaceIds.Count).OrderBy(index => topology.FaceIds[index]))
        {
            if (visited[seed]) continue;
            var queue = new Queue<int>();
            var faces = new List<int>();
            visited[seed] = true;
            queue.Enqueue(seed);
            while (queue.Count > 0)
            {
                var face = queue.Dequeue();
                faces.Add(face);
                for (var corner = topology.FaceOffsets[face]; corner < topology.FaceOffsets[face + 1]; corner++)
                {
                    var edge = topology.CornerEdgeIndices[corner];
                    if (seamEdges.Contains(edge)) continue;
                    foreach (var neighbor in edgeFaces[edge].OrderBy(index => topology.FaceIds[index]))
                    {
                        if (neighbor == face || visited[neighbor]) continue;
                        visited[neighbor] = true;
                        queue.Enqueue(neighbor);
                    }
                }
            }
            faces.Sort((a, b) => topology.FaceIds[a].CompareTo(topology.FaceIds[b]));
            var corners = faces.SelectMany(face => Enumerable.Range(topology.FaceOffsets[face], topology.FaceOffsets[face + 1] - topology.FaceOffsets[face]))
                .Select(index => topology.CornerIds[index]).Order().ToArray();
            islands.Add(new(islands.Count, faces.Select(index => topology.FaceIds[index]).ToArray(), corners));
        }
        return islands;
    }
}
