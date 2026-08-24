using Silk.NET.Vulkan;

namespace Rekall.Age.Rendering;

public static class RekallAgeVulkanHighFidelityFormatValidator
{
    private const FormatFeatureFlags ShadowDepthRequiredFeatures =
        FormatFeatureFlags.DepthStencilAttachmentBit
        | FormatFeatureFlags.SampledImageBit
        | FormatFeatureFlags.SampledImageFilterLinearBit
        | FormatFeatureFlags.TransferSrcBit;

    private const FormatFeatureFlags FogFroxelRequiredFeatures =
        FormatFeatureFlags.StorageImageBit
        | FormatFeatureFlags.SampledImageBit
        | FormatFeatureFlags.TransferSrcBit;

    public static string? ValidateShadowDepthFormat(FormatFeatureFlags available) =>
        ValidateOptimalTilingFeatures(
            Format.D32Sfloat,
            available,
            ShadowDepthRequiredFeatures,
            "shadow-atlas");

    public static string? ValidateFogFroxelFormat(FormatFeatureFlags available) =>
        ValidateOptimalTilingFeatures(
            Format.R16G16B16A16Sfloat,
            available,
            FogFroxelRequiredFeatures,
            "fog-froxel");

    public static string? ValidateShadowAtlasLimits(
        uint requestedResolution,
        uint requestedLayers,
        uint maximumResolution,
        uint maximumLayers) =>
        requestedResolution <= maximumResolution && requestedLayers <= maximumLayers
            ? null
            : $"REKALL_SHADOW_ATLAS_LIMIT_EXCEEDED: requested {requestedResolution}x{requestedResolution} "
                + $"with {requestedLayers} layers; device limits are {maximumResolution}x{maximumResolution} "
                + $"with {maximumLayers} layers.";

    public static string? ValidateOptimalTilingFeatures(
        Format format,
        FormatFeatureFlags available,
        FormatFeatureFlags required,
        string resource)
    {
        var missing = required & ~available;
        return missing == 0
            ? null
            : $"REKALL_RENDER_FORMAT_UNSUPPORTED: '{resource}' cannot use {format}; missing optimal-tiling features '{missing}'.";
    }
}
