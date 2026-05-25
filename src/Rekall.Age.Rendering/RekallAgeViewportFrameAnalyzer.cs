namespace Rekall.Age.Rendering;

public sealed record RekallAgeViewportFrameAnalysis(
    bool Analyzed,
    bool VisuallyInformative,
    int TotalPixels,
    int OpaquePixelCount,
    int DistinctColorCount,
    double DominantColorRatio,
    double NonTransparentPixelRatio,
    double AverageLuminance,
    double LuminanceStandardDeviation,
    IReadOnlyList<string> WarningCodes)
{
    public static RekallAgeViewportFrameAnalysis NotAnalyzed { get; } = new(
        false,
        false,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        Array.Empty<string>());
}

public static class RekallAgeViewportFrameAnalyzer
{
    public static RekallAgeViewportFrameAnalysis Analyze(RekallAgeRgbaImage image)
    {
        if (image.Width <= 0 || image.Height <= 0 || image.Rgba.Length != image.Width * image.Height * 4)
        {
            return RekallAgeViewportFrameAnalysis.NotAnalyzed;
        }

        var totalPixels = image.Width * image.Height;
        var opaquePixels = 0;
        var nonTransparentPixels = 0;
        var luminanceSum = 0d;
        var luminanceSquaredSum = 0d;
        var colorCounts = new Dictionary<int, int>();

        for (var i = 0; i < image.Rgba.Length; i += 4)
        {
            var r = image.Rgba[i + 0];
            var g = image.Rgba[i + 1];
            var b = image.Rgba[i + 2];
            var a = image.Rgba[i + 3];
            if (a > 0)
            {
                nonTransparentPixels++;
            }

            if (a == 255)
            {
                opaquePixels++;
            }

            var color = r << 24 | g << 16 | b << 8 | a;
            colorCounts[color] = colorCounts.TryGetValue(color, out var count) ? count + 1 : 1;

            var luminance = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255d;
            luminanceSum += luminance;
            luminanceSquaredSum += luminance * luminance;
        }

        var dominantColorRatio = colorCounts.Count == 0
            ? 0
            : colorCounts.Values.Max() / (double)totalPixels;
        var averageLuminance = luminanceSum / totalPixels;
        var variance = Math.Max(0, luminanceSquaredSum / totalPixels - averageLuminance * averageLuminance);
        var luminanceDeviation = Math.Sqrt(variance);
        var nonTransparentRatio = nonTransparentPixels / (double)totalPixels;
        var warningCodes = CreateWarningCodes(
            colorCounts.Count,
            dominantColorRatio,
            nonTransparentRatio,
            averageLuminance,
            luminanceDeviation);

        return new RekallAgeViewportFrameAnalysis(
            true,
            warningCodes.Count == 0,
            totalPixels,
            opaquePixels,
            colorCounts.Count,
            dominantColorRatio,
            nonTransparentRatio,
            averageLuminance,
            luminanceDeviation,
            warningCodes);
    }

    private static IReadOnlyList<string> CreateWarningCodes(
        int distinctColorCount,
        double dominantColorRatio,
        double nonTransparentRatio,
        double averageLuminance,
        double luminanceDeviation)
    {
        var warnings = new List<string>();
        if (nonTransparentRatio < 0.01)
        {
            warnings.Add("REKALL_VIEWPORT_NEARLY_TRANSPARENT");
        }

        if (distinctColorCount <= 1)
        {
            warnings.Add("REKALL_VIEWPORT_FLAT_COLOR");
        }
        else if (dominantColorRatio >= 0.985)
        {
            warnings.Add("REKALL_VIEWPORT_DOMINATED_BY_ONE_COLOR");
        }

        if (luminanceDeviation < 0.01)
        {
            warnings.Add("REKALL_VIEWPORT_LOW_LUMINANCE_VARIANCE");
        }

        if (averageLuminance < 0.015)
        {
            warnings.Add("REKALL_VIEWPORT_VERY_DARK");
        }

        return warnings;
    }
}
