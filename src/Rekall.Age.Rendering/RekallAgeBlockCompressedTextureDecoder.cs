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

        if (IsBc3Format(texture.Format))
        {
            return DecodeBc3(topLevel.Width, topLevel.Height, topLevel.Bytes);
        }

        if (IsBc4Format(texture.Format))
        {
            return DecodeBc4AlphaMask(topLevel.Width, topLevel.Height, topLevel.Bytes);
        }

        if (IsBc5Format(texture.Format))
        {
            return DecodeBc5NormalMap(topLevel.Width, topLevel.Height, topLevel.Bytes);
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

    private static RekallAgeRgbaImage DecodeBc3(int width, int height, byte[] blocks)
    {
        var blockColumns = Math.Max(1, (width + 3) / 4);
        var blockRows = Math.Max(1, (height + 3) / 4);
        var expectedBytes = checked(blockColumns * blockRows * 16);
        if (blocks.Length < expectedBytes)
        {
            throw new InvalidDataException($"BC3 texture data is {blocks.Length} bytes but {expectedBytes} bytes were expected for {width}x{height}.");
        }

        var rgba = new byte[checked(width * height * 4)];
        Span<byte> alphaPalette = stackalloc byte[8];
        for (var blockY = 0; blockY < blockRows; blockY++)
        {
            for (var blockX = 0; blockX < blockColumns; blockX++)
            {
                var blockOffset = (blockY * blockColumns + blockX) * 16;
                BuildBc3AlphaPalette(blocks[blockOffset], blocks[blockOffset + 1], alphaPalette);
                var alphaBits =
                    (ulong)blocks[blockOffset + 2]
                    | ((ulong)blocks[blockOffset + 3] << 8)
                    | ((ulong)blocks[blockOffset + 4] << 16)
                    | ((ulong)blocks[blockOffset + 5] << 24)
                    | ((ulong)blocks[blockOffset + 6] << 32)
                    | ((ulong)blocks[blockOffset + 7] << 40);
                DecodeBc1Block(
                    blocks,
                    blockOffset + 8,
                    rgba,
                    width,
                    height,
                    blockX * 4,
                    blockY * 4,
                    allowPunchThroughAlpha: false);
                ApplyBc3AlphaBlock(alphaPalette, alphaBits, rgba, width, height, blockX * 4, blockY * 4);
            }
        }

        return new RekallAgeRgbaImage(width, height, rgba);
    }

    private static RekallAgeRgbaImage DecodeBc4AlphaMask(int width, int height, byte[] blocks)
    {
        var blockColumns = Math.Max(1, (width + 3) / 4);
        var blockRows = Math.Max(1, (height + 3) / 4);
        var expectedBytes = checked(blockColumns * blockRows * 8);
        if (blocks.Length < expectedBytes)
        {
            throw new InvalidDataException($"BC4 texture data is {blocks.Length} bytes but {expectedBytes} bytes were expected for {width}x{height}.");
        }

        var rgba = new byte[checked(width * height * 4)];
        Span<byte> palette = stackalloc byte[8];
        for (var blockY = 0; blockY < blockRows; blockY++)
        {
            for (var blockX = 0; blockX < blockColumns; blockX++)
            {
                var blockOffset = (blockY * blockColumns + blockX) * 8;
                BuildBc3AlphaPalette(blocks[blockOffset], blocks[blockOffset + 1], palette);
                ApplyBc4AlphaMaskBlock(palette, ReadBc4IndexBits(blocks, blockOffset), rgba, width, height, blockX * 4, blockY * 4);
            }
        }

        return new RekallAgeRgbaImage(width, height, rgba);
    }

    private static RekallAgeRgbaImage DecodeBc5NormalMap(int width, int height, byte[] blocks)
    {
        var blockColumns = Math.Max(1, (width + 3) / 4);
        var blockRows = Math.Max(1, (height + 3) / 4);
        var expectedBytes = checked(blockColumns * blockRows * 16);
        if (blocks.Length < expectedBytes)
        {
            throw new InvalidDataException($"BC5 texture data is {blocks.Length} bytes but {expectedBytes} bytes were expected for {width}x{height}.");
        }

        var rgba = new byte[checked(width * height * 4)];
        Span<byte> redPalette = stackalloc byte[8];
        Span<byte> greenPalette = stackalloc byte[8];
        for (var blockY = 0; blockY < blockRows; blockY++)
        {
            for (var blockX = 0; blockX < blockColumns; blockX++)
            {
                var blockOffset = (blockY * blockColumns + blockX) * 16;
                BuildBc3AlphaPalette(blocks[blockOffset], blocks[blockOffset + 1], redPalette);
                BuildBc3AlphaPalette(blocks[blockOffset + 8], blocks[blockOffset + 9], greenPalette);
                ApplyBc5NormalBlock(
                    redPalette,
                    ReadBc4IndexBits(blocks, blockOffset),
                    greenPalette,
                    ReadBc4IndexBits(blocks, blockOffset + 8),
                    rgba,
                    width,
                    height,
                    blockX * 4,
                    blockY * 4);
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

    private static void BuildBc3AlphaPalette(byte a0, byte a1, Span<byte> palette)
    {
        palette[0] = a0;
        palette[1] = a1;
        if (a0 > a1)
        {
            palette[2] = LerpByte(a0, a1, 6, 1, 7);
            palette[3] = LerpByte(a0, a1, 5, 2, 7);
            palette[4] = LerpByte(a0, a1, 4, 3, 7);
            palette[5] = LerpByte(a0, a1, 3, 4, 7);
            palette[6] = LerpByte(a0, a1, 2, 5, 7);
            palette[7] = LerpByte(a0, a1, 1, 6, 7);
            return;
        }

        palette[2] = LerpByte(a0, a1, 4, 1, 5);
        palette[3] = LerpByte(a0, a1, 3, 2, 5);
        palette[4] = LerpByte(a0, a1, 2, 3, 5);
        palette[5] = LerpByte(a0, a1, 1, 4, 5);
        palette[6] = 0;
        palette[7] = 255;
    }

    private static void ApplyBc3AlphaBlock(
        ReadOnlySpan<byte> alphaPalette,
        ulong alphaBits,
        byte[] rgba,
        int width,
        int height,
        int x0,
        int y0)
    {
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

                var paletteIndex = (int)((alphaBits >> ((py * 4 + px) * 3)) & 0x7);
                var target = (y * width + x) * 4;
                rgba[target + 3] = alphaPalette[paletteIndex];
            }
        }
    }

    private static void ApplyBc4AlphaMaskBlock(
        ReadOnlySpan<byte> palette,
        ulong indexBits,
        byte[] rgba,
        int width,
        int height,
        int x0,
        int y0)
    {
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

                var paletteIndex = (int)((indexBits >> ((py * 4 + px) * 3)) & 0x7);
                var target = (y * width + x) * 4;
                rgba[target] = 255;
                rgba[target + 1] = 255;
                rgba[target + 2] = 255;
                rgba[target + 3] = palette[paletteIndex];
            }
        }
    }

    private static void ApplyBc5NormalBlock(
        ReadOnlySpan<byte> redPalette,
        ulong redIndexBits,
        ReadOnlySpan<byte> greenPalette,
        ulong greenIndexBits,
        byte[] rgba,
        int width,
        int height,
        int x0,
        int y0)
    {
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

                var bitOffset = (py * 4 + px) * 3;
                var r = redPalette[(int)((redIndexBits >> bitOffset) & 0x7)];
                var g = greenPalette[(int)((greenIndexBits >> bitOffset) & 0x7)];
                var target = (y * width + x) * 4;
                rgba[target] = r;
                rgba[target + 1] = g;
                rgba[target + 2] = ReconstructNormalZ(r, g);
                rgba[target + 3] = 255;
            }
        }
    }

    private static ulong ReadBc4IndexBits(byte[] blocks, int blockOffset)
    {
        return (ulong)blocks[blockOffset + 2]
            | ((ulong)blocks[blockOffset + 3] << 8)
            | ((ulong)blocks[blockOffset + 4] << 16)
            | ((ulong)blocks[blockOffset + 5] << 24)
            | ((ulong)blocks[blockOffset + 6] << 32)
            | ((ulong)blocks[blockOffset + 7] << 40);
    }

    private static byte ReconstructNormalZ(byte r, byte g)
    {
        var x = (r / 255.0 * 2.0) - 1.0;
        var y = (g / 255.0 * 2.0) - 1.0;
        var z = Math.Sqrt(Math.Max(0, 1.0 - (x * x) - (y * y)));
        return checked((byte)Math.Clamp(Math.Round(((z * 0.5) + 0.5) * 255.0), 0, 255));
    }

    private static byte LerpByte(byte a, byte b, int aw, int bw, int divisor)
    {
        return checked((byte)((a * aw + b * bw) / divisor));
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

    private static bool IsBc3Format(string? format)
    {
        return format is "BC3_UNorm" or "VK_FORMAT_BC3_UNORM_BLOCK" or "VK_FORMAT_BC3_SRGB_BLOCK";
    }

    private static bool IsBc4Format(string? format)
    {
        return format is "BC4_UNorm" or "VK_FORMAT_BC4_UNORM_BLOCK";
    }

    private static bool IsBc5Format(string? format)
    {
        return format is "BC5_UNorm" or "VK_FORMAT_BC5_UNORM_BLOCK";
    }

    private readonly record struct Rgba(byte R, byte G, byte B, byte A);
}
