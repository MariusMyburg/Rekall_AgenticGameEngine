using System.Globalization;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Rendering;

/// <summary>
/// Resolves generic authored quality intent into backend-neutral feature settings without mutating world state.
/// </summary>
public sealed class RekallAgeRenderQualityProfileResolver
{
    public RekallAgeResolvedRenderFeaturePlan Resolve(
        RekallAgeRenderQualityIntent intent,
        RekallAgeRenderingDeviceCapabilities capabilities,
        int outputWidth,
        int outputHeight)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(capabilities);

        var degradations = new List<RekallAgeRenderFeatureDegradation>();
        var requestedPreset = intent.Preset ?? string.Empty;
        if (!Presets.TryGetValue(NormalizePreset(requestedPreset), out var preset))
        {
            preset = Presets["high"];
            AddInvalid(degradations, "preset", requestedPreset, preset.Name);
        }

        var resolutionScale = ResolveResolutionScale(intent.ResolutionScale, preset.ResolutionScale, degradations);
        var cascadeCount = ResolveBoundedInt(
            intent.ShadowCascadeCount, preset.CascadeCount, "shadowCascadeCount", 1, 4, degradations);
        var shadowResolution = ResolveBoundedInt(
            intent.ShadowResolution, preset.ShadowResolution, "shadowResolution", 1, int.MaxValue, degradations);
        var fogMode = ResolveFogMode(intent.FogMode, preset.FogMode, degradations);
        var particles = ResolveBoundedInt(
            intent.MaximumActiveParticles, preset.MaximumActiveParticles, "maximumActiveParticles", 0, int.MaxValue, degradations);
        var bloom = intent.Bloom ?? preset.Bloom;
        var ssao = intent.Ssao ?? preset.Ssao;
        var textureAnisotropy = preset.MaximumAnisotropy;
        var deviceAnisotropy = Math.Max(1, capabilities.MaximumSamplerAnisotropy);
        if (textureAnisotropy > deviceAnisotropy)
        {
            var requested = textureAnisotropy;
            textureAnisotropy = deviceAnisotropy;
            AddDeviceClamp(degradations, "textureAnisotropy", requested, textureAnisotropy);
        }

        if (shadowResolution > capabilities.MaximumTextureDimension2D)
        {
            var requested = shadowResolution;
            shadowResolution = Math.Max(1, capabilities.MaximumTextureDimension2D);
            AddDeviceClamp(degradations, "shadowResolution", requested, shadowResolution);
        }

        if (fogMode != "analytic" && (!capabilities.SupportsCompute || !capabilities.SupportsStorageTextures))
        {
            AddDeviceClamp(degradations, "fogMode", fogMode, "analytic");
            fogMode = "analytic";
        }

        if (particles > 0 && (!capabilities.SupportsCompute || !capabilities.SupportsStorageBuffers))
        {
            var requested = particles;
            particles = 0;
            AddDeviceClamp(degradations, "maximumActiveParticles", requested, particles);
        }

        var timestamps = intent.EnableGpuTimestamps;
        if (timestamps && !capabilities.SupportsTimestampQueries)
        {
            AddDeviceClamp(degradations, "gpuTimestamps", true, false);
            timestamps = false;
        }

        var safeOutputWidth = Math.Max(1, outputWidth);
        var safeOutputHeight = Math.Max(1, outputHeight);
        if (safeOutputWidth != outputWidth || safeOutputHeight != outputHeight)
        {
            AddDeviceClamp(
                degradations,
                "outputResolution",
                $"{outputWidth}x{outputHeight}",
                $"{safeOutputWidth}x{safeOutputHeight}");
        }

        var renderWidth = Math.Max(1, (int)Math.Round(safeOutputWidth * resolutionScale, MidpointRounding.AwayFromZero));
        var renderHeight = Math.Max(1, (int)Math.Round(safeOutputHeight * resolutionScale, MidpointRounding.AwayFromZero));
        var requestedRenderWidth = renderWidth;
        var requestedRenderHeight = renderHeight;
        var maximumRenderDimension = Math.Max(1, capabilities.MaximumTextureDimension2D);
        if (renderWidth > maximumRenderDimension || renderHeight > maximumRenderDimension)
        {
            var clampScale = Math.Min(
                (double)maximumRenderDimension / renderWidth,
                (double)maximumRenderDimension / renderHeight);
            renderWidth = Math.Min(maximumRenderDimension, Math.Max(1, (int)Math.Round(renderWidth * clampScale, MidpointRounding.AwayFromZero)));
            renderHeight = Math.Min(maximumRenderDimension, Math.Max(1, (int)Math.Round(renderHeight * clampScale, MidpointRounding.AwayFromZero)));
            resolutionScale *= clampScale;
            AddDeviceClamp(
                degradations,
                "renderResolution",
                $"{requestedRenderWidth}x{requestedRenderHeight}",
                $"{renderWidth}x{renderHeight}");
        }

        var fog = ResolveFogQuality(fogMode, renderWidth, renderHeight);
        if (!fog.Mode.Equals("analytic", StringComparison.Ordinal))
        {
            var computeDimensionLimit = capabilities.MaximumComputeWorkgroupsPerDimension > int.MaxValue / 4u
                ? int.MaxValue
                : checked((int)capabilities.MaximumComputeWorkgroupsPerDimension * 4);
            var maximumFogDimension = Math.Max(
                1,
                Math.Min(capabilities.MaximumTextureDimension3D, computeDimensionLimit));
            var largestFogDimension = Math.Max(fog.FroxelWidth, Math.Max(fog.FroxelHeight, fog.FroxelDepth));
            if (largestFogDimension > maximumFogDimension)
            {
                var requestedFog = fog;
                var fogScale = (double)maximumFogDimension / largestFogDimension;
                fog = fog with
                {
                    FroxelWidth = Math.Max(1, (int)Math.Round(fog.FroxelWidth * fogScale, MidpointRounding.AwayFromZero)),
                    FroxelHeight = Math.Max(1, (int)Math.Round(fog.FroxelHeight * fogScale, MidpointRounding.AwayFromZero)),
                    FroxelDepth = Math.Max(1, (int)Math.Round(fog.FroxelDepth * fogScale, MidpointRounding.AwayFromZero))
                };
                AddDeviceClamp(
                    degradations,
                    "fogGrid",
                    $"{requestedFog.FroxelWidth}x{requestedFog.FroxelHeight}x{requestedFog.FroxelDepth}",
                    $"{fog.FroxelWidth}x{fog.FroxelHeight}x{fog.FroxelDepth}");
            }
        }
        var shadow = new RekallAgeResolvedShadowQuality(cascadeCount, shadowResolution, preset.FilterTapCount);
        var post = new RekallAgeResolvedPostQuality(bloom, ssao, timestamps);
        var particleQuality = new RekallAgeResolvedParticleQuality(particles);

        var renderPixels = (long)renderWidth * renderHeight;
        var fogDebugReadbackBytes = fog.Mode.Equals("analytic", StringComparison.Ordinal)
            ? 0L
            : (long)fog.FroxelWidth * fog.FroxelHeight * fog.FroxelDepth * 8L;
        var transient = renderPixels * (bloom ? 20L : 12L)
            + (ssao ? renderPixels * 2L : 0L)
            + fogDebugReadbackBytes;
        var persistent = (long)shadowResolution * shadowResolution * cascadeCount * 4L
            + (long)particles * 64L
            + (fog.Mode.Equals("analytic", StringComparison.Ordinal)
                ? 0L
                : (long)fog.FroxelWidth * fog.FroxelHeight * fog.FroxelDepth * 8L);

        return new RekallAgeResolvedRenderFeaturePlan(
            requestedPreset,
            preset.Name,
            safeOutputWidth,
            safeOutputHeight,
            renderWidth,
            renderHeight,
            resolutionScale,
            shadow,
            fog,
            post,
            particleQuality,
            transient,
            persistent,
            degradations)
        {
            Lighting = new RekallAgeResolvedLightingQuality(preset.MaximumPointLights),
            Textures = new RekallAgeResolvedTextureQuality(textureAnisotropy)
        };
    }

    private static readonly IReadOnlyDictionary<string, Preset> Presets = new Dictionary<string, Preset>(StringComparer.Ordinal)
    {
        ["performance"] = new("Performance", 0.50, 1, 512, "analytic", 2_000, false, false, 2, 2, 1),
        ["low"] = new("Low", 0.67, 1, 1024, "analytic", 8_000, true, false, 4, 4, 1),
        ["medium"] = new("Medium", 0.75, 2, 1024, "froxel-low", 24_000, true, true, 8, 8, 2),
        ["high"] = new("High", 1.00, 3, 2048, "froxel", 64_000, true, true, 12, 16, 8),
        ["ultra"] = new("Ultra", 1.00, 4, 2048, "froxel-high", 128_000, true, true, 16, 16, 16),
        ["epic"] = new("Epic", 1.25, 4, 4096, "froxel-epic", 250_000, true, true, 24, 16, 16)
    };

    private static string NormalizePreset(string value) => value.Trim().ToLowerInvariant();

    private static double ResolveResolutionScale(
        double? value,
        double fallback,
        ICollection<RekallAgeRenderFeatureDegradation> degradations)
    {
        if (!value.HasValue)
        {
            return fallback;
        }

        if (!double.IsFinite(value.Value) || value.Value <= 0 || value.Value > 2)
        {
            AddInvalid(degradations, "resolutionScale", value.Value, fallback);
            return fallback;
        }

        return value.Value;
    }

    private static int ResolveBoundedInt(
        int? value,
        int fallback,
        string feature,
        int minimum,
        int maximum,
        ICollection<RekallAgeRenderFeatureDegradation> degradations)
    {
        if (!value.HasValue)
        {
            return fallback;
        }

        if (value.Value < minimum || value.Value > maximum)
        {
            AddInvalid(degradations, feature, value.Value, fallback);
            return fallback;
        }

        return value.Value;
    }

    private static string ResolveFogMode(
        string? value,
        string fallback,
        ICollection<RekallAgeRenderFeatureDegradation> degradations)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized is "analytic" or "froxel-low" or "froxel" or "froxel-high" or "froxel-epic")
        {
            return normalized;
        }

        AddInvalid(degradations, "fogMode", value, fallback);
        return fallback;
    }

    private static RekallAgeResolvedFogQuality ResolveFogQuality(string mode, int width, int height)
    {
        return mode switch
        {
            "froxel-low" => new RekallAgeResolvedFogQuality(mode, Math.Max(1, width / 16), Math.Max(1, height / 16), 32),
            "froxel" => new RekallAgeResolvedFogQuality(mode, 160, 90, 48),
            "froxel-high" => new RekallAgeResolvedFogQuality(mode, 240, 135, 64),
            "froxel-epic" => new RekallAgeResolvedFogQuality(mode, 320, 180, 96),
            _ => new RekallAgeResolvedFogQuality("analytic")
        };
    }

    private static void AddInvalid(
        ICollection<RekallAgeRenderFeatureDegradation> degradations,
        string feature,
        object? requested,
        object? resolved) => degradations.Add(new(
            "REKALL_RENDER_QUALITY_OVERRIDE_INVALID",
            feature,
            ToInvariantString(requested),
            ToInvariantString(resolved),
            $"The authored {feature} override is invalid; the preset value was retained."));

    private static void AddDeviceClamp(
        ICollection<RekallAgeRenderFeatureDegradation> degradations,
        string feature,
        object? requested,
        object? resolved) => degradations.Add(new(
            "REKALL_RENDER_FEATURE_DEVICE_CLAMPED",
            feature,
            ToInvariantString(requested),
            ToInvariantString(resolved),
            $"The device does not support the requested {feature}; a supported value was resolved."));

    private static string ToInvariantString(object? value) => value switch
    {
        null => string.Empty,
        bool boolean => boolean ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    private sealed record Preset(
        string Name,
        double ResolutionScale,
        int CascadeCount,
        int ShadowResolution,
        string FogMode,
        int MaximumActiveParticles,
        bool Bloom,
        bool Ssao,
        int FilterTapCount,
        int MaximumPointLights,
        int MaximumAnisotropy);
}
