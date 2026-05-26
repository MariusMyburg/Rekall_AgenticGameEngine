using System.Globalization;
using System.Numerics;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Rendering;

public sealed class RekallAgeVulkanSceneMeshBuilder
{
    public IReadOnlyList<RekallAgeVulkanSceneMesh> BuildMeshes(RekallAgeRuntimeViewportFrame frame)
    {
        return BuildMeshes(frame, RekallAgeRuntimeViewportAssetSet.Empty);
    }

    public IReadOnlyList<RekallAgeVulkanSceneMesh> BuildMeshes(
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeRuntimeViewportAssetSet assets)
    {
        return frame.Renderables
            .Where(renderable => renderable.Kind.Equals("mesh", StringComparison.Ordinal))
            .SelectMany(renderable => BuildMeshes(renderable, assets))
            .ToArray();
    }

    public static string? TryGetSupportedPrimitive(RekallAgeRuntimeViewportRenderable renderable)
    {
        if (!renderable.Kind.Equals("mesh", StringComparison.Ordinal))
        {
            return null;
        }

        var variant = renderable.Variant ?? renderable.AssetId;
        if (string.IsNullOrWhiteSpace(variant))
        {
            return null;
        }

        var normalized = variant.Trim().ToLowerInvariant();
        if (normalized.StartsWith("rekall.geometry.", StringComparison.Ordinal))
        {
            normalized = normalized["rekall.geometry.".Length..];
        }
        else if (normalized.StartsWith("rekall.planet.", StringComparison.Ordinal))
        {
            normalized = normalized["rekall.planet.".Length..];
        }
        else if (normalized.StartsWith("rekall.primitive.", StringComparison.Ordinal))
        {
            normalized = normalized["rekall.primitive.".Length..];
        }

        return normalized is "cube" or "sphere" or "cylinder" or "cone" or "plane" or "surface"
            ? normalized
            : null;
    }

    public static bool IsSupportedMeshRenderable(RekallAgeRuntimeViewportRenderable renderable)
    {
        return HasValidViewportLineSegments(renderable)
            || HasValidAuthoredGeometryMesh(renderable)
            || TryGetSupportedPrimitive(renderable) is not null;
    }

    private static IEnumerable<RekallAgeVulkanSceneMesh> BuildMeshes(
        RekallAgeRuntimeViewportRenderable renderable,
        RekallAgeRuntimeViewportAssetSet assets)
    {
        if (HasValidViewportLineSegments(renderable))
        {
            yield return BindRenderableMaterial(BuildViewportLineSegments(renderable), renderable, assets);
            yield break;
        }

        if (HasValidAuthoredGeometryMesh(renderable))
        {
            yield return BindRenderableMaterial(BuildAuthoredGeometryMesh(renderable), renderable, assets);
            yield break;
        }

        var primitive = TryGetSupportedPrimitive(renderable);
        if (primitive is not null)
        {
            var mesh = primitive switch
            {
                "cube" => BuildCube(renderable, primitive),
                "plane" => BuildPlane(renderable, primitive),
                "sphere" => BuildSphere(renderable, primitive, 12, 8),
                "surface" => BuildSphere(renderable, "planet", 64, 32),
                "cylinder" => BuildCylinder(renderable, primitive, 16),
                "cone" => BuildCone(renderable, primitive, 16),
                _ => throw new InvalidOperationException($"Unsupported primitive '{primitive}'.")
            };
            yield return BindRenderableMaterial(mesh, renderable, assets);
            yield break;
        }

        if (renderable.AssetId is null
            || !assets.Models.TryGetValue(renderable.AssetId, out var modelMeshes))
        {
            yield break;
        }

        foreach (var mesh in modelMeshes)
        {
            yield return BindRenderableMaterial(mesh with
            {
                EntityId = renderable.EntityId,
                EntityName = renderable.EntityName
            }, renderable, assets);
        }
    }

    private static RekallAgeVulkanSceneMesh BindRenderableMaterial(
        RekallAgeVulkanSceneMesh mesh,
        RekallAgeRuntimeViewportRenderable renderable,
        RekallAgeRuntimeViewportAssetSet assets)
    {
        var procedural = renderable.ProceduralMaterial is null
            ? null
            : RekallAgeProceduralMaterialTextureGenerator.Generate(renderable.EntityId, renderable.ProceduralMaterial);
        return mesh with
        {
            BaseColorTexture = ResolveTexture(renderable.TextureAssetId, assets) ?? mesh.BaseColorTexture ?? procedural?.BaseColorTexture,
            MetallicRoughnessTexture = ResolveTexture(renderable.MetallicRoughnessTextureAssetId, assets) ?? mesh.MetallicRoughnessTexture ?? procedural?.MetallicRoughnessTexture,
            NormalTexture = ResolveTexture(renderable.NormalTextureAssetId, assets) ?? mesh.NormalTexture ?? procedural?.NormalTexture,
            OcclusionTexture = ResolveTexture(renderable.OcclusionTextureAssetId, assets) ?? mesh.OcclusionTexture,
            EmissiveTexture = ResolveTexture(renderable.EmissiveTextureAssetId, assets) ?? mesh.EmissiveTexture ?? procedural?.EmissiveTexture,
            MetallicFactor = renderable.MetallicFactor != 0
                ? (float)Math.Clamp(renderable.MetallicFactor, 0, 1)
                : procedural is not null
                    ? (float)Math.Clamp(renderable.ProceduralMaterial!.MetallicFactor, 0, 1)
                    : mesh.MetallicFactor,
            RoughnessFactor = renderable.RoughnessFactor == 1 ? mesh.RoughnessFactor : (float)Math.Clamp(renderable.RoughnessFactor, 0.04, 1),
            NormalScale = renderable.NormalScale == 1 ? mesh.NormalScale : (float)Math.Clamp(renderable.NormalScale, 0, 4),
            OcclusionStrength = renderable.OcclusionStrength == 1 ? mesh.OcclusionStrength : (float)Math.Clamp(renderable.OcclusionStrength, 0, 1),
            EmissiveFactor = renderable.EmissiveStrength > 0
                ? ResolveEmissiveFactor(renderable)
                : procedural?.EmissiveFactor ?? mesh.EmissiveFactor
        };
    }

    private static RekallAgeVulkanSceneTexture? ResolveTexture(
        string? assetId,
        RekallAgeRuntimeViewportAssetSet assets)
    {
        if (string.IsNullOrWhiteSpace(assetId))
        {
            return null;
        }

        if (assets.Images.TryGetValue(assetId, out var image))
        {
            return new RekallAgeVulkanSceneTexture(
                assetId,
                image.Width,
                image.Height,
                image.Rgba,
                DefaultSampler());
        }

        return assets.Textures.TryGetValue(assetId, out var runtimeTexture)
            ? new RekallAgeVulkanSceneTexture(
                assetId,
                runtimeTexture.Width,
                runtimeTexture.Height,
                [],
                DefaultSampler(),
                runtimeTexture)
            : null;
    }

    private static Vector4 ResolveEmissiveFactor(RekallAgeRuntimeViewportRenderable renderable)
    {
        if (renderable.EmissiveStrength <= 0)
        {
            return Vector4.Zero;
        }

        var color = ParseColor(renderable.EmissiveColor ?? "#ffffff");
        return new Vector4(
            color.R,
            color.G,
            color.B,
            (float)Math.Clamp(renderable.EmissiveStrength, 0, 64));
    }

    private static RekallAgeVulkanSceneMesh BuildAuthoredGeometryMesh(RekallAgeRuntimeViewportRenderable renderable)
    {
        var geometry = renderable.GeometryMesh!;
        var fallbackColor = ParseColor(renderable.MaterialColor);
        var vertices = geometry.Vertices
            .Select(vertex =>
            {
                var color = ResolveColor(vertex, fallbackColor);
                var normal = Normalize((float)vertex.NormalX, (float)vertex.NormalY, (float)vertex.NormalZ);
                return new RekallAgeVulkanSceneVertex(
                    (float)vertex.X,
                    (float)vertex.Y,
                    (float)vertex.Z,
                    normal.X,
                    normal.Y,
                    normal.Z,
                    color.R,
                    color.G,
                    color.B,
                    color.A,
                    (float)vertex.U,
                    (float)vertex.V);
            })
            .ToArray();

        return new RekallAgeVulkanSceneMesh(
            renderable.EntityId,
            renderable.EntityName,
            "mesh",
            vertices,
            geometry.Indices.Select(index => (uint)index).ToArray());
    }

    private static RekallAgeVulkanSceneMesh BuildViewportLineSegments(RekallAgeRuntimeViewportRenderable renderable)
    {
        var lineSegments = renderable.LineSegments!;
        var color = ParseColor(renderable.MaterialColor);
        var thickness = Math.Clamp((float)lineSegments.Thickness, 0.001f, 100f);
        var vertices = new List<RekallAgeVulkanSceneVertex>(lineSegments.Segments.Count * 8);
        var indices = new List<uint>(lineSegments.Segments.Count * 12);

        foreach (var segment in lineSegments.Segments)
        {
            AddLineSegmentRibbon(
                vertices,
                indices,
                color,
                new Vector3((float)segment.FromX, (float)segment.FromY, (float)segment.FromZ),
                new Vector3((float)segment.ToX, (float)segment.ToY, (float)segment.ToZ),
                thickness);
        }

        return new RekallAgeVulkanSceneMesh(
            renderable.EntityId,
            renderable.EntityName,
            "line-segments",
            vertices,
            indices);
    }

    private static bool HasValidViewportLineSegments(RekallAgeRuntimeViewportRenderable renderable)
    {
        return renderable.Kind.Equals("mesh", StringComparison.Ordinal)
            && renderable.LineSegments is { } lineSegments
            && lineSegments.Segments.Count > 0
            && lineSegments.Thickness > 0
            && lineSegments.Segments.Count <= ushort.MaxValue / 8;
    }

    private static void AddLineSegmentRibbon(
        List<RekallAgeVulkanSceneVertex> vertices,
        List<uint> indices,
        SceneColor color,
        Vector3 from,
        Vector3 to,
        float thickness)
    {
        var direction = to - from;
        if (direction.LengthSquared() <= 0.000001f)
        {
            return;
        }

        var side = Vector3.Normalize(Vector3.Cross(direction, Vector3.UnitY));
        if (!IsFinite(side) || side.LengthSquared() <= 0.000001f)
        {
            side = Vector3.Normalize(Vector3.Cross(direction, Vector3.UnitX));
        }

        var secondSide = Vector3.Normalize(Vector3.Cross(direction, side));
        AddLineRibbon(vertices, indices, color, from, to, side, thickness);
        AddLineRibbon(vertices, indices, color, from, to, secondSide, thickness);
    }

    private static void AddLineRibbon(
        List<RekallAgeVulkanSceneVertex> vertices,
        List<uint> indices,
        SceneColor color,
        Vector3 from,
        Vector3 to,
        Vector3 side,
        float thickness)
    {
        if (!IsFinite(side) || side.LengthSquared() <= 0.000001f)
        {
            return;
        }

        var normal = Normalize(side.X, side.Y, side.Z);
        var offset = side * (thickness * 0.5f);
        var start = checked((uint)vertices.Count);
        vertices.Add(Vertex((from.X + offset.X, from.Y + offset.Y, from.Z + offset.Z), normal, color, 0, 0));
        vertices.Add(Vertex((from.X - offset.X, from.Y - offset.Y, from.Z - offset.Z), normal, color, 0, 1));
        vertices.Add(Vertex((to.X + offset.X, to.Y + offset.Y, to.Z + offset.Z), normal, color, 1, 0));
        vertices.Add(Vertex((to.X - offset.X, to.Y - offset.Y, to.Z - offset.Z), normal, color, 1, 1));
        indices.Add(start);
        indices.Add(start + 2);
        indices.Add(start + 1);
        indices.Add(start + 1);
        indices.Add(start + 2);
        indices.Add(start + 3);
    }

    private static bool IsFinite(Vector3 value)
    {
        return float.IsFinite(value.X)
            && float.IsFinite(value.Y)
            && float.IsFinite(value.Z);
    }

    private static bool HasValidAuthoredGeometryMesh(RekallAgeRuntimeViewportRenderable renderable)
    {
        if (!renderable.Kind.Equals("mesh", StringComparison.Ordinal)
            || renderable.GeometryMesh is not { } geometry
            || geometry.Vertices.Count == 0
            || geometry.Vertices.Count > ushort.MaxValue
            || geometry.Indices.Count < 3
            || geometry.Indices.Count % 3 != 0)
        {
            return false;
        }

        return geometry.Indices.All(index => index < geometry.Vertices.Count);
    }

    private static RekallAgeVulkanSceneMesh BuildCube(RekallAgeRuntimeViewportRenderable renderable, string primitive)
    {
        var color = ParseColor(renderable.MaterialColor);
        var vertices = new List<RekallAgeVulkanSceneVertex>(24);
        var indices = new List<uint>(36);
        AddQuad(vertices, indices, color, (-0.5f, -0.5f, 0.5f), (0.5f, -0.5f, 0.5f), (0.5f, 0.5f, 0.5f), (-0.5f, 0.5f, 0.5f), (0, 0, 1));
        AddQuad(vertices, indices, color, (0.5f, -0.5f, -0.5f), (-0.5f, -0.5f, -0.5f), (-0.5f, 0.5f, -0.5f), (0.5f, 0.5f, -0.5f), (0, 0, -1));
        AddQuad(vertices, indices, color, (-0.5f, 0.5f, 0.5f), (0.5f, 0.5f, 0.5f), (0.5f, 0.5f, -0.5f), (-0.5f, 0.5f, -0.5f), (0, 1, 0));
        AddQuad(vertices, indices, color, (-0.5f, -0.5f, -0.5f), (0.5f, -0.5f, -0.5f), (0.5f, -0.5f, 0.5f), (-0.5f, -0.5f, 0.5f), (0, -1, 0));
        AddQuad(vertices, indices, color, (0.5f, -0.5f, 0.5f), (0.5f, -0.5f, -0.5f), (0.5f, 0.5f, -0.5f), (0.5f, 0.5f, 0.5f), (1, 0, 0));
        AddQuad(vertices, indices, color, (-0.5f, -0.5f, -0.5f), (-0.5f, -0.5f, 0.5f), (-0.5f, 0.5f, 0.5f), (-0.5f, 0.5f, -0.5f), (-1, 0, 0));
        return new RekallAgeVulkanSceneMesh(renderable.EntityId, renderable.EntityName, primitive, vertices, indices);
    }

    private static RekallAgeVulkanSceneMesh BuildPlane(RekallAgeRuntimeViewportRenderable renderable, string primitive)
    {
        var color = ParseColor(renderable.MaterialColor);
        var vertices = new List<RekallAgeVulkanSceneVertex>(4);
        var indices = new List<uint>(6);
        AddQuad(vertices, indices, color, (-0.5f, 0, 0.5f), (0.5f, 0, 0.5f), (0.5f, 0, -0.5f), (-0.5f, 0, -0.5f), (0, 1, 0));
        return new RekallAgeVulkanSceneMesh(renderable.EntityId, renderable.EntityName, primitive, vertices, indices);
    }

    private static RekallAgeVulkanSceneMesh BuildSphere(RekallAgeRuntimeViewportRenderable renderable, string primitive, int slices, int stacks)
    {
        var color = ParseColor(renderable.MaterialColor);
        var vertices = new List<RekallAgeVulkanSceneVertex>();
        var indices = new List<uint>();
        for (var stack = 0; stack <= stacks; stack++)
        {
            var v = stack / (float)stacks;
            var phi = MathF.PI * v;
            var y = MathF.Cos(phi) * 0.5f;
            var radius = MathF.Sin(phi) * 0.5f;
            for (var slice = 0; slice <= slices; slice++)
            {
                var u = slice / (float)slices;
                var theta = MathF.PI * 2 * u;
                var x = MathF.Cos(theta) * radius;
                var z = MathF.Sin(theta) * radius;
                var normal = Normalize(x, y, z);
                vertices.Add(Vertex((x, y, z), normal, color, u, v));
            }
        }

        for (var stack = 0; stack < stacks; stack++)
        {
            for (var slice = 0; slice < slices; slice++)
            {
                var a = (uint)(stack * (slices + 1) + slice);
                var b = (uint)(a + slices + 1);
                indices.Add(a);
                indices.Add(a + 1);
                indices.Add(b);
                indices.Add(a + 1);
                indices.Add(b + 1);
                indices.Add(b);
            }
        }

        return new RekallAgeVulkanSceneMesh(renderable.EntityId, renderable.EntityName, primitive, vertices, indices);
    }

    private static RekallAgeVulkanSceneMesh BuildCylinder(RekallAgeRuntimeViewportRenderable renderable, string primitive, int slices)
    {
        var color = ParseColor(renderable.MaterialColor);
        var vertices = new List<RekallAgeVulkanSceneVertex>();
        var indices = new List<uint>();
        for (var slice = 0; slice <= slices; slice++)
        {
            var u = slice / (float)slices;
            var theta = MathF.PI * 2 * u;
            var x = MathF.Cos(theta) * 0.5f;
            var z = MathF.Sin(theta) * 0.5f;
            var normal = Normalize(x, 0, z);
            vertices.Add(Vertex((x, -0.5f, z), normal, color, u, 1));
            vertices.Add(Vertex((x, 0.5f, z), normal, color, u, 0));
        }

        for (var slice = 0; slice < slices; slice++)
        {
            var a = (uint)(slice * 2);
            indices.Add(a);
            indices.Add(a + 1);
            indices.Add(a + 2);
            indices.Add(a + 2);
            indices.Add(a + 1);
            indices.Add(a + 3);
        }

        AddCap(vertices, indices, color, slices, 0.5f, (0, 1, 0), top: true);
        AddCap(vertices, indices, color, slices, -0.5f, (0, -1, 0), top: false);
        return new RekallAgeVulkanSceneMesh(renderable.EntityId, renderable.EntityName, primitive, vertices, indices);
    }

    private static RekallAgeVulkanSceneMesh BuildCone(RekallAgeRuntimeViewportRenderable renderable, string primitive, int slices)
    {
        var color = ParseColor(renderable.MaterialColor);
        var vertices = new List<RekallAgeVulkanSceneVertex>();
        var indices = new List<uint>();
        for (var slice = 0; slice <= slices; slice++)
        {
            var u = slice / (float)slices;
            var theta = MathF.PI * 2 * u;
            var x = MathF.Cos(theta) * 0.5f;
            var z = MathF.Sin(theta) * 0.5f;
            var normal = Normalize(x, 0.5f, z);
            vertices.Add(Vertex((x, -0.5f, z), normal, color, u, 1));
            vertices.Add(Vertex((0, 0.5f, 0), normal, color, u, 0));
        }

        for (var slice = 0; slice < slices; slice++)
        {
            var a = (uint)(slice * 2);
            indices.Add(a);
            indices.Add(a + 1);
            indices.Add(a + 2);
        }

        AddCap(vertices, indices, color, slices, -0.5f, (0, -1, 0), top: false);
        return new RekallAgeVulkanSceneMesh(renderable.EntityId, renderable.EntityName, primitive, vertices, indices);
    }

    private static void AddQuad(
        List<RekallAgeVulkanSceneVertex> vertices,
        List<uint> indices,
        SceneColor color,
        (float X, float Y, float Z) a,
        (float X, float Y, float Z) b,
        (float X, float Y, float Z) c,
        (float X, float Y, float Z) d,
        (float X, float Y, float Z) normal)
    {
        var start = checked((uint)vertices.Count);
        vertices.Add(Vertex(a, normal, color, 0, 1));
        vertices.Add(Vertex(b, normal, color, 1, 1));
        vertices.Add(Vertex(c, normal, color, 1, 0));
        vertices.Add(Vertex(d, normal, color, 0, 0));
        indices.Add(start);
        indices.Add(start + 1);
        indices.Add(start + 2);
        indices.Add(start);
        indices.Add(start + 2);
        indices.Add(start + 3);
    }

    private static void AddCap(
        List<RekallAgeVulkanSceneVertex> vertices,
        List<uint> indices,
        SceneColor color,
        int slices,
        float y,
        (float X, float Y, float Z) normal,
        bool top)
    {
        var center = checked((uint)vertices.Count);
        vertices.Add(Vertex((0, y, 0), normal, color, 0.5f, 0.5f));
        for (var slice = 0; slice <= slices; slice++)
        {
            var u = slice / (float)slices;
            var theta = MathF.PI * 2 * u;
            var x = MathF.Cos(theta) * 0.5f;
            var z = MathF.Sin(theta) * 0.5f;
            vertices.Add(Vertex((x, y, z), normal, color, (x + 0.5f), (z + 0.5f)));
        }

        for (var slice = 0; slice < slices; slice++)
        {
            var a = center + 1 + (uint)slice;
            var b = a + 1;
            indices.Add(center);
            indices.Add(top ? b : a);
            indices.Add(top ? a : b);
        }
    }

    private static RekallAgeVulkanSceneVertex Vertex(
        (float X, float Y, float Z) position,
        (float X, float Y, float Z) normal,
        SceneColor color,
        float u,
        float v)
    {
        return new RekallAgeVulkanSceneVertex(position.X, position.Y, position.Z, normal.X, normal.Y, normal.Z, color.R, color.G, color.B, color.A, u, v);
    }

    private static (float X, float Y, float Z) Normalize(float x, float y, float z)
    {
        var length = MathF.Sqrt(x * x + y * y + z * z);
        return length <= 0.0001f ? (0, 1, 0) : (x / length, y / length, z / length);
    }

    private static SceneColor ParseColor(string? color)
    {
        if (color is { Length: 7 or 9 } && color[0] == '#'
            && byte.TryParse(color.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
            && byte.TryParse(color.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
            && byte.TryParse(color.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            var alpha = color.Length == 9
                && byte.TryParse(color.AsSpan(7, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var a)
                ? a / 255f
                : 1;
            return new SceneColor(r / 255f, g / 255f, b / 255f, alpha);
        }

        return new SceneColor(0.35f, 0.58f, 0.85f, 1);
    }

    private static SceneColor ResolveColor(RekallAgeRuntimeViewportGeometryVertex vertex, SceneColor fallback)
    {
        return new SceneColor(
            ResolveUnit(vertex.R, fallback.R),
            ResolveUnit(vertex.G, fallback.G),
            ResolveUnit(vertex.B, fallback.B),
            ResolveUnit(vertex.A, fallback.A));
    }

    private static float ResolveUnit(double value, float fallback)
    {
        return double.IsNaN(value) ? fallback : (float)Math.Clamp(value, 0, 1);
    }

    private static RekallAgeVulkanSceneSampler DefaultSampler()
    {
        return new RekallAgeVulkanSceneSampler(
            RekallAgeVulkanSceneFilter.Linear,
            RekallAgeVulkanSceneFilter.Linear,
            RekallAgeVulkanSceneWrapMode.Repeat,
            RekallAgeVulkanSceneWrapMode.Repeat);
    }

    private readonly record struct SceneColor(float R, float G, float B, float A);
}
