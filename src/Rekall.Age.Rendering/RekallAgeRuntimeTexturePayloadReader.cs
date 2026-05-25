using System.Buffers.Binary;
using ZstdSharp;

namespace Rekall.Age.Rendering;

public static class RekallAgeRuntimeTexturePayloadReader
{
    private static readonly byte[] Ktx2Identifier = [0xAB, 0x4B, 0x54, 0x58, 0x20, 0x32, 0x30, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A];

    public static async ValueTask<RekallAgeRuntimeTextureAsset?> ReadAsync(
        string assetId,
        string path,
        Rekall.Age.Assets.RekallAgeTextureMetadata metadata,
        CancellationToken cancellationToken)
    {
        if (!metadata.GpuCompressed
            || (metadata.Supercompression is not null
                && !metadata.Supercompression.Equals("Zstandard", StringComparison.Ordinal)))
        {
            return null;
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return metadata.Container switch
        {
            "ktx2" => TryReadKtx2(assetId, bytes, metadata, out var ktx2) ? ktx2 : null,
            "dds" => TryReadDds(assetId, bytes, metadata, out var dds) ? dds : null,
            _ => null
        };
    }

    private static bool TryReadKtx2(
        string assetId,
        byte[] bytes,
        Rekall.Age.Assets.RekallAgeTextureMetadata metadata,
        out RekallAgeRuntimeTextureAsset texture)
    {
        texture = default!;
        if (bytes.Length < 104 || !bytes.AsSpan(0, Ktx2Identifier.Length).SequenceEqual(Ktx2Identifier))
        {
            return false;
        }

        var levelCount = Math.Max(1, checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(40, 4))));
        var levelIndexOffset = 80;
        var levels = new List<RekallAgeRuntimeTextureMipLevel>(levelCount);
        for (var level = 0; level < levelCount; level++)
        {
            var entryOffset = levelIndexOffset + level * 24;
            if (entryOffset + 24 > bytes.Length)
            {
                return false;
            }

            var byteOffset = checked((long)BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(entryOffset, 8)));
            var byteLength = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(entryOffset + 8, 8)));
            var uncompressedByteLength = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(entryOffset + 16, 8)));
            if (byteOffset < 0 || byteLength < 0 || byteOffset + byteLength > bytes.Length)
            {
                return false;
            }

            var levelBytes = bytes.AsSpan(checked((int)byteOffset), byteLength).ToArray();
            if (metadata.Supercompression?.Equals("Zstandard", StringComparison.Ordinal) == true)
            {
                if (uncompressedByteLength <= 0)
                {
                    return false;
                }

                using var decompressor = new Decompressor();
                var decompressed = new byte[uncompressedByteLength];
                var written = decompressor.Unwrap(levelBytes, decompressed, 0);
                if (written != uncompressedByteLength)
                {
                    Array.Resize(ref decompressed, written);
                }

                levelBytes = decompressed;
            }

            var expectedMipBytes = CalculateBlockCompressedMipBytes(
                metadata.Format,
                Math.Max(1, metadata.Width >> level),
                Math.Max(1, metadata.Height >> level));
            if (expectedMipBytes > 0 && levelBytes.Length > expectedMipBytes)
            {
                levelBytes = levelBytes.AsSpan(0, expectedMipBytes).ToArray();
            }

            levels.Add(new RekallAgeRuntimeTextureMipLevel(
                level,
                Math.Max(1, metadata.Width >> level),
                Math.Max(1, metadata.Height >> level),
                levelBytes));
        }

        texture = CreateTexture(assetId, metadata, levels);
        return true;
    }

    private static bool TryReadDds(
        string assetId,
        byte[] bytes,
        Rekall.Age.Assets.RekallAgeTextureMetadata metadata,
        out RekallAgeRuntimeTextureAsset texture)
    {
        texture = default!;
        if (bytes.Length < 128
            || bytes[0] != (byte)'D'
            || bytes[1] != (byte)'D'
            || bytes[2] != (byte)'S'
            || bytes[3] != (byte)' ')
        {
            return false;
        }

        var offset = 128;
        var levels = new List<RekallAgeRuntimeTextureMipLevel>(metadata.MipLevelCount);
        for (var level = 0; level < Math.Max(1, metadata.MipLevelCount); level++)
        {
            var width = Math.Max(1, metadata.Width >> level);
            var height = Math.Max(1, metadata.Height >> level);
            var byteLength = CalculateBlockCompressedMipBytes(metadata.Format, width, height);
            if (byteLength <= 0 || offset + byteLength > bytes.Length)
            {
                return false;
            }

            levels.Add(new RekallAgeRuntimeTextureMipLevel(
                level,
                width,
                height,
                bytes.AsSpan(offset, byteLength).ToArray()));
            offset += byteLength;
        }

        texture = CreateTexture(assetId, metadata, levels);
        return true;
    }

    private static int CalculateBlockCompressedMipBytes(string? format, int width, int height)
    {
        var blockBytes = format switch
        {
            "BC1_UNorm" or "VK_FORMAT_BC1_RGB_UNORM_BLOCK" or "VK_FORMAT_BC1_RGB_SRGB_BLOCK"
                or "VK_FORMAT_BC1_RGBA_UNORM_BLOCK" or "VK_FORMAT_BC1_RGBA_SRGB_BLOCK" => 8,
            "BC2_UNorm" or "BC3_UNorm" or "BC4_UNorm" or "BC5_UNorm" or "VK_FORMAT_BC2_UNORM_BLOCK"
                or "VK_FORMAT_BC2_SRGB_BLOCK" or "VK_FORMAT_BC3_UNORM_BLOCK" or "VK_FORMAT_BC3_SRGB_BLOCK"
                or "VK_FORMAT_BC4_UNORM_BLOCK" or "VK_FORMAT_BC4_SNORM_BLOCK" or "VK_FORMAT_BC5_UNORM_BLOCK"
                or "VK_FORMAT_BC5_SNORM_BLOCK" or "VK_FORMAT_BC7_UNORM_BLOCK" or "VK_FORMAT_BC7_SRGB_BLOCK" => 16,
            _ => 0
        };
        if (blockBytes == 0)
        {
            return 0;
        }

        return checked(Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * blockBytes);
    }

    private static RekallAgeRuntimeTextureAsset CreateTexture(
        string assetId,
        Rekall.Age.Assets.RekallAgeTextureMetadata metadata,
        IReadOnlyList<RekallAgeRuntimeTextureMipLevel> levels)
    {
        return new RekallAgeRuntimeTextureAsset(
            assetId,
            metadata.Container,
            metadata.Width,
            metadata.Height,
            metadata.MipLevelCount,
            metadata.Format,
            metadata.Supercompression,
            metadata.GpuCompressed,
            levels);
    }
}
