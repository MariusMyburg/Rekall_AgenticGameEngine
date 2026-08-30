namespace Rekall.Age.Studio;

internal enum RekallAgeStudioViewportRenderStyle
{
    Textured,
    SmoothShaded,
    FlatShaded,
    Wireframe,
    Clay
}

internal static class RekallAgeStudioViewportRenderStyles
{
    public static IReadOnlyList<string> Labels { get; } =
        ["Textured", "Smooth shaded", "Flat shaded", "Wireframe", "Clay"];

    public static RekallAgeStudioViewportRenderStyle Parse(string? label) => label switch
    {
        "Textured" => RekallAgeStudioViewportRenderStyle.Textured,
        "Flat shaded" => RekallAgeStudioViewportRenderStyle.FlatShaded,
        "Wireframe" => RekallAgeStudioViewportRenderStyle.Wireframe,
        "Clay" => RekallAgeStudioViewportRenderStyle.Clay,
        _ => RekallAgeStudioViewportRenderStyle.SmoothShaded
    };
}
