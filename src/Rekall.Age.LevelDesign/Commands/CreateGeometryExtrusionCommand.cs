using System.Globalization;
using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.World;

namespace Rekall.Age.LevelDesign.Commands;

public sealed record CreateGeometryExtrusionPoint(double X, double Y);

public sealed record CreateGeometryExtrusionRequest(
    string ProjectRoot,
    string SceneName,
    string Name,
    IReadOnlyList<CreateGeometryExtrusionPoint> Profile,
    double Depth,
    double X = 0,
    double Y = 0,
    double Z = 0,
    double Pitch = 0,
    double Yaw = 0,
    double Roll = 0,
    double ScaleX = 1,
    double ScaleY = 1,
    double ScaleZ = 1,
    string Color = "#8ab4f8");

public sealed record CreateGeometryExtrusionResult(
    string EntityId,
    int VertexCount,
    int IndexCount,
    RekallAgeSceneDocument? Scene);

public sealed class CreateGeometryExtrusionCommand
    : IRekallAgeCommand<CreateGeometryExtrusionRequest, CreateGeometryExtrusionResult>
{
    private readonly RekallAgeSceneStore _store = new();

    public string Name => "rekall.geometry.create_extrusion";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Creates a renderable 3D extrusion mesh from a 2D profile and depth.",
        typeof(CreateGeometryExtrusionRequest).FullName!,
        typeof(CreateGeometryExtrusionResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<CreateGeometryExtrusionResult>> ExecuteAsync(
        CreateGeometryExtrusionRequest request,
        RekallAgeCommandContext context)
    {
        var color = NormalizeColor(request.Color);
        if (color is null)
        {
            var error = new RekallAgeCommandError(
                "REKALL_GEOMETRY_COLOR_INVALID",
                "Geometry extrusion color must be a #RRGGBB hex color.",
                request.Color);
            return RekallAgeCommandResult<CreateGeometryExtrusionResult>.Failure(Empty(), error.Message, [error]);
        }

        if (!RekallAgeGeometryExtrusionMeshBuilder.TryBuild(request.Profile, request.Depth, out var mesh, out var message))
        {
            var error = new RekallAgeCommandError("REKALL_GEOMETRY_EXTRUSION_INVALID", message, null);
            return RekallAgeCommandResult<CreateGeometryExtrusionResult>.Failure(Empty(), error.Message, [error]);
        }

        var scene = await _store.LoadAsync(request.ProjectRoot, request.SceneName, context.CancellationToken);
        var entity = RekallAgeEntityDocument.Create(request.Name, ["geometry", "mesh", "extrusion"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Transform3D",
                new JsonObject
                {
                    ["x"] = request.X,
                    ["y"] = request.Y,
                    ["z"] = request.Z,
                    ["pitch"] = request.Pitch,
                    ["yaw"] = request.Yaw,
                    ["roll"] = request.Roll,
                    ["scaleX"] = request.ScaleX,
                    ["scaleY"] = request.ScaleY,
                    ["scaleZ"] = request.ScaleZ
                }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.GeometryExtrusion",
                new JsonObject
                {
                    ["profile"] = ToProfileArray(request.Profile),
                    ["depth"] = request.Depth,
                    ["color"] = color
                }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.GeometryMesh",
                new JsonObject
                {
                    ["color"] = color,
                    ["vertices"] = ToVertexArray(mesh.Vertices),
                    ["indices"] = ToIndexArray(mesh.Indices)
                }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.MeshRenderer",
                new JsonObject
                {
                    ["mesh"] = "rekall.geometry.mesh"
                }));

        var updated = scene.AddEntity(entity);
        await _store.SaveAsync(request.ProjectRoot, updated, context.CancellationToken);
        context.Transaction.RecordChangedResource(_store.GetScenePath(request.ProjectRoot, request.SceneName));
        return RekallAgeCommandResult<CreateGeometryExtrusionResult>.Success(
            new CreateGeometryExtrusionResult(entity.Id, mesh.Vertices.Count, mesh.Indices.Count, updated),
            $"Created geometry extrusion '{entity.Name}' with {mesh.Vertices.Count.ToString(CultureInfo.InvariantCulture)} vertices and {mesh.Indices.Count.ToString(CultureInfo.InvariantCulture)} indices.");
    }

    private static JsonArray ToProfileArray(IReadOnlyList<CreateGeometryExtrusionPoint> profile)
    {
        var array = new JsonArray();
        foreach (var point in profile)
        {
            array.Add(new JsonObject { ["x"] = point.X, ["y"] = point.Y });
        }

        return array;
    }

    private static JsonArray ToVertexArray(IReadOnlyList<CreateGeometryMeshVertex> vertices)
    {
        var array = new JsonArray();
        foreach (var vertex in vertices)
        {
            array.Add(new JsonObject
            {
                ["x"] = vertex.X,
                ["y"] = vertex.Y,
                ["z"] = vertex.Z,
                ["nx"] = vertex.NormalX ?? 0,
                ["ny"] = vertex.NormalY ?? 1,
                ["nz"] = vertex.NormalZ ?? 0,
                ["u"] = vertex.U,
                ["v"] = vertex.V
            });
        }

        return array;
    }

    private static JsonArray ToIndexArray(IReadOnlyList<ushort> indices)
    {
        var array = new JsonArray();
        foreach (var index in indices)
        {
            array.Add(index);
        }

        return array;
    }

    private static string? NormalizeColor(string color)
    {
        var normalized = string.IsNullOrWhiteSpace(color) ? "#8ab4f8" : color.Trim();
        if (normalized.Length != 7 || normalized[0] != '#')
        {
            return null;
        }

        for (var i = 1; i < normalized.Length; i++)
        {
            if (!Uri.IsHexDigit(normalized[i]))
            {
                return null;
            }
        }

        return normalized.ToLowerInvariant();
    }

    private static CreateGeometryExtrusionResult Empty()
    {
        return new CreateGeometryExtrusionResult(string.Empty, 0, 0, null);
    }
}

public sealed record RekallAgeGeometryGeneratedMesh(
    IReadOnlyList<CreateGeometryMeshVertex> Vertices,
    IReadOnlyList<ushort> Indices);

public static class RekallAgeGeometryExtrusionMeshBuilder
{
    public static RekallAgeGeometryGeneratedMesh Build(
        IReadOnlyList<CreateGeometryExtrusionPoint> profile,
        double depth)
    {
        if (!TryBuild(profile, depth, out var mesh, out var error))
        {
            throw new ArgumentException(error);
        }

        return mesh;
    }

    public static bool TryBuild(
        IReadOnlyList<CreateGeometryExtrusionPoint> profile,
        double depth,
        out RekallAgeGeometryGeneratedMesh mesh,
        out string error)
    {
        mesh = new RekallAgeGeometryGeneratedMesh([], []);
        error = string.Empty;
        if (profile.Count < 3)
        {
            error = "Geometry extrusion profile requires at least three points.";
            return false;
        }

        if (profile.Count * 6 > ushort.MaxValue)
        {
            error = "Geometry extrusion profile is too large for 16-bit mesh indices.";
            return false;
        }

        if (!IsFinite(depth) || depth <= 0)
        {
            error = "Geometry extrusion depth must be greater than zero.";
            return false;
        }

        if (profile.Any(point => !IsFinite(point.X) || !IsFinite(point.Y)))
        {
            error = "Geometry extrusion profile points must be finite numbers.";
            return false;
        }

        var oriented = profile.ToArray();
        var area = SignedArea(oriented);
        if (Math.Abs(area) <= 0.000001)
        {
            error = "Geometry extrusion profile must enclose a non-zero area.";
            return false;
        }

        if (area < 0)
        {
            Array.Reverse(oriented);
        }

        mesh = BuildOriented(oriented, depth);
        return true;
    }

    private static RekallAgeGeometryGeneratedMesh BuildOriented(
        IReadOnlyList<CreateGeometryExtrusionPoint> profile,
        double depth)
    {
        var vertices = new List<CreateGeometryMeshVertex>();
        var indices = new List<ushort>();
        var halfDepth = depth * 0.5;
        for (var i = 0; i < profile.Count; i++)
        {
            var point = profile[i];
            vertices.Add(new CreateGeometryMeshVertex(point.X, point.Y, halfDepth, NormalX: 0, NormalY: 0, NormalZ: 1));
        }

        var backStart = vertices.Count;
        for (var i = 0; i < profile.Count; i++)
        {
            var point = profile[i];
            vertices.Add(new CreateGeometryMeshVertex(point.X, point.Y, -halfDepth, NormalX: 0, NormalY: 0, NormalZ: -1));
        }

        for (var i = 1; i < profile.Count - 1; i++)
        {
            indices.Add(0);
            indices.Add((ushort)i);
            indices.Add((ushort)(i + 1));

            indices.Add((ushort)backStart);
            indices.Add((ushort)(backStart + i + 1));
            indices.Add((ushort)(backStart + i));
        }

        for (var i = 0; i < profile.Count; i++)
        {
            var next = (i + 1) % profile.Count;
            var a = profile[i];
            var b = profile[next];
            var normal = Normalize(b.Y - a.Y, -(b.X - a.X), 0);
            var start = checked((ushort)vertices.Count);
            vertices.Add(new CreateGeometryMeshVertex(a.X, a.Y, halfDepth, NormalX: normal.X, NormalY: normal.Y, NormalZ: normal.Z, U: 0, V: 0));
            vertices.Add(new CreateGeometryMeshVertex(b.X, b.Y, halfDepth, NormalX: normal.X, NormalY: normal.Y, NormalZ: normal.Z, U: 1, V: 0));
            vertices.Add(new CreateGeometryMeshVertex(b.X, b.Y, -halfDepth, NormalX: normal.X, NormalY: normal.Y, NormalZ: normal.Z, U: 1, V: 1));
            vertices.Add(new CreateGeometryMeshVertex(a.X, a.Y, -halfDepth, NormalX: normal.X, NormalY: normal.Y, NormalZ: normal.Z, U: 0, V: 1));

            indices.Add(start);
            indices.Add((ushort)(start + 2));
            indices.Add((ushort)(start + 1));
            indices.Add(start);
            indices.Add((ushort)(start + 3));
            indices.Add((ushort)(start + 2));
        }

        return new RekallAgeGeometryGeneratedMesh(vertices, indices);
    }

    private static double SignedArea(IReadOnlyList<CreateGeometryExtrusionPoint> profile)
    {
        var area = 0.0;
        for (var i = 0; i < profile.Count; i++)
        {
            var current = profile[i];
            var next = profile[(i + 1) % profile.Count];
            area += current.X * next.Y - next.X * current.Y;
        }

        return area * 0.5;
    }

    private static (double X, double Y, double Z) Normalize(double x, double y, double z)
    {
        var length = Math.Sqrt(x * x + y * y + z * z);
        return length <= 0.000001 ? (0, 1, 0) : (x / length, y / length, z / length);
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
