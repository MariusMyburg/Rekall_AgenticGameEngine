using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Rendering;

/// <summary>
/// Rasterizes only authored 2D UI surfaces into a transparent upload texture for the Vulkan
/// compositor. This is not a scene renderer and cannot draw cameras, meshes, lights, sprites,
/// materials, post processing, or any world geometry.
/// </summary>
public sealed class RekallAgeRuntimeUiOverlayRasterizer
{
    public byte[] Rasterize(
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeRuntimeViewportAssetSet? assets = null)
    {
        var pixels = new byte[frame.Width * frame.Height * 4];
        foreach (var renderable in frame.Renderables.Where(renderable => renderable.UiVisual is not null))
        {
            var visual = renderable.UiVisual!;
            var image = visual.AssetId is { } assetId
                && assets?.Images.TryGetValue(assetId, out var resolved) == true ? resolved : null;
            var font = visual.FontAssetId is { } fontAssetId
                && assets?.Fonts.TryGetValue(fontAssetId, out var resolvedFont) == true ? resolvedFont : null;
            DrawVisual(frame, visual, image, font, pixels);
        }
        return pixels;
    }

    private static void DrawVisual(
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeRuntimeViewportUiVisual visual,
        RekallAgeRgbaImage? image,
        RekallAgeRuntimeFontAsset? font,
        byte[] pixels)
    {
        var clip = RekallAgeRuntimeUiClipRect.Resolve(frame, visual);
        if (clip.Right <= clip.Left || clip.Bottom <= clip.Top) return;
        var background = ParseColor(visual.BackgroundColor, default);
        var border = ParseColor(visual.BorderColor, default);
        for (var y = clip.Top; y < clip.Bottom; y++)
        for (var x = clip.Left; x < clip.Right; x++)
        {
            var borderPixel = visual.BorderWidth > 0
                && (x < visual.X + visual.BorderWidth || x >= visual.X + visual.Width - visual.BorderWidth
                    || y < visual.Y + visual.BorderWidth || y >= visual.Y + visual.Height - visual.BorderWidth);
            var color = borderPixel ? border : background;
            Blend(pixels, Index(frame, x, y), color.R, color.G, color.B, color.A);
        }

        if (image is not null)
        {
            for (var y = clip.Top; y < clip.Bottom; y++)
            {
                var sourceY = Math.Clamp((y - visual.Y) * image.Height / Math.Max(1, visual.Height), 0, image.Height - 1);
                for (var x = clip.Left; x < clip.Right; x++)
                {
                    var sourceX = Math.Clamp((x - visual.X) * image.Width / Math.Max(1, visual.Width), 0, image.Width - 1);
                    var source = (sourceY * image.Width + sourceX) * 4;
                    Blend(pixels, Index(frame, x, y), image.Rgba[source], image.Rgba[source + 1], image.Rgba[source + 2], image.Rgba[source + 3]);
                }
            }
        }

        if (string.IsNullOrEmpty(visual.Text)) return;
        var layout = RekallAgeRuntimeUiTextLayoutResolver.Resolve(frame, visual, font);
        for (var localY = 0; localY < layout.Raster.Height; localY++)
        {
            var y = layout.Y + localY;
            if (y < clip.Top || y >= clip.Bottom) continue;
            for (var localX = 0; localX < layout.Raster.Width; localX++)
            {
                var x = layout.X + localX;
                if (x < clip.Left || x >= clip.Right) continue;
                var source = (localY * layout.Raster.Width + localX) * 4;
                if (layout.Raster.Rgba[source + 3] == 0) continue;
                Blend(pixels, Index(frame, x, y), layout.Raster.Rgba[source], layout.Raster.Rgba[source + 1], layout.Raster.Rgba[source + 2], layout.Raster.Rgba[source + 3]);
            }
        }
    }

    private static Color ParseColor(string? value, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(value) || value[0] != '#' || value.Length is not (7 or 9)) return fallback;
        try
        {
            return new(
                Convert.ToByte(value.Substring(1, 2), 16),
                Convert.ToByte(value.Substring(3, 2), 16),
                Convert.ToByte(value.Substring(5, 2), 16),
                value.Length == 9 ? Convert.ToByte(value.Substring(7, 2), 16) : (byte)255);
        }
        catch (FormatException) { return fallback; }
    }

    private static int Index(RekallAgeRuntimeViewportFrame frame, int x, int y) => (y * frame.Width + x) * 4;

    private static void Blend(byte[] pixels, int destination, byte r, byte g, byte b, byte a)
    {
        if (a == 0) return;
        if (a == 255)
        {
            pixels[destination] = r; pixels[destination + 1] = g; pixels[destination + 2] = b; pixels[destination + 3] = 255;
            return;
        }
        var inverse = 255 - a;
        pixels[destination] = (byte)((r * a + pixels[destination] * inverse + 127) / 255);
        pixels[destination + 1] = (byte)((g * a + pixels[destination + 1] * inverse + 127) / 255);
        pixels[destination + 2] = (byte)((b * a + pixels[destination + 2] * inverse + 127) / 255);
        pixels[destination + 3] = 255;
    }

    private readonly record struct Color(byte R, byte G, byte B, byte A);
}
