using System.Globalization;
using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.World;

namespace Rekall.Age.LevelDesign.Commands;

public sealed record CreateGeometryMeshVertex(
    double X,
    double Y,
    double Z,
    double? NormalX = null,
    double? NormalY = null,
    double? NormalZ = null,
    double? R = null,
    double? G = null,
    double? B = null,
    double? A = null,
    double U = 0,
    double V = 0);

public sealed record CreateGeometryMeshRequest(
    string ProjectRoot,
    string SceneName,
    string Name,
    IReadOnlyList<CreateGeometryMeshVertex> Vertices,
    IReadOnlyList<ushort> Indices,
    double X = 0,
    double Y = 0,
    double Z = 0,
    double Pitch = 0,
    double Yaw = 0,
    double Roll = 0,
    double ScaleX = 1,
    double ScaleY = 1,
    double ScaleZ = 1,
    string Color = "#8ab4f8",
    string? TextureAssetId = null);

public sealed record CreateGeometryMeshResult(
    string EntityId,
    int VertexCount,
    int IndexCount,
    RekallAgeSceneDocument? Scene);

public sealed class CreateGeometryMeshCommand
    : IRekallAgeCommand<CreateGeometryMeshRequest, CreateGeometryMeshResult>
{
    private readonly RekallAgeSceneStore _store = new();

    public string Name => "rekall.geometry.create_mesh";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Creates a renderable authored 3D triangle mesh entity with transform, geometry mesh payload, and mesh renderer components.",
        typeof(CreateGeometryMeshRequest).FullName!,
        typeof(CreateGeometryMeshResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<CreateGeometryMeshResult>> ExecuteAsync(
        CreateGeometryMeshRequest request,
        RekallAgeCommandContext context)
    {
        var color = NormalizeColor(request.Color);
        if (color is null)
        {
            var error = new RekallAgeCommandError(
                "REKALL_GEOMETRY_COLOR_INVALID",
                "Geometry mesh color must be a #RRGGBB hex color.",
                request.Color);
            return RekallAgeCommandResult<CreateGeometryMeshResult>.Failure(Empty(), error.Message, [error]);
        }

        var validation = ValidateMesh(request.Vertices, request.Indices);
        if (validation is not null)
        {
            return RekallAgeCommandResult<CreateGeometryMeshResult>.Failure(Empty(), validation.Message, [validation]);
        }

        var scene = await _store.LoadAsync(request.ProjectRoot, request.SceneName, context.CancellationToken);
        var geometryProperties = new JsonObject
        {
            ["color"] = color,
            ["vertices"] = ToVertexArray(request.Vertices, request.Indices),
            ["indices"] = ToIndexArray(request.Indices)
        };
        if (!string.IsNullOrWhiteSpace(request.TextureAssetId))
        {
            geometryProperties["textureAssetId"] = request.TextureAssetId.Trim();
        }

        var entity = RekallAgeEntityDocument.Create(request.Name, ["geometry", "mesh"])
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
                "Rekall.GeometryMesh",
                geometryProperties))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.MeshRenderer",
                new JsonObject
                {
                    ["mesh"] = "rekall.geometry.mesh"
                }));

        var updated = scene.AddEntity(entity);
        await _store.SaveAsync(request.ProjectRoot, updated, context.CancellationToken);
        context.Transaction.RecordChangedResource(_store.GetScenePath(request.ProjectRoot, request.SceneName));
        return RekallAgeCommandResult<CreateGeometryMeshResult>.Success(
            new CreateGeometryMeshResult(entity.Id, request.Vertices.Count, request.Indices.Count, updated),
            $"Created geometry mesh '{entity.Name}' with {request.Vertices.Count.ToString(CultureInfo.InvariantCulture)} vertices and {request.Indices.Count.ToString(CultureInfo.InvariantCulture)} indices.");
    }

    private static RekallAgeCommandError? ValidateMesh(
        IReadOnlyList<CreateGeometryMeshVertex> vertices,
        IReadOnlyList<ushort> indices)
    {
        if (vertices.Count == 0 || vertices.Count > ushort.MaxValue || indices.Count < 3 || indices.Count % 3 != 0)
        {
            return Invalid("Geometry mesh requires 1-65535 vertices and triangle-list indices in groups of three.");
        }

        for (var i = 0; i < vertices.Count; i++)
        {
            var vertex = vertices[i];
            if (!IsFinite(vertex.X)
                || !IsFinite(vertex.Y)
                || !IsFinite(vertex.Z)
                || !IsFinite(vertex.NormalX)
                || !IsFinite(vertex.NormalY)
                || !IsFinite(vertex.NormalZ)
                || !IsFinite(vertex.U)
                || !IsFinite(vertex.V)
                || !IsUnit(vertex.R)
                || !IsUnit(vertex.G)
                || !IsUnit(vertex.B)
                || !IsUnit(vertex.A))
            {
                return Invalid($"Geometry mesh vertex {i.ToString(CultureInfo.InvariantCulture)} contains invalid numeric data.");
            }
        }

        foreach (var index in indices)
        {
            if (index >= vertices.Count)
            {
                return Invalid("Geometry mesh indices must reference existing vertices.");
            }
        }

        return null;

        static RekallAgeCommandError Invalid(string message)
        {
            return new RekallAgeCommandError("REKALL_GEOMETRY_MESH_INVALID", message, null);
        }
    }

    private static JsonArray ToVertexArray(
        IReadOnlyList<CreateGeometryMeshVertex> vertices,
        IReadOnlyList<ushort> indices)
    {
        var array = new JsonArray();
        var inferredNormals = InferNormals(vertices, indices);
        for (var i = 0; i < vertices.Count; i++)
        {
            var vertex = vertices[i];
            var normal = ResolveNormal(vertex, inferredNormals[i]);
            var item = new JsonObject
            {
                ["x"] = vertex.X,
                ["y"] = vertex.Y,
                ["z"] = vertex.Z,
                ["nx"] = normal.X,
                ["ny"] = normal.Y,
                ["nz"] = normal.Z,
                ["u"] = vertex.U,
                ["v"] = vertex.V
            };
            AddOptionalUnit(item, "r", vertex.R);
            AddOptionalUnit(item, "g", vertex.G);
            AddOptionalUnit(item, "b", vertex.B);
            AddOptionalUnit(item, "a", vertex.A);
            array.Add(item);
        }

        return array;
    }

    private static IReadOnlyList<MeshVector3> InferNormals(
        IReadOnlyList<CreateGeometryMeshVertex> vertices,
        IReadOnlyList<ushort> indices)
    {
        var normals = Enumerable.Repeat(new MeshVector3(0, 0, 0), vertices.Count).ToArray();
        for (var i = 0; i + 2 < indices.Count; i += 3)
        {
            var aIndex = indices[i];
            var bIndex = indices[i + 1];
            var cIndex = indices[i + 2];
            var a = vertices[aIndex];
            var b = vertices[bIndex];
            var c = vertices[cIndex];
            var normal = Normalize(Cross(
                new MeshVector3(b.X - a.X, b.Y - a.Y, b.Z - a.Z),
                new MeshVector3(c.X - a.X, c.Y - a.Y, c.Z - a.Z)));
            normals[aIndex] = Add(normals[aIndex], normal);
            normals[bIndex] = Add(normals[bIndex], normal);
            normals[cIndex] = Add(normals[cIndex], normal);
        }

        for (var i = 0; i < normals.Length; i++)
        {
            normals[i] = Normalize(normals[i]);
        }

        return normals;
    }

    private static MeshVector3 ResolveNormal(CreateGeometryMeshVertex vertex, MeshVector3 inferred)
    {
        if (vertex.NormalX.HasValue || vertex.NormalY.HasValue || vertex.NormalZ.HasValue)
        {
            return Normalize(new MeshVector3(vertex.NormalX ?? 0, vertex.NormalY ?? 1, vertex.NormalZ ?? 0));
        }

        return inferred.LengthSquared <= 0.000001 ? new MeshVector3(0, 1, 0) : inferred;
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

    private static void AddOptionalUnit(JsonObject item, string name, double? value)
    {
        if (value.HasValue)
        {
            item[name] = value.Value;
        }
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

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static bool IsFinite(double? value)
    {
        return !value.HasValue || IsFinite(value.Value);
    }

    private static bool IsUnit(double? value)
    {
        return !value.HasValue || IsFinite(value.Value) && value.Value >= 0 && value.Value <= 1;
    }

    private static CreateGeometryMeshResult Empty()
    {
        return new CreateGeometryMeshResult(string.Empty, 0, 0, null);
    }

    private static MeshVector3 Add(MeshVector3 left, MeshVector3 right)
    {
        return new MeshVector3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
    }

    private static MeshVector3 Cross(MeshVector3 left, MeshVector3 right)
    {
        return new MeshVector3(
            left.Y * right.Z - left.Z * right.Y,
            left.Z * right.X - left.X * right.Z,
            left.X * right.Y - left.Y * right.X);
    }

    private static MeshVector3 Normalize(MeshVector3 value)
    {
        var length = Math.Sqrt(value.LengthSquared);
        return length <= 0.000001
            ? new MeshVector3(0, 0, 0)
            : new MeshVector3(value.X / length, value.Y / length, value.Z / length);
    }

    private readonly record struct MeshVector3(double X, double Y, double Z)
    {
        public double LengthSquared => X * X + Y * Y + Z * Z;
    }
}
