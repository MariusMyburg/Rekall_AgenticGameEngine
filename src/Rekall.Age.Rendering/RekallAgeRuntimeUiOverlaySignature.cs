using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Rendering;

public static class RekallAgeRuntimeUiOverlaySignature
{
    public static int Compute(RekallAgeRuntimeViewportFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var hash = new HashCode();
        hash.Add(frame.Width);
        hash.Add(frame.Height);
        foreach (var renderable in frame.Renderables)
        {
            if (renderable.UiVisual is not { } visual)
            {
                continue;
            }

            hash.Add(renderable.EntityId, StringComparer.Ordinal);
            hash.Add(renderable.AssetId, StringComparer.Ordinal);
            hash.Add(visual.Kind, StringComparer.Ordinal);
            hash.Add(visual.X);
            hash.Add(visual.Y);
            hash.Add(visual.Width);
            hash.Add(visual.Height);
            hash.Add(visual.ClipX);
            hash.Add(visual.ClipY);
            hash.Add(visual.ClipWidth);
            hash.Add(visual.ClipHeight);
            hash.Add(visual.Text, StringComparer.Ordinal);
            hash.Add(visual.BackgroundColor, StringComparer.Ordinal);
            hash.Add(visual.ForegroundColor, StringComparer.Ordinal);
            hash.Add(visual.BorderColor, StringComparer.Ordinal);
            hash.Add(visual.BorderWidth);
            hash.Add(visual.FontSize);
            hash.Add(visual.AssetId, StringComparer.Ordinal);
            hash.Add(visual.FontFamily, StringComparer.Ordinal);
            hash.Add(visual.FontWeight, StringComparer.Ordinal);
            hash.Add(visual.FontStyle, StringComparer.Ordinal);
            hash.Add(visual.FontAssetId, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }
}
