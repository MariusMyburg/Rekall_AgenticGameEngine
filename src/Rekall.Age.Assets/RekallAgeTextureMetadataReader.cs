using System.Buffers.Binary;
using System.Text;

namespace Rekall.Age.Assets;

public static class RekallAgeTextureMetadataReader
{
    private static readonly byte[] Ktx2Identifier = [0xAB, 0x4B, 0x54, 0x58, 0x20, 0x32, 0x30, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A];

    public static async ValueTask<RekallAgeTextureMetadata?> ReadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var extension = Path.GetExtension(path).ToLowerInvariant();
        var bytesToRead = extension switch
        {
            ".ktx2" => 80,
            ".dds" => 148,
            ".png" => 32,
            _ => 0
        };
        if (bytesToRead == 0)
        {
            return null;
        }

        var bytes = new byte[bytesToRead];
        await using var stream = File.OpenRead(path);
        var read = await stream.ReadAsync(bytes.AsMemory(0, bytes.Length), cancellationToken);
        return extension switch
        {
            ".ktx2" => TryReadKtx2(bytes.AsSpan(0, read), out var ktx2) ? ktx2 : null,
            ".dds" => TryReadDds(bytes.AsSpan(0, read), out var dds) ? dds : null,
            ".png" => TryReadPng(bytes.AsSpan(0, read), out var png) ? png : null,
            _ => null
        };
    }

    private static bool TryReadKtx2(
        ReadOnlySpan<byte> bytes,
        out RekallAgeTextureMetadata metadata)
    {
        metadata = default!;
        if (bytes.Length < 48 || !bytes[..Ktx2Identifier.Length].SequenceEqual(Ktx2Identifier))
        {
            return false;
        }

        var vkFormat = BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..16]);
        var width = BinaryPrimitives.ReadUInt32LittleEndian(bytes[20..24]);
        var height = BinaryPrimitives.ReadUInt32LittleEndian(bytes[24..28]);
        var mipLevels = BinaryPrimitives.ReadUInt32LittleEndian(bytes[40..44]);
        var supercompression = BinaryPrimitives.ReadUInt32LittleEndian(bytes[44..48]);
        if (width == 0 || height == 0)
        {
            return false;
        }

        metadata = new RekallAgeTextureMetadata(
            "ktx2",
            checked((int)width),
            checked((int)height),
            checked((int)Math.Max(1, mipLevels)),
            ToVulkanFormatName(vkFormat),
            ToKtx2SupercompressionName(supercompression),
            IsGpuCompressedVulkanFormat(vkFormat) || supercompression != 0);
        return true;
    }

    private static bool TryReadDds(
        ReadOnlySpan<byte> bytes,
        out RekallAgeTextureMetadata metadata)
    {
        metadata = default!;
        if (bytes.Length < 128
            || bytes[0] != (byte)'D'
            || bytes[1] != (byte)'D'
            || bytes[2] != (byte)'S'
            || bytes[3] != (byte)' '
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..8]) != 124)
        {
            return false;
        }

        var height = BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..16]);
        var width = BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..20]);
        var mipLevels = BinaryPrimitives.ReadUInt32LittleEndian(bytes[28..32]);
        var fourCc = Encoding.ASCII.GetString(bytes[84..88]).TrimEnd('\0', ' ');
        if (width == 0 || height == 0)
        {
            return false;
        }

        metadata = new RekallAgeTextureMetadata(
            "dds",
            checked((int)width),
            checked((int)height),
            checked((int)Math.Max(1, mipLevels)),
            ToDdsFormatName(fourCc),
            null,
            IsGpuCompressedDdsFormat(fourCc));
        return true;
    }

    private static bool TryReadPng(
        ReadOnlySpan<byte> bytes,
        out RekallAgeTextureMetadata metadata)
    {
        metadata = default!;
        var signature = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        if (bytes.Length < 24 || !bytes[..signature.Length].SequenceEqual(signature))
        {
            return false;
        }

        var width = BinaryPrimitives.ReadUInt32BigEndian(bytes[16..20]);
        var height = BinaryPrimitives.ReadUInt32BigEndian(bytes[20..24]);
        if (width == 0 || height == 0)
        {
            return false;
        }

        metadata = new RekallAgeTextureMetadata(
            "png",
            checked((int)width),
            checked((int)height),
            1,
            "R8G8B8A8_UNorm",
            null,
            false);
        return true;
    }

    private static string ToVulkanFormatName(uint vkFormat)
    {
        return vkFormat switch
        {
            0 => "VK_FORMAT_UNDEFINED",
            9 => "VK_FORMAT_R8_UNORM",
            37 => "VK_FORMAT_R8G8B8A8_UNORM",
            43 => "VK_FORMAT_R8G8B8A8_SRGB",
            70 => "VK_FORMAT_R16_UNORM",
            97 => "VK_FORMAT_R32_SFLOAT",
            131 => "VK_FORMAT_BC1_RGB_UNORM_BLOCK",
            132 => "VK_FORMAT_BC1_RGB_SRGB_BLOCK",
            133 => "VK_FORMAT_BC1_RGBA_UNORM_BLOCK",
            134 => "VK_FORMAT_BC1_RGBA_SRGB_BLOCK",
            135 => "VK_FORMAT_BC2_UNORM_BLOCK",
            136 => "VK_FORMAT_BC2_SRGB_BLOCK",
            137 => "VK_FORMAT_BC3_UNORM_BLOCK",
            138 => "VK_FORMAT_BC3_SRGB_BLOCK",
            139 => "VK_FORMAT_BC4_UNORM_BLOCK",
            140 => "VK_FORMAT_BC4_SNORM_BLOCK",
            141 => "VK_FORMAT_BC5_UNORM_BLOCK",
            142 => "VK_FORMAT_BC5_SNORM_BLOCK",
            143 => "VK_FORMAT_BC6H_UFLOAT_BLOCK",
            144 => "VK_FORMAT_BC6H_SFLOAT_BLOCK",
            145 => "VK_FORMAT_BC7_UNORM_BLOCK",
            146 => "VK_FORMAT_BC7_SRGB_BLOCK",
            _ => $"VK_FORMAT_{vkFormat}"
        };
    }

    private static string? ToKtx2SupercompressionName(uint scheme)
    {
        return scheme switch
        {
            0 => null,
            1 => "BasisLZ",
            2 => "Zstandard",
            3 => "ZLib",
            _ => $"KTX2_SUPERCOMPRESSION_{scheme}"
        };
    }

    private static bool IsGpuCompressedVulkanFormat(uint vkFormat)
    {
        return vkFormat is >= 131 and <= 146;
    }

    private static string? ToDdsFormatName(string fourCc)
    {
        return fourCc switch
        {
            "DXT1" => "BC1_UNorm",
            "DXT3" => "BC2_UNorm",
            "DXT5" => "BC3_UNorm",
            "ATI1" or "BC4U" => "BC4_UNorm",
            "ATI2" or "BC5U" => "BC5_UNorm",
            "DX10" => "DDS_DX10",
            "" => null,
            _ => fourCc
        };
    }

    private static bool IsGpuCompressedDdsFormat(string fourCc)
    {
        return fourCc is "DXT1" or "DXT3" or "DXT5" or "ATI1" or "ATI2" or "BC4U" or "BC5U" or "DX10";
    }
}
