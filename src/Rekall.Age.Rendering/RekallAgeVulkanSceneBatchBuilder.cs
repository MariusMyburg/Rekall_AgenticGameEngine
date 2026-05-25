using System.Numerics;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Rendering;

public sealed class RekallAgeVulkanSceneBatchBuilder
{
    public RekallAgeVulkanSceneBatch Build(
        RekallAgeRuntimeViewportFrame frame,
        IReadOnlyList<RekallAgeVulkanSceneMesh> meshes)
    {
        var vertices = BuildLocalVertices(meshes);
        var indices = FlattenIndices(frame, meshes, out var draws);
        return new RekallAgeVulkanSceneBatch(
            vertices,
            indices,
            draws,
            BuildFrameUniform(frame, meshes));
    }

    private static IReadOnlyList<RekallAgeVulkanSceneVertex> BuildLocalVertices(
        IReadOnlyList<RekallAgeVulkanSceneMesh> meshes)
    {
        var vertices = new List<RekallAgeVulkanSceneVertex>();
        foreach (var mesh in meshes)
        {
            vertices.AddRange(mesh.Vertices);
        }

        return vertices;
    }

    private static IReadOnlyList<uint> FlattenIndices(
        RekallAgeRuntimeViewportFrame frame,
        IReadOnlyList<RekallAgeVulkanSceneMesh> meshes,
        out IReadOnlyList<RekallAgeVulkanSceneDraw> draws)
    {
        var indices = new List<uint>();
        var ranges = new List<RekallAgeVulkanSceneDraw>();
        var vertexOffset = 0;
        foreach (var mesh in meshes)
        {
            var renderable = frame.Renderables.FirstOrDefault(candidate =>
                candidate.EntityId.Equals(mesh.EntityId, StringComparison.Ordinal));
            ranges.Add(new RekallAgeVulkanSceneDraw(
                (uint)indices.Count,
                (uint)mesh.Indices.Count,
                vertexOffset,
                (uint)mesh.Vertices.Count,
                CreateModelMatrix(renderable),
                mesh.BaseColorTexture?.Id,
                mesh.MetallicRoughnessTexture?.Id,
                mesh.NormalTexture?.Id,
                mesh.OcclusionTexture?.Id,
                new Vector4(
                    Math.Clamp(mesh.MetallicFactor, 0, 1),
                    Math.Clamp(mesh.RoughnessFactor, 0.04f, 1),
                    mesh.NormalTexture is null ? 0 : Math.Clamp(mesh.NormalScale, 0, 4),
                    mesh.OcclusionTexture is null ? 0 : Math.Clamp(mesh.OcclusionStrength, 0, 1))));
            indices.AddRange(mesh.Indices);
            vertexOffset = checked(vertexOffset + mesh.Vertices.Count);
        }

        draws = ranges;
        return indices;
    }

    private static RekallAgeVulkanSceneFrameUniform BuildFrameUniform(
        RekallAgeRuntimeViewportFrame frame,
        IReadOnlyList<RekallAgeVulkanSceneMesh> meshes)
    {
        var bounds = ComputeWorldBounds(frame, meshes);
        var center = new Vector3(
            (bounds.MinX + bounds.MaxX) * 0.5f,
            (bounds.MinY + bounds.MaxY) * 0.5f,
            (bounds.MinZ + bounds.MaxZ) * 0.5f);
        var extent = MathF.Max(1f, MathF.Max(
            bounds.MaxX - bounds.MinX,
            MathF.Max(bounds.MaxY - bounds.MinY, bounds.MaxZ - bounds.MinZ)));
        var (eye, target) = ResolveCamera(frame.ActiveCamera, center, extent);
        var view = Matrix4x4.CreateLookAt(eye, target, Vector3.UnitY);
        var projection = CreateProjection(frame.ActiveCamera, frame, extent);
        projection.M22 *= -1f;

        var lightIntensity = ResolveLightIntensity(frame);
        return new RekallAgeVulkanSceneFrameUniform(
            view * projection,
            ResolveLightDirection(frame),
            new Vector4(lightIntensity, lightIntensity, lightIntensity, 1));
    }

    private static SceneBounds ComputeWorldBounds(
        RekallAgeRuntimeViewportFrame frame,
        IReadOnlyList<RekallAgeVulkanSceneMesh> meshes)
    {
        var bounds = new SceneBounds(float.MaxValue, float.MinValue, float.MaxValue, float.MinValue, float.MaxValue, float.MinValue);
        foreach (var mesh in meshes)
        {
            var renderable = frame.Renderables.FirstOrDefault(candidate =>
                candidate.EntityId.Equals(mesh.EntityId, StringComparison.Ordinal));
            var model = CreateModelMatrix(renderable);
            foreach (var vertex in mesh.Vertices)
            {
                var world = Vector3.Transform(new Vector3(vertex.X, vertex.Y, vertex.Z), model);
                bounds = new SceneBounds(
                    MathF.Min(bounds.MinX, world.X),
                    MathF.Max(bounds.MaxX, world.X),
                    MathF.Min(bounds.MinY, world.Y),
                    MathF.Max(bounds.MaxY, world.Y),
                    MathF.Min(bounds.MinZ, world.Z),
                    MathF.Max(bounds.MaxZ, world.Z));
            }
        }

        return bounds.MinX == float.MaxValue
            ? new SceneBounds(-1, 1, -1, 1, -1, 1)
            : bounds;
    }

    private static (Vector3 Eye, Vector3 Target) ResolveCamera(
        RekallAgeRuntimeViewportCamera? camera,
        Vector3 fallbackCenter,
        float fallbackExtent)
    {
        if (camera is null || IsDefaultCamera(camera))
        {
            return (
                new Vector3(fallbackCenter.X, fallbackCenter.Y, fallbackCenter.Z + MathF.Max(3f, fallbackExtent * 2.5f)),
                fallbackCenter);
        }

        var eye = new Vector3((float)camera.X, (float)camera.Y, (float)camera.Z);
        return (eye, eye + DirectionFromEuler(camera.RotationX, camera.RotationY, camera.RotationZ));
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

    private static Vector3 ResolveLightDirection(RekallAgeRuntimeViewportFrame frame)
    {
        var light = frame.Renderables.FirstOrDefault(renderable =>
            renderable.Kind.Equals("light", StringComparison.Ordinal));
        return light is null
            ? Vector3.Normalize(new Vector3(-0.45f, -0.65f, -0.6f))
            : DirectionFromEuler(light.RotationX, light.RotationY, light.RotationZ);
    }

    private static float ResolveLightIntensity(RekallAgeRuntimeViewportFrame frame)
    {
        return (float)Math.Clamp(
            frame.Renderables
                .Where(renderable => renderable.Kind.Equals("light", StringComparison.Ordinal))
                .Select(renderable => renderable.Intensity)
                .DefaultIfEmpty(1)
                .Max(),
            0.05,
            4.0);
    }

    private static Vector3 DirectionFromEuler(double degreesX, double degreesY, double degreesZ)
    {
        var vector = Rotate(0, 0, -1, degreesX, degreesY, degreesZ);
        return Vector3.Normalize(new Vector3(vector.X, vector.Y, vector.Z));
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

    private readonly record struct SceneBounds(float MinX, float MaxX, float MinY, float MaxY, float MinZ, float MaxZ);
}
