using Rekall.Age.Rendering.Abstractions;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using DrawingFontStyle = System.Drawing.FontStyle;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;

namespace Rekall.Age.Rendering;

internal sealed record RekallAgeRuntimeTextRaster(
    int Width,
    int Height,
    byte[] Rgba,
    bool UsedBitmapFallback,
    int FullWidth,
    int FullHeight,
    bool WasTruncated);

internal sealed class RekallAgeRuntimeTextRasterizer
{
    internal const int MaximumRasterWidth = 2048;
    internal const int MaximumRasterHeight = 512;
    internal const long MaximumCacheBytes = 16 * 1024 * 1024;
    private const int MaximumCacheEntries = 256;
    private const int MaximumTextCharacters = 4096;
    private readonly object _gate = new();
    private readonly Dictionary<TextRasterKey, RekallAgeRuntimeTextRaster> _cache = [];
    private readonly Queue<TextRasterKey> _cacheOrder = [];
    private long _cachedBytes;

    public static RekallAgeRuntimeTextRasterizer Shared { get; } = new();

    internal int RasterizationCount { get; private set; }
    internal long CachedBytes
    {
        get
        {
            lock (_gate)
            {
                return _cachedBytes;
            }
        }
    }

    public RekallAgeRuntimeTextRaster Rasterize(
        RekallAgeRuntimeViewportUiVisual visual,
        RekallAgeRuntimeFontAsset? fontAsset,
        int maximumWidth = MaximumRasterWidth,
        int maximumHeight = MaximumRasterHeight)
    {
        maximumWidth = Math.Clamp(maximumWidth, 1, MaximumRasterWidth);
        maximumHeight = Math.Clamp(maximumHeight, 1, MaximumRasterHeight);
        var renderedText = BoundedKeyPart(visual.Text, MaximumTextCharacters);
        var fontStamp = fontAsset is null || !File.Exists(fontAsset.Path)
            ? 0
            : File.GetLastWriteTimeUtc(fontAsset.Path).Ticks;
        var key = new TextRasterKey(
            renderedText,
            visual.Text.Length,
            BoundedKeyPart(visual.ForegroundColor, 16),
            BoundedKeyPart(visual.FontFamily, 256),
            BoundedKeyPart(visual.FontWeight, 32),
            BoundedKeyPart(visual.FontStyle, 32),
            visual.FontSize,
            BoundedNullableKeyPart(visual.FontAssetId, 256),
            BoundedNullableKeyPart(fontAsset?.Path, 512),
            fontStamp,
            maximumWidth,
            maximumHeight);

        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var cached))
            {
                return cached;
            }
        }

        var raster = OperatingSystem.IsWindowsVersionAtLeast(6, 1)
            ? TryRasterizeWindowsFont(visual, fontAsset, maximumWidth, maximumHeight)
                ?? RasterizeBitmapFont(visual, maximumWidth, maximumHeight)
            : RasterizeBitmapFont(visual, maximumWidth, maximumHeight);

        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var retainedBytes = raster.Rgba.LongLength + key.EstimatedBytes;
            while ((_cache.Count >= MaximumCacheEntries
                    || _cachedBytes + retainedBytes > MaximumCacheBytes)
                && _cacheOrder.TryDequeue(out var oldest))
            {
                if (_cache.Remove(oldest, out var evicted))
                {
                    _cachedBytes -= evicted.Rgba.LongLength + oldest.EstimatedBytes;
                }
            }
            _cache[key] = raster;
            _cacheOrder.Enqueue(key);
            _cachedBytes += retainedBytes;
            RasterizationCount++;
            return raster;
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows6.1")]
    private static RekallAgeRuntimeTextRaster? TryRasterizeWindowsFont(
        RekallAgeRuntimeViewportUiVisual visual,
        RekallAgeRuntimeFontAsset? fontAsset,
        int maximumWidth,
        int maximumHeight)
    {
        try
        {
            var style = DrawingFontStyle.Regular;
            if (visual.FontWeight.Equals("bold", StringComparison.OrdinalIgnoreCase))
            {
                style |= DrawingFontStyle.Bold;
            }
            if (visual.FontStyle.Equals("italic", StringComparison.OrdinalIgnoreCase))
            {
                style |= DrawingFontStyle.Italic;
            }

            using var privateFonts = new PrivateFontCollection();
            using var installedFonts = new InstalledFontCollection();
            FontFamily? family;
            if (!string.IsNullOrWhiteSpace(visual.FontAssetId))
            {
                if (fontAsset is null)
                {
                    return null;
                }
                privateFonts.AddFontFile(fontAsset.Path);
                family = privateFonts.Families.FirstOrDefault();
            }
            else
            {
                var requestedFamily = string.IsNullOrWhiteSpace(visual.FontFamily)
                    ? "Segoe UI"
                    : visual.FontFamily;
                family = installedFonts.Families.FirstOrDefault(candidate =>
                    candidate.Name.Equals(requestedFamily, StringComparison.OrdinalIgnoreCase));
            }

            if (family is null || !family.IsStyleAvailable(style))
            {
                return null;
            }

            var authoredFontSize = Math.Max(1L, visual.FontSize);
            var renderedFontSize = (int)Math.Min(authoredFontSize, MaximumRasterHeight * 2L);
            var processedText = visual.Text.Length <= MaximumTextCharacters
                ? visual.Text
                : visual.Text[..MaximumTextCharacters];
            using var font = new Font(family, renderedFontSize, style, GraphicsUnit.Pixel);
            using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
            format.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.NoClip;
            SizeF measured;
            using (var measurementBitmap = new Bitmap(1, 1, DrawingPixelFormat.Format32bppArgb))
            using (var measurementGraphics = Graphics.FromImage(measurementBitmap))
            {
                measurementGraphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                measured = measurementGraphics.MeasureString(processedText, font, int.MaxValue, format);
            }

            var sizeRatio = authoredFontSize / renderedFontSize;
            var textRatio = visual.Text.Length == 0
                ? 1d
                : (double)visual.Text.Length / Math.Max(1, processedText.Length);
            var fullWidth = SaturatingDimension((measured.Width + 2) * sizeRatio * textRatio);
            var fullHeight = SaturatingDimension((measured.Height + 2) * sizeRatio);
            var width = Math.Clamp((int)Math.Ceiling(measured.Width) + 2, 1, maximumWidth);
            var height = Math.Clamp((int)Math.Ceiling(measured.Height) + 2, 1, maximumHeight);
            using var bitmap = new Bitmap(width, height, DrawingPixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            using (var brush = new SolidBrush(ParseColor(visual.ForegroundColor)))
            {
                graphics.Clear(Color.Transparent);
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                graphics.DrawString(processedText, font, brush, 0, 0, format);
            }

            var rgba = new byte[checked(width * height * 4)];
            var data = bitmap.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.ReadOnly,
                DrawingPixelFormat.Format32bppArgb);
            try
            {
                var stride = Math.Abs(data.Stride);
                var bgra = new byte[stride * height];
                Marshal.Copy(data.Scan0, bgra, 0, bgra.Length);
                for (var y = 0; y < height; y++)
                {
                    var sourceRow = data.Stride >= 0 ? y : height - y - 1;
                    for (var x = 0; x < width; x++)
                    {
                        var source = sourceRow * stride + x * 4;
                        var destination = (y * width + x) * 4;
                        rgba[destination] = bgra[source + 2];
                        rgba[destination + 1] = bgra[source + 1];
                        rgba[destination + 2] = bgra[source];
                        rgba[destination + 3] = bgra[source + 3];
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            return new RekallAgeRuntimeTextRaster(
                width,
                height,
                rgba,
                false,
                fullWidth,
                fullHeight,
                processedText.Length != visual.Text.Length || width < fullWidth || height < fullHeight);
        }
        catch (Exception exception) when (exception is ArgumentException
            or FileNotFoundException
            or ExternalException
            or OutOfMemoryException)
        {
            return null;
        }
    }

    private static RekallAgeRuntimeTextRaster RasterizeBitmapFont(
        RekallAgeRuntimeViewportUiVisual visual,
        int maximumWidth,
        int maximumHeight)
    {
        var authoredScale = Math.Max(1L, visual.FontSize / 5L);
        var scale = (int)Math.Min(authoredScale, Math.Max(1, maximumHeight / 5));
        var processedText = visual.Text.Length <= MaximumTextCharacters
            ? visual.Text
            : visual.Text[..MaximumTextCharacters];
        long processedUnits = 0;
        foreach (var character in processedText)
        {
            processedUnits = SaturatingAdd(
                processedUnits,
                character == ' ' ? 2 : RekallAgeBitmapFont.Width(character) + 1);
        }
        var estimatedUnits = visual.Text.Length == 0 || processedText.Length == visual.Text.Length
            ? processedUnits
            : SaturatingMultiply(processedUnits, visual.Text.Length) / processedText.Length;
        var fullWidth = SaturatingDimension(SaturatingMultiply(estimatedUnits, authoredScale));
        var fullHeight = SaturatingDimension(SaturatingMultiply(5, authoredScale));
        var width = Math.Clamp(SaturatingDimension(SaturatingMultiply(processedUnits, scale)), 1, maximumWidth);
        var height = Math.Clamp(checked(5 * scale), 1, maximumHeight);
        var rgba = new byte[checked(width * height * 4)];
        var color = ParseColor(visual.ForegroundColor);
        var cursorX = 0;
        foreach (var character in processedText)
        {
            if (cursorX >= width)
            {
                break;
            }
            if (character == ' ')
            {
                cursorX += 2 * scale;
                continue;
            }

            var rows = RekallAgeBitmapFont.Rows(character);
            var glyphWidth = RekallAgeBitmapFont.Width(character);
            for (var row = 0; row < rows.Count; row++)
            for (var column = 0; column < glyphWidth; column++)
            {
                if ((rows[row] & (1 << (glyphWidth - column - 1))) == 0)
                {
                    continue;
                }
                for (var sy = 0; sy < scale; sy++)
                for (var sx = 0; sx < scale; sx++)
                {
                    var x = cursorX + column * scale + sx;
                    var y = row * scale + sy;
                    if (x >= width || y >= height)
                    {
                        continue;
                    }
                    var index = (y * width + x) * 4;
                    rgba[index] = color.R;
                    rgba[index + 1] = color.G;
                    rgba[index + 2] = color.B;
                    rgba[index + 3] = color.A;
                }
            }
            cursorX += (glyphWidth + 1) * scale;
        }
        return new RekallAgeRuntimeTextRaster(
            width,
            height,
            rgba,
            true,
            fullWidth,
            fullHeight,
            processedText.Length != visual.Text.Length || width < fullWidth || height < fullHeight);
    }

    private static int SaturatingDimension(double value) =>
        !double.IsFinite(value) || value >= int.MaxValue ? int.MaxValue : Math.Max(1, (int)Math.Ceiling(value));

    private static int SaturatingDimension(long value) =>
        value >= int.MaxValue ? int.MaxValue : Math.Max(1, (int)value);

    private static long SaturatingAdd(long left, long right) =>
        left > long.MaxValue - right ? long.MaxValue : left + right;

    private static long SaturatingMultiply(long left, long right) =>
        left == 0 || right == 0 ? 0 : left > long.MaxValue / right ? long.MaxValue : left * right;

    private static string BoundedKeyPart(string value, int maximumCharacters) =>
        value.Length <= maximumCharacters ? value : value[..maximumCharacters];

    private static string? BoundedNullableKeyPart(string? value, int maximumCharacters)
    {
        if (value is null || value.Length <= maximumCharacters)
        {
            return value;
        }
        return value[..maximumCharacters];
    }

    private static Color ParseColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value[0] != '#' || value.Length is not (7 or 9))
        {
            return Color.White;
        }
        try
        {
            return Color.FromArgb(
                value.Length == 9 ? Convert.ToByte(value.Substring(7, 2), 16) : 255,
                Convert.ToByte(value.Substring(1, 2), 16),
                Convert.ToByte(value.Substring(3, 2), 16),
                Convert.ToByte(value.Substring(5, 2), 16));
        }
        catch (FormatException)
        {
            return Color.White;
        }
    }

    private sealed record TextRasterKey(
        string Text,
        int OriginalTextLength,
        string ForegroundColor,
        string FontFamily,
        string FontWeight,
        string FontStyle,
        int FontSize,
        string? FontAssetId,
        string? FontPath,
        long FontStamp,
        int MaximumWidth,
        int MaximumHeight)
    {
        public long EstimatedBytes => 128L + 2L * (
            Text.Length
            + ForegroundColor.Length
            + FontFamily.Length
            + FontWeight.Length
            + FontStyle.Length
            + (FontAssetId?.Length ?? 0)
            + (FontPath?.Length ?? 0));
    }
}

internal readonly record struct RekallAgeRuntimeUiClipRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Math.Max(0, Right - Left);
    public int Height => Math.Max(0, Bottom - Top);

    public static RekallAgeRuntimeUiClipRect Resolve(
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeRuntimeViewportUiVisual visual) => new(
        Math.Max(0, Math.Max(visual.X, visual.ClipX)),
        Math.Max(0, Math.Max(visual.Y, visual.ClipY)),
        Math.Min(frame.Width, Math.Min(SaturatingAdd(visual.X, visual.Width), SaturatingAdd(visual.ClipX, visual.ClipWidth))),
        Math.Min(frame.Height, Math.Min(SaturatingAdd(visual.Y, visual.Height), SaturatingAdd(visual.ClipY, visual.ClipHeight))));

    private static int SaturatingAdd(int left, int right)
    {
        var result = (long)left + right;
        return result > int.MaxValue ? int.MaxValue : result < int.MinValue ? int.MinValue : (int)result;
    }
}

internal sealed record RekallAgeRuntimeUiTextLayout(
    RekallAgeRuntimeUiClipRect Clip,
    int X,
    int Y,
    RekallAgeRuntimeTextRaster Raster);

internal static class RekallAgeRuntimeUiTextLayoutResolver
{
    public static RekallAgeRuntimeUiTextLayout Resolve(
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeRuntimeViewportUiVisual visual,
        RekallAgeRuntimeFontAsset? font)
    {
        var clip = RekallAgeRuntimeUiClipRect.Resolve(frame, visual);
        var padding = Math.Max(2L, (long)visual.BorderWidth + 2);
        var x = (int)Math.Clamp((long)visual.X + padding, int.MinValue, int.MaxValue);
        var availableWidth = (long)clip.Right - Math.Min((long)x, clip.Left);
        var maximumWidth = (int)Math.Clamp(availableWidth, 1, RekallAgeRuntimeTextRasterizer.MaximumRasterWidth);
        var maximumHeight = Math.Max(1, clip.Height);
        var raster = RekallAgeRuntimeTextRasterizer.Shared.Rasterize(
            visual,
            font,
            maximumWidth,
            maximumHeight);
        var y = visual.Y + Math.Max(0, (visual.Height - raster.Height) / 2);
        return new RekallAgeRuntimeUiTextLayout(clip, x, y, raster);
    }
}
