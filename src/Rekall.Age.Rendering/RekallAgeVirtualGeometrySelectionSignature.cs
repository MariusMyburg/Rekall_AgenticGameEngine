using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Rendering;

public static class RekallAgeVirtualGeometrySelectionSignature
{
    public static int Compute(RekallAgeRuntimeViewportFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var hash = new HashCode();
        var count = 0;
        foreach (var renderable in frame.Renderables)
        {
            if (renderable.VirtualGeometry is not { Enabled: true } settings)
            {
                continue;
            }

            count++;
            hash.Add(renderable.EntityId, StringComparer.Ordinal);
            hash.Add(settings);
            hash.Add(RekallAgeVirtualGeometryReducer.ResolveDistanceLodLevel(renderable, frame.ActiveCamera));
        }

        hash.Add(count);
        return hash.ToHashCode();
    }
}
