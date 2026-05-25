namespace Rekall.Age.Rendering;

public static class RekallAgeBlockCompressedTextureDecoder
{
    public static RekallAgeRgbaImage? TryDecodeTopLevel(RekallAgeRuntimeTextureAsset texture)
    {
        var topLevel = texture.MipLevels
            .OrderBy(level => level.Level)
            .FirstOrDefault();
        if (topLevel is null)
        {
            return null;
        }

        if (TryResolveBc1AlphaMode(texture.Format, out var allowPunchThroughAlpha))
        {
            return DecodeBc1(topLevel.Width, topLevel.Height, topLevel.Bytes, allowPunchThroughAlpha);
        }

        return null;
    }

    private static RekallAgeRgbaImage DecodeBc1(int width, int height, byte[] blocks, bool allowPunchThroughAlpha)
    {
        var blockColumns = Math.Max(1, (width + 3) / 4);
        var blockRows = Math.Max(1, (height + 3) / 4);
        var expectedBytes = checked(blockColumns * blockRows * 8);
        if (blocks.Length < expectedBytes)
        {
            throw new InvalidDataException($"BC1 texture data is {blocks.Length} bytes but {expectedBytes} bytes were expected for {width}x{height}.");
        }

        var rgba = new byte[checked(width * height * 4)];
        for (var blockY = 0; blockY < blockRows; blockY++)
        {
            for (var blockX = 0; blockX < blockColumns; blockX++)
            {
                var blockOffset = (blockY * blockColumns + blockX) * 8;
                DecodeBc1Block(blocks, blockOffset, rgba, width, height, blockX * 4, blockY * 4, allowPunchThroughAlpha);
            }
        }

        return new RekallAgeRgbaImage(width, height, rgba);
    }

    private static void DecodeBc1Block(
        byte[] blocks,
        int blockOffset,
        byte[] rgba,
        int width,
        int height,
        int x0,
        int y0,
        bool allowPunchThroughAlpha)
    {
        var c0 = blocks[blockOffset] | (blocks[blockOffset + 1] << 8);
        var c1 = blocks[blockOffset + 2] | (blocks[blockOffset + 3] << 8);
        Span<Rgba> palette = stackalloc Rgba[4];
        palette[0] = DecodeRgb565(c0, 255);
        palette[1] = DecodeRgb565(c1, 255);
        if (c0 > c1 || !allowPunchThroughAlpha)
        {
            palette[2] = Lerp(palette[0], palette[1], 2, 1, 3, 255);
            palette[3] = Lerp(palette[0], palette[1], 1, 2, 3, 255);
        }
        else
        {
            palette[2] = Lerp(palette[0], palette[1], 1, 1, 2, 255);
            palette[3] = new Rgba(0, 0, 0, 0);
        }

        var indices = (uint)(blocks[blockOffset + 4]
            | (blocks[blockOffset + 5] << 8)
            | (blocks[blockOffset + 6] << 16)
            | (blocks[blockOffset + 7] << 24));
        for (var py = 0; py < 4; py++)
        {
            var y = y0 + py;
            if (y >= height)
            {
                continue;
            }

            for (var px = 0; px < 4; px++)
            {
                var x = x0 + px;
                if (x >= width)
                {
                    continue;
                }

                var paletteIndex = (int)((indices >> ((py * 4 + px) * 2)) & 0x3);
                var color = palette[paletteIndex];
                var target = (y * width + x) * 4;
                rgba[target] = color.R;
                rgba[target + 1] = color.G;
                rgba[target + 2] = color.B;
                rgba[target + 3] = color.A;
            }
        }
    }

    private static Rgba DecodeRgb565(int value, byte alpha)
    {
        var r = (value >> 11) & 0x1f;
        var g = (value >> 5) & 0x3f;
        var b = value & 0x1f;
        return new Rgba(
            ExpandBits(r, 31),
            ExpandBits(g, 63),
            ExpandBits(b, 31),
            alpha);
    }

    private static byte ExpandBits(int value, int max)
    {
        return checked((byte)((value * 255 + max / 2) / max));
    }

    private static Rgba Lerp(Rgba a, Rgba b, int aw, int bw, int divisor, byte alpha)
    {
        return new Rgba(
            checked((byte)((a.R * aw + b.R * bw) / divisor)),
            checked((byte)((a.G * aw + b.G * bw) / divisor)),
            checked((byte)((a.B * aw + b.B * bw) / divisor)),
            alpha);
    }

    private static bool TryResolveBc1AlphaMode(string? format, out bool allowPunchThroughAlpha)
    {
        allowPunchThroughAlpha = false;
        switch (format)
        {
            case "BC1_UNorm":
            case "VK_FORMAT_BC1_RGBA_UNORM_BLOCK":
            case "VK_FORMAT_BC1_RGBA_SRGB_BLOCK":
                allowPunchThroughAlpha = true;
                return true;
            case "VK_FORMAT_BC1_RGB_UNORM_BLOCK":
            case "VK_FORMAT_BC1_RGB_SRGB_BLOCK":
                return true;
            default:
                return false;
        }
    }

    private readonly record struct Rgba(byte R, byte G, byte B, byte A);
}
