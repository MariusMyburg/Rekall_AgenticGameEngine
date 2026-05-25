using System.Globalization;
using System.Text.Json.Nodes;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Rendering;

public sealed class RekallAgeRuntimeRenderFrameBuilder
{
    public RekallAgeRuntimeViewportFrame Build(
        RekallAgeRuntimeWorld world,
        int width,
        int height,
        bool debugOverlay)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Viewport width must be greater than zero.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Viewport height must be greater than zero.");
        }

        var cameras = world.Subsystems.Rendering.Cameras
            .Select(camera =>
            {
                var transform = FindTransform(world, camera.EntityId);
                return new RekallAgeRuntimeViewportCamera(
                    camera.EntityId,
                    camera.EntityName,
                    camera.Kind,
                    camera.Active,
                    transform.Position3D.X,
                    transform.Position3D.Y,
                    transform.Position3D.Z,
                    transform.Rotation3D.X,
                    transform.Rotation3D.Y,
                    transform.Rotation3D.Z,
                    camera.ProjectionMode,
                    camera.FieldOfViewDegrees,
                    camera.OrthographicSize,
                    camera.NearClip,
                    camera.FarClip,
                    camera.ClearColor);
            })
            .OrderByDescending(camera => camera.Active)
            .ThenBy(camera => camera.EntityName, StringComparer.Ordinal)
            .ThenBy(camera => camera.EntityId, StringComparer.Ordinal)
            .ToArray();
        var activeCamera = cameras.FirstOrDefault(camera => camera.Active) ?? cameras.FirstOrDefault();
        var renderableSource = debugOverlay
            ? BuildRenderables(world).Concat(BuildColliderDebugRenderables(world))
            : BuildRenderables(world);
        var renderables = renderableSource
            .OrderBy(renderable => renderable.SortKey)
            .ThenBy(renderable => renderable.EntityName, StringComparer.Ordinal)
            .ThenBy(renderable => renderable.EntityId, StringComparer.Ordinal)
            .ToArray();

        return new RekallAgeRuntimeViewportFrame(
            world.SceneName,
            world.FrameIndex,
            world.ElapsedTime.TotalSeconds,
            width,
            height,
            activeCamera,
            cameras,
            renderables,
            world.Subsystems.Rendering.UiLayers.Count,
            new RekallAgeRuntimeViewportOverlay(debugOverlay, world.Observations.Count),
            world.Observations
                .Select(observation => new RekallAgeRuntimeViewportObservation(
                    observation.Code,
                    observation.Severity,
                    observation.Subsystem,
                    observation.TargetName.Length > 0 ? observation.TargetName : observation.TargetId,
                    observation.Message))
                .ToArray());
    }

    private static IEnumerable<RekallAgeRuntimeViewportRenderable> BuildRenderables(RekallAgeRuntimeWorld world)
    {
        foreach (var sprite in world.Subsystems.Rendering.Sprites)
        {
            var transform = FindTransform(world, sprite.EntityId);
            yield return new RekallAgeRuntimeViewportRenderable(
                sprite.EntityId,
                sprite.EntityName,
                "sprite",
                sprite.AssetId,
                transform.Position2D.X,
                transform.Position2D.Y,
                transform.Position3D.Z,
                100,
                RotationZ: transform.Rotation2D,
                ScaleX: transform.Scale2D.X,
                ScaleY: transform.Scale2D.Y);
        }

        foreach (var mesh in world.Subsystems.Rendering.Meshes)
        {
            var entity = FindEntity(world, mesh.EntityId);
            var transform = entity?.Transform ?? RekallAgeRuntimeTransform.Identity;
            var meshRendererComponent = entity?.Components.FirstOrDefault(component =>
                component.Type is "Rekall.MeshRenderer" or "Rekall.MeshSet");
            var planetComponent = entity?.Components.FirstOrDefault(component =>
                component.Type.Equals("Rekall.PlanetRenderer", StringComparison.Ordinal));
            var geometry = entity?.Components.FirstOrDefault(component =>
                component.Type.Equals("Rekall.GeometryPrimitive", StringComparison.Ordinal));
            var geometryMeshComponent = entity?.Components.FirstOrDefault(component =>
                component.Type.Equals("Rekall.GeometryMesh", StringComparison.Ordinal));
            var primitive = ReadString(geometry, "primitive");
            var geometryMesh = ReadGeometryMesh(geometryMeshComponent);
            var materialColor = ReadString(geometryMeshComponent, "color") ?? ReadString(geometry, "color");
            var textureAssetId = ReadString(geometryMeshComponent, "textureAssetId")
                ?? ReadString(geometryMeshComponent, "texture")
                ?? ReadString(geometry, "textureAssetId")
                ?? ReadString(geometry, "texture")
                ?? ReadString(planetComponent, "surfaceTexture")
                ?? ReadString(planetComponent, "SurfaceTexture");
            var variant = geometryMesh is not null
                ? "rekall.geometry.mesh"
                : planetComponent is not null
                ? "rekall.planet.surface"
                : string.IsNullOrWhiteSpace(primitive)
                ? mesh.AssetId
                : $"rekall.geometry.{primitive.Trim().ToLowerInvariant()}";
            var radius = Math.Max(0.0001, ReadNumber(planetComponent, "radius", 0.5));
            var scaleX = planetComponent is null ? transform.Scale3D.X : transform.Scale3D.X * radius * 2;
            var scaleY = planetComponent is null ? transform.Scale3D.Y : transform.Scale3D.Y * radius * 2;
            var scaleZ = planetComponent is null ? transform.Scale3D.Z : transform.Scale3D.Z * radius * 2;
            var sortKey = mesh.SortKey
                + (entity?.Components.Any(component => component.Type.Equals("Rekall.Rigidbody3D", StringComparison.Ordinal)) == true ? 20 : 0);
            yield return new RekallAgeRuntimeViewportRenderable(
                mesh.EntityId,
                mesh.EntityName,
                string.IsNullOrWhiteSpace(mesh.Kind) ? "mesh" : mesh.Kind,
                mesh.AssetId,
                transform.Position3D.X,
                transform.Position3D.Y,
                transform.Position3D.Z,
                sortKey,
                Variant: mesh.Variant ?? variant,
                RotationX: transform.Rotation3D.X,
                RotationY: transform.Rotation3D.Y,
                RotationZ: transform.Rotation3D.Z,
                ScaleX: scaleX,
                ScaleY: scaleY,
                ScaleZ: scaleZ,
                MaterialColor: mesh.MaterialColor ?? ReadString(planetComponent, "color") ?? ReadString(planetComponent, "Color") ?? materialColor,
                GeometryMesh: geometryMesh,
                TextureAssetId: mesh.TextureAssetId ?? textureAssetId,
                ShaderPipeline: ToViewportShaderPipeline(mesh.ShaderPipeline) ?? ReadShaderPipeline(meshRendererComponent));
        }

        foreach (var light in world.Subsystems.Rendering.Lights)
        {
            var transform = FindTransform(world, light.EntityId);
            yield return new RekallAgeRuntimeViewportRenderable(
                light.EntityId,
                light.EntityName,
                "light",
                null,
                transform.Position3D.X,
                transform.Position3D.Y,
                transform.Position3D.Z,
                300,
                Variant: light.Kind,
                RotationX: transform.Rotation3D.X,
                RotationY: transform.Rotation3D.Y,
                RotationZ: transform.Rotation3D.Z,
                Intensity: light.Intensity);
        }

        foreach (var uiLayer in world.Subsystems.Rendering.UiLayers)
        {
            yield return new RekallAgeRuntimeViewportRenderable(
                uiLayer.EntityId,
                uiLayer.EntityName,
                "ui",
                null,
                0,
                uiLayer.Layer,
                0,
                400 + uiLayer.Layer);
        }
    }

    private static IEnumerable<RekallAgeRuntimeViewportRenderable> BuildColliderDebugRenderables(RekallAgeRuntimeWorld world)
    {
        foreach (var entity in world.Entities)
        {
            var transform = entity.Transform;
            var collider = entity.Components.FirstOrDefault(component =>
                component.Type is
                    "Rekall.BoxCollider3D" or
                    "Rekall.SphereCollider3D" or
                    "Rekall.CapsuleCollider3D" or
                    "Rekall.MeshCollider");
            if (collider is null)
            {
                continue;
            }

            switch (collider.Type)
            {
                case "Rekall.BoxCollider3D":
                    yield return new RekallAgeRuntimeViewportRenderable(
                        $"{entity.Id}:collider",
                        $"{entity.Name} Collider",
                        "mesh",
                        null,
                        transform.Position3D.X,
                        transform.Position3D.Y,
                        transform.Position3D.Z,
                        920,
                        Variant: "rekall.geometry.cube",
                        RotationX: transform.Rotation3D.X,
                        RotationY: transform.Rotation3D.Y,
                        RotationZ: transform.Rotation3D.Z,
                        ScaleX: ReadNumber(collider, "width", 1),
                        ScaleY: ReadNumber(collider, "height", 1),
                        ScaleZ: ReadNumber(collider, "depth", 1),
                        MaterialColor: "#33ddff66");
                    break;
                case "Rekall.SphereCollider3D":
                    var radius = Math.Max(0.0001, ReadNumber(collider, "radius", 0.5));
                    yield return new RekallAgeRuntimeViewportRenderable(
                        $"{entity.Id}:collider",
                        $"{entity.Name} Collider",
                        "mesh",
                        null,
                        transform.Position3D.X,
                        transform.Position3D.Y,
                        transform.Position3D.Z,
                        940,
                        Variant: "rekall.geometry.sphere",
                        RotationX: transform.Rotation3D.X,
                        RotationY: transform.Rotation3D.Y,
                        RotationZ: transform.Rotation3D.Z,
                        ScaleX: radius * 2,
                        ScaleY: radius * 2,
                        ScaleZ: radius * 2,
                        MaterialColor: "#ffea0066");
                    break;
                case "Rekall.CapsuleCollider3D":
                    var capsuleRadius = Math.Max(0.0001, ReadNumber(collider, "radius", 0.5));
                    var length = Math.Max(0.0001, ReadNumber(collider, "length", 1));
                    yield return new RekallAgeRuntimeViewportRenderable(
                        $"{entity.Id}:collider",
                        $"{entity.Name} Collider",
                        "mesh",
                        null,
                        transform.Position3D.X,
                        transform.Position3D.Y,
                        transform.Position3D.Z,
                        940,
                        Variant: "rekall.geometry.cylinder",
                        RotationX: transform.Rotation3D.X,
                        RotationY: transform.Rotation3D.Y,
                        RotationZ: transform.Rotation3D.Z,
                        ScaleX: capsuleRadius * 2,
                        ScaleY: length + capsuleRadius * 2,
                        ScaleZ: capsuleRadius * 2,
                        MaterialColor: "#ff66ff66");
                    break;
                case "Rekall.MeshCollider":
                    var geometryMesh = ReadGeometryMesh(entity.Components.FirstOrDefault(component =>
                        component.Type.Equals("Rekall.GeometryMesh", StringComparison.Ordinal)));
                    if (geometryMesh is null)
                    {
                        break;
                    }

                    yield return new RekallAgeRuntimeViewportRenderable(
                        $"{entity.Id}:collider",
                        $"{entity.Name} Collider",
                        "mesh",
                        null,
                        transform.Position3D.X,
                        transform.Position3D.Y,
                        transform.Position3D.Z,
                        930,
                        Variant: "rekall.geometry.mesh",
                        RotationX: transform.Rotation3D.X,
                        RotationY: transform.Rotation3D.Y,
                        RotationZ: transform.Rotation3D.Z,
                        ScaleX: transform.Scale3D.X,
                        ScaleY: transform.Scale3D.Y,
                        ScaleZ: transform.Scale3D.Z,
                        MaterialColor: "#66ff9966",
                        GeometryMesh: geometryMesh);
                    break;
            }
        }
    }

    private static RekallAgeRuntimeTransform FindTransform(RekallAgeRuntimeWorld world, string entityId)
    {
        return world.Entities.FirstOrDefault(entity => entity.Id.Equals(entityId, StringComparison.Ordinal))?.Transform
            ?? RekallAgeRuntimeTransform.Identity;
    }

    private static RekallAgeRuntimeEntity? FindEntity(RekallAgeRuntimeWorld world, string entityId)
    {
        return world.Entities.FirstOrDefault(entity => entity.Id.Equals(entityId, StringComparison.Ordinal));
    }

    private static string? ReadString(RekallAgeRuntimeComponent? component, string name)
    {
        if (component is null
            || !TryGetPropertyValue(component.Properties, name, out var node)
            || node is not JsonValue value)
        {
            return null;
        }

        return value.TryGetValue<string>(out var text) ? text : null;
    }

    private static double ReadNumber(RekallAgeRuntimeComponent? component, string name, double fallback)
    {
        return component is null ? fallback : ReadNumber(component.Properties, name, fallback);
    }

    private static RekallAgeRuntimeViewportShaderPipeline? ReadShaderPipeline(RekallAgeRuntimeComponent? component)
    {
        var vertexShader = ReadString(component, "vertexShader");
        var fragmentShader = ReadString(component, "fragmentShader");
        return string.IsNullOrWhiteSpace(vertexShader) || string.IsNullOrWhiteSpace(fragmentShader)
            ? null
            : new RekallAgeRuntimeViewportShaderPipeline(vertexShader.Trim(), fragmentShader.Trim());
    }

    private static RekallAgeRuntimeViewportShaderPipeline? ToViewportShaderPipeline(
        RekallAgeRuntimeRenderShaderPipeline? pipeline)
    {
        return pipeline is null
            ? null
            : new RekallAgeRuntimeViewportShaderPipeline(
                pipeline.VertexShader.Trim(),
                pipeline.FragmentShader.Trim());
    }

    private static RekallAgeRuntimeViewportGeometryMesh? ReadGeometryMesh(RekallAgeRuntimeComponent? component)
    {
        if (component is null
            || !component.Properties.TryGetPropertyValue("vertices", out var verticesNode)
            || verticesNode is not JsonArray verticesArray
            || !component.Properties.TryGetPropertyValue("indices", out var indicesNode)
            || indicesNode is not JsonArray indicesArray)
        {
            return null;
        }

        var indices = new List<ushort>(indicesArray.Count);
        foreach (var node in indicesArray)
        {
            if (node is not JsonValue value || !TryReadUInt16(value, verticesArray.Count, out var index))
            {
                return null;
            }

            indices.Add(index);
        }

        if (indices.Count < 3 || indices.Count % 3 != 0)
        {
            return null;
        }

        var materialColor = ParseColor(ReadString(component, "color"));
        var vertices = new List<ParsedGeometryVertex>(verticesArray.Count);
        foreach (var node in verticesArray)
        {
            if (node is not JsonObject vertex)
            {
                return null;
            }

            var color = ReadVertexColor(vertex, materialColor);
            vertices.Add(new ParsedGeometryVertex(
                ReadNumber(vertex, "x", 0),
                ReadNumber(vertex, "y", 0),
                ReadNumber(vertex, "z", 0),
                ReadOptionalNumber(vertex, "nx") ?? ReadOptionalNumber(vertex, "normalX"),
                ReadOptionalNumber(vertex, "ny") ?? ReadOptionalNumber(vertex, "normalY"),
                ReadOptionalNumber(vertex, "nz") ?? ReadOptionalNumber(vertex, "normalZ"),
                color.R,
                color.G,
                color.B,
                color.A,
                ReadNumber(vertex, "u", 0),
                ReadNumber(vertex, "v", 0)));
        }

        if (vertices.Count == 0 || vertices.Count > ushort.MaxValue)
        {
            return null;
        }

        return new RekallAgeRuntimeViewportGeometryMesh(CreateGeometryVertices(vertices, indices), indices);
    }

    private static SceneColor ReadVertexColor(JsonObject vertex, SceneColor fallback)
    {
        return new SceneColor(
            ReadUnit(vertex, "r", fallback.R),
            ReadUnit(vertex, "g", fallback.G),
            ReadUnit(vertex, "b", fallback.B),
            ReadUnit(vertex, "a", fallback.A));
    }

    private static double ReadUnit(JsonObject properties, string name, double fallback)
    {
        return Math.Clamp(ReadNumber(properties, name, fallback), 0, 1);
    }

    private static double ReadNumber(JsonObject properties, string name, double fallback)
    {
        if (!TryGetPropertyValue(properties, name, out var node) || node is not JsonValue value)
        {
            return fallback;
        }

        if (value.TryGetValue<double>(out var doubleValue))
        {
            return doubleValue;
        }

        if (value.TryGetValue<int>(out var intValue))
        {
            return intValue;
        }

        if (value.TryGetValue<long>(out var longValue))
        {
            return longValue;
        }

        return value.TryGetValue<string>(out var text)
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    private static bool TryGetPropertyValue(JsonObject properties, string name, out JsonNode? node)
    {
        if (properties.TryGetPropertyValue(name, out node))
        {
            return true;
        }

        if (name.Length > 0)
        {
            var pascalName = char.ToUpperInvariant(name[0]) + name[1..];
            if (properties.TryGetPropertyValue(pascalName, out node))
            {
                return true;
            }
        }

        node = null;
        return false;
    }

    private static double? ReadOptionalNumber(JsonObject properties, string name)
    {
        return properties.TryGetPropertyValue(name, out var node) && node is JsonValue value
            ? ReadNumber(value)
            : null;
    }

    private static double? ReadNumber(JsonValue value)
    {
        if (value.TryGetValue<double>(out var doubleValue))
        {
            return doubleValue;
        }

        if (value.TryGetValue<int>(out var intValue))
        {
            return intValue;
        }

        if (value.TryGetValue<long>(out var longValue))
        {
            return longValue;
        }

        return value.TryGetValue<string>(out var text)
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static IReadOnlyList<RekallAgeRuntimeViewportGeometryVertex> CreateGeometryVertices(
        IReadOnlyList<ParsedGeometryVertex> vertices,
        IReadOnlyList<ushort> indices)
    {
        var inferredNormals = InferNormals(vertices, indices);
        var result = new RekallAgeRuntimeViewportGeometryVertex[vertices.Count];
        for (var i = 0; i < vertices.Count; i++)
        {
            var vertex = vertices[i];
            var normal = ResolveNormal(vertex, inferredNormals[i]);
            result[i] = new RekallAgeRuntimeViewportGeometryVertex(
                vertex.X,
                vertex.Y,
                vertex.Z,
                normal.X,
                normal.Y,
                normal.Z,
                vertex.R,
                vertex.G,
                vertex.B,
                vertex.A,
                vertex.U,
                vertex.V);
        }

        return result;
    }

    private static IReadOnlyList<MeshVector3> InferNormals(
        IReadOnlyList<ParsedGeometryVertex> vertices,
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

    private static MeshVector3 ResolveNormal(ParsedGeometryVertex vertex, MeshVector3 inferred)
    {
        if (vertex.NormalX.HasValue || vertex.NormalY.HasValue || vertex.NormalZ.HasValue)
        {
            return Normalize(new MeshVector3(vertex.NormalX ?? 0, vertex.NormalY ?? 1, vertex.NormalZ ?? 0));
        }

        return inferred.LengthSquared <= 0.000001 ? new MeshVector3(0, 1, 0) : inferred;
    }

    private static bool TryReadUInt16(JsonValue value, int vertexCount, out ushort index)
    {
        index = 0;
        int integer;
        if (!value.TryGetValue<int>(out integer))
        {
            if (value.TryGetValue<long>(out var longValue) && longValue >= int.MinValue && longValue <= int.MaxValue)
            {
                integer = (int)longValue;
            }
            else
            {
                return false;
            }
        }

        if (integer < 0 || integer >= vertexCount || integer > ushort.MaxValue)
        {
            return false;
        }

        index = (ushort)integer;
        return true;
    }

    private static SceneColor ParseColor(string? color)
    {
        if (color is { Length: 7 } && color[0] == '#'
            && byte.TryParse(color.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
            && byte.TryParse(color.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
            && byte.TryParse(color.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return new SceneColor(r / 255d, g / 255d, b / 255d, 1);
        }

        return new SceneColor(0.35, 0.58, 0.85, 1);
    }

    private readonly record struct SceneColor(double R, double G, double B, double A);

    private readonly record struct ParsedGeometryVertex(
        double X,
        double Y,
        double Z,
        double? NormalX,
        double? NormalY,
        double? NormalZ,
        double R,
        double G,
        double B,
        double A,
        double U,
        double V);

    private readonly record struct MeshVector3(double X, double Y, double Z)
    {
        public double LengthSquared => X * X + Y * Y + Z * Z;
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
}
