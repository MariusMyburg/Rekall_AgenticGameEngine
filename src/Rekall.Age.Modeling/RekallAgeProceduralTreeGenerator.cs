using System.Numerics;
using System.Text.Json;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

/// <summary>
/// Deterministic broadleaf-tree generator. It builds a botanical branch hierarchy first and
/// realizes that skeleton as separate bark and atlas-ready leaf-card surfaces. Rendering stays
/// generic: callers assign ordinary AGE materials, LOD groups, and animation.
/// </summary>
public static class RekallAgeProceduralTreeGenerator
{
    private static readonly LodRecipe[] Recipes =
    [
        new(0, 60, 4, 6, 8, 1.00),
        new(1, 120, 3, 4, 6, 0.63),
        new(2, 240, 2, 3, 5, 0.36)
    ];

    public static RekallAgeGeneratedTree Generate(
        string assetId,
        string name,
        RekallAgeProceduralTreeSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Validate(settings);

        var lods = new List<RekallAgeGeneratedTreeLod>(Recipes.Length);
        foreach (var recipe in Recipes)
            lods.Add(GenerateLod(assetId, name, settings, recipe.Level));

        return new(assetId, name, settings, lods);
    }

    /// <summary>Generates only one requested quality tier, avoiding unused allocation for
    /// streaming or procedural runtime callers that have already selected a distance tier.</summary>
    public static RekallAgeGeneratedTreeLod GenerateLod(
        string assetId,
        string name,
        RekallAgeProceduralTreeSettings settings,
        int level)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Validate(settings);
        if (level < 0 || level >= Recipes.Length) throw new ArgumentOutOfRangeException(nameof(level));
        var recipe = Recipes[level];
        var rng = new TreeRandom(settings.Seed);
        var branches = GrowSkeleton(settings, recipe, rng);
        var bark = BuildBark($"{assetId}.lod{recipe.Level}.bark", $"{name} LOD {recipe.Level} Bark", branches, settings, recipe);
        var foliage = BuildFoliage($"{assetId}.lod{recipe.Level}.foliage", $"{name} LOD {recipe.Level} Foliage", branches, settings, recipe, rng, out var cards);
        return new(recipe.Level, recipe.MaximumDistance, bark, foliage, branches.Count, cards);
    }

    private static void Validate(RekallAgeProceduralTreeSettings settings)
    {
        if (!double.IsFinite(settings.Height) || settings.Height < 2 || settings.Height > 100)
            throw new ArgumentOutOfRangeException(nameof(settings), "Tree height must be between 2 and 100 metres.");
        if (!double.IsFinite(settings.TrunkRadius) || settings.TrunkRadius <= 0 || settings.TrunkRadius >= settings.Height * 0.2)
            throw new ArgumentOutOfRangeException(nameof(settings), "Trunk radius must be positive and proportional to height.");
        if (!double.IsFinite(settings.CrownRadius) || settings.CrownRadius <= settings.TrunkRadius)
            throw new ArgumentOutOfRangeException(nameof(settings), "Crown radius must exceed trunk radius.");
        if (settings.PrimaryBranchCount is < 4 or > 64)
            throw new ArgumentOutOfRangeException(nameof(settings), "Primary branch count must be between 4 and 64.");
        if (settings.NearLeafBudget < settings.MidLeafBudget || settings.MidLeafBudget < settings.FarLeafBudget || settings.FarLeafBudget < 8)
            throw new ArgumentOutOfRangeException(nameof(settings), "Leaf budgets must decrease from near to far and remain useful.");
    }

    private static List<Branch> GrowSkeleton(RekallAgeProceduralTreeSettings settings, LodRecipe recipe, TreeRandom rng)
    {
        var branches = new List<Branch>();
        var trunkPoints = new List<Vector3>();
        for (var i = 0; i <= recipe.CenterlineSegments + 3; i++)
        {
            var t = i / (double)(recipe.CenterlineSegments + 3);
            var sway = settings.Irregularity * settings.TrunkRadius * Math.Sin(t * 5.7 + rng.Phase);
            trunkPoints.Add(new((float)(sway * (0.55 + t)), (float)(settings.Height * t),
                (float)(settings.Irregularity * settings.TrunkRadius * Math.Sin(t * 4.1 + rng.Phase * 0.63))));
        }
        branches.Add(new(trunkPoints, settings.TrunkRadius * 1.28, settings.TrunkRadius * 0.16, 0));

        var primaryCount = Math.Max(5, (int)Math.Round(settings.PrimaryBranchCount * recipe.BranchFraction));
        const double goldenAngle = Math.PI * (3 - 2.23606797749979);
        for (var i = 0; i < primaryCount; i++)
        {
            var rank = (i + 0.45) / primaryCount;
            var yT = settings.CrownStart + rank * (0.92 - settings.CrownStart);
            var azimuth = i * goldenAngle + rng.Signed(settings.Irregularity * 0.7);
            var crownEnvelope = Math.Pow(Math.Sin(Math.PI * Math.Clamp((yT - settings.CrownStart) / (1 - settings.CrownStart), 0.05, 0.95)), 0.55);
            var length = settings.CrownRadius * (0.62 + crownEnvelope * 0.48) * rng.Range(0.82, 1.14);
            var rise = settings.Height * (0.08 + settings.ApicalDominance * rank * 0.22);
            var start = SamplePolyline(trunkPoints, yT);
            var horizontal = new Vector3((float)Math.Cos(azimuth), 0, (float)Math.Sin(azimuth));
            // A mild seed-stable prevailing-light bias prevents the mathematically round crown
            // that makes procedural trees read as ornamental/cartoon topiary.
            var crownBias = new Vector3(1.08f, 1, 0.91f);
            var end = start + Vector3.Multiply(horizontal, crownBias) * (float)length + Vector3.UnitY * (float)rise;
            var primary = CurvedBranch(start, end, recipe.CenterlineSegments, settings, rng, generation: 1);
            var baseRadius = settings.TrunkRadius * (0.43 - 0.18 * rank) * rng.Range(0.88, 1.1);
            branches.Add(new(primary, baseRadius, baseRadius * 0.16, 1));
            AddChildren(branches, primary, baseRadius, 2, recipe, settings, rng, azimuth);
        }
        return branches;
    }

    private static void AddChildren(List<Branch> branches, IReadOnlyList<Vector3> parent, double parentRadius,
        int generation, LodRecipe recipe, RekallAgeProceduralTreeSettings settings, TreeRandom rng, double parentAzimuth)
    {
        if (generation > recipe.Generations) return;
        var childCount = generation == 2 ? 3 : 2;
        for (var i = 0; i < childCount; i++)
        {
            var t = 0.42 + i * 0.21 + rng.Signed(0.06);
            var start = SamplePolyline(parent, t);
            var parentDirection = Vector3.Normalize(parent[^1] - parent[Math.Max(0, parent.Count - 2)]);
            var azimuth = parentAzimuth + (i - (childCount - 1) * 0.5) * 0.9 + rng.Signed(0.55);
            var outward = new Vector3((float)Math.Cos(azimuth), 0, (float)Math.Sin(azimuth));
            var direction = Vector3.Normalize(parentDirection * 0.32f + outward * 0.78f + Vector3.UnitY * (float)(settings.Tropism + 0.1));
            direction.Y -= (float)(settings.Droop * (generation - 1) * 0.22);
            direction = Vector3.Normalize(direction);
            var length = settings.CrownRadius * Math.Pow(0.49, generation - 1) * rng.Range(0.78, 1.18);
            var end = start + direction * (float)length;
            var points = CurvedBranch(start, end, Math.Max(2, recipe.CenterlineSegments - generation + 1), settings, rng, generation);
            var radius = parentRadius * rng.Range(0.48, 0.60);
            branches.Add(new(points, radius, Math.Max(radius * 0.12, 0.012), generation));
            AddChildren(branches, points, radius, generation + 1, recipe, settings, rng, azimuth);
        }
    }

    private static List<Vector3> CurvedBranch(Vector3 start, Vector3 end, int segments,
        RekallAgeProceduralTreeSettings settings, TreeRandom rng, int generation)
    {
        var result = new List<Vector3>(segments + 1) { start };
        var lateral = Vector3.Normalize(new Vector3(-(end - start).Z, 0.15f, (end - start).X));
        var amplitude = (float)(settings.Irregularity * (end - start).Length() * 0.18 / generation);
        for (var i = 1; i <= segments; i++)
        {
            var t = i / (float)segments;
            var bow = MathF.Sin(t * MathF.PI) * amplitude * (float)rng.Range(0.55, 1.0);
            var point = Vector3.Lerp(start, end, t) + lateral * bow;
            point.Y -= (float)(settings.Droop * (end - start).Length() * t * t * generation * 0.11);
            result.Add(point);
        }
        return result;
    }

    private static RekallAgeMeshAsset BuildBark(string id, string name, IReadOnlyList<Branch> branches,
        RekallAgeProceduralTreeSettings settings, LodRecipe recipe)
    {
        var mesh = new TreeMeshBuilder(id, name, "Bark", new(0.30, 0.19, 0.105, 1));
        foreach (var branch in branches)
        {
            var rings = new int[branch.Points.Count][];
            Vector3 previousNormal = Vector3.UnitX;
            var distance = 0.0;
            for (var p = 0; p < branch.Points.Count; p++)
            {
                if (p > 0) distance += Vector3.Distance(branch.Points[p - 1], branch.Points[p]);
                var t = p / (double)(branch.Points.Count - 1);
                var tangent = p == branch.Points.Count - 1
                    ? Vector3.Normalize(branch.Points[p] - branch.Points[p - 1])
                    : Vector3.Normalize(branch.Points[p + 1] - branch.Points[p]);
                var normal = previousNormal - tangent * Vector3.Dot(previousNormal, tangent);
                if (normal.LengthSquared() < 0.001f)
                    normal = Vector3.Normalize(Vector3.Cross(tangent, Math.Abs(tangent.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX));
                else normal = Vector3.Normalize(normal);
                var binormal = Vector3.Normalize(Vector3.Cross(tangent, normal));
                previousNormal = normal;
                var radius = branch.BaseRadius * Math.Pow(branch.TipRadius / branch.BaseRadius, t);
                if (branch.Generation == 0)
                    radius *= 1 + 0.38 * Math.Pow(1 - t, 7); // root flare, not a cylindrical pole
                rings[p] = new int[recipe.RadialSegments];
                for (var side = 0; side < recipe.RadialSegments; side++)
                {
                    var angle = side * Math.PI * 2 / recipe.RadialSegments;
                    var irregular = 1 + settings.Irregularity * 0.10 * Math.Sin(angle * 3 + p * 1.73 + branch.Generation);
                    var radial = normal * (float)Math.Cos(angle) + binormal * (float)Math.Sin(angle);
                    var position = branch.Points[p] + radial * (float)(radius * irregular);
                    rings[p][side] = mesh.Add(position, radial, new(side / (double)recipe.RadialSegments, distance / 2.2));
                }
            }
            for (var p = 0; p < rings.Length - 1; p++)
                for (var side = 0; side < recipe.RadialSegments; side++)
                {
                    var next = (side + 1) % recipe.RadialSegments;
                    mesh.Quad(rings[p][side], rings[p][next], rings[p + 1][next], rings[p + 1][side]);
                }
        }
        return mesh.Build();
    }

    private static RekallAgeMeshAsset BuildFoliage(string id, string name, IReadOnlyList<Branch> branches,
        RekallAgeProceduralTreeSettings settings, LodRecipe recipe, TreeRandom rng, out int cardCount)
    {
        var mesh = new TreeMeshBuilder(id, name, "Foliage", new(0.19, 0.39, 0.105, 1));
        var terminals = branches.Where(branch => branch.Generation >= recipe.Generations).ToArray();
        var requested = recipe.Level switch { 0 => settings.NearLeafBudget, 1 => settings.MidLeafBudget, _ => settings.FarLeafBudget };
        cardCount = requested;
        for (var i = 0; i < requested; i++)
        {
            var branch = terminals[i % terminals.Length];
            var t = rng.Range(0.42, 1.03);
            var center = SamplePolyline(branch.Points, Math.Min(1, t));
            center += new Vector3((float)rng.Signed(0.32), (float)rng.Signed(0.22), (float)rng.Signed(0.32));
            var size = (float)(settings.Height * rng.Range(0.022, 0.037) * recipe.LeafScale);
            var azimuth = (float)rng.Range(0, Math.PI * 2);
            var right = Vector3.Normalize(new Vector3(MathF.Cos(azimuth), 0, MathF.Sin(azimuth)));
            var up = Vector3.Normalize(Vector3.UnitY * (float)rng.Range(0.65, 0.95) + new Vector3(right.Z, 0, -right.X) * (float)rng.Signed(0.32));
            AddCrossedLeafCard(mesh, center, right, up, size);
        }
        return mesh.Build();
    }

    private static void AddCrossedLeafCard(TreeMeshBuilder mesh, Vector3 center, Vector3 right, Vector3 up, float size)
    {
        AddPlane(right, up);
        var secondRight = Vector3.Normalize(Vector3.Cross(up, right));
        if (secondRight.LengthSquared() < 0.01f) secondRight = Vector3.UnitZ;
        AddPlane(secondRight, up);

        void AddPlane(Vector3 r, Vector3 u)
        {
            var normal = Vector3.Normalize(Vector3.Cross(r, u));
            var leafLength = size * 1.35f;
            var leafWidth = size * 0.72f;
            var points = new[]
            {
                mesh.Add(center - u * leafLength, normal, new(0.5, 1.0)),
                mesh.Add(center - u * leafLength * 0.28f + r * leafWidth, normal, new(1.0, 0.67)),
                mesh.Add(center + u * leafLength * 0.42f + r * leafWidth * 0.72f, normal, new(0.86, 0.28)),
                mesh.Add(center + u * leafLength, normal, new(0.5, 0.0)),
                mesh.Add(center + u * leafLength * 0.42f - r * leafWidth * 0.72f, normal, new(0.14, 0.28)),
                mesh.Add(center - u * leafLength * 0.28f - r * leafWidth, normal, new(0.0, 0.67))
            };
            mesh.Triangle(points[0], points[1], points[2]);
            mesh.Triangle(points[0], points[2], points[3]);
            mesh.Triangle(points[0], points[3], points[4]);
            mesh.Triangle(points[0], points[4], points[5]);
        }
    }

    private static Vector3 SamplePolyline(IReadOnlyList<Vector3> points, double t)
    {
        var scaled = Math.Clamp(t, 0, 1) * (points.Count - 1);
        var index = Math.Min((int)scaled, points.Count - 2);
        return Vector3.Lerp(points[index], points[index + 1], (float)(scaled - index));
    }

    private sealed record Branch(IReadOnlyList<Vector3> Points, double BaseRadius, double TipRadius, int Generation);
    private sealed record LodRecipe(int Level, double MaximumDistance, int Generations, int CenterlineSegments, int RadialSegments, double BranchFraction)
    { public double LeafScale => 1 + Level * 0.18; }

    private sealed class TreeRandom
    {
        private ulong _state;
        public TreeRandom(int seed) { _state = (ulong)(uint)seed + 0x9E3779B97F4A7C15UL; Phase = Unit() * Math.PI * 2; }
        public double Phase { get; }
        public double Unit() { _state += 0x9E3779B97F4A7C15UL; var z = _state; z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL; z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL; return ((z ^ (z >> 31)) >> 11) * (1.0 / (1UL << 53)); }
        public double Range(double min, double max) => min + Unit() * (max - min);
        public double Signed(double magnitude) => Range(-magnitude, magnitude);
    }

    private sealed class TreeMeshBuilder
    {
        private readonly string _id, _name, _material;
        private readonly RekallAgeGeometryVector4 _color;
        private readonly List<RekallAgeGeometryVector3> _positions = [];
        private readonly List<RekallAgeGeometryVector3> _normals = [];
        private readonly List<RekallAgeGeometryVector2> _uvs = [];
        private readonly List<int[]> _faces = [];
        public TreeMeshBuilder(string id, string name, string material, RekallAgeGeometryVector4 color) { _id = id; _name = name; _material = material; _color = color; }
        public int Add(Vector3 p, Vector3 n, RekallAgeGeometryVector2 uv) { _positions.Add(new(p.X, p.Y, p.Z)); _normals.Add(new(n.X, n.Y, n.Z)); _uvs.Add(uv); return _positions.Count - 1; }
        public void Quad(int a, int b, int c, int d) => _faces.Add([a, b, c, d]);
        public void Triangle(int a, int b, int c) => _faces.Add([a, b, c]);

        public RekallAgeMeshAsset Build()
        {
            var edges = new List<RekallAgeMeshEdgePointIndices>();
            var edgeMap = new Dictionary<(int, int), int>();
            var corners = new List<int>(); var cornerEdges = new List<int>(); var offsets = new List<int> { 0 };
            foreach (var face in _faces)
            {
                for (var i = 0; i < face.Length; i++)
                {
                    var a = face[i]; var b = face[(i + 1) % face.Length]; var key = a < b ? (a, b) : (b, a);
                    if (!edgeMap.TryGetValue(key, out var edge)) { edge = edges.Count; edgeMap[key] = edge; edges.Add(new(a, b)); }
                    corners.Add(a); cornerEdges.Add(edge);
                }
                offsets.Add(corners.Count);
            }
            var attributes = new RekallAgeGeometryAttribute[]
            {
                Attribute("normal", RekallAgeGeometryValueType.Float3, "normal", _normals.Select(v => JsonSerializer.SerializeToElement(new[] { v.X, v.Y, v.Z })).ToArray()),
                Attribute("uv0", RekallAgeGeometryValueType.Float2, "texcoord-0", _uvs.Select(v => JsonSerializer.SerializeToElement(new[] { v.X, v.Y })).ToArray()),
                Attribute("color", RekallAgeGeometryValueType.ColorLinear, "color", Enumerable.Repeat(JsonSerializer.SerializeToElement(new[] { _color.X, _color.Y, _color.Z, _color.W }), _positions.Count).ToArray())
            };
            var topology = new RekallAgeMeshTopology(
                Ids(_positions.Count, 1), _positions, Ids(edges.Count, 1_000_001), edges,
                Ids(_faces.Count, 2_000_001), offsets, Ids(corners.Count, 3_000_001), corners, cornerEdges);
            return RekallAgeMeshAsset.Create(_id, _name, topology, attributes, [new(_material, null)]);
        }
        private static RekallAgeGeometryAttribute Attribute(string name, RekallAgeGeometryValueType type, string semantic, IReadOnlyList<JsonElement> values) => new(name, RekallAgeGeometryDomain.Point, type, values, semantic);
        private static ulong[] Ids(int count, ulong start) => Enumerable.Range(0, count).Select(i => start + (ulong)i).ToArray();
    }
}
