namespace Rekall.Age.Rendering;

public static class RekallAgeInteractiveAntialiasing
{
    public const int DefaultSupersampleFactor = 2;
    public const int MaximumSupersampleFactor = 4;

    public static int ResolveSupersampleFactor(int? requested) =>
        Math.Clamp(requested ?? DefaultSupersampleFactor, 1, MaximumSupersampleFactor);

    public static byte[] ResolveRgba(
        ReadOnlySpan<byte> source,
        int sourceWidth,
        int sourceHeight,
        int supersampleFactor)
    {
        supersampleFactor = Math.Clamp(supersampleFactor, 1, MaximumSupersampleFactor);
        if (sourceWidth < 1 || sourceHeight < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceWidth));
        }
        if (sourceWidth % supersampleFactor != 0 || sourceHeight % supersampleFactor != 0)
        {
            throw new ArgumentException("Supersampled dimensions must be divisible by the sample factor.");
        }
        if (source.Length != checked(sourceWidth * sourceHeight * 4))
        {
            throw new ArgumentException("RGBA byte count does not match the supplied source dimensions.", nameof(source));
        }

        if (supersampleFactor == 1) return source.ToArray();

        var outputWidth = sourceWidth / supersampleFactor;
        var outputHeight = sourceHeight / supersampleFactor;
        var output = new byte[checked(outputWidth * outputHeight * 4)];
        var sampleCount = supersampleFactor * supersampleFactor;
        for (var y = 0; y < outputHeight; y++)
        {
            for (var x = 0; x < outputWidth; x++)
            {
                var red = 0;
                var green = 0;
                var blue = 0;
                var alpha = 0;
                for (var sampleY = 0; sampleY < supersampleFactor; sampleY++)
                {
                    for (var sampleX = 0; sampleX < supersampleFactor; sampleX++)
                    {
                        var sourceIndex = (((y * supersampleFactor) + sampleY) * sourceWidth
                            + (x * supersampleFactor) + sampleX) * 4;
                        red += source[sourceIndex];
                        green += source[sourceIndex + 1];
                        blue += source[sourceIndex + 2];
                        alpha += source[sourceIndex + 3];
                    }
                }

                var outputIndex = (y * outputWidth + x) * 4;
                output[outputIndex] = checked((byte)((red + sampleCount / 2) / sampleCount));
                output[outputIndex + 1] = checked((byte)((green + sampleCount / 2) / sampleCount));
                output[outputIndex + 2] = checked((byte)((blue + sampleCount / 2) / sampleCount));
                output[outputIndex + 3] = checked((byte)((alpha + sampleCount / 2) / sampleCount));
            }
        }

        return output;
    }
}
