using Silk.NET.Vulkan;

namespace Rekall.Age.Rendering;

public static class RekallAgeVulkanTextureFormatMapper
{
    public static bool TryMapBlockCompressedFormat(string? format, out Format vulkanFormat)
    {
        var resolved = format switch
        {
            "BC1_UNorm" or "VK_FORMAT_BC1_RGB_UNORM_BLOCK" => (Format?)Format.BC1RgbUnormBlock,
            "VK_FORMAT_BC1_RGB_SRGB_BLOCK" => Format.BC1RgbSrgbBlock,
            "VK_FORMAT_BC1_RGBA_UNORM_BLOCK" => Format.BC1RgbaUnormBlock,
            "VK_FORMAT_BC1_RGBA_SRGB_BLOCK" => Format.BC1RgbaSrgbBlock,
            "BC2_UNorm" or "VK_FORMAT_BC2_UNORM_BLOCK" => Format.BC2UnormBlock,
            "VK_FORMAT_BC2_SRGB_BLOCK" => Format.BC2SrgbBlock,
            "BC3_UNorm" or "VK_FORMAT_BC3_UNORM_BLOCK" => Format.BC3UnormBlock,
            "VK_FORMAT_BC3_SRGB_BLOCK" => Format.BC3SrgbBlock,
            "BC4_UNorm" or "VK_FORMAT_BC4_UNORM_BLOCK" => Format.BC4UnormBlock,
            "VK_FORMAT_BC4_SNORM_BLOCK" => Format.BC4SNormBlock,
            "BC5_UNorm" or "VK_FORMAT_BC5_UNORM_BLOCK" => Format.BC5UnormBlock,
            "VK_FORMAT_BC5_SNORM_BLOCK" => Format.BC5SNormBlock,
            "VK_FORMAT_BC7_UNORM_BLOCK" => Format.BC7UnormBlock,
            "VK_FORMAT_BC7_SRGB_BLOCK" => Format.BC7SrgbBlock,
            _ => null
        };
        vulkanFormat = resolved.GetValueOrDefault();
        return resolved.HasValue;
    }
}
