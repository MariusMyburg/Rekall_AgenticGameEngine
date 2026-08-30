using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Studio;

internal static class RekallAgeStudioViewportStyleAdapter
{
    public static RekallAgeRuntimeViewportFrame Apply(
        RekallAgeRuntimeViewportFrame frame,
        RekallAgeStudioViewportRenderStyle style)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (style is RekallAgeStudioViewportRenderStyle.Textured or RekallAgeStudioViewportRenderStyle.SmoothShaded)
            return frame;

        IReadOnlyList<RekallAgeRuntimeViewportRenderable> Adapt(
            IReadOnlyList<RekallAgeRuntimeViewportRenderable> renderables) =>
            renderables.Select(renderable => Apply(renderable, style)).ToArray();

        return frame with
        {
            Renderables = Adapt(frame.Renderables),
            CameraViews = frame.CameraViews.Select(view => view with { Renderables = Adapt(view.Renderables) }).ToArray()
        };
    }

    private static RekallAgeRuntimeViewportRenderable Apply(
        RekallAgeRuntimeViewportRenderable renderable,
        RekallAgeStudioViewportRenderStyle style) => style switch
    {
        RekallAgeStudioViewportRenderStyle.FlatShaded => renderable with
        {
            NormalTextureAssetId = null,
            NormalScale = 0,
            RoughnessFactor = 1
        },
        RekallAgeStudioViewportRenderStyle.Clay => renderable with
        {
            MaterialColor = "#B6AA97",
            TextureAssetId = null,
            MetallicRoughnessTextureAssetId = null,
            NormalTextureAssetId = null,
            OcclusionTextureAssetId = null,
            MetallicFactor = 0,
            RoughnessFactor = 1,
            NormalScale = 0,
            OcclusionStrength = 0,
            EmissiveColor = null,
            EmissiveTextureAssetId = null,
            EmissiveStrength = 0,
            ProceduralMaterial = null
        },
        RekallAgeStudioViewportRenderStyle.Wireframe => renderable with
        {
            MaterialColor = "#DCE8F2",
            TextureAssetId = null,
            MetallicRoughnessTextureAssetId = null,
            NormalTextureAssetId = null,
            OcclusionTextureAssetId = null,
            MetallicFactor = 0,
            RoughnessFactor = 1
        },
        _ => renderable
    };
}
