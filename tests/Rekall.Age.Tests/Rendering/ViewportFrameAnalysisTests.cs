using Rekall.Age.Rendering;

namespace Rekall.Age.Tests.Rendering;

public sealed class ViewportFrameAnalysisTests
{
    [Fact]
    public void AnalyzerReportsFlatColorFramesAsLowInformation()
    {
        var image = new RekallAgeRgbaImage(
            4,
            4,
            Enumerable.Range(0, 16)
                .SelectMany(_ => new byte[] { 0x11, 0x22, 0x33, 255 })
                .ToArray());

        var analysis = RekallAgeViewportFrameAnalyzer.Analyze(image);

        Assert.True(analysis.Analyzed);
        Assert.Equal(16, analysis.TotalPixels);
        Assert.Equal(1, analysis.DistinctColorCount);
        Assert.Equal(1, analysis.DominantColorRatio);
        Assert.Contains("REKALL_VIEWPORT_FLAT_COLOR", analysis.WarningCodes);
        Assert.Contains("REKALL_VIEWPORT_LOW_LUMINANCE_VARIANCE", analysis.WarningCodes);
        Assert.False(analysis.VisuallyInformative);
    }

    [Fact]
    public void AnalyzerReportsVariedFramesAsInformative()
    {
        byte[] rgba =
        [
            0, 0, 0, 255,
            255, 255, 255, 255,
            255, 0, 0, 255,
            0, 255, 0, 255,
            0, 0, 255, 255,
            255, 255, 0, 255,
            0, 255, 255, 255,
            255, 0, 255, 255,
            64, 64, 64, 255,
            192, 192, 192, 255,
            128, 32, 16, 255,
            16, 128, 32, 255,
            32, 16, 128, 255,
            220, 140, 40, 255,
            40, 220, 140, 255,
            140, 40, 220, 255
        ];

        var analysis = RekallAgeViewportFrameAnalyzer.Analyze(new RekallAgeRgbaImage(4, 4, rgba));

        Assert.True(analysis.VisuallyInformative);
        Assert.True(analysis.DistinctColorCount > 8);
        Assert.DoesNotContain("REKALL_VIEWPORT_FLAT_COLOR", analysis.WarningCodes);
    }
}
