namespace Rekall.Age.Rendering;

public sealed record RekallAgeScreenshotResult(
    string ScreenshotPath,
    bool NonBlank,
    int Width,
    int Height,
    int VisibleRenderers,
    string? ActiveCamera);
