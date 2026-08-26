using System.Globalization;
using System.Numerics;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Rendering;

/// <summary>
/// Resolves the authored environment background fallback shared by native capture
/// and interactive players. Sky assets may replace this color once a backend can
/// render them; until then the fallback remains deterministic and inspectable.
/// </summary>
public static class RekallAgeEnvironmentBackgroundResolver
{
    private static readonly Vector4 DefaultColor = new(0.08f, 0.10f, 0.14f, 1f);

    public static Vector4 Resolve(RekallAgeRuntimeViewportFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var cameraColor = Parse(frame.ActiveCamera?.ClearColor, DefaultColor);
        var environment = frame.Environment;
        if (environment is null
            || string.Equals(environment.BackgroundPolicy, "camera", StringComparison.OrdinalIgnoreCase)
            || string.Equals(environment.BackgroundPolicy, "clear", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(environment.BackgroundColor))
        {
            return cameraColor;
        }

        return Parse(environment.BackgroundColor, cameraColor);
    }

    private static Vector4 Parse(string? value, Vector4 fallback)
    {
        if (value is { Length: 7 }
            && value[0] == '#'
            && byte.TryParse(value.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red)
            && byte.TryParse(value.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green)
            && byte.TryParse(value.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue))
        {
            return new Vector4(red / 255f, green / 255f, blue / 255f, 1f);
        }

        return fallback;
    }
}
