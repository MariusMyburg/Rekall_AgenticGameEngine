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
        var hash = Math.Abs(HashCode.Combine(frame.SceneName.GetHashCode(StringComparison.Ordinal), frame.FrameIndex));
        for (var y = 0; y < frame.Height; y++)
        {
            for (var x = 0; x < frame.Width; x++)
            {
                var index = ToIndex(frame, x, y);
                var stripe = (x / 16 + y / 16 + hash) % 2 == 0;
                pixels[index + 0] = stripe ? (byte)18 : (byte)8;
                pixels[index + 1] = stripe ? (byte)46 : (byte)24;
                pixels[index + 2] = stripe ? (byte)86 : (byte)54;
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
        for (var i = 4; i < pixels.Length; i += 4)
        {
            if (pixels[i] != pixels[0] || pixels[i + 1] != pixels[1] || pixels[i + 2] != pixels[2])
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
}
