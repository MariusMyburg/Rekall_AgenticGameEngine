using System.Globalization;
using System.Numerics;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Rendering;

public sealed class RekallAgePerspectiveSoftwareSceneRenderer
{
    public byte[] Render(
        RekallAgeVulkanSceneBatch batch,
        int width,
        int height,
        Matrix4x4 viewProjection,
        string? clearColor = null,
        IReadOnlyDictionary<string, RekallAgeRgbaImage>? textures = null)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Render width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Render height must be positive.");
        }

        var clear = ParseColor(clearColor, new Rgba(14, 20, 30, 255));
        var pixels = new byte[checked(width * height * 4)];
        var depth = new float[checked(width * height)];
        Array.Fill(depth, float.PositiveInfinity);
        Fill(pixels, clear);

        foreach (var draw in batch.Draws)
        {
            var endIndex = Math.Min(batch.Indices.Count, checked((int)(draw.FirstIndex + draw.IndexCount)));
            for (var index = (int)draw.FirstIndex; index + 2 < endIndex; index += 3)
            {
                var a = ReadVertex(batch, draw, batch.Indices[index + 0]);
                var b = ReadVertex(batch, draw, batch.Indices[index + 1]);
                var c = ReadVertex(batch, draw, batch.Indices[index + 2]);
                if (a is null || b is null || c is null)
                {
                    continue;
                }

                var worldA = Vector3.Transform(a.Value.Position, draw.Model);
                var worldB = Vector3.Transform(b.Value.Position, draw.Model);
                var worldC = Vector3.Transform(c.Value.Position, draw.Model);
                var clipA = Vector4.Transform(new Vector4(worldA, 1), viewProjection);
                var clipB = Vector4.Transform(new Vector4(worldB, 1), viewProjection);
                var clipC = Vector4.Transform(new Vector4(worldC, 1), viewProjection);
                if (!TryProject(clipA, width, height, out var screenA)
                    || !TryProject(clipB, width, height, out var screenB)
                    || !TryProject(clipC, width, height, out var screenC))
                {
                    continue;
                }

                var normal = Vector3.Normalize(Vector3.Cross(worldB - worldA, worldC - worldA));
                var light = Vector3.Normalize(-batch.Frame.LightDirection);
                var shade = Math.Clamp(0.35f + MathF.Max(0, Vector3.Dot(normal, light)) * 0.75f, 0.22f, 1.15f);
                var texture = textures is not null
                    && draw.TextureId is not null
                    && textures.TryGetValue(draw.TextureId, out var resolvedTexture)
                        ? resolvedTexture
                        : null;
                DrawTriangle(
                    pixels,
                    depth,
                    width,
                    height,
                    screenA,
                    screenB,
                    screenC,
                    a.Value,
                    b.Value,
                    c.Value,
                    texture,
                    shade);
            }
        }

        return pixels;
    }

    public Matrix4x4 CreateCameraViewProjection(
        RekallAgeRuntimeViewportCamera camera,
        int width,
        int height,
        Quaternion relativeOrientation,
        Vector3 relativePosition)
    {
        var pose = CreateCameraPose(camera);
        var localForward = Vector3.Transform(Vector3.UnitZ, relativeOrientation);
        var localUp = Vector3.Transform(Vector3.UnitY, relativeOrientation);
        var worldForward = NormalizeOrFallback(
            pose.Right * localForward.X + pose.Up * localForward.Y + pose.Forward * localForward.Z,
            pose.Forward);
        var worldUp = NormalizeOrFallback(
            pose.Right * localUp.X + pose.Up * localUp.Y + pose.Forward * localUp.Z,
            pose.Up);
        var eye = pose.Eye
            + pose.Right * relativePosition.X
            + pose.Up * relativePosition.Y
            + pose.Forward * relativePosition.Z;
        var view = Matrix4x4.CreateLookAt(eye, eye + worldForward, worldUp);
        var aspect = height <= 0 ? 1 : width / (float)height;
        var nearClip = MathF.Max(0.001f, (float)camera.NearClip);
        var farClip = MathF.Max(nearClip + 0.001f, (float)camera.FarClip);
        var fov = Math.Clamp((float)camera.FieldOfViewDegrees, 1, 179);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(ToRadians(fov), aspect, nearClip, farClip);
        return view * projection;
    }

    private static SceneVertex? ReadVertex(
        RekallAgeVulkanSceneBatch batch,
        RekallAgeVulkanSceneDraw draw,
        uint localIndex)
    {
        var index = draw.VertexOffset + checked((int)localIndex);
        if (index < 0 || index >= batch.Vertices.Count)
        {
            return null;
        }

        var vertex = batch.Vertices[index];
        return new SceneVertex(
            new Vector3(vertex.X, vertex.Y, vertex.Z),
            new Vector4(vertex.R, vertex.G, vertex.B, vertex.A),
            new Vector2(vertex.U, vertex.V));
    }

    private static bool TryProject(Vector4 clip, int width, int height, out ScreenVertex vertex)
    {
        vertex = default;
        if (MathF.Abs(clip.W) <= 0.0001f)
        {
            return false;
        }

        var ndc = new Vector3(clip.X, clip.Y, clip.Z) / clip.W;
        if (ndc.Z < 0 || ndc.Z > 1
            || ndc.X < -1.25f || ndc.X > 1.25f
            || ndc.Y < -1.25f || ndc.Y > 1.25f)
        {
            return false;
        }

        vertex = new ScreenVertex(
            (ndc.X * 0.5f + 0.5f) * (width - 1),
            (1 - (ndc.Y * 0.5f + 0.5f)) * (height - 1),
            ndc.Z);
        return true;
    }

    private static void DrawTriangle(
        byte[] pixels,
        float[] depth,
        int width,
        int height,
        ScreenVertex a,
        ScreenVertex b,
        ScreenVertex c,
        SceneVertex vertexA,
        SceneVertex vertexB,
        SceneVertex vertexC,
        RekallAgeRgbaImage? texture,
        float shade)
    {
        var area = Edge(a.X, a.Y, b.X, b.Y, c.X, c.Y);
        if (MathF.Abs(area) < 0.00001f)
        {
            return;
        }

        var minX = Math.Max(0, (int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, c.X))));
        var maxX = Math.Min(width - 1, (int)MathF.Ceiling(MathF.Max(a.X, MathF.Max(b.X, c.X))));
        var minY = Math.Max(0, (int)MathF.Floor(MathF.Min(a.Y, MathF.Min(b.Y, c.Y))));
        var maxY = Math.Min(height - 1, (int)MathF.Ceiling(MathF.Max(a.Y, MathF.Max(b.Y, c.Y))));
        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var px = x + 0.5f;
                var py = y + 0.5f;
                var w0 = Edge(b.X, b.Y, c.X, c.Y, px, py);
                var w1 = Edge(c.X, c.Y, a.X, a.Y, px, py);
                var w2 = Edge(a.X, a.Y, b.X, b.Y, px, py);
                if (!((w0 >= 0 && w1 >= 0 && w2 >= 0 && area > 0)
                    || (w0 <= 0 && w1 <= 0 && w2 <= 0 && area < 0)))
                {
                    continue;
                }

                var invArea = 1f / area;
                var alpha = w0 * invArea;
                var beta = w1 * invArea;
                var gamma = w2 * invArea;
                var z = a.Z * alpha + b.Z * beta + c.Z * gamma;
                var depthIndex = y * width + x;
                if (z >= depth[depthIndex])
                {
                    continue;
                }

                depth[depthIndex] = z;
                var color = texture is not null
                    ? Sample(texture, vertexA.Uv * alpha + vertexB.Uv * beta + vertexC.Uv * gamma, shade)
                    : Average(vertexA.Color, vertexB.Color, vertexC.Color, shade);
                var pixelIndex = depthIndex * 4;
                pixels[pixelIndex + 0] = color.R;
                pixels[pixelIndex + 1] = color.G;
                pixels[pixelIndex + 2] = color.B;
                pixels[pixelIndex + 3] = color.A;
            }
        }
    }

    private static float Edge(float ax, float ay, float bx, float by, float cx, float cy)
    {
        return (cx - ax) * (by - ay) - (cy - ay) * (bx - ax);
    }

    private static Rgba Average(Vector4 a, Vector4 b, Vector4 c, float shade)
    {
        return new Rgba(
            ToByte((a.X + b.X + c.X) / 3f * shade),
            ToByte((a.Y + b.Y + c.Y) / 3f * shade),
            ToByte((a.Z + b.Z + c.Z) / 3f * shade),
            255);
    }

    private static Rgba Sample(RekallAgeRgbaImage texture, Vector2 uv, float shade)
    {
        if (texture.Width <= 0 || texture.Height <= 0 || texture.Rgba.Length < texture.Width * texture.Height * 4)
        {
            return new Rgba(255, 255, 255, 255);
        }

        var u = uv.X - MathF.Floor(uv.X);
        var v = uv.Y - MathF.Floor(uv.Y);
        var x = Math.Clamp((int)MathF.Round(u * (texture.Width - 1)), 0, texture.Width - 1);
        var y = Math.Clamp((int)MathF.Round(v * (texture.Height - 1)), 0, texture.Height - 1);
        var offset = (y * texture.Width + x) * 4;
        return new Rgba(
            ToByte(texture.Rgba[offset + 0] / 255f * shade),
            ToByte(texture.Rgba[offset + 1] / 255f * shade),
            ToByte(texture.Rgba[offset + 2] / 255f * shade),
            texture.Rgba[offset + 3]);
    }

    private static byte ToByte(float value)
    {
        return (byte)Math.Clamp((int)MathF.Round(Math.Clamp(value, 0, 1) * 255), 0, 255);
    }

    private static void Fill(byte[] pixels, Rgba color)
    {
        for (var i = 0; i + 3 < pixels.Length; i += 4)
        {
            pixels[i + 0] = color.R;
            pixels[i + 1] = color.G;
            pixels[i + 2] = color.B;
            pixels[i + 3] = color.A;
        }
    }

    private static Rgba ParseColor(string? color, Rgba fallback)
    {
        if (color is { Length: 7 or 9 } && color[0] == '#'
            && byte.TryParse(color.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
            && byte.TryParse(color.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
            && byte.TryParse(color.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return new Rgba(r, g, b, 255);
        }

        return fallback;
    }

    private static CameraPose CreateCameraPose(RekallAgeRuntimeViewportCamera camera)
    {
        var eye = new Vector3((float)camera.X, (float)camera.Y, (float)camera.Z);
        var forward = Rotate(0, 0, 1, camera.RotationX, camera.RotationY, camera.RotationZ);
        var right = Rotate(1, 0, 0, camera.RotationX, camera.RotationY, camera.RotationZ);
        var up = Rotate(0, 1, 0, camera.RotationX, camera.RotationY, camera.RotationZ);
        return new CameraPose(
            eye,
            NormalizeOrFallback(forward, Vector3.UnitZ),
            NormalizeOrFallback(right, Vector3.UnitX),
            NormalizeOrFallback(up, Vector3.UnitY));
    }

    private static Vector3 Rotate(float x, float y, float z, double degreesX, double degreesY, double degreesZ)
    {
        var rx = ToRadians((float)degreesX);
        var ry = ToRadians((float)degreesY);
        var rz = ToRadians((float)degreesZ);

        var cos = MathF.Cos(rx);
        var sin = MathF.Sin(rx);
        (y, z) = (y * cos - z * sin, y * sin + z * cos);

        cos = MathF.Cos(ry);
        sin = MathF.Sin(ry);
        (x, z) = (x * cos + z * sin, -x * sin + z * cos);

        cos = MathF.Cos(rz);
        sin = MathF.Sin(rz);
        (x, y) = (x * cos - y * sin, x * sin + y * cos);
        return new Vector3(x, y, z);
    }

    private static Vector3 NormalizeOrFallback(Vector3 vector, Vector3 fallback)
    {
        return vector.LengthSquared() <= 0.000001f ? fallback : Vector3.Normalize(vector);
    }

    private static float ToRadians(float degrees)
    {
        return MathF.PI / 180f * degrees;
    }

    private readonly record struct SceneVertex(Vector3 Position, Vector4 Color, Vector2 Uv);

    private readonly record struct ScreenVertex(float X, float Y, float Z);

    private readonly record struct Rgba(byte R, byte G, byte B, byte A);

    private readonly record struct CameraPose(Vector3 Eye, Vector3 Forward, Vector3 Right, Vector3 Up);
}
