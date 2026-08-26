using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Tests.Rendering;

public sealed class RuntimeUiOverlaySignatureTests
{
    [Fact]
    public void SignatureIgnoresSimulationFrameButChangesWithVisibleUiContent()
    {
        var frame = Frame("INTEGRITY 100");

        var unchanged = RekallAgeRuntimeUiOverlaySignature.Compute(frame with { FrameIndex = 42, ElapsedSeconds = 3 });
        var changed = RekallAgeRuntimeUiOverlaySignature.Compute(Frame("INTEGRITY 88"));

        Assert.Equal(RekallAgeRuntimeUiOverlaySignature.Compute(frame), unchanged);
        Assert.NotEqual(unchanged, changed);
    }

    private static RekallAgeRuntimeViewportFrame Frame(string text) => new(
        "Main",
        1,
        0,
        1280,
        720,
        null,
        [],
        [new RekallAgeRuntimeViewportRenderable(
            "status",
            "Status",
            "ui",
            null,
            0,
            0,
            0,
            0,
            UiVisual: new RekallAgeRuntimeViewportUiVisual(
                "label", 24, 24, 300, 40, 0, 0, 1280, 720, text,
                "#00000000", "#ffffff", "#00000000", 0, 18))],
        1,
        new RekallAgeRuntimeViewportOverlay(false, 0),
        []);
}
