using System.Globalization;
using System.Numerics;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Rendering;

public sealed class RekallAgeVulkanSceneBatchBuilder
{
    public RekallAgeVulkanSceneBatch Build(
        RekallAgeRuntimeViewportFrame frame,
        IReadOnlyList<RekallAgeVulkanSceneMesh> meshes)
    {
        var renderablesByEntityId = BuildRenderableLookup(frame);
        var vertices = BuildLocalVertices(meshes);
        var indices = FlattenIndices(renderablesByEntityId, meshes, out var draws, out var bounds);
        return new RekallAgeVulkanSceneBatch(
            vertices,
            indices,
            draws,
            BuildFrameUniform(frame, bounds),
            BuildStereoFrame(frame, bounds));
    }

    private static IReadOnlyList<RekallAgeVulkanSceneVertex> BuildLocalVertices(
        IReadOnlyList<RekallAgeVulkanSceneMesh> meshes)
    {
        var vertexCount = 0;
        foreach (var mesh in meshes)
        {
            vertexCount = checked(vertexCount + mesh.Vertices.Count);
        }

        var vertices = new List<RekallAgeVulkanSceneVertex>(vertexCount);
        foreach (var mesh in meshes)
        {
            vertices.AddRange(mesh.Vertices);
        }

        return vertices;
    }

    private static IReadOnlyList<uint> FlattenIndices(
        IReadOnlyDictionary<string, RekallAgeRuntimeViewportRenderable> renderablesByEntityId,
        IReadOnlyList<RekallAgeVulkanSceneMesh> meshes,
        out IReadOnlyList<RekallAgeVulkanSceneDraw> draws,
        out SceneBounds bounds)
    {
        var indexCount = 0;
        foreach (var mesh in meshes)
        {
            indexCount = checked(indexCount + mesh.Indices.Count);
        }

        var indices = new List<uint>(indexCount);
        var ranges = new List<RekallAgeVulkanSceneDraw>(meshes.Count);
        bounds = SceneBounds.Empty;
        var vertexOffset = 0;
        foreach (var mesh in meshes)
        {
            renderablesByEntityId.TryGetValue(mesh.EntityId, out var renderable);
            var model = CreateModelMatrix(renderable);
            ranges.Add(new RekallAgeVulkanSceneDraw(
                (uint)indices.Count,
                (uint)mesh.Indices.Count,
                vertexOffset,
                (uint)mesh.Vertices.Count,
                model,
                mesh.BaseColorTexture?.Id,
                mesh.MetallicRoughnessTexture?.Id,
                mesh.NormalTexture?.Id,
                mesh.OcclusionTexture?.Id,
                mesh.EmissiveTexture?.Id,
                new Vector4(
                    Math.Clamp(mesh.MetallicFactor, 0, 1),
                    Math.Clamp(mesh.RoughnessFactor, 0.04f, 1),
                    mesh.NormalTexture is null ? 0 : Math.Clamp(mesh.NormalScale, 0, 4),
                    mesh.OcclusionTexture is null ? 0 : Math.Clamp(mesh.OcclusionStrength, 0, 1)),
                new Vector4(
                    Math.Clamp(mesh.EmissiveFactor.X, 0, 16),
                    Math.Clamp(mesh.EmissiveFactor.Y, 0, 16),
                    Math.Clamp(mesh.EmissiveFactor.Z, 0, 16),
                    Math.Clamp(mesh.EmissiveFactor.W, 0, 64))));
            foreach (var vertex in mesh.Vertices)
            {
                var world = Vector3.Transform(new Vector3(vertex.X, vertex.Y, vertex.Z), model);
                bounds = bounds.Include(world);
            }

            indices.AddRange(mesh.Indices);
            vertexOffset = checked(vertexOffset + mesh.Vertices.Count);
        }

        draws = ranges;
        return indices;
    }

    private static RekallAgeVulkanSceneFrameUniform BuildFrameUniform(
        RekallAgeRuntimeViewportFrame frame,
        SceneBounds bounds)
    {
        bounds = bounds.OrDefault();
        var center = new Vector3(
            (bounds.MinX + bounds.MaxX) * 0.5f,
            (bounds.MinY + bounds.MaxY) * 0.5f,
            (bounds.MinZ + bounds.MaxZ) * 0.5f);
        var extent = MathF.Max(1f, MathF.Max(
            bounds.MaxX - bounds.MinX,
            MathF.Max(bounds.MaxY - bounds.MinY, bounds.MaxZ - bounds.MinZ)));
        var pose = ResolveCameraPose(frame.ActiveCamera, center, extent);
        var view = Matrix4x4.CreateLookAt(pose.Eye, pose.Eye + pose.Forward, pose.Up);
        var projection = CreateProjection(frame.ActiveCamera, frame, extent);
        projection.M22 *= -1f;

        var light = ResolvePrimaryLight(frame);
        return new RekallAgeVulkanSceneFrameUniform(
            view * projection,
            light.Direction,
            light.Color,
            light.Position);
    }

    private static RekallAgeVulkanSceneStereoFrame? BuildStereoFrame(
        RekallAgeRuntimeViewportFrame frame,
        SceneBounds bounds)
    {
        var camera = frame.HeadsetCamera ?? frame.ActiveCamera;
        if (frame.Stereo is not { Enabled: true } stereo || camera is null)
        {
            return null;
        }

        bounds = bounds.OrDefault();
        var center = new Vector3(
            (bounds.MinX + bounds.MaxX) * 0.5f,
            (bounds.MinY + bounds.MaxY) * 0.5f,
            (bounds.MinZ + bounds.MaxZ) * 0.5f);
        var extent = MathF.Max(1f, MathF.Max(
            bounds.MaxX - bounds.MinX,
            MathF.Max(bounds.MaxY - bounds.MinY, bounds.MaxZ - bounds.MinZ)));
        var pose = ResolveCameraPose(camera, center, extent);
        var projection = CreateProjection(camera, frame, extent);
        projection.M22 *= -1f;
        var views = stereo.Eyes
            .Select(eye =>
            {
                var offset = pose.Right * (float)eye.OffsetX
                    + pose.Up * (float)eye.OffsetY
                    + pose.Forward * (float)eye.OffsetZ;
                var eyePosition = pose.Eye + offset;
                var view = Matrix4x4.CreateLookAt(eyePosition, eyePosition + pose.Forward, pose.Up);
                return new RekallAgeVulkanSceneViewUniform(
                    eye.Name,
                    eye.Index,
                    view * projection,
                    new Vector4(eyePosition, 1),
                    new Vector4(
                        (float)eye.ViewportX,
                        (float)eye.ViewportY,
                        (float)Math.Max(1, eye.ViewportWidth),
                        (float)Math.Max(1, eye.ViewportHeight)));
            })
            .OrderBy(view => view.Index)
            .ToArray();
        return new RekallAgeVulkanSceneStereoFrame(
            true,
            stereo.RenderMode,
            stereo.PreferSinglePassMultiview,
            views);
    }

    private static IReadOnlyDictionary<string, RekallAgeRuntimeViewportRenderable> BuildRenderableLookup(
        RekallAgeRuntimeViewportFrame frame)
    {
        var lookup = new Dictionary<string, RekallAgeRuntimeViewportRenderable>(
            frame.Renderables.Count,
            StringComparer.Ordinal);
        foreach (var renderable in frame.Renderables)
        {
            lookup.TryAdd(renderable.EntityId, renderable);
        }

        return lookup;
    }

    private static CameraPose ResolveCameraPose(
        RekallAgeRuntimeViewportCamera? camera,
        Vector3 fallbackCenter,
        float fallbackExtent)
    {
        if (camera is null || IsDefaultCamera(camera))
        {
            var fallbackEye = new Vector3(fallbackCenter.X, fallbackCenter.Y, fallbackCenter.Z + MathF.Max(3f, fallbackExtent * 2.5f));
            var fallbackForward = Vector3.Normalize(fallbackCenter - fallbackEye);
            var fallbackRight = Vector3.Normalize(Vector3.Cross(fallbackForward, Vector3.UnitY));
            var fallbackUp = Vector3.Normalize(Vector3.Cross(fallbackRight, fallbackForward));
            return new CameraPose(fallbackEye, fallbackForward, fallbackRight, fallbackUp);
        }

        var cameraEye = new Vector3((float)camera.X, (float)camera.Y, (float)camera.Z);
        var cameraForward = DirectionFromEuler(camera.RotationX, camera.RotationY, camera.RotationZ);
        var rightVector = Rotate(1, 0, 0, camera.RotationX, camera.RotationY, camera.RotationZ);
        var upVector = Rotate(0, 1, 0, camera.RotationX, camera.RotationY, camera.RotationZ);
        return new CameraPose(
            cameraEye,
            cameraForward,
            NormalizeOrFallback(new Vector3(rightVector.X, rightVector.Y, rightVector.Z), Vector3.UnitX),
            NormalizeOrFallback(new Vector3(upVector.X, upVector.Y, upVector.Z), Vector3.UnitY));
    }

    private static bool IsDefaultCamera(RekallAgeRuntimeViewportCamera camera)
    {
        return Math.Abs(camera.X) < 0.0001
            && Math.Abs(camera.Y) < 0.0001
            && Math.Abs(camera.Z) < 0.0001
            && Math.Abs(camera.RotationX) < 0.0001
            && Math.Abs(camera.RotationY) < 0.0001
            && Math.Abs(camera.RotationZ) < 0.0001;
    }

    private static Matrix4x4 CreateProjection(
        RekallAgeRuntimeViewportCamera? camera,
        RekallAgeRuntimeViewportFrame frame,
        float extent)
    {
        var aspect = frame.Height <= 0 ? 1f : frame.Width / (float)frame.Height;
        var nearClip = MathF.Max(0.001f, (float)(camera?.NearClip ?? 0.05));
        var farClip = MathF.Max(nearClip + 0.001f, (float)(camera?.FarClip ?? Math.Max(100f, extent * 16f)));
        if (camera?.ProjectionMode.Equals("orthographic", StringComparison.OrdinalIgnoreCase) == true)
        {
            var height = MathF.Max(0.001f, (float)camera.OrthographicSize);
            return Matrix4x4.CreateOrthographic(height * aspect, height, nearClip, farClip);
        }

        var fieldOfView = Math.Clamp((float)(camera?.FieldOfViewDegrees ?? 65), 1f, 179f);
        return Matrix4x4.CreatePerspectiveFieldOfView(
            ToRadians(fieldOfView),
            aspect,
            nearClip,
            farClip);
    }

    private static Matrix4x4 CreateModelMatrix(RekallAgeRuntimeViewportRenderable? renderable)
    {
        if (renderable is null)
        {
            return Matrix4x4.Identity;
        }

        return Matrix4x4.CreateScale(
                (float)Math.Max(0.001, renderable.ScaleX),
                (float)Math.Max(0.001, renderable.ScaleY),
                (float)Math.Max(0.001, renderable.ScaleZ))
            * Matrix4x4.CreateRotationX(ToRadians(renderable.RotationX))
            * Matrix4x4.CreateRotationY(ToRadians(renderable.RotationY))
            * Matrix4x4.CreateRotationZ(ToRadians(renderable.RotationZ))
            * Matrix4x4.CreateTranslation((float)renderable.X, (float)renderable.Y, (float)renderable.Z);
    }

    private static SceneLight ResolvePrimaryLight(RekallAgeRuntimeViewportFrame frame)
    {
        RekallAgeRuntimeViewportRenderable? firstLight = null;
        RekallAgeRuntimeViewportRenderable? firstPointLight = null;
        foreach (var renderable in frame.Renderables)
        {
            if (!renderable.Kind.Equals("light", StringComparison.Ordinal))
            {
                continue;
            }

            firstLight ??= renderable;
            if (firstPointLight is null && IsPointLight(renderable))
            {
                firstPointLight = renderable;
            }
        }

        var light = firstPointLight ?? firstLight;
        if (light is null)
        {
            return new SceneLight(
                Vector3.Normalize(new Vector3(-0.45f, -0.65f, -0.6f)),
                new Vector4(0, 0, 0, 0),
                Vector4.One);
        }

        return new SceneLight(
            DirectionFromEuler(light.RotationX, light.RotationY, light.RotationZ),
            IsPointLight(light)
                ? new Vector4((float)light.X, (float)light.Y, (float)light.Z, 1)
                : new Vector4((float)light.X, (float)light.Y, (float)light.Z, 0),
            ResolveLightColor(light));
    }

    private static Vector4 ResolveLightColor(RekallAgeRuntimeViewportRenderable light)
    {
        var color = ParseColor(light.MaterialColor);
        var intensity = (float)Math.Clamp(light.Intensity, 0.05, 4.0);
        return new Vector4(color.X * intensity, color.Y * intensity, color.Z * intensity, 1);
    }

    private static Vector3 ParseColor(string? color)
    {
        if (color is { Length: 7 or 9 } && color[0] == '#'
            && byte.TryParse(color.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
            && byte.TryParse(color.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
            && byte.TryParse(color.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return new Vector3(r / 255f, g / 255f, b / 255f);
        }

        return Vector3.One;
    }

    private static bool IsPointLight(RekallAgeRuntimeViewportRenderable renderable)
    {
        return renderable.Variant?.Contains("PointLight", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static Vector3 DirectionFromEuler(double degreesX, double degreesY, double degreesZ)
    {
        var vector = Rotate(0, 0, 1, degreesX, degreesY, degreesZ);
        return Vector3.Normalize(new Vector3(vector.X, vector.Y, vector.Z));
    }

    private static Vector3 NormalizeOrFallback(Vector3 vector, Vector3 fallback)
    {
        return vector.LengthSquared() < 0.000001f
            ? fallback
            : Vector3.Normalize(vector);
    }

    private static (float X, float Y, float Z) Rotate(float x, float y, float z, double degreesX, double degreesY, double degreesZ)
    {
        var rx = MathF.PI / 180f * (float)degreesX;
        var ry = MathF.PI / 180f * (float)degreesY;
        var rz = MathF.PI / 180f * (float)degreesZ;

        var cos = MathF.Cos(rx);
        var sin = MathF.Sin(rx);
        (y, z) = (y * cos - z * sin, y * sin + z * cos);

        cos = MathF.Cos(ry);
        sin = MathF.Sin(ry);
        (x, z) = (x * cos + z * sin, -x * sin + z * cos);

        cos = MathF.Cos(rz);
        sin = MathF.Sin(rz);
        (x, y) = (x * cos - y * sin, x * sin + y * cos);
        return (x, y, z);
    }

    private static float ToRadians(double degrees)
    {
        return MathF.PI / 180f * (float)degrees;
    }

    private readonly record struct SceneBounds(float MinX, float MaxX, float MinY, float MaxY, float MinZ, float MaxZ)
    {
        public static SceneBounds Empty { get; } = new(float.MaxValue, float.MinValue, float.MaxValue, float.MinValue, float.MaxValue, float.MinValue);

        public SceneBounds Include(Vector3 point)
        {
            return new SceneBounds(
                MathF.Min(MinX, point.X),
                MathF.Max(MaxX, point.X),
                MathF.Min(MinY, point.Y),
                MathF.Max(MaxY, point.Y),
                MathF.Min(MinZ, point.Z),
                MathF.Max(MaxZ, point.Z));
        }

        public SceneBounds OrDefault()
        {
            return MinX == float.MaxValue
                ? new SceneBounds(-1, 1, -1, 1, -1, 1)
                : this;
        }
    }

    private readonly record struct SceneLight(Vector3 Direction, Vector4 Position, Vector4 Color);

    private readonly record struct CameraPose(Vector3 Eye, Vector3 Forward, Vector3 Right, Vector3 Up);
}
