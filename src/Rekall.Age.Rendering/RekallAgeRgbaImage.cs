namespace Rekall.Age.Rendering;

public sealed record RekallAgeRgbaImage(int Width, int Height, byte[] Rgba)
{
    public RekallAgeRgbaPixel GetPixel(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            throw new ArgumentOutOfRangeException(nameof(x), $"Pixel coordinate {x},{y} is outside {Width}x{Height}.");
        }

        var index = (y * Width + x) * 4;
        return new RekallAgeRgbaPixel(Rgba[index], Rgba[index + 1], Rgba[index + 2], Rgba[index + 3]);
    }
}

public readonly record struct RekallAgeRgbaPixel(byte R, byte G, byte B, byte A);
