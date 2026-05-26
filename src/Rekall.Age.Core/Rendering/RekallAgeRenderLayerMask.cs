namespace Rekall.Age.Core.Rendering;

public static class RekallAgeRenderLayerMask
{
    private static readonly char[] Separators = [',', ';', '|', ' ', '\t', '\r', '\n'];

    public static string NormalizeLayer(string? layer)
    {
        return string.IsNullOrWhiteSpace(layer)
            ? "default"
            : layer.Trim().ToLowerInvariant();
    }

    public static string NormalizeCullingMask(string? cullingMask)
    {
        return string.IsNullOrWhiteSpace(cullingMask)
            ? "*"
            : cullingMask.Trim();
    }

    public static bool IncludesLayer(string? layer, string? cullingMask)
    {
        if (string.IsNullOrWhiteSpace(cullingMask)
            || cullingMask.Trim().Equals("*", StringComparison.Ordinal))
        {
            return true;
        }

        var normalizedLayer = NormalizeLayer(layer);
        var tokens = SplitMask(cullingMask).ToArray();
        if (tokens
            .Where(IsExclusion)
            .Select(TrimExclusion)
            .Select(NormalizeLayer)
            .Any(mask => mask.Equals(normalizedLayer, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return tokens
            .Where(token => !IsExclusion(token))
            .Any(mask => mask.Equals("all", StringComparison.OrdinalIgnoreCase)
                || mask.Equals("*", StringComparison.Ordinal)
                || mask.Equals(normalizedLayer, StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<string> EnumerateIncludedLayers(string? cullingMask)
    {
        return SplitMask(cullingMask)
            .Where(token => !IsExclusion(token))
            .Select(NormalizeLayer)
            .Where(layer => layer is not "*" and not "all")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(layer => layer, StringComparer.Ordinal)
            .ToArray();
    }

    public static IReadOnlyList<string> EnumerateExcludedLayers(string? cullingMask)
    {
        return SplitMask(cullingMask)
            .Where(IsExclusion)
            .Select(TrimExclusion)
            .Select(NormalizeLayer)
            .Where(layer => layer is not "*" and not "all")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(layer => layer, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<string> SplitMask(string? cullingMask)
    {
        return string.IsNullOrWhiteSpace(cullingMask)
            ? []
            : cullingMask.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool IsExclusion(string token)
    {
        return token.StartsWith('!') || token.StartsWith('-');
    }

    private static string TrimExclusion(string token)
    {
        return IsExclusion(token) ? token[1..] : token;
    }
}
