using Rekall.Age.Rendering.Abstractions;
using System.Text;

namespace Rekall.Age.Rendering;

public static class RekallAgeRenderingDeviceValidator
{
    public static RekallAgeGraphicsValidationResult Validate(
        RekallAgeBufferDescriptor descriptor,
        RekallAgeRenderingDeviceCapabilities capabilities)
    {
        var diagnostics = new List<RekallAgeGraphicsDiagnostic>();
        if (descriptor.SizeBytes == 0 || descriptor.SizeBytes > capabilities.MaximumBufferSizeBytes)
        {
            diagnostics.Add(new(
                "REKALL_GPU_BUFFER_SIZE_INVALID",
                $"Buffer size must be between 1 and {capabilities.MaximumBufferSizeBytes} bytes.",
                descriptor.Label));
        }

        if (descriptor.Usage == RekallAgeBufferUsage.None)
        {
            diagnostics.Add(new("REKALL_GPU_BUFFER_USAGE_REQUIRED", "Buffer usage cannot be empty.", descriptor.Label));
        }

        var alignment = descriptor.Usage.HasFlag(RekallAgeBufferUsage.Uniform) ? 16UL : 4UL;
        if (descriptor.SizeBytes % alignment != 0)
        {
            diagnostics.Add(new(
                "REKALL_GPU_BUFFER_ALIGNMENT_INVALID",
                $"Buffer size must be aligned to {alignment} bytes for its declared usage.",
                descriptor.Label));
        }

        if (descriptor.Usage.HasFlag(RekallAgeBufferUsage.Storage) && !capabilities.SupportsStorageBuffers)
        {
            diagnostics.Add(Feature("storage buffers", descriptor.Label));
        }

        if (descriptor.Usage.HasFlag(RekallAgeBufferUsage.Indirect) && !capabilities.SupportsIndirectDrawing)
        {
            diagnostics.Add(Feature("indirect drawing", descriptor.Label));
        }

        if (descriptor.MemoryAccess == RekallAgeMemoryAccess.Readback
            && !descriptor.Usage.HasFlag(RekallAgeBufferUsage.Readback))
        {
            diagnostics.Add(new(
                "REKALL_GPU_BUFFER_ACCESS_INVALID",
                "Readback memory requires Readback buffer usage.",
                descriptor.Label));
        }

        return new(diagnostics);
    }

    public static RekallAgeGraphicsValidationResult Validate(
        RekallAgeTextureDescriptor descriptor,
        RekallAgeRenderingDeviceCapabilities capabilities)
    {
        var diagnostics = new List<RekallAgeGraphicsDiagnostic>();
        var maximumWidth = descriptor.Dimension switch
        {
            RekallAgeTextureDimension.Texture1D => capabilities.MaximumTextureDimension1D,
            RekallAgeTextureDimension.Texture3D => capabilities.MaximumTextureDimension3D,
            _ => capabilities.MaximumTextureDimension2D
        };
        if (descriptor.Width is < 1 || descriptor.Width > int.MaxValue || descriptor.Width > maximumWidth
            || descriptor.Height < 1
            || descriptor.Dimension != RekallAgeTextureDimension.Texture1D && descriptor.Height > capabilities.MaximumTextureDimension2D
            || descriptor.Depth < 1
            || descriptor.Dimension == RekallAgeTextureDimension.Texture3D && descriptor.Depth > capabilities.MaximumTextureDimension3D)
        {
            diagnostics.Add(new(
                "REKALL_GPU_TEXTURE_DIMENSION_LIMIT",
                "Texture dimensions are outside the selected backend's bounded limits.",
                descriptor.Label));
        }

        if (descriptor.Dimension == RekallAgeTextureDimension.Texture1D && (descriptor.Height != 1 || descriptor.Depth != 1)
            || descriptor.Dimension == RekallAgeTextureDimension.Texture2D && descriptor.Depth != 1
            || descriptor.Dimension == RekallAgeTextureDimension.Cube && (descriptor.Depth != 1 || descriptor.Width != descriptor.Height || descriptor.ArrayLayers % 6 != 0))
        {
            diagnostics.Add(new("REKALL_GPU_TEXTURE_SHAPE_INVALID", "Texture dimensions do not match the declared texture kind.", descriptor.Label));
        }

        if (descriptor.MipLevels < 1
            || descriptor.ArrayLayers < 1
            || descriptor.ArrayLayers > capabilities.MaximumTextureArrayLayers
            || descriptor.SampleCount is not (1 or 2 or 4 or 8))
        {
            diagnostics.Add(new("REKALL_GPU_TEXTURE_LAYOUT_INVALID", "Mip, layer, or sample counts are invalid or exceed backend limits.", descriptor.Label));
        }

        var depth = descriptor.Format is RekallAgeTextureFormat.Depth24Stencil8 or RekallAgeTextureFormat.Depth32Float;
        if (descriptor.Usage == RekallAgeTextureUsage.None
            || depth && descriptor.Usage.HasFlag(RekallAgeTextureUsage.ColorAttachment)
            || !depth && descriptor.Usage.HasFlag(RekallAgeTextureUsage.DepthStencilAttachment)
            || depth && descriptor.Usage.HasFlag(RekallAgeTextureUsage.Storage))
        {
            diagnostics.Add(new("REKALL_GPU_TEXTURE_USAGE_INVALID", "Texture format and usage flags are incompatible.", descriptor.Label));
        }

        if (descriptor.Usage.HasFlag(RekallAgeTextureUsage.Storage) && !capabilities.SupportsStorageTextures)
        {
            diagnostics.Add(Feature("storage textures", descriptor.Label));
        }

        return new(diagnostics);
    }

    public static RekallAgeGraphicsValidationResult Validate(
        RekallAgeSamplerDescriptor descriptor,
        RekallAgeRenderingDeviceCapabilities capabilities)
    {
        var diagnostics = new List<RekallAgeGraphicsDiagnostic>();
        if (!float.IsFinite(descriptor.MinimumLod)
            || !float.IsFinite(descriptor.MaximumLod)
            || descriptor.MinimumLod < 0
            || descriptor.MaximumLod < descriptor.MinimumLod)
        {
            diagnostics.Add(new("REKALL_GPU_SAMPLER_LOD_INVALID", "Sampler LOD bounds must be finite, nonnegative, and ordered.", descriptor.Label));
        }
        if (descriptor.MaximumAnisotropy < 1 || descriptor.MaximumAnisotropy > capabilities.MaximumSamplerAnisotropy)
        {
            diagnostics.Add(new("REKALL_GPU_SAMPLER_ANISOTROPY_LIMIT", $"Sampler anisotropy must be between 1 and {capabilities.MaximumSamplerAnisotropy}.", descriptor.Label));
        }
        return new(diagnostics);
    }

    public static RekallAgeGraphicsValidationResult Validate(
        RekallAgeShaderModuleDescriptor descriptor,
        RekallAgeRenderingDeviceCapabilities capabilities)
    {
        var diagnostics = new List<RekallAgeGraphicsDiagnostic>();
        if (descriptor.Stage is not (RekallAgeShaderStage.Vertex or RekallAgeShaderStage.Fragment or RekallAgeShaderStage.Compute))
        {
            diagnostics.Add(new("REKALL_GPU_SHADER_STAGE_INVALID", "A shader module must declare exactly one stage.", descriptor.Label));
        }
        if (descriptor.Stage == RekallAgeShaderStage.Compute && !capabilities.SupportsCompute)
        {
            diagnostics.Add(Feature("compute shaders", descriptor.Label));
        }
        if (string.IsNullOrWhiteSpace(descriptor.EntryPoint))
        {
            diagnostics.Add(new("REKALL_GPU_SHADER_ENTRY_REQUIRED", "Shader entry point is required.", descriptor.Label));
        }
        if (string.IsNullOrWhiteSpace(descriptor.Source)
            || Encoding.UTF8.GetByteCount(descriptor.Source) > capabilities.MaximumShaderSourceBytes)
        {
            diagnostics.Add(new("REKALL_GPU_SHADER_SOURCE_INVALID", $"Shader source must be nonempty and no larger than {capabilities.MaximumShaderSourceBytes} UTF-8 bytes.", descriptor.Label));
        }
        return new(diagnostics);
    }

    public static RekallAgeGraphicsValidationResult Validate(
        RekallAgeBindingLayoutDescriptor descriptor,
        RekallAgeRenderingDeviceCapabilities capabilities)
    {
        var diagnostics = new List<RekallAgeGraphicsDiagnostic>();
        if (descriptor.Entries.Count > capabilities.MaximumBindingsPerLayout)
        {
            diagnostics.Add(new("REKALL_GPU_BINDING_LIMIT", $"Binding layout exceeds {capabilities.MaximumBindingsPerLayout} entries.", descriptor.Label));
        }
        foreach (var duplicate in descriptor.Entries.GroupBy(entry => entry.Binding).Where(group => group.Count() > 1))
        {
            diagnostics.Add(new("REKALL_GPU_BINDING_DUPLICATE", $"Binding index {duplicate.Key} is declared more than once.", descriptor.Label));
        }
        foreach (var entry in descriptor.Entries)
        {
            if (entry.Binding < 0 || entry.Visibility == RekallAgeShaderStage.None
                || (entry.Visibility & ~(RekallAgeShaderStage.Vertex | RekallAgeShaderStage.Fragment | RekallAgeShaderStage.Compute)) != 0)
            {
                diagnostics.Add(new("REKALL_GPU_BINDING_INVALID", "Binding index and shader visibility must be valid.", descriptor.Label));
            }
            if (entry.Visibility.HasFlag(RekallAgeShaderStage.Compute) && !capabilities.SupportsCompute)
            {
                diagnostics.Add(Feature("compute shaders", descriptor.Label));
            }
            if (entry.Type is RekallAgeBindingType.ReadOnlyStorageBuffer or RekallAgeBindingType.StorageBuffer
                && !capabilities.SupportsStorageBuffers)
            {
                diagnostics.Add(Feature("storage buffers", descriptor.Label));
            }
            if (entry.Type is RekallAgeBindingType.ReadOnlyStorageTexture or RekallAgeBindingType.StorageTexture
                && !capabilities.SupportsStorageTextures)
            {
                diagnostics.Add(Feature("storage textures", descriptor.Label));
            }
        }
        return new(diagnostics);
    }

    public static RekallAgeGraphicsValidationResult Validate(
        RekallAgeGraphicsPipelineDescriptor descriptor,
        RekallAgeRenderingDeviceCapabilities capabilities)
    {
        var diagnostics = new List<RekallAgeGraphicsDiagnostic>();
        if (!IsHandleKind(descriptor.VertexShader, RekallAgeGraphicsResourceKind.ShaderModule)
            || !IsHandleKind(descriptor.FragmentShader, RekallAgeGraphicsResourceKind.ShaderModule))
        {
            diagnostics.Add(new("REKALL_GPU_PIPELINE_SHADER_INVALID", "Graphics pipeline requires valid vertex and fragment shader module handles.", descriptor.Label));
        }
        if (descriptor.BindingLayouts.Any(handle => !IsHandleKind(handle, RekallAgeGraphicsResourceKind.BindingLayout)))
        {
            diagnostics.Add(new("REKALL_GPU_PIPELINE_LAYOUT_INVALID", "Graphics pipeline binding layouts must be valid layout handles.", descriptor.Label));
        }
        if (descriptor.ColorTargets.Count == 0 || descriptor.ColorTargets.Count > capabilities.MaximumColorAttachments)
        {
            diagnostics.Add(new("REKALL_GPU_COLOR_ATTACHMENT_LIMIT", $"Graphics pipeline requires 1 to {capabilities.MaximumColorAttachments} color targets.", descriptor.Label));
        }
        if (descriptor.ColorTargets.Any(target => IsDepth(target.Format))
            || descriptor.DepthStencil is not null && !IsDepth(descriptor.DepthStencil.Format))
        {
            diagnostics.Add(new("REKALL_GPU_PIPELINE_FORMAT_INVALID", "Color and depth targets must use compatible formats.", descriptor.Label));
        }
        return new(diagnostics);
    }

    public static RekallAgeGraphicsValidationResult Validate(
        RekallAgeComputePipelineDescriptor descriptor,
        RekallAgeRenderingDeviceCapabilities capabilities)
    {
        var diagnostics = new List<RekallAgeGraphicsDiagnostic>();
        if (!capabilities.SupportsCompute)
        {
            diagnostics.Add(Feature("compute shaders", descriptor.Label));
        }
        if (!IsHandleKind(descriptor.ComputeShader, RekallAgeGraphicsResourceKind.ShaderModule))
        {
            diagnostics.Add(new("REKALL_GPU_PIPELINE_SHADER_INVALID", "Compute pipeline requires a valid compute shader module handle.", descriptor.Label));
        }
        if (descriptor.BindingLayouts.Any(handle => !IsHandleKind(handle, RekallAgeGraphicsResourceKind.BindingLayout)))
        {
            diagnostics.Add(new("REKALL_GPU_PIPELINE_LAYOUT_INVALID", "Compute pipeline binding layouts must be valid layout handles.", descriptor.Label));
        }
        return new(diagnostics);
    }

    private static bool IsHandleKind(RekallAgeGraphicsResourceHandle handle, RekallAgeGraphicsResourceKind kind) =>
        handle.IsValid && handle.Kind == kind;

    private static bool IsDepth(RekallAgeTextureFormat format) =>
        format is RekallAgeTextureFormat.Depth24Stencil8 or RekallAgeTextureFormat.Depth32Float;

    private static RekallAgeGraphicsDiagnostic Feature(string feature, string? target) => new(
        "REKALL_GPU_FEATURE_REQUIRED",
        $"The selected backend does not support required feature '{feature}'.",
        target);
}
