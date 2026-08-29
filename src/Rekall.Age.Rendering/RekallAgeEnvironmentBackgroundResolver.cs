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

    public static Vector4 Resolve(RekallAgeRuntimeViewportFrame frame) =>
        ResolveForHdr(frame).EncodedSrgb;

    public static RekallAgeResolvedEnvironmentBackground ResolveForHdr(
        RekallAgeRuntimeViewportFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var cameraColor = Parse(frame.ActiveCamera?.ClearColor, DefaultColor);
        var environment = frame.Environment;
        var usesSkyTexture = environment is not null
            && !string.IsNullOrWhiteSpace(environment.SkyAssetId)
            && !string.Equals(environment.BackgroundPolicy, "camera", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(environment.BackgroundPolicy, "clear", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(environment.BackgroundPolicy, "color", StringComparison.OrdinalIgnoreCase);
        Vector4 encoded;
        if (environment is null
            || string.Equals(environment.BackgroundPolicy, "camera", StringComparison.OrdinalIgnoreCase)
            || string.Equals(environment.BackgroundPolicy, "clear", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(environment.BackgroundColor))
        {
            encoded = cameraColor;
        }
        else
        {
            encoded = Parse(environment.BackgroundColor, cameraColor);
        }

        return new RekallAgeResolvedEnvironmentBackground(
            encoded,
            new Vector4(
                SrgbToLinear(encoded.X),
                SrgbToLinear(encoded.Y),
                SrgbToLinear(encoded.Z),
                encoded.W),
            IsSolidColor: !usesSkyTexture);
    }

    private static Vector4 Parse(string? value, Vector4 fallback)
    {
        if (value is { Length: 7 or 9 }
            && value[0] == '#'
            && byte.TryParse(value.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red)
            && byte.TryParse(value.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green)
            && byte.TryParse(value.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue))
        {
            byte parsedAlpha = 255;
            if (value.Length == 9
                && !byte.TryParse(
                    value.AsSpan(7, 2),
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out parsedAlpha))
            {
                return fallback;
            }

            var alpha = parsedAlpha / 255f;
            return new Vector4(red / 255f, green / 255f, blue / 255f, alpha);
        }

        return fallback;
    }

    private static float SrgbToLinear(float encoded) =>
        encoded <= 0.04045f
            ? encoded / 12.92f
            : MathF.Pow((encoded + 0.055f) / 1.055f, 2.4f);
}

public readonly record struct RekallAgeResolvedEnvironmentBackground(
    Vector4 EncodedSrgb,
    Vector4 LinearRgba,
    bool IsSolidColor);
