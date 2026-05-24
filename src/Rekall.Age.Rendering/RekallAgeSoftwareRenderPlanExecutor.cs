using System.Globalization;

namespace Rekall.Age.Rendering;

public sealed class RekallAgeSoftwareRenderPlanExecutor
{
    public const int Width = 160;
    public const int Height = 90;

    public async ValueTask<RekallAgeRenderPlanExecutionResult> ExecuteAsync(
        RekallAgeRenderPlanDocument plan,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        if (!plan.BackendId.Equals("software", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Render backend '{plan.BackendId}' cannot be executed by the software render plan executor.");
        }

        Directory.CreateDirectory(outputDirectory);
        var pixels = new byte[Width * Height * 4];
        Clear(pixels, 18, 20, 26, 255);

        foreach (var commandBuffer in plan.CommandBuffers.OrderBy(buffer => buffer.Id, StringComparer.Ordinal))
        {
            foreach (var command in commandBuffer.Commands)
            {
                ExecuteCommand(pixels, command);
            }
        }

        var outputPath = Path.Combine(outputDirectory, $"{SanitizeFileName(plan.Name)}.render.png");
        await RekallAgePngWriter.WriteRgbaAsync(outputPath, Width, Height, pixels, cancellationToken);
        return new RekallAgeRenderPlanExecutionResult(outputPath, IsNonBlank(pixels), Width, Height);
    }

    private static void ExecuteCommand(byte[] pixels, RekallAgeRenderCommand command)
    {
        switch (command.Op.Trim().ToLowerInvariant())
        {
            case "clear":
                var clear = ParseColor(command.Arguments.GetValueOrDefault("color"), 18, 20, 26, 255);
                Clear(pixels, clear.R, clear.G, clear.B, clear.A);
                break;
            case "begin-render-pass":
            case "end-render-pass":
                break;
            case "draw-rect":
                DrawRect(pixels, command.Arguments);
                break;
        }
    }

    private static void DrawRect(byte[] pixels, IReadOnlyDictionary<string, string> arguments)
    {
        var x = ParseInt(arguments.GetValueOrDefault("x"), 0);
        var y = ParseInt(arguments.GetValueOrDefault("y"), 0);
        var width = ParseInt(arguments.GetValueOrDefault("width"), 1);
        var height = ParseInt(arguments.GetValueOrDefault("height"), 1);
        var color = ParseColor(arguments.GetValueOrDefault("color"), 255, 255, 255, 255);

        var left = Math.Clamp(x, 0, Width);
        var top = Math.Clamp(y, 0, Height);
        var right = Math.Clamp(x + Math.Max(width, 0), 0, Width);
        var bottom = Math.Clamp(y + Math.Max(height, 0), 0, Height);

        for (var py = top; py < bottom; py++)
        {
            for (var px = left; px < right; px++)
            {
                var index = ((py * Width) + px) * 4;
                pixels[index] = color.R;
                pixels[index + 1] = color.G;
                pixels[index + 2] = color.B;
                pixels[index + 3] = color.A;
            }
        }
    }

    private static void Clear(byte[] pixels, byte r, byte g, byte b, byte a)
    {
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = r;
            pixels[index + 1] = g;
            pixels[index + 2] = b;
            pixels[index + 3] = a;
        }
    }

    private static int ParseInt(string? value, int defaultValue)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : defaultValue;
    }

    private static RekallAgeSoftwareColor ParseColor(
        string? value,
        byte defaultR,
        byte defaultG,
        byte defaultB,
        byte defaultA)
    {
        if (value is not { Length: 7 } || value[0] != '#')
        {
            return new RekallAgeSoftwareColor(defaultR, defaultG, defaultB, defaultA);
        }

        return byte.TryParse(value.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
            && byte.TryParse(value.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
            && byte.TryParse(value.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b)
            ? new RekallAgeSoftwareColor(r, g, b, 255)
            : new RekallAgeSoftwareColor(defaultR, defaultG, defaultB, defaultA);
    }

    private static bool IsNonBlank(byte[] pixels)
    {
        var r = pixels[0];
        var g = pixels[1];
        var b = pixels[2];
        var a = pixels[3];
        for (var index = 4; index < pixels.Length; index += 4)
        {
            if (pixels[index] != r
                || pixels[index + 1] != g
                || pixels[index + 2] != b
                || pixels[index + 3] != a)
            {
                return true;
            }
        }

        return false;
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var clean = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "render-plan" : clean;
    }

    private readonly record struct RekallAgeSoftwareColor(byte R, byte G, byte B, byte A);
}
