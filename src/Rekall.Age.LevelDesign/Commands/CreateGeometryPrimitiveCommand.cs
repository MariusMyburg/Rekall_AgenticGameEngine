using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.World;

namespace Rekall.Age.LevelDesign.Commands;

public sealed record CreateGeometryPrimitiveRequest(
    string ProjectRoot,
    string SceneName,
    string Name,
    string Primitive,
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

public sealed record CreateGeometryPrimitiveResult(
    string EntityId,
    string Primitive,
    RekallAgeSceneDocument? Scene);

public sealed class CreateGeometryPrimitiveCommand
    : IRekallAgeCommand<CreateGeometryPrimitiveRequest, CreateGeometryPrimitiveResult>
{
    private static readonly HashSet<string> SupportedPrimitives = new(StringComparer.Ordinal)
    {
        "cube",
        "sphere",
        "cylinder",
        "cone",
        "plane"
    };

    private readonly RekallAgeSceneStore _store = new();

    public string Name => "rekall.geometry.create_primitive";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Creates a renderable 3D geometry primitive entity with transform, primitive metadata, and mesh renderer components.",
        typeof(CreateGeometryPrimitiveRequest).FullName!,
        typeof(CreateGeometryPrimitiveResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<CreateGeometryPrimitiveResult>> ExecuteAsync(
        CreateGeometryPrimitiveRequest request,
        RekallAgeCommandContext context)
    {
        var primitive = NormalizePrimitive(request.Primitive);
        if (primitive is null)
        {
            var error = new RekallAgeCommandError(
                "REKALL_GEOMETRY_PRIMITIVE_UNSUPPORTED",
                $"Geometry primitive '{request.Primitive}' is not supported. Use cube, sphere, cylinder, cone, or plane.",
                request.Primitive);
            return RekallAgeCommandResult<CreateGeometryPrimitiveResult>.Failure(Empty(), error.Message, [error]);
        }

        var color = NormalizeColor(request.Color);
        if (color is null)
        {
            var error = new RekallAgeCommandError(
                "REKALL_GEOMETRY_COLOR_INVALID",
                "Geometry primitive color must be a #RRGGBB hex color.",
                request.Color);
            return RekallAgeCommandResult<CreateGeometryPrimitiveResult>.Failure(Empty(), error.Message, [error]);
        }

        var scene = await _store.LoadAsync(request.ProjectRoot, request.SceneName, context.CancellationToken);
        var entity = RekallAgeEntityDocument.Create(request.Name, ["geometry", "primitive", primitive])
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
                "Rekall.GeometryPrimitive",
                new JsonObject
                {
                    ["primitive"] = primitive,
                    ["color"] = color
                }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.MeshRenderer",
                new JsonObject
                {
                    ["mesh"] = $"rekall.geometry.{primitive}"
                }));
        var updated = scene.AddEntity(entity);
        await _store.SaveAsync(request.ProjectRoot, updated, context.CancellationToken);
        context.Transaction.RecordChangedResource(_store.GetScenePath(request.ProjectRoot, request.SceneName));
        return RekallAgeCommandResult<CreateGeometryPrimitiveResult>.Success(
            new CreateGeometryPrimitiveResult(entity.Id, primitive, updated),
            $"Created {primitive} geometry primitive '{entity.Name}'.");
    }

    private static string? NormalizePrimitive(string primitive)
    {
        var normalized = primitive.Trim().ToLowerInvariant();
        return SupportedPrimitives.Contains(normalized) ? normalized : null;
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

    private static CreateGeometryPrimitiveResult Empty()
    {
        return new CreateGeometryPrimitiveResult(string.Empty, string.Empty, null);
    }
}
