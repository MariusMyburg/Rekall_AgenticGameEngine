using Csg = CSG.Sharp;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

/// <summary>
/// Voronoi-style mesh fracture: splits a closed manifold source mesh into N convex-ish chunks
/// around random seed points. Reuses the existing CSG.Sharp intersect kernel entirely - a chunk
/// is the source mesh intersected, once per other seed, against a large oriented half-space slab
/// (a thin box whose near face lies on the perpendicular bisector plane between the two seeds).
/// No new geometry kernel and no polytope math: fracture is "build slab meshes, call the same
/// Csg.CSG.Intersect the boolean node already uses, N-1 times per chunk."
/// </summary>
public static class RekallAgeMeshFracture
{
    /// <summary>
    /// Bounded number of deterministic reseed attempts before giving up. CSG.Sharp's BSP
    /// construction can throw (observed as a NullReferenceException from Node.Invert, but the
    /// exact failure mode is a third-party implementation detail we don't control the source of)
    /// when a seed pair's perpendicular-bisector cutting plane is numerically degenerate against
    /// the source mesh's specific geometry - found while authoring a real example game, where
    /// some seeds on an otherwise perfectly valid sphere crashed and others didn't. Retrying with
    /// a different, but deterministically-derived, seed keeps the function pure (the same input
    /// seed always retries through the same sequence and lands on the same result) while turning
    /// an occasional unhandled crash into a successful, still-reproducible fracture.
    /// </summary>
    private const int MaximumReseedAttempts = 8;

    public static IReadOnlyList<RekallAgeMeshAsset> Fracture(RekallAgeMeshAsset source, int chunkCount, long seed)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (chunkCount < 2 || chunkCount > 64)
            throw new ArgumentOutOfRangeException(nameof(chunkCount), "Fracture chunk count must be 2-64.");
        var validation = new RekallAgeMeshValidator().Validate(source);
        if (!validation.IsValid || validation.Summary.BoundaryEdgeCount != 0 || validation.Summary.NonManifoldEdgeCount != 0 || validation.Summary.FaceCount == 0)
            throw new ArgumentException("Fracture source must be a non-empty, closed manifold mesh.", nameof(source));

        for (var attempt = 0; attempt < MaximumReseedAttempts; attempt++)
        {
            try
            {
                return FractureOnce(source, chunkCount, DeriveAttemptSeed(seed, attempt));
            }
            catch (Exception error) when (error is NullReferenceException or InvalidOperationException or IndexOutOfRangeException)
            {
                // A degenerate cutting plane for this particular seed; try the next deterministic
                // reseed rather than surfacing a raw third-party crash.
            }
        }

        throw new InvalidOperationException(
            $"Could not fracture mesh '{source.AssetId}' into {chunkCount} chunks after {MaximumReseedAttempts} deterministic reseed attempts starting from seed {seed}. " +
            "The source geometry may be producing degenerate cutting planes for every attempted seed point set; try a different seed or chunk count.");
    }

    /// <summary>Combines the caller's seed with a retry attempt index into one deterministic
    /// 32-bit seed, so a given (seed, attempt) pair always produces the same result.</summary>
    private static int DeriveAttemptSeed(long seed, int attempt) =>
        unchecked((int)((ulong)seed * 0x9E3779B97F4A7C15UL + (ulong)attempt * 0xBF58476D1CE4E5B9UL));

    private static IReadOnlyList<RekallAgeMeshAsset> FractureOnce(RekallAgeMeshAsset source, int chunkCount, int effectiveSeed)
    {
        var (min, max) = ComputeBounds(source.Topology.Positions);
        var random = new Random(effectiveSeed);
        var seeds = Enumerable.Range(0, chunkCount)
            .Select(_ => new RekallAgeGeometryVector3(
                Lerp(min.X, max.X, random.NextDouble()),
                Lerp(min.Y, max.Y, random.NextDouble()),
                Lerp(min.Z, max.Z, random.NextDouble())))
            .ToArray();

        var sourceCsg = RekallAgeMeshCsgKernel.ToCsg(source, "source");
        var span = Math.Max(max.X - min.X, Math.Max(max.Y - min.Y, max.Z - min.Z));
        var slabHalfExtent = Math.Max(span * 4, 1);

        var chunks = new List<RekallAgeMeshAsset>(chunkCount);
        for (var i = 0; i < seeds.Length; i++)
        {
            var cell = sourceCsg;
            for (var j = 0; j < seeds.Length; j++)
            {
                if (i == j) continue;
                var slab = HalfSpaceSlab(seeds[i], seeds[j], slabHalfExtent);
                cell = cell.Intersect(slab);
            }
            chunks.Add(RekallAgeMeshCsgKernel.FromPolygonsToMesh(cell, $"{source.AssetId}-chunk-{i}", $"{source.Name} Chunk {i}"));
        }
        return chunks;
    }

    /// <summary>
    /// A thin box, in CSG form, whose near face lies on the perpendicular bisector plane between
    /// <paramref name="keep"/> and <paramref name="other"/>, oriented so intersecting the source
    /// mesh against it keeps only the half of space closer to <paramref name="keep"/>.
    /// </summary>
    private static Csg.CSG HalfSpaceSlab(RekallAgeGeometryVector3 keep, RekallAgeGeometryVector3 other, double halfExtent)
    {
        var midpoint = new RekallAgeGeometryVector3((keep.X + other.X) / 2, (keep.Y + other.Y) / 2, (keep.Z + other.Z) / 2);
        var normal = RekallAgeMeshCsgKernel.Unit(RekallAgeMeshCsgKernel.Subtract(keep, other));
        var (tangent, bitangent) = OrthonormalBasis(normal);
        // The box's center sits `halfExtent` behind the bisector plane along -normal, so its near
        // face (at +halfExtent along normal from the box center) lands exactly on the plane, and
        // the whole box extends `2 * halfExtent` further toward `keep`.
        var center = new RekallAgeGeometryVector3(
            midpoint.X - normal.X * halfExtent,
            midpoint.Y - normal.Y * halfExtent,
            midpoint.Z - normal.Z * halfExtent);
        var boxMesh = BuildOrientedBox(center, normal, tangent, bitangent, halfExtent, halfExtent * 2, halfExtent * 2);
        return RekallAgeMeshCsgKernel.ToCsg(boxMesh, "slab");
    }

    /// <summary>Any two unit vectors orthogonal to <paramref name="normal"/> and to each other.</summary>
    private static (RekallAgeGeometryVector3 Tangent, RekallAgeGeometryVector3 Bitangent) OrthonormalBasis(RekallAgeGeometryVector3 normal)
    {
        var reference = Math.Abs(normal.X) < 0.9 ? new RekallAgeGeometryVector3(1, 0, 0) : new RekallAgeGeometryVector3(0, 1, 0);
        var tangent = RekallAgeMeshCsgKernel.Unit(RekallAgeMeshCsgKernel.Cross(reference, normal));
        var bitangent = RekallAgeMeshCsgKernel.Cross(normal, tangent);
        return (tangent, bitangent);
    }

    /// <summary>
    /// A box mesh centered at <paramref name="center"/>, with half-extent <paramref name="halfU"/>
    /// along <paramref name="u"/> and <paramref name="halfV"/>/<paramref name="halfW"/> along the
    /// two axes orthogonal to it, faces wound outward (counter-clockwise viewed from outside).
    /// </summary>
    private static RekallAgeMeshAsset BuildOrientedBox(
        RekallAgeGeometryVector3 center,
        RekallAgeGeometryVector3 u,
        RekallAgeGeometryVector3 v,
        RekallAgeGeometryVector3 w,
        double halfU,
        double halfV,
        double halfW)
    {
        RekallAgeGeometryVector3 Corner(double su, double sv, double sw) => new(
            center.X + u.X * su * halfU + v.X * sv * halfV + w.X * sw * halfW,
            center.Y + u.Y * su * halfU + v.Y * sv * halfV + w.Y * sw * halfW,
            center.Z + u.Z * su * halfU + v.Z * sv * halfV + w.Z * sw * halfW);

        // Local-space corner indices, axes (u,v,w) playing the role of (x,y,z):
        // 0:(-,-,-) 1:(+,-,-) 2:(+,+,-) 3:(-,+,-) 4:(-,-,+) 5:(+,-,+) 6:(+,+,+) 7:(-,+,+)
        var positions = new[]
        {
            Corner(-1, -1, -1), Corner(1, -1, -1), Corner(1, 1, -1), Corner(-1, 1, -1),
            Corner(-1, -1, 1), Corner(1, -1, 1), Corner(1, 1, 1), Corner(-1, 1, 1)
        };
        int[][] faces =
        [
            [0, 3, 2, 1], // -w face
            [4, 5, 6, 7], // +w face
            [0, 1, 5, 4], // -v face
            [3, 7, 6, 2], // +v face
            [0, 4, 7, 3], // -u face
            [1, 2, 6, 5]  // +u face
        ];

        var edgeMap = new Dictionary<(int A, int B), int>();
        var edges = new List<RekallAgeMeshEdgePointIndices>();
        var cornerPoints = new List<int>();
        var cornerEdges = new List<int>();
        var faceOffsets = new List<int> { 0 };
        foreach (var face in faces)
        {
            for (var corner = 0; corner < face.Length; corner++)
            {
                var a = face[corner]; var b = face[(corner + 1) % face.Length];
                var key = a < b ? (a, b) : (b, a);
                if (!edgeMap.TryGetValue(key, out var edge))
                {
                    edge = edges.Count; edgeMap[key] = edge; edges.Add(new(a, b));
                }
                cornerPoints.Add(a); cornerEdges.Add(edge);
            }
            faceOffsets.Add(cornerPoints.Count);
        }
        var topology = new RekallAgeMeshTopology(
            Enumerable.Range(1, positions.Length).Select(value => (ulong)value).ToArray(), positions,
            Enumerable.Range(1, edges.Count).Select(value => (ulong)(100 + value)).ToArray(), edges,
            Enumerable.Range(1, faces.Length).Select(value => (ulong)(200 + value)).ToArray(), faceOffsets,
            Enumerable.Range(1, cornerPoints.Count).Select(value => (ulong)(300 + value)).ToArray(), cornerPoints, cornerEdges);
        return RekallAgeMeshAsset.Create("fracture-slab", "Fracture Slab", topology);
    }

    private static (RekallAgeGeometryVector3 Min, RekallAgeGeometryVector3 Max) ComputeBounds(IReadOnlyList<RekallAgeGeometryVector3> positions)
    {
        var min = positions[0];
        var max = positions[0];
        foreach (var position in positions)
        {
            min = new(Math.Min(min.X, position.X), Math.Min(min.Y, position.Y), Math.Min(min.Z, position.Z));
            max = new(Math.Max(max.X, position.X), Math.Max(max.Y, position.Y), Math.Max(max.Z, position.Z));
        }
        return (min, max);
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
