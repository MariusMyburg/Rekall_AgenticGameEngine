using System.Numerics;
using System.Text.Json.Nodes;
using Rekall.Age.Modules;

namespace Game.Modules.MidnightRiderRules;

/// <summary>
/// A recursive branching tree-mesh generator, implementing the same "tapered profile sweep
/// along a curve" mechanic docs/superpowers/specs/2026-08-27-procedural-tree-generation-
/// feasibility.md identified as the actual hard geometric problem - each branch is a tube whose
/// cross-section radius shrinks from base to tip, built directly here in C# rather than through
/// the full modeling-graph node system (rekall.modeling.curve.profile_sweep and friends), which
/// is the architecturally "proper" home for this per that spec but a much larger integration
/// than a first working version needs. This produces a single combined mesh (bark tube
/// triangles plus small foliage clusters at branch tips) as raw vertex/index arrays, authored
/// directly onto a <c>Rekall.GeometryMesh</c> component - a format the renderer already reads
/// with no asset-import step required (see RekallAgeRuntimeRenderFrameBuilder.ReadGeometryMesh).
///
/// Every random choice is drawn from <see cref="RekallAgeRuntimeModuleSdk.DeterministicUnit"/>/
/// <see cref="RekallAgeRuntimeModuleSdk.DeterministicRange"/> through <see cref="DeterministicRng"/>,
/// the same seeded, replay-stable randomness the rest of MidnightRiderRulesModule already uses
/// for hazard placement - two trees generated with the same seed and sequence are byte-identical.
/// </summary>
internal static class ProceduralTreeGenerator
{
    // Kept deliberately low-poly: the renderer re-derives each authored Rekall.GeometryMesh's
    // vertex/index JSON into a viewport renderable every single frame (RekallAgeRuntimeRenderFrameBuilder
    // has no cache for authored geometry, unlike physics's signature-gated joint/body rebuilds) -
    // measured directly, ~16 trees at the previous depth (3 generations, 6 radial segments) cost
    // ~16ms/frame in headless `runtime inspect` timing alone (300 frames: 8.3s with trees vs.
    // 3.46s without, both isolating dotnet startup overhead via a 5-frame baseline). A real fix
    // would cache the parsed mesh per entity in the render frame builder; this is the
    // content-side mitigation done instead, given render-pipeline caching is a larger, separate
    // engine change - see docs/production/PROGRESS.md for the measurement and the caching note.
    private const int TrunkGenerations = 2;
    private const int CenterlineSegments = 3;
    private const int RadialSegments = 5;
    private static readonly (double R, double G, double B) BarkColor = (0.30, 0.20, 0.12);
    private static readonly (double R, double G, double B) FoliageColorLight = (0.22, 0.42, 0.16);
    private static readonly (double R, double G, double B) FoliageColorDark = (0.14, 0.30, 0.11);

    public readonly record struct GeneratedMesh(JsonArray Vertices, JsonArray Indices);

    /// <summary>
    /// Generates one complete tree mesh, centered at the local origin (the caller positions it
    /// via the entity's own Transform3D) with the trunk growing along +Y.
    /// </summary>
    public static GeneratedMesh Generate(int seed, long sequenceBase)
    {
        var rng = new DeterministicRng(seed, sequenceBase);
        var builder = new MeshBuilder();
        var trunkHeight = 2.6 + rng.NextRange(0, 1.4);
        var trunkRadius = 0.16 + rng.NextRange(0, 0.08);
        GenerateBranch(builder, rng, Vector3.Zero, Vector3.UnitY, trunkHeight, trunkRadius, generation: 0);
        return new GeneratedMesh(builder.BuildVertices(), builder.BuildIndices());
    }

    private static void GenerateBranch(
        MeshBuilder mesh,
        DeterministicRng rng,
        Vector3 start,
        Vector3 direction,
        double length,
        double baseRadius,
        int generation)
    {
        var tipRadius = baseRadius * 0.55;
        var points = new Vector3[CenterlineSegments + 1];
        var radii = new double[CenterlineSegments + 1];
        points[0] = start;
        radii[0] = baseRadius;

        var dir = direction;
        var cur = start;
        var bendScale = 0.16 / (generation + 1); // higher generations bend less - thinner, stiffer twigs
        for (var i = 1; i <= CenterlineSegments; i++)
        {
            var t = i / (double)CenterlineSegments;
            var bendAxis = new Vector3(
                (float)rng.NextRange(-1, 1),
                (float)rng.NextRange(-0.3, 0.3),
                (float)rng.NextRange(-1, 1));
            if (bendAxis.LengthSquared() > 1e-6f)
            {
                bendAxis = Vector3.Normalize(bendAxis);
                dir = Vector3.Normalize(RotateAroundAxis(dir, bendAxis, (float)rng.NextRange(-bendScale, bendScale)));
            }

            cur += dir * (float)(length / CenterlineSegments);
            points[i] = cur;
            radii[i] = baseRadius + ((tipRadius - baseRadius) * t);
        }

        BuildTube(mesh, points, radii, generation == 0);

        if (generation < TrunkGenerations)
        {
            var childCount = generation == 0 ? 3 : 2;
            for (var c = 0; c < childCount; c++)
            {
                var tAlong = rng.NextRange(0.45, 0.88);
                var branchPoint = LerpAlongPolyline(points, tAlong);
                var spreadAngle = (float)rng.NextRange(0.5, 1.05);
                var randomAxis = new Vector3(
                    (float)rng.NextRange(-1, 1),
                    (float)rng.NextRange(-1, 1),
                    (float)rng.NextRange(-1, 1));
                if (randomAxis.LengthSquared() < 1e-6f)
                {
                    randomAxis = Vector3.UnitZ;
                }

                randomAxis = Vector3.Normalize(randomAxis);
                var childDir = RotateAroundAxis(direction, randomAxis, spreadAngle);
                childDir = Vector3.Normalize(childDir + (Vector3.UnitY * 0.35f)); // gravitropism: bias upward
                var childLength = length * 0.62 * (0.85 + rng.NextRange(0, 0.3));
                var childBaseRadius = baseRadius * 0.55;
                GenerateBranch(mesh, rng, branchPoint, childDir, childLength, childBaseRadius, generation + 1);
            }
        }
        else
        {
            AddFoliageCluster(mesh, rng, points[CenterlineSegments], direction);
        }
    }

    private static void BuildTube(MeshBuilder mesh, Vector3[] points, double[] radii, bool capBase)
    {
        var ringIndices = new int[points.Length][];
        var reference = Vector3.UnitX;
        for (var i = 0; i < points.Length; i++)
        {
            var tangent = i < points.Length - 1
                ? Vector3.Normalize(points[i + 1] - points[i])
                : Vector3.Normalize(points[i] - points[i - 1]);
            if (MathF.Abs(Vector3.Dot(reference, tangent)) > 0.98f)
            {
                reference = MathF.Abs(tangent.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
            }

            var normal = Vector3.Normalize(reference - (tangent * Vector3.Dot(reference, tangent)));
            var binormal = Vector3.Cross(tangent, normal);
            reference = normal;

            var ring = new int[RadialSegments];
            for (var k = 0; k < RadialSegments; k++)
            {
                var theta = k / (double)RadialSegments * Math.PI * 2;
                var offset = (normal * (float)(Math.Cos(theta) * radii[i]))
                    + (binormal * (float)(Math.Sin(theta) * radii[i]));
                var pos = points[i] + offset;
                var vertexNormal = offset.LengthSquared() > 1e-8f ? Vector3.Normalize(offset) : normal;
                ring[k] = mesh.AddVertex(pos, vertexNormal, BarkColor);
            }

            ringIndices[i] = ring;
        }

        for (var i = 0; i < points.Length - 1; i++)
        {
            var a = ringIndices[i];
            var b = ringIndices[i + 1];
            for (var k = 0; k < RadialSegments; k++)
            {
                var next = (k + 1) % RadialSegments;
                mesh.AddTriangle(a[k], b[k], a[next]);
                mesh.AddTriangle(a[next], b[k], b[next]);
            }
        }

        if (capBase)
        {
            var baseCenter = mesh.AddVertex(points[0], -Vector3.Normalize(points[1] - points[0]), BarkColor);
            var baseRing = ringIndices[0];
            for (var k = 0; k < RadialSegments; k++)
            {
                var next = (k + 1) % RadialSegments;
                mesh.AddTriangle(baseCenter, baseRing[next], baseRing[k]);
            }
        }
    }

    private static void AddFoliageCluster(MeshBuilder mesh, DeterministicRng rng, Vector3 tip, Vector3 direction)
    {
        var clusterCount = 2 + (int)Math.Round(rng.NextRange(0, 1));
        for (var i = 0; i < clusterCount; i++)
        {
            var jitter = new Vector3(
                (float)rng.NextRange(-0.35, 0.35),
                (float)rng.NextRange(-0.1, 0.35),
                (float)rng.NextRange(-0.35, 0.35));
            var center = tip + (direction * 0.15f) + jitter;
            var size = 0.28 + rng.NextRange(0, 0.22);
            var color = rng.NextUnit() > 0.5 ? FoliageColorLight : FoliageColorDark;
            AddLeafBlob(mesh, center, (float)size, color);
        }
    }

    /// <summary>An octahedral "leaf blob": six vertices (two poles, four equatorial), eight
    /// triangular faces - the cheapest volumetric shape that reads as a foliage clump rather
    /// than a flat card at a distance, matching the "low-poly stylized" quality tier
    /// docs/superpowers/specs/2026-08-27-procedural-tree-generation-feasibility.md describes as
    /// the first LOD rung; a higher-detail LOD would scatter many of these or real leaf cards
    /// instead of scaling this shape up.</summary>
    private static void AddLeafBlob(MeshBuilder mesh, Vector3 center, float size, (double R, double G, double B) color)
    {
        var top = mesh.AddVertex(center + (Vector3.UnitY * size), Vector3.UnitY, color);
        var bottom = mesh.AddVertex(center - (Vector3.UnitY * size * 0.6f), -Vector3.UnitY, color);
        var equator = new int[4];
        for (var k = 0; k < 4; k++)
        {
            var theta = k / 4.0 * Math.PI * 2;
            var offset = new Vector3((float)(Math.Cos(theta) * size), 0, (float)(Math.Sin(theta) * size));
            var pos = center + offset;
            equator[k] = mesh.AddVertex(pos, Vector3.Normalize(offset), color);
        }

        for (var k = 0; k < 4; k++)
        {
            var next = (k + 1) % 4;
            mesh.AddTriangle(top, equator[k], equator[next]);
            mesh.AddTriangle(bottom, equator[next], equator[k]);
        }
    }

    private static Vector3 LerpAlongPolyline(Vector3[] points, double t)
    {
        var scaled = t * (points.Length - 1);
        var index = Math.Clamp((int)scaled, 0, points.Length - 2);
        var local = (float)(scaled - index);
        return Vector3.Lerp(points[index], points[index + 1], local);
    }

    private static Vector3 RotateAroundAxis(Vector3 vector, Vector3 axis, float angleRadians)
    {
        // Rodrigues' rotation formula.
        var cos = MathF.Cos(angleRadians);
        var sin = MathF.Sin(angleRadians);
        return (vector * cos)
            + (Vector3.Cross(axis, vector) * sin)
            + (axis * Vector3.Dot(axis, vector) * (1 - cos));
    }

    private sealed class MeshBuilder
    {
        private readonly List<Vector3> _positions = new();
        private readonly List<Vector3> _normals = new();
        private readonly List<(double R, double G, double B)> _colors = new();
        private readonly List<int> _indices = new();

        public int AddVertex(Vector3 position, Vector3 normal, (double R, double G, double B) color)
        {
            _positions.Add(position);
            _normals.Add(normal);
            _colors.Add(color);
            return _positions.Count - 1;
        }

        public void AddTriangle(int a, int b, int c)
        {
            _indices.Add(a);
            _indices.Add(b);
            _indices.Add(c);
        }

        public JsonArray BuildVertices()
        {
            var array = new JsonArray();
            for (var i = 0; i < _positions.Count; i++)
            {
                var p = _positions[i];
                var n = _normals[i];
                var (r, g, bl) = _colors[i];
                array.Add(new JsonObject
                {
                    ["x"] = (double)p.X,
                    ["y"] = (double)p.Y,
                    ["z"] = (double)p.Z,
                    ["nx"] = (double)n.X,
                    ["ny"] = (double)n.Y,
                    ["nz"] = (double)n.Z,
                    ["r"] = r,
                    ["g"] = g,
                    ["b"] = bl,
                    ["u"] = 0.0,
                    ["v"] = 0.0
                });
            }

            return array;
        }

        public JsonArray BuildIndices()
        {
            var array = new JsonArray();
            foreach (var index in _indices)
            {
                array.Add(index);
            }

            return array;
        }
    }

    /// <summary>A tiny stateful wrapper around RekallAgeRuntimeModuleSdk's pure
    /// (seed, sequence) -> deterministic-value functions, advancing its own sequence counter
    /// each draw so a single generator call can make many distinct deterministic choices while
    /// staying fully reproducible from (seed, sequenceBase) alone.</summary>
    private sealed class DeterministicRng
    {
        private readonly int _seed;
        private long _sequence;

        public DeterministicRng(int seed, long sequenceBase)
        {
            _seed = seed;
            _sequence = sequenceBase;
        }

        public double NextUnit() => RekallAgeRuntimeModuleSdk.DeterministicUnit(_seed, _sequence++);

        public double NextRange(double minimum, double maximum) =>
            RekallAgeRuntimeModuleSdk.DeterministicRange(_seed, _sequence++, minimum, maximum);
    }
}
