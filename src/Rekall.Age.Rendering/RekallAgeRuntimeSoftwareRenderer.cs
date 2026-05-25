using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Rendering;

public sealed class RekallAgeRuntimeSoftwareRenderer
{
    public async ValueTask<RekallAgeRuntimeViewportCapture> CaptureAsync(
        RekallAgeRuntimeViewportFrame frame,
        string outputDirectory,
        string fileName,
        CancellationToken cancellationToken)
    {
        return await CaptureAsync(
            frame,
            outputDirectory,
            fileName,
            RekallAgeRuntimeViewportAssetSet.Empty,
            cancellationToken);
    }

    public async ValueTask<RekallAgeRuntimeViewportCapture> CaptureAsync(
        RekallAgeRuntimeViewportFrame frame,
        string outputDirectory,
        string fileName,
        RekallAgeRuntimeViewportAssetSet assets,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        var pixels = new byte[frame.Width * frame.Height * 4];
        FillBackground(frame, pixels);

        var assetBackedCount = 0;
        var fallbackCount = 0;
        foreach (var renderable in frame.Renderables)
        {
            if (TryDrawAssetRenderable(frame, renderable, assets, pixels))
            {
                assetBackedCount++;
            }
            else if (TryDrawEngineRenderable(frame, renderable, pixels))
            {
                continue;
            }
            else
            {
                DrawRenderableMarker(frame, renderable, pixels);
                fallbackCount++;
            }
        }

        if (frame.DebugOverlay.Enabled)
        {
            DrawDebugOverlay(frame, pixels);
        }

        var path = Path.Combine(outputDirectory, fileName);
        await RekallAgePngWriter.WriteRgbaAsync(path, frame.Width, frame.Height, pixels, cancellationToken);
        return new RekallAgeRuntimeViewportCapture(
            Captured: true,
            ScreenshotPath: path,
            NonBlank: IsNonBlank(pixels),
            Width: frame.Width,
            Height: frame.Height,
            FrameIndex: frame.FrameIndex,
            ActiveCamera: frame.ActiveCamera?.EntityName,
            RenderableCount: frame.Renderables.Count,
            ObservationCount: frame.Observations.Count,
            AssetBackedRenderableCount: assetBackedCount,
            FallbackRenderableCount: fallbackCount,
            MissingAssetCount: assets.Issues.Count(issue =>
                issue.Code.Equals("REKALL_RENDER_ASSET_MISSING", StringComparison.Ordinal)),
            UnsupportedAssetCount: assets.Issues.Count(issue =>
                issue.Code.Equals("REKALL_RENDER_ASSET_UNSUPPORTED", StringComparison.Ordinal)),
            AssetIssueCodes: assets.Issues
                .Select(issue => issue.Code)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(code => code, StringComparer.Ordinal)
                .ToArray());
    }

    private static void FillBackground(RekallAgeRuntimeViewportFrame frame, byte[] pixels)
    {
        var color = ParseHexColor(frame.ActiveCamera?.ClearColor, new SoftwareColor(18, 46, 86));
        for (var y = 0; y < frame.Height; y++)
        {
            for (var x = 0; x < frame.Width; x++)
            {
                var index = ToIndex(frame, x, y);
                pixels[index + 0] = color.R;
                pixels[index + 1] = color.G;
                pixels[index + 2] = color.B;
                pixels[index + 3] = 255;
            }
        }
    }

    private static bool TryDrawAssetRenderable(
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeRuntimeViewportRenderable renderable,
        RekallAgeRuntimeViewportAssetSet assets,
        byte[] pixels)
    {
        if (!renderable.Kind.Equals("sprite", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(renderable.AssetId)
            || !assets.Images.TryGetValue(renderable.AssetId, out var image))
        {
            return false;
        }

        DrawImage(frame, renderable, image, pixels);
        return true;
    }

    private static bool TryDrawEngineRenderable(
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeRuntimeViewportRenderable renderable,
        byte[] pixels)
    {
        if (renderable.Kind.Equals("mesh", StringComparison.Ordinal)
            && renderable.LineSegments is { Segments.Count: > 0 })
        {
            DrawViewportLineSegments(frame, renderable, pixels);
            return true;
        }

        if (renderable.Kind.Equals("mesh", StringComparison.Ordinal)
            && renderable.GeometryMesh is not null)
        {
            DrawAuthoredGeometryMesh(frame, renderable, pixels);
            return true;
        }

        var primitive = TryGetPrimitiveKind(renderable);
        if (primitive is not null)
        {
            DrawGeometryPrimitive(frame, renderable, primitive, pixels);
            return true;
        }

        if (renderable.Kind.Equals("light", StringComparison.Ordinal))
        {
            return renderable.Variant?.Contains("DirectionalLight", StringComparison.Ordinal) == true;
        }

        return false;
    }

    private static string? TryGetPrimitiveKind(RekallAgeRuntimeViewportRenderable renderable)
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

    private static void DrawGeometryPrimitive(
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeRuntimeViewportRenderable renderable,
        string primitive,
        byte[] pixels)
    {
        var material = ResolveMaterialColor(renderable, new SoftwareColor(88, 148, 218));
        switch (primitive)
        {
            case "cube":
                DrawPrimitiveCube(frame, renderable, pixels, material);
                break;
            case "sphere":
            case "surface":
                DrawPrimitiveSphere(frame, renderable, pixels, material);
                break;
            case "cylinder":
                DrawPrimitiveCylinder(frame, renderable, pixels, material);
                break;
            case "cone":
                DrawPrimitiveCone(frame, renderable, pixels, material);
                break;
            case "plane":
                DrawPrimitivePlane(frame, renderable, pixels, material);
                break;
        }
    }

    private static void DrawAuthoredGeometryMesh(
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeRuntimeViewportRenderable renderable,
        byte[] pixels)
    {
        var geometry = renderable.GeometryMesh;
        if (geometry is null || geometry.Vertices.Count == 0 || geometry.Indices.Count < 3)
        {
            return;
        }

        var material = ResolveMaterialColor(renderable, new SoftwareColor(88, 148, 218));
        var (centerX, centerY) = ResolveRenderableCenter(frame, renderable);
        var size = ResolveAuthoredMeshSize(frame, renderable, geometry);
        var transformed = geometry.Vertices
            .Select(vertex => Rotate(
                new SoftwareVec3(
                    vertex.X * Math.Max(0.1, renderable.ScaleX),
                    vertex.Y * Math.Max(0.1, renderable.ScaleY),
                    vertex.Z * Math.Max(0.1, renderable.ScaleZ)),
                renderable.RotationX,
                renderable.RotationY,
                renderable.RotationZ))
            .ToArray();
        var light = ResolveDirectionalLight(frame);
        var triangles = new List<SoftwareMeshTriangle>();

        for (var i = 0; i + 2 < geometry.Indices.Count; i += 3)
        {
            var aIndex = geometry.Indices[i];
            var bIndex = geometry.Indices[i + 1];
            var cIndex = geometry.Indices[i + 2];
            if (aIndex >= transformed.Length || bIndex >= transformed.Length || cIndex >= transformed.Length)
            {
                continue;
            }

            var a = transformed[aIndex];
            var b = transformed[bIndex];
            var c = transformed[cIndex];
            var normal = ResolveTriangleNormal(a, b, c, geometry.Vertices[aIndex], geometry.Vertices[bIndex], geometry.Vertices[cIndex], renderable);
            var color = ResolveTriangleColor(material, geometry.Vertices[aIndex], geometry.Vertices[bIndex], geometry.Vertices[cIndex]);
            triangles.Add(new SoftwareMeshTriangle(
                Project(a, centerX, centerY, size),
                Project(b, centerX, centerY, size),
                Project(c, centerX, centerY, size),
                normal,
                (a.Z + b.Z + c.Z) / 3.0,
                color));
        }

        foreach (var triangle in triangles.OrderBy(triangle => triangle.AverageZ))
        {
            var diffuse = Math.Max(0, Dot(triangle.Normal, light.Direction));
            var shade = Math.Clamp(0.6 + diffuse * 0.4 * light.Intensity, 0.5, 1.1);
            var (r, g, b) = Shade(triangle.Color, shade);
            FillTriangle(frame, pixels, triangle.A, triangle.B, triangle.C, r, g, b);
        }
    }

    private static void DrawViewportLineSegments(
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeRuntimeViewportRenderable renderable,
        byte[] pixels)
    {
        var lineSegments = renderable.LineSegments;
        if (lineSegments is null || lineSegments.Segments.Count == 0)
        {
            return;
        }

        var material = ResolveMaterialColor(renderable, new SoftwareColor(51, 221, 255));
        var (centerX, centerY) = ResolveRenderableCenter(frame, renderable);
        var size = ResolveLineSegmentSize(frame, renderable, lineSegments);
        var thicknessPixels = Math.Max(1, (int)Math.Round(lineSegments.Thickness * size));

        foreach (var segment in lineSegments.Segments)
        {
            var from = TransformLinePoint(segment.FromX, segment.FromY, segment.FromZ, renderable);
            var to = TransformLinePoint(segment.ToX, segment.ToY, segment.ToZ, renderable);
            DrawLine(
                frame,
                pixels,
                Project(from, centerX, centerY, size),
                Project(to, centerX, centerY, size),
                material,
                thicknessPixels);
        }
    }

    private static void DrawPrimitiveCube(
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeRuntimeViewportRenderable renderable,
        byte[] pixels,
        SoftwareColor material)
    {
        var (centerX, centerY) = ResolveRenderableCenter(frame, renderable);
        var size = ResolvePrimitiveSize(frame, renderable);
        var light = ResolveDirectionalLight(frame);
        var vertices = CubeVertices
            .Select(vertex => Rotate(
                new SoftwareVec3(
                    vertex.X * Math.Max(0.1, renderable.ScaleX),
                    vertex.Y * Math.Max(0.1, renderable.ScaleY),
                    vertex.Z * Math.Max(0.1, renderable.ScaleZ)),
                renderable.RotationX,
                renderable.RotationY,
                renderable.RotationZ))
            .ToArray();

        var faces = CubeFaces
            .Select(face =>
            {
                var normal = Normalize(Rotate(face.Normal, renderable.RotationX, renderable.RotationY, renderable.RotationZ));
                var projected = face.VertexIndexes
                    .Select(index => Project(vertices[index], centerX, centerY, size))
                    .ToArray();
                return new SoftwareCubeFace(projected, normal, face.VertexIndexes.Average(index => vertices[index].Z));
            })
            .OrderBy(face => face.AverageZ)
            .ToArray();

        foreach (var face in faces)
        {
            var diffuse = Math.Max(0, Dot(face.Normal, light.Direction));
            var shade = Math.Clamp(0.55 + diffuse * 0.45 * light.Intensity, 0.55, 1.05);
            var r = (byte)Math.Clamp(
                (int)Math.Round(material.R * shade + Math.Abs(face.Normal.X) * 18),
                0,
                255);
            var g = (byte)Math.Clamp(
                (int)Math.Round(material.G * shade + Math.Abs(face.Normal.Y) * 18),
                0,
                255);
            var b = (byte)Math.Clamp(
                (int)Math.Round(material.B * shade + Math.Abs(face.Normal.Z) * 18),
                0,
                255);
            FillQuad(frame, pixels, face.Points, r, g, b);
        }
    }

    private static void DrawPrimitiveSphere(
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeRuntimeViewportRenderable renderable,
        byte[] pixels,
        SoftwareColor material)
    {
        var (centerX, centerY) = ResolveRenderableCenter(frame, renderable);
        var size = ResolvePrimitiveSize(frame, renderable);
        var radiusX = Math.Max(7, size * 0.48);
        var radiusY = Math.Max(7, size * 0.48);
        var minX = Math.Max(0, (int)Math.Floor(centerX - radiusX));
        var maxX = Math.Min(frame.Width - 1, (int)Math.Ceiling(centerX + radiusX));
        var minY = Math.Max(0, (int)Math.Floor(centerY - radiusY));
        var maxY = Math.Min(frame.Height - 1, (int)Math.Ceiling(centerY + radiusY));

        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var nx = (x + 0.5 - centerX) / radiusX;
                var ny = (y + 0.5 - centerY) / radiusY;
                var distance = nx * nx + ny * ny;
                if (distance > 1)
                {
                    continue;
                }

                var highlight = Math.Max(0, 1 - distance);
                var directional = Math.Max(0, (-nx * 0.35) + (-ny * 0.45));
                SetPixel(frame, pixels, x, y, material, Math.Clamp(0.54 + highlight * 0.32 + directional * 0.16, 0.42, 1.08));
            }
        }
    }

    private static void DrawPrimitiveCylinder(
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeRuntimeViewportRenderable renderable,
        byte[] pixels,
        SoftwareColor material)
    {
        var (centerX, centerY) = ResolveRenderableCenter(frame, renderable);
        var size = ResolvePrimitiveSize(frame, renderable);
        var radiusX = Math.Max(7, size * 0.38);
        var radiusY = Math.Max(4, size * 0.13);
        var halfHeight = Math.Max(10, size * 0.44);
        var topY = (int)Math.Round(centerY - halfHeight);
        var bottomY = (int)Math.Round(centerY + halfHeight);
        var left = Math.Max(0, (int)Math.Floor(centerX - radiusX));
        var right = Math.Min(frame.Width - 1, (int)Math.Ceiling(centerX + radiusX));

        for (var y = Math.Max(0, topY); y <= Math.Min(frame.Height - 1, bottomY); y++)
        {
            for (var x = left; x <= right; x++)
            {
                var nx = Math.Abs((x + 0.5 - centerX) / radiusX);
                if (nx > 1)
                {
                    continue;
                }

                SetPixel(frame, pixels, x, y, material, 0.52 + (1 - nx) * 0.3);
            }
        }

        FillEllipse(frame, pixels, centerX, topY, radiusX, radiusY, material, 1.02);
        FillEllipse(frame, pixels, centerX, bottomY, radiusX, radiusY, material, 0.48);
    }

    private static void DrawPrimitiveCone(
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeRuntimeViewportRenderable renderable,
        byte[] pixels,
        SoftwareColor material)
    {
        var (centerX, centerY) = ResolveRenderableCenter(frame, renderable);
        var size = ResolvePrimitiveSize(frame, renderable);
        var radiusX = Math.Max(8, size * 0.42);
        var radiusY = Math.Max(4, size * 0.13);
        var apex = new SoftwarePoint(centerX, centerY - size * 0.58);
        var left = new SoftwarePoint(centerX - radiusX, centerY + size * 0.38);
        var right = new SoftwarePoint(centerX + radiusX, centerY + size * 0.38);
        var (r, g, b) = Shade(material, 0.8);
        FillTriangle(frame, pixels, apex, left, right, r, g, b);
        FillEllipse(frame, pixels, centerX, (int)Math.Round(centerY + size * 0.38), radiusX, radiusY, material, 0.58);
    }

    private static void DrawPrimitivePlane(
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeRuntimeViewportRenderable renderable,
        byte[] pixels,
        SoftwareColor material)
    {
        var (centerX, centerY) = ResolveRenderableCenter(frame, renderable);
        var size = ResolvePrimitiveSize(frame, renderable);
        var points = new[]
        {
            new SoftwarePoint(centerX - size * 0.62, centerY + size * 0.16),
            new SoftwarePoint(centerX - size * 0.12, centerY - size * 0.26),
            new SoftwarePoint(centerX + size * 0.62, centerY - size * 0.08),
            new SoftwarePoint(centerX + size * 0.08, centerY + size * 0.34)
        };
        var (r, g, b) = Shade(material, 0.82);
        FillQuad(frame, pixels, points, r, g, b);
    }

    private static double ResolvePrimitiveSize(
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeRuntimeViewportRenderable renderable)
    {
        var scale = Math.Max(0.1, Math.Max(renderable.ScaleX, Math.Max(renderable.ScaleY, renderable.ScaleZ)));
        return Math.Max(14, Math.Min(frame.Width, frame.Height) * 0.18 * scale);
    }

    private static double ResolveAuthoredMeshSize(
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeRuntimeViewportRenderable renderable,
        RekallAgeRuntimeViewportGeometryMesh geometry)
    {
        var minX = geometry.Vertices.Min(vertex => vertex.X);
        var maxX = geometry.Vertices.Max(vertex => vertex.X);
        var minY = geometry.Vertices.Min(vertex => vertex.Y);
        var maxY = geometry.Vertices.Max(vertex => vertex.Y);
        var minZ = geometry.Vertices.Min(vertex => vertex.Z);
        var maxZ = geometry.Vertices.Max(vertex => vertex.Z);
        var extent = Math.Max(0.1, Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ)));
        var scale = Math.Max(0.1, Math.Max(renderable.ScaleX, Math.Max(renderable.ScaleY, renderable.ScaleZ)));
        return Math.Max(14, Math.Min(frame.Width, frame.Height) * 0.22 * scale / extent);
    }

    private static double ResolveLineSegmentSize(
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeRuntimeViewportRenderable renderable,
        RekallAgeRuntimeViewportLineSegments lineSegments)
    {
        var minX = lineSegments.Segments.Min(segment => Math.Min(segment.FromX, segment.ToX));
        var maxX = lineSegments.Segments.Max(segment => Math.Max(segment.FromX, segment.ToX));
        var minY = lineSegments.Segments.Min(segment => Math.Min(segment.FromY, segment.ToY));
        var maxY = lineSegments.Segments.Max(segment => Math.Max(segment.FromY, segment.ToY));
        var minZ = lineSegments.Segments.Min(segment => Math.Min(segment.FromZ, segment.ToZ));
        var maxZ = lineSegments.Segments.Max(segment => Math.Max(segment.FromZ, segment.ToZ));
        var extent = Math.Max(0.1, Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ)));
        var scale = Math.Max(0.1, Math.Max(renderable.ScaleX, Math.Max(renderable.ScaleY, renderable.ScaleZ)));
        return Math.Max(14, Math.Min(frame.Width, frame.Height) * 0.22 * scale / extent);
    }

    private static SoftwareColor ResolveMaterialColor(
        RekallAgeRuntimeViewportRenderable renderable,
        SoftwareColor fallback)
    {
        return ParseHexColor(renderable.MaterialColor, fallback);
    }

    private static SoftwareColor ParseHexColor(
        string? color,
        SoftwareColor fallback)
    {
        if (string.IsNullOrWhiteSpace(color)
            || color.Length != 7
            || color[0] != '#')
        {
            return fallback;
        }

        try
        {
            return new SoftwareColor(
                Convert.ToByte(color.Substring(1, 2), 16),
                Convert.ToByte(color.Substring(3, 2), 16),
                Convert.ToByte(color.Substring(5, 2), 16));
        }
        catch (FormatException)
        {
            return fallback;
        }
    }

    private static void FillEllipse(
        RekallAgeRuntimeViewportFrame frame,
        byte[] pixels,
        int centerX,
        int centerY,
        double radiusX,
        double radiusY,
        SoftwareColor material,
        double shade)
    {
        var minX = Math.Max(0, (int)Math.Floor(centerX - radiusX));
        var maxX = Math.Min(frame.Width - 1, (int)Math.Ceiling(centerX + radiusX));
        var minY = Math.Max(0, (int)Math.Floor(centerY - radiusY));
        var maxY = Math.Min(frame.Height - 1, (int)Math.Ceiling(centerY + radiusY));

        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var nx = (x + 0.5 - centerX) / radiusX;
                var ny = (y + 0.5 - centerY) / radiusY;
                if (nx * nx + ny * ny <= 1)
                {
                    SetPixel(frame, pixels, x, y, material, shade);
                }
            }
        }
    }

    private static void SetPixel(
        RekallAgeRuntimeViewportFrame frame,
        byte[] pixels,
        int x,
        int y,
        SoftwareColor material,
        double shade)
    {
        if (x < 0 || y < 0 || x >= frame.Width || y >= frame.Height)
        {
            return;
        }

        var (r, g, b) = Shade(material, shade);
        var index = ToIndex(frame, x, y);
        pixels[index + 0] = r;
        pixels[index + 1] = g;
        pixels[index + 2] = b;
        pixels[index + 3] = 255;
    }

    private static void DrawLine(
        RekallAgeRuntimeViewportFrame frame,
        byte[] pixels,
        SoftwarePoint from,
        SoftwarePoint to,
        SoftwareColor material,
        int thickness)
    {
        var deltaX = to.X - from.X;
        var deltaY = to.Y - from.Y;
        var steps = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(deltaX), Math.Abs(deltaY))));
        var radius = Math.Max(0, thickness / 2);
        for (var step = 0; step <= steps; step++)
        {
            var t = step / (double)steps;
            var x = (int)Math.Round(from.X + deltaX * t);
            var y = (int)Math.Round(from.Y + deltaY * t);
            for (var offsetY = -radius; offsetY <= radius; offsetY++)
            {
                for (var offsetX = -radius; offsetX <= radius; offsetX++)
                {
                    if (offsetX * offsetX + offsetY * offsetY <= radius * radius + 1)
                    {
                        SetPixel(frame, pixels, x + offsetX, y + offsetY, material, 1);
                    }
                }
            }
        }
    }

    private static (byte R, byte G, byte B) Shade(SoftwareColor material, double shade)
    {
        return (
            (byte)Math.Clamp((int)Math.Round(material.R * shade), 0, 255),
            (byte)Math.Clamp((int)Math.Round(material.G * shade), 0, 255),
            (byte)Math.Clamp((int)Math.Round(material.B * shade), 0, 255));
    }

    private static SoftwareVec3 ResolveTriangleNormal(
        SoftwareVec3 a,
        SoftwareVec3 b,
        SoftwareVec3 c,
        RekallAgeRuntimeViewportGeometryVertex vertexA,
        RekallAgeRuntimeViewportGeometryVertex vertexB,
        RekallAgeRuntimeViewportGeometryVertex vertexC,
        RekallAgeRuntimeViewportRenderable renderable)
    {
        var authored = new SoftwareVec3(
            (vertexA.NormalX + vertexB.NormalX + vertexC.NormalX) / 3.0,
            (vertexA.NormalY + vertexB.NormalY + vertexC.NormalY) / 3.0,
            (vertexA.NormalZ + vertexB.NormalZ + vertexC.NormalZ) / 3.0);
        if (Math.Abs(authored.X) + Math.Abs(authored.Y) + Math.Abs(authored.Z) > 0.000001)
        {
            return Normalize(Rotate(authored, renderable.RotationX, renderable.RotationY, renderable.RotationZ));
        }

        return Normalize(Cross(
            new SoftwareVec3(b.X - a.X, b.Y - a.Y, b.Z - a.Z),
            new SoftwareVec3(c.X - a.X, c.Y - a.Y, c.Z - a.Z)));
    }

    private static SoftwareColor ResolveTriangleColor(
        SoftwareColor fallback,
        RekallAgeRuntimeViewportGeometryVertex a,
        RekallAgeRuntimeViewportGeometryVertex b,
        RekallAgeRuntimeViewportGeometryVertex c)
    {
        return new SoftwareColor(
            (byte)Math.Clamp((int)Math.Round((ResolveUnit(a.R, fallback.R) + ResolveUnit(b.R, fallback.R) + ResolveUnit(c.R, fallback.R)) / 3.0), 0, 255),
            (byte)Math.Clamp((int)Math.Round((ResolveUnit(a.G, fallback.G) + ResolveUnit(b.G, fallback.G) + ResolveUnit(c.G, fallback.G)) / 3.0), 0, 255),
            (byte)Math.Clamp((int)Math.Round((ResolveUnit(a.B, fallback.B) + ResolveUnit(b.B, fallback.B) + ResolveUnit(c.B, fallback.B)) / 3.0), 0, 255));
    }

    private static double ResolveUnit(double value, byte fallback)
    {
        return double.IsNaN(value)
            ? fallback
            : Math.Clamp(value, 0, 1) * 255;
    }

    private static SoftwareDirectionalLight ResolveDirectionalLight(RekallAgeRuntimeViewportFrame frame)
    {
        var light = frame.Renderables
            .Where(renderable => renderable.Kind.Equals("light", StringComparison.Ordinal))
            .Where(renderable => renderable.Variant?.Contains("DirectionalLight", StringComparison.Ordinal) == true)
            .OrderByDescending(renderable => renderable.Intensity)
            .ThenBy(renderable => renderable.EntityName, StringComparer.Ordinal)
            .FirstOrDefault();

        if (light is null)
        {
            return new SoftwareDirectionalLight(Normalize(new SoftwareVec3(0.35, 0.55, 0.75)), 1);
        }

        return new SoftwareDirectionalLight(
            DirectionFromEuler(light.RotationX, light.RotationY),
            Math.Clamp(light.Intensity, 0, 4));
    }

    private static SoftwareVec3 DirectionFromEuler(double pitchDegrees, double yawDegrees)
    {
        var pitch = DegreesToRadians(pitchDegrees);
        var yaw = DegreesToRadians(yawDegrees);
        return Normalize(new SoftwareVec3(
            Math.Sin(yaw) * Math.Cos(pitch),
            -Math.Sin(pitch),
            Math.Cos(yaw) * Math.Cos(pitch)));
    }

    private static SoftwarePoint Project(SoftwareVec3 vertex, int centerX, int centerY, double size)
    {
        var x = centerX + (vertex.X - vertex.Z * 0.42) * size;
        var y = centerY - (vertex.Y + vertex.Z * 0.28) * size;
        return new SoftwarePoint(x, y);
    }

    private static SoftwareVec3 TransformLinePoint(
        double x,
        double y,
        double z,
        RekallAgeRuntimeViewportRenderable renderable)
    {
        return Rotate(
            new SoftwareVec3(
                x * Math.Max(0.1, renderable.ScaleX),
                y * Math.Max(0.1, renderable.ScaleY),
                z * Math.Max(0.1, renderable.ScaleZ)),
            renderable.RotationX,
            renderable.RotationY,
            renderable.RotationZ);
    }

    private static void FillQuad(
        RekallAgeRuntimeViewportFrame frame,
        byte[] pixels,
        IReadOnlyList<SoftwarePoint> points,
        byte r,
        byte g,
        byte b)
    {
        FillTriangle(frame, pixels, points[0], points[1], points[2], r, g, b);
        FillTriangle(frame, pixels, points[0], points[2], points[3], r, g, b);
    }

    private static void FillTriangle(
        RekallAgeRuntimeViewportFrame frame,
        byte[] pixels,
        SoftwarePoint a,
        SoftwarePoint b,
        SoftwarePoint c,
        byte r,
        byte g,
        byte blue)
    {
        var minX = Math.Max(0, (int)Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X))));
        var maxX = Math.Min(frame.Width - 1, (int)Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))));
        var minY = Math.Max(0, (int)Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y))));
        var maxY = Math.Min(frame.Height - 1, (int)Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))));
        var area = Edge(a, b, c);

        if (Math.Abs(area) < 0.000001)
        {
            return;
        }

        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var point = new SoftwarePoint(x + 0.5, y + 0.5);
                var w0 = Edge(b, c, point);
                var w1 = Edge(c, a, point);
                var w2 = Edge(a, b, point);
                if ((w0 >= 0 && w1 >= 0 && w2 >= 0 && area > 0)
                    || (w0 <= 0 && w1 <= 0 && w2 <= 0 && area < 0))
                {
                    var index = ToIndex(frame, x, y);
                    pixels[index + 0] = r;
                    pixels[index + 1] = g;
                    pixels[index + 2] = blue;
                    pixels[index + 3] = 255;
                }
            }
        }
    }

    private static double Edge(SoftwarePoint a, SoftwarePoint b, SoftwarePoint c)
    {
        return (c.X - a.X) * (b.Y - a.Y) - (c.Y - a.Y) * (b.X - a.X);
    }

    private static SoftwareVec3 Rotate(
        SoftwareVec3 point,
        double pitchDegrees,
        double yawDegrees,
        double rollDegrees)
    {
        var pitch = DegreesToRadians(pitchDegrees);
        var yaw = DegreesToRadians(yawDegrees);
        var roll = DegreesToRadians(rollDegrees);

        var x1 = point.X;
        var y1 = point.Y * Math.Cos(pitch) - point.Z * Math.Sin(pitch);
        var z1 = point.Y * Math.Sin(pitch) + point.Z * Math.Cos(pitch);

        var x2 = x1 * Math.Cos(yaw) + z1 * Math.Sin(yaw);
        var y2 = y1;
        var z2 = -x1 * Math.Sin(yaw) + z1 * Math.Cos(yaw);

        return new SoftwareVec3(
            x2 * Math.Cos(roll) - y2 * Math.Sin(roll),
            x2 * Math.Sin(roll) + y2 * Math.Cos(roll),
            z2);
    }

    private static double Dot(SoftwareVec3 left, SoftwareVec3 right)
    {
        return left.X * right.X + left.Y * right.Y + left.Z * right.Z;
    }

    private static SoftwareVec3 Cross(SoftwareVec3 left, SoftwareVec3 right)
    {
        return new SoftwareVec3(
            left.Y * right.Z - left.Z * right.Y,
            left.Z * right.X - left.X * right.Z,
            left.X * right.Y - left.Y * right.X);
    }

    private static SoftwareVec3 Normalize(SoftwareVec3 value)
    {
        var length = Math.Sqrt(value.X * value.X + value.Y * value.Y + value.Z * value.Z);
        return length <= 0.000001
            ? new SoftwareVec3(0, 0, 1)
            : new SoftwareVec3(value.X / length, value.Y / length, value.Z / length);
    }

    private static double DegreesToRadians(double degrees)
    {
        return degrees * Math.PI / 180.0;
    }

    private static void DrawImage(
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeRuntimeViewportRenderable renderable,
        RekallAgeRgbaImage image,
        byte[] pixels)
    {
        if (image.Width <= 0 || image.Height <= 0 || image.Rgba.Length != image.Width * image.Height * 4)
        {
            return;
        }

        var (cx, cy) = ResolveRenderableCenter(frame, renderable);
        var longest = Math.Max(image.Width, image.Height);
        var scale = longest < 16
            ? 16.0 / longest
            : longest > 64
                ? 64.0 / longest
                : 1.0;
        var destinationWidth = Math.Max(1, (int)Math.Round(image.Width * scale));
        var destinationHeight = Math.Max(1, (int)Math.Round(image.Height * scale));
        var left = cx - destinationWidth / 2;
        var top = cy - destinationHeight / 2;

        for (var y = 0; y < destinationHeight; y++)
        {
            var targetY = top + y;
            if (targetY < 0 || targetY >= frame.Height)
            {
                continue;
            }

            var sourceY = Math.Min(image.Height - 1, y * image.Height / destinationHeight);
            for (var x = 0; x < destinationWidth; x++)
            {
                var targetX = left + x;
                if (targetX < 0 || targetX >= frame.Width)
                {
                    continue;
                }

                var sourceX = Math.Min(image.Width - 1, x * image.Width / destinationWidth);
                var source = (sourceY * image.Width + sourceX) * 4;
                var destination = ToIndex(frame, targetX, targetY);
                AlphaBlend(
                    pixels,
                    destination,
                    image.Rgba[source],
                    image.Rgba[source + 1],
                    image.Rgba[source + 2],
                    image.Rgba[source + 3]);
            }
        }
    }

    private static void AlphaBlend(byte[] pixels, int destination, byte r, byte g, byte b, byte a)
    {
        if (a == 0)
        {
            return;
        }

        if (a == 255)
        {
            pixels[destination + 0] = r;
            pixels[destination + 1] = g;
            pixels[destination + 2] = b;
            pixels[destination + 3] = 255;
            return;
        }

        var inverse = 255 - a;
        pixels[destination + 0] = (byte)((r * a + pixels[destination + 0] * inverse + 127) / 255);
        pixels[destination + 1] = (byte)((g * a + pixels[destination + 1] * inverse + 127) / 255);
        pixels[destination + 2] = (byte)((b * a + pixels[destination + 2] * inverse + 127) / 255);
        pixels[destination + 3] = 255;
    }

    private static void DrawRenderableMarker(
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeRuntimeViewportRenderable renderable,
        byte[] pixels)
    {
        var (cx, cy) = ResolveRenderableCenter(frame, renderable);
        var (r, g, b, radius) = renderable.Kind switch
        {
            "sprite" => ((byte)245, (byte)124, (byte)52, 5),
            "mesh" => ((byte)82, (byte)180, (byte)255, 6),
            "light" => ((byte)255, (byte)238, (byte)90, 3),
            "ui" => ((byte)86, (byte)240, (byte)180, 4),
            _ => ((byte)220, (byte)220, (byte)220, 4)
        };

        for (var y = cy - radius; y <= cy + radius; y++)
        {
            for (var x = cx - radius; x <= cx + radius; x++)
            {
                if (x < 0 || y < 0 || x >= frame.Width || y >= frame.Height)
                {
                    continue;
                }

                if ((x - cx) * (x - cx) + (y - cy) * (y - cy) > radius * radius)
                {
                    continue;
                }

                var index = ToIndex(frame, x, y);
                pixels[index + 0] = r;
                pixels[index + 1] = g;
                pixels[index + 2] = b;
                pixels[index + 3] = 255;
            }
        }
    }

    private static (int X, int Y) ResolveRenderableCenter(
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeRuntimeViewportRenderable renderable)
    {
        if (renderable.Kind.Equals("mesh", StringComparison.Ordinal))
        {
            var meshX = (int)Math.Round(frame.Width / 2.0 + renderable.X * 18);
            var meshY = (int)Math.Round(frame.Height / 2.0 - renderable.Y * 18);
            return (
                Math.Clamp(meshX, 16, Math.Max(16, frame.Width - 17)),
                Math.Clamp(meshY, 16, Math.Max(16, frame.Height - 17)));
        }

        var seed = Math.Abs(renderable.EntityId.GetHashCode(StringComparison.Ordinal));
        var x = 12 + (seed + (int)Math.Round(renderable.X * 7)) % Math.Max(1, frame.Width - 24);
        var y = 16 + (seed / 17 + (int)Math.Round(renderable.Y * 7)) % Math.Max(1, frame.Height - 28);
        return (x, y);
    }

    private static void DrawDebugOverlay(RekallAgeRuntimeViewportFrame frame, byte[] pixels)
    {
        var bandHeight = Math.Min(8, frame.Height);
        var litWidth = Math.Min(frame.Width, 8 + frame.Renderables.Count * 12 + frame.DebugOverlay.ObservationCount * 4);
        for (var y = 0; y < bandHeight; y++)
        {
            for (var x = 0; x < frame.Width; x++)
            {
                var index = ToIndex(frame, x, y);
                pixels[index + 0] = x < litWidth ? (byte)74 : (byte)22;
                pixels[index + 1] = x < litWidth ? (byte)210 : (byte)48;
                pixels[index + 2] = x < litWidth ? (byte)190 : (byte)72;
                pixels[index + 3] = 255;
            }
        }
    }

    private static bool IsNonBlank(byte[] pixels)
    {
        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 0 || pixels[i + 1] != 0 || pixels[i + 2] != 0 || pixels[i + 3] != 0)
            {
                return true;
            }
        }

        return false;
    }

    private static int ToIndex(RekallAgeRuntimeViewportFrame frame, int x, int y)
    {
        return (y * frame.Width + x) * 4;
    }

    private static readonly SoftwareVec3[] CubeVertices =
    [
        new(-0.5, -0.5, -0.5),
        new(0.5, -0.5, -0.5),
        new(0.5, 0.5, -0.5),
        new(-0.5, 0.5, -0.5),
        new(-0.5, -0.5, 0.5),
        new(0.5, -0.5, 0.5),
        new(0.5, 0.5, 0.5),
        new(-0.5, 0.5, 0.5)
    ];

    private static readonly SoftwareCubeFaceDefinition[] CubeFaces =
    [
        new([0, 3, 2, 1], new SoftwareVec3(0, 0, -1)),
        new([4, 5, 6, 7], new SoftwareVec3(0, 0, 1)),
        new([0, 4, 7, 3], new SoftwareVec3(-1, 0, 0)),
        new([1, 2, 6, 5], new SoftwareVec3(1, 0, 0)),
        new([0, 1, 5, 4], new SoftwareVec3(0, -1, 0)),
        new([3, 7, 6, 2], new SoftwareVec3(0, 1, 0))
    ];

    private readonly record struct SoftwareColor(byte R, byte G, byte B);

    private readonly record struct SoftwareVec3(double X, double Y, double Z);

    private readonly record struct SoftwarePoint(double X, double Y);

    private sealed record SoftwareCubeFace(
        IReadOnlyList<SoftwarePoint> Points,
        SoftwareVec3 Normal,
        double AverageZ);

    private sealed record SoftwareCubeFaceDefinition(
        IReadOnlyList<int> VertexIndexes,
        SoftwareVec3 Normal);

    private sealed record SoftwareMeshTriangle(
        SoftwarePoint A,
        SoftwarePoint B,
        SoftwarePoint C,
        SoftwareVec3 Normal,
        double AverageZ,
        SoftwareColor Color);

    private sealed record SoftwareDirectionalLight(
        SoftwareVec3 Direction,
        double Intensity);
}
