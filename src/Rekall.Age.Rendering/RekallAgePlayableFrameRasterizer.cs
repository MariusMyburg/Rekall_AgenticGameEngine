using Rekall.Age.Playback;

namespace Rekall.Age.Rendering;

public sealed record RekallAgePlayableFrameRasterResult(
    byte[] Pixels,
    bool NonBlank,
    int NonBackgroundPixels);

public sealed class RekallAgePlayableFrameRasterizer
{
    public RekallAgePlayableFrameRasterResult Rasterize(
        RekallAgePlaybackRenderFrame frame,
        int width,
        int height)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be greater than zero.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be greater than zero.");
        }

        var pixels = new byte[width * height * 4];
        Fill(pixels, width, height, 8, 10, 14, 255);
        var background = (R: pixels[0], G: pixels[1], B: pixels[2], A: pixels[3]);

        foreach (var command in frame.DrawCommands)
        {
            switch (command.Kind.Trim().ToLowerInvariant())
            {
                case "clear":
                    var clear = ParseColor(command.Fill, (8, 10, 14, 255));
                    Fill(pixels, width, height, clear.R, clear.G, clear.B, clear.A);
                    background = clear;
                    break;
                case "rect":
                    var rect = ParseColor(command.Fill, (255, 255, 255, 255));
                    FillRect(pixels, width, height, command.X, command.Y, command.Width, command.Height, rect);
                    break;
                case "circle":
                    var circle = ParseColor(command.Fill, (255, 255, 255, 255));
                    FillCircle(pixels, width, height, command.X, command.Y, command.Width, command.Height, circle);
                    break;
                case "text":
                    var text = ParseColor(command.Fill, (255, 255, 255, 255));
                    DrawTinyText(pixels, width, height, command.X, command.Y, command.Text, text);
                    break;
            }
        }

        var nonBackground = CountNonBackground(pixels, background);
        return new RekallAgePlayableFrameRasterResult(pixels, nonBackground > 0, nonBackground);
    }

    private static void Fill(byte[] pixels, int width, int height, byte r, byte g, byte b, byte a)
    {
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = (y * width + x) * 4;
                pixels[index + 0] = r;
                pixels[index + 1] = g;
                pixels[index + 2] = b;
                pixels[index + 3] = a;
            }
        }
    }

    private static void FillRect(
        byte[] pixels,
        int width,
        int height,
        double x,
        double y,
        double rectWidth,
        double rectHeight,
        (byte R, byte G, byte B, byte A) color)
    {
        var left = ClampToPixel(x, width);
        var top = ClampToPixel(y, height);
        var right = ClampToPixel(x + Math.Max(0, rectWidth), width);
        var bottom = ClampToPixel(y + Math.Max(0, rectHeight), height);

        for (var py = top; py < bottom; py++)
        {
            for (var px = left; px < right; px++)
            {
                SetPixel(pixels, width, px, py, color);
            }
        }
    }

    private static void FillCircle(
        byte[] pixels,
        int width,
        int height,
        double x,
        double y,
        double circleWidth,
        double circleHeight,
        (byte R, byte G, byte B, byte A) color)
    {
        var diameterX = Math.Max(1, circleWidth);
        var diameterY = Math.Max(1, circleHeight);
        var centerX = x + diameterX / 2;
        var centerY = y + diameterY / 2;
        var radiusX = diameterX / 2;
        var radiusY = diameterY / 2;
        var left = ClampToPixel(x, width);
        var top = ClampToPixel(y, height);
        var right = ClampToPixel(x + diameterX, width);
        var bottom = ClampToPixel(y + diameterY, height);

        for (var py = top; py < bottom; py++)
        {
            for (var px = left; px < right; px++)
            {
                var dx = (px + 0.5 - centerX) / radiusX;
                var dy = (py + 0.5 - centerY) / radiusY;
                if (dx * dx + dy * dy <= 1)
                {
                    SetPixel(pixels, width, px, py, color);
                }
            }
        }
    }

    private static void DrawTinyText(
        byte[] pixels,
        int width,
        int height,
        double x,
        double y,
        string text,
        (byte R, byte G, byte B, byte A) color)
    {
        var startX = ClampToPixel(x, width);
        var startY = ClampToPixel(y, height);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == ' ')
            {
                continue;
            }

            var glyphX = startX + i * 5;
            FillRect(pixels, width, height, glyphX, startY, 3, 5, color);
            FillRect(pixels, width, height, glyphX + 1, startY + 1, 1, 3, (0, 0, 0, 255));
        }
    }

    private static void SetPixel(byte[] pixels, int width, int x, int y, (byte R, byte G, byte B, byte A) color)
    {
        var index = (y * width + x) * 4;
        pixels[index + 0] = color.R;
        pixels[index + 1] = color.G;
        pixels[index + 2] = color.B;
        pixels[index + 3] = color.A;
    }

    private static int ClampToPixel(double value, int maximum)
    {
        return Math.Clamp((int)Math.Round(value, MidpointRounding.AwayFromZero), 0, maximum);
    }

    private static int CountNonBackground(byte[] pixels, (byte R, byte G, byte B, byte A) background)
    {
        var count = 0;
        for (var index = 0; index < pixels.Length; index += 4)
        {
            if (pixels[index + 0] != background.R ||
                pixels[index + 1] != background.G ||
                pixels[index + 2] != background.B ||
                pixels[index + 3] != background.A)
            {
                count++;
            }
        }

        return count;
    }

    private static (byte R, byte G, byte B, byte A) ParseColor(
        string value,
        (byte R, byte G, byte B, byte A) fallback)
    {
        if (value.Length == 7 &&
            value[0] == '#' &&
            byte.TryParse(value.AsSpan(1, 2), System.Globalization.NumberStyles.HexNumber, null, out var r) &&
            byte.TryParse(value.AsSpan(3, 2), System.Globalization.NumberStyles.HexNumber, null, out var g) &&
            byte.TryParse(value.AsSpan(5, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
        {
            return (r, g, b, 255);
        }

        return fallback;
    }
}
