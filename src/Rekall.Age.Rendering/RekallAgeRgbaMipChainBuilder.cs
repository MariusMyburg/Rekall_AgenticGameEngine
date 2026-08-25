namespace Rekall.Age.Rendering;

public sealed record RekallAgeRgbaMipLevel(int Level, int Width, int Height, byte[] Bytes);

public static class RekallAgeRgbaMipChainBuilder
{
    public static IReadOnlyList<RekallAgeRgbaMipLevel> Build(int width, int height, byte[] rgba)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentNullException.ThrowIfNull(rgba);
        if (rgba.Length != checked(width * height * 4))
        {
            throw new ArgumentException("RGBA payload size does not match its dimensions.", nameof(rgba));
        }

        var levels = new List<RekallAgeRgbaMipLevel>();
        var currentWidth = width;
        var currentHeight = height;
        var current = rgba;
        var level = 0;
        while (true)
        {
            levels.Add(new RekallAgeRgbaMipLevel(level, currentWidth, currentHeight, current));
            if (currentWidth == 1 && currentHeight == 1)
            {
                return levels;
            }

            var nextWidth = Math.Max(1, currentWidth / 2);
            var nextHeight = Math.Max(1, currentHeight / 2);
            var next = new byte[checked(nextWidth * nextHeight * 4)];
            for (var y = 0; y < nextHeight; y++)
            {
                for (var x = 0; x < nextWidth; x++)
                {
                    for (var channel = 0; channel < 4; channel++)
                    {
                        var sum = 0;
                        for (var offsetY = 0; offsetY < 2; offsetY++)
                        {
                            var sourceY = Math.Min(currentHeight - 1, y * 2 + offsetY);
                            for (var offsetX = 0; offsetX < 2; offsetX++)
                            {
                                var sourceX = Math.Min(currentWidth - 1, x * 2 + offsetX);
                                sum += current[(sourceY * currentWidth + sourceX) * 4 + channel];
                            }
                        }

                        next[(y * nextWidth + x) * 4 + channel] = checked((byte)((sum + 2) / 4));
                    }
                }
            }

            current = next;
            currentWidth = nextWidth;
            currentHeight = nextHeight;
            level++;
        }
    }
}
