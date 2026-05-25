using System.Globalization;
using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.World;

namespace Rekall.Age.LevelDesign.Commands;

public sealed record CreateGeometryRecipePart(
    string Kind,
    double X = 0,
    double Y = 0,
    double Z = 0,
    double Pitch = 0,
    double Yaw = 0,
    double Roll = 0,
    double ScaleX = 1,
    double ScaleY = 1,
    double ScaleZ = 1,
    string? Color = null,
    int Segments = 24,
    int Rings = 12);

public sealed record CreateGeometryRecipeRequest(
    string ProjectRoot,
    string SceneName,
    string Name,
    IReadOnlyList<CreateGeometryRecipePart> Parts,
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

public sealed record CreateGeometryRecipeResult(
    string EntityId,
    int PartCount,
    int VertexCount,
    int IndexCount,
    RekallAgeSceneDocument? Scene);

public sealed class CreateGeometryRecipeCommand
    : IRekallAgeCommand<CreateGeometryRecipeRequest, CreateGeometryRecipeResult>
{
    private readonly RekallAgeSceneStore _store = new();

    public string Name => "rekall.geometry.create_recipe";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Creates a renderable authored 3D mesh from high-level procedural recipe parts such as ellipsoids, spheres, and capsules.",
        typeof(CreateGeometryRecipeRequest).FullName!,
        typeof(CreateGeometryRecipeResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<CreateGeometryRecipeResult>> ExecuteAsync(
        CreateGeometryRecipeRequest request,
        RekallAgeCommandContext context)
    {
        var defaultColor = NormalizeColor(request.Color);
        if (defaultColor is null)
        {
            return Failure("Geometry recipe color must be a #RRGGBB hex color.", request.Color);
        }

        var validation = ValidateRecipe(request, defaultColor);
        if (validation is not null)
        {
            return RekallAgeCommandResult<CreateGeometryRecipeResult>.Failure(Empty(), validation.Message, [validation]);
        }

        RecipeMesh mesh;
        try
        {
            mesh = BuildMesh(request.Parts, defaultColor);
        }
        catch (ArgumentException ex)
        {
            return Failure(ex.Message, null);
        }

        var meshResult = await new CreateGeometryMeshCommand().ExecuteAsync(
            new CreateGeometryMeshRequest(
                request.ProjectRoot,
                request.SceneName,
                request.Name,
                mesh.Vertices,
                mesh.Indices,
                request.X,
                request.Y,
                request.Z,
                request.Pitch,
                request.Yaw,
                request.Roll,
                request.ScaleX,
                request.ScaleY,
                request.ScaleZ,
                defaultColor),
            context);

        if (!meshResult.Ok)
        {
            return RekallAgeCommandResult<CreateGeometryRecipeResult>.Failure(
                Empty(),
                meshResult.Summary,
                meshResult.Errors);
        }

        var scene = meshResult.Value.Scene
            ?? await _store.LoadAsync(request.ProjectRoot, request.SceneName, context.CancellationToken);
        var updated = scene.UpdateEntity(
            meshResult.Value.EntityId,
            entity => (entity with { Tags = AddTag(entity.Tags, "recipe") })
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.GeometryRecipe",
                    new JsonObject
                    {
                        ["version"] = 1,
                        ["defaultColor"] = defaultColor,
                        ["parts"] = ToPartsArray(request.Parts, defaultColor)
                    })));

        await _store.SaveAsync(request.ProjectRoot, updated, context.CancellationToken);
        context.Transaction.RecordChangedResource(_store.GetScenePath(request.ProjectRoot, request.SceneName));

        return RekallAgeCommandResult<CreateGeometryRecipeResult>.Success(
            new CreateGeometryRecipeResult(
                meshResult.Value.EntityId,
                request.Parts.Count,
                mesh.Vertices.Count,
                mesh.Indices.Count,
                updated),
            $"Created geometry recipe '{request.Name}' with {request.Parts.Count.ToString(CultureInfo.InvariantCulture)} parts, {mesh.Vertices.Count.ToString(CultureInfo.InvariantCulture)} vertices, and {mesh.Indices.Count.ToString(CultureInfo.InvariantCulture)} indices.");
    }

    private static RekallAgeCommandError? ValidateRecipe(CreateGeometryRecipeRequest request, string defaultColor)
    {
        if (request.Parts is null || request.Parts.Count == 0)
        {
            return Invalid("Geometry recipe requires at least one part.");
        }

        if (!IsFinite(request.X)
            || !IsFinite(request.Y)
            || !IsFinite(request.Z)
            || !IsFinite(request.Pitch)
            || !IsFinite(request.Yaw)
            || !IsFinite(request.Roll)
            || !IsPositiveFinite(request.ScaleX)
            || !IsPositiveFinite(request.ScaleY)
            || !IsPositiveFinite(request.ScaleZ))
        {
            return Invalid("Geometry recipe transform contains invalid numeric data.");
        }

        for (var i = 0; i < request.Parts.Count; i++)
        {
            var part = request.Parts[i];
            if (NormalizeKind(part.Kind) is null)
            {
                return Invalid($"Geometry recipe part {i.ToString(CultureInfo.InvariantCulture)} has unsupported kind '{part.Kind}'.");
            }

            if (!IsFinite(part.X)
                || !IsFinite(part.Y)
                || !IsFinite(part.Z)
                || !IsFinite(part.Pitch)
                || !IsFinite(part.Yaw)
                || !IsFinite(part.Roll)
                || !IsPositiveFinite(part.ScaleX)
                || !IsPositiveFinite(part.ScaleY)
                || !IsPositiveFinite(part.ScaleZ))
            {
                return Invalid($"Geometry recipe part {i.ToString(CultureInfo.InvariantCulture)} contains invalid numeric data.");
            }

            if (part.Color is not null && NormalizeColor(part.Color) is null)
            {
                return Invalid($"Geometry recipe part {i.ToString(CultureInfo.InvariantCulture)} color must be a #RRGGBB hex color.");
            }
        }

        return null;

        static RekallAgeCommandError Invalid(string message)
        {
            return new RekallAgeCommandError("REKALL_GEOMETRY_RECIPE_INVALID", message, null);
        }
    }

    private static RecipeMesh BuildMesh(IReadOnlyList<CreateGeometryRecipePart> parts, string defaultColor)
    {
        var vertices = new List<CreateGeometryMeshVertex>();
        var indices = new List<ushort>();
        foreach (var part in parts)
        {
            var kind = NormalizeKind(part.Kind)!;
            if (kind.Equals("capsule", StringComparison.Ordinal))
            {
                AddCapsule(vertices, indices, part, NormalizeColor(part.Color ?? defaultColor)!);
            }
            else
            {
                AddEllipsoid(vertices, indices, part, NormalizeColor(part.Color ?? defaultColor)!);
            }
        }

        return new RecipeMesh(vertices, indices);
    }

    private static void AddEllipsoid(
        List<CreateGeometryMeshVertex> vertices,
        List<ushort> indices,
        CreateGeometryRecipePart part,
        string color)
    {
        var segments = Clamp(part.Segments, 8, 64);
        var rings = Clamp(part.Rings, 4, 32);
        var start = vertices.Count;
        var (red, green, blue) = ReadColor(color);

        for (var ring = 0; ring <= rings; ring++)
        {
            var phi = Math.PI * ring / rings;
            var y = Math.Cos(phi) * 0.5;
            var radius = Math.Sin(phi) * 0.5;
            for (var segment = 0; segment <= segments; segment++)
            {
                var theta = Math.Tau * segment / segments;
                var unitNormal = new RecipeVector3(
                    Math.Cos(theta) * Math.Sin(phi),
                    Math.Cos(phi),
                    Math.Sin(theta) * Math.Sin(phi));
                var local = new RecipeVector3(
                    Math.Cos(theta) * radius * part.ScaleX,
                    y * part.ScaleY,
                    Math.Sin(theta) * radius * part.ScaleZ);
                var point = TransformPoint(local, part);
                var normal = Rotate(unitNormal, part.Pitch, part.Yaw, part.Roll).Normalize();
                AddVertex(vertices, point, normal, red, green, blue, (double)segment / segments, (double)ring / rings);
            }
        }

        AddGridIndices(indices, start, rings, segments);
    }

    private static void AddCapsule(
        List<CreateGeometryMeshVertex> vertices,
        List<ushort> indices,
        CreateGeometryRecipePart part,
        string color)
    {
        var segments = Clamp(part.Segments, 8, 64);
        var capRings = Math.Max(3, Clamp(part.Rings, 6, 32) / 2);
        var horizontalRadius = Math.Max(part.ScaleX, part.ScaleZ) * 0.5;
        var capRadiusY = Math.Min(part.ScaleY * 0.5, horizontalRadius);
        var cylinderHalf = Math.Max(0, part.ScaleY * 0.5 - capRadiusY);
        var profile = new List<CapsuleProfilePoint>();

        for (var i = 0; i <= capRings; i++)
        {
            var angle = -Math.PI / 2 + Math.PI * 0.5 * i / capRings;
            profile.Add(new CapsuleProfilePoint(
                -cylinderHalf + Math.Sin(angle) * capRadiusY,
                Math.Cos(angle),
                Math.Sin(angle)));
        }

        if (cylinderHalf > 0.000001)
        {
            profile.Add(new CapsuleProfilePoint(cylinderHalf, 1, 0));
        }

        for (var i = 1; i <= capRings; i++)
        {
            var angle = Math.PI * 0.5 * i / capRings;
            profile.Add(new CapsuleProfilePoint(
                cylinderHalf + Math.Sin(angle) * capRadiusY,
                Math.Cos(angle),
                Math.Sin(angle)));
        }

        var start = vertices.Count;
        var (red, green, blue) = ReadColor(color);
        for (var ring = 0; ring < profile.Count; ring++)
        {
            var point = profile[ring];
            for (var segment = 0; segment <= segments; segment++)
            {
                var theta = Math.Tau * segment / segments;
                var horizontalNormal = new RecipeVector3(Math.Cos(theta), 0, Math.Sin(theta));
                var unitNormal = new RecipeVector3(
                    horizontalNormal.X * point.Radial,
                    point.NormalY,
                    horizontalNormal.Z * point.Radial).Normalize();
                var local = new RecipeVector3(
                    Math.Cos(theta) * point.Radial * part.ScaleX * 0.5,
                    point.Y,
                    Math.Sin(theta) * point.Radial * part.ScaleZ * 0.5);
                var transformed = TransformPoint(local, part);
                var normal = Rotate(unitNormal, part.Pitch, part.Yaw, part.Roll).Normalize();
                AddVertex(vertices, transformed, normal, red, green, blue, (double)segment / segments, (double)ring / (profile.Count - 1));
            }
        }

        AddGridIndices(indices, start, profile.Count - 1, segments);
    }

    private static void AddGridIndices(List<ushort> indices, int start, int rings, int segments)
    {
        for (var ring = 0; ring < rings; ring++)
        {
            for (var segment = 0; segment < segments; segment++)
            {
                var a = CheckedIndex(start + ring * (segments + 1) + segment);
                var b = CheckedIndex(start + (ring + 1) * (segments + 1) + segment);
                indices.Add(a);
                indices.Add(CheckedIndex(a + 1));
                indices.Add(b);
                indices.Add(CheckedIndex(a + 1));
                indices.Add(CheckedIndex(b + 1));
                indices.Add(b);
            }
        }
    }

    private static void AddVertex(
        List<CreateGeometryMeshVertex> vertices,
        RecipeVector3 point,
        RecipeVector3 normal,
        double red,
        double green,
        double blue,
        double u,
        double v)
    {
        if (vertices.Count >= ushort.MaxValue)
        {
            throw new ArgumentException("Geometry recipe generated more than 65535 vertices.");
        }

        vertices.Add(new CreateGeometryMeshVertex(
            point.X,
            point.Y,
            point.Z,
            normal.X,
            normal.Y,
            normal.Z,
            red,
            green,
            blue,
            1,
            u,
            v));
    }

    private static RecipeVector3 TransformPoint(RecipeVector3 local, CreateGeometryRecipePart part)
    {
        var rotated = Rotate(local, part.Pitch, part.Yaw, part.Roll);
        return new RecipeVector3(rotated.X + part.X, rotated.Y + part.Y, rotated.Z + part.Z);
    }

    private static RecipeVector3 Rotate(RecipeVector3 value, double pitch, double yaw, double roll)
    {
        var xRadians = DegreesToRadians(pitch);
        var yRadians = DegreesToRadians(yaw);
        var zRadians = DegreesToRadians(roll);

        var x = value.X;
        var y = value.Y * Math.Cos(xRadians) - value.Z * Math.Sin(xRadians);
        var z = value.Y * Math.Sin(xRadians) + value.Z * Math.Cos(xRadians);

        var yawX = x * Math.Cos(yRadians) + z * Math.Sin(yRadians);
        var yawZ = -x * Math.Sin(yRadians) + z * Math.Cos(yRadians);
        x = yawX;
        z = yawZ;

        var rollX = x * Math.Cos(zRadians) - y * Math.Sin(zRadians);
        var rollY = x * Math.Sin(zRadians) + y * Math.Cos(zRadians);
        return new RecipeVector3(rollX, rollY, z);
    }

    private static JsonArray ToPartsArray(IReadOnlyList<CreateGeometryRecipePart> parts, string defaultColor)
    {
        var array = new JsonArray();
        foreach (var part in parts)
        {
            array.Add(new JsonObject
            {
                ["kind"] = NormalizeKind(part.Kind),
                ["x"] = part.X,
                ["y"] = part.Y,
                ["z"] = part.Z,
                ["pitch"] = part.Pitch,
                ["yaw"] = part.Yaw,
                ["roll"] = part.Roll,
                ["scaleX"] = part.ScaleX,
                ["scaleY"] = part.ScaleY,
                ["scaleZ"] = part.ScaleZ,
                ["color"] = NormalizeColor(part.Color ?? defaultColor),
                ["segments"] = Clamp(part.Segments, 8, 64),
                ["rings"] = Clamp(part.Rings, 4, 32)
            });
        }

        return array;
    }

    private static IReadOnlyList<string> AddTag(IReadOnlyList<string> tags, string tag)
    {
        return tags
            .Append(tag)
            .Select(item => item.Trim().ToLowerInvariant())
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
    }

    private static string? NormalizeKind(string kind)
    {
        var normalized = string.IsNullOrWhiteSpace(kind) ? string.Empty : kind.Trim().ToLowerInvariant();
        return normalized switch
        {
            "sphere" => "ellipsoid",
            "ellipsoid" => "ellipsoid",
            "capsule" => "capsule",
            _ => null
        };
    }

    private static string? NormalizeColor(string? color)
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

    private static (double Red, double Green, double Blue) ReadColor(string color)
    {
        return (
            int.Parse(color.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0,
            int.Parse(color.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0,
            int.Parse(color.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0);
    }

    private static int Clamp(int value, int min, int max)
    {
        return Math.Min(max, Math.Max(min, value));
    }

    private static ushort CheckedIndex(int value)
    {
        if (value < 0 || value > ushort.MaxValue)
        {
            throw new ArgumentException("Geometry recipe generated indices outside the 16-bit mesh range.");
        }

        return (ushort)value;
    }

    private static double DegreesToRadians(double degrees)
    {
        return degrees * Math.PI / 180;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static bool IsPositiveFinite(double value)
    {
        return IsFinite(value) && value > 0;
    }

    private static RekallAgeCommandResult<CreateGeometryRecipeResult> Failure(string message, string? target)
    {
        var error = new RekallAgeCommandError("REKALL_GEOMETRY_RECIPE_INVALID", message, target);
        return RekallAgeCommandResult<CreateGeometryRecipeResult>.Failure(Empty(), message, [error]);
    }

    private static CreateGeometryRecipeResult Empty()
    {
        return new CreateGeometryRecipeResult(string.Empty, 0, 0, 0, null);
    }

    private sealed record RecipeMesh(
        IReadOnlyList<CreateGeometryMeshVertex> Vertices,
        IReadOnlyList<ushort> Indices);

    private readonly record struct CapsuleProfilePoint(double Y, double Radial, double NormalY);

    private readonly record struct RecipeVector3(double X, double Y, double Z)
    {
        public RecipeVector3 Normalize()
        {
            var length = Math.Sqrt(X * X + Y * Y + Z * Z);
            return length <= 0.000001
                ? new RecipeVector3(0, 1, 0)
                : new RecipeVector3(X / length, Y / length, Z / length);
        }
    }
}
