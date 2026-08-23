using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Rendering;

/// <summary>
/// Persistent, content-hash-keyed cache of the generic <see cref="IRekallAgeRenderingDevice"/> resources needed to
/// draw a <see cref="RekallAgeRuntimeViewportFrame"/>: the shared scene pipeline, the frame/geometry buffers, and
/// one uniform buffer plus binding set per draw slot. Resources are only recreated when their required shape
/// changes; unchanged content is rewritten in place.
/// </summary>
public sealed class RekallAgeRenderingDeviceSceneResources
{
    public const uint FrameUniformSizeBytes = 128;
    public const uint DrawUniformSizeBytes = 96;
    public const int VertexStrideBytes = 48;

    /// <summary>
    /// The depth format the scene renderer always requests for its own, internally-composed depth attachment. A
    /// fixed format keeps the pipeline's <see cref="RekallAgeDepthStencilDescriptor"/> stable across frames instead
    /// of depending on whatever the caller's color target happens to be formatted as.
    /// </summary>
    public const RekallAgeTextureFormat DepthFormat = RekallAgeTextureFormat.Depth32Float;

    private readonly IRekallAgeRenderingDevice _device;

    private RekallAgeGraphicsResourceHandle _pipeline;
    private RekallAgeGraphicsResourceHandle _bindingLayout;
    private RekallAgeTextureFormat? _pipelineColorFormat;

    private RekallAgeGraphicsResourceHandle _composedTargetSourceColorTarget;
    private RekallAgeGraphicsResourceHandle _composedTarget;
    private RekallAgeGraphicsResourceHandle _depthTexture;
    private int _composedWidth;
    private int _composedHeight;

    private RekallAgeGraphicsResourceHandle _vertexBuffer;
    private ulong _vertexBufferCapacity;
    private RekallAgeGraphicsResourceHandle _indexBuffer;
    private ulong _indexBufferCapacity;
    private string? _geometryContentHash;

    private RekallAgeGraphicsResourceHandle _frameUniformBuffer;

    private readonly List<RekallAgeGraphicsResourceHandle> _drawUniformBuffers = [];
    private readonly List<RekallAgeGraphicsResourceHandle> _drawBindingSets = [];

    public RekallAgeRenderingDeviceSceneResources(IRekallAgeRenderingDevice device)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
    }

    public readonly record struct ResolvedPipeline(
        RekallAgeGraphicsResourceHandle Pipeline,
        RekallAgeGraphicsResourceHandle BindingLayout);

    public ResolvedPipeline? ResolvePipeline(RekallAgeTextureFormat colorFormat, List<RekallAgeGraphicsDiagnostic> diagnostics)
    {
        if (_pipeline.IsValid && _pipelineColorFormat == colorFormat)
        {
            return new ResolvedPipeline(_pipeline, _bindingLayout);
        }

        var vertexShader = _device.CreateShaderModule(new RekallAgeShaderModuleDescriptor(
            RekallAgeShaderStage.Vertex,
            RekallAgeShaderSourceLanguage.Wgsl,
            RekallAgeSceneWgslShaderSource.Source,
            "vs_main",
            "rekall-scene-vertex"));
        if (!vertexShader.Created)
        {
            diagnostics.AddRange(vertexShader.Diagnostics);
            return null;
        }

        var fragmentShader = _device.CreateShaderModule(new RekallAgeShaderModuleDescriptor(
            RekallAgeShaderStage.Fragment,
            RekallAgeShaderSourceLanguage.Wgsl,
            RekallAgeSceneWgslShaderSource.Source,
            "fs_main",
            "rekall-scene-fragment"));
        if (!fragmentShader.Created)
        {
            diagnostics.AddRange(fragmentShader.Diagnostics);
            return null;
        }

        var bindingLayout = _device.CreateBindingLayout(new RekallAgeBindingLayoutDescriptor(
            [
                new RekallAgeBindingLayoutEntry(0, RekallAgeBindingType.UniformBuffer, RekallAgeShaderStage.Vertex | RekallAgeShaderStage.Fragment, FrameUniformSizeBytes),
                new RekallAgeBindingLayoutEntry(1, RekallAgeBindingType.UniformBuffer, RekallAgeShaderStage.Vertex | RekallAgeShaderStage.Fragment, DrawUniformSizeBytes)
            ],
            "rekall-scene-binding-layout"));
        if (!bindingLayout.Created)
        {
            diagnostics.AddRange(bindingLayout.Diagnostics);
            return null;
        }

        var pipeline = _device.CreateGraphicsPipeline(new RekallAgeGraphicsPipelineDescriptor(
            vertexShader.Handle,
            fragmentShader.Handle,
            [bindingLayout.Handle],
            [new RekallAgeColorTargetDescriptor(colorFormat)],
            DepthStencil: new RekallAgeDepthStencilDescriptor(DepthFormat),
            Label: "rekall-scene-pipeline")
        {
            VertexBuffers =
            [
                new RekallAgeVertexBufferLayoutDescriptor(
                    VertexStrideBytes,
                    RekallAgeVertexStepMode.Vertex,
                    [
                        new RekallAgeVertexAttributeDescriptor("position", 0, RekallAgeVertexFormat.Float32x3, 0),
                        new RekallAgeVertexAttributeDescriptor("normal", 1, RekallAgeVertexFormat.Float32x3, 12),
                        new RekallAgeVertexAttributeDescriptor("color", 2, RekallAgeVertexFormat.Float32x4, 24),
                        new RekallAgeVertexAttributeDescriptor("uv", 3, RekallAgeVertexFormat.Float32x2, 40)
                    ])
            ]
        });
        if (!pipeline.Created)
        {
            diagnostics.AddRange(pipeline.Diagnostics);
            return null;
        }

        _bindingLayout = bindingLayout.Handle;
        _pipeline = pipeline.Handle;
        _pipelineColorFormat = colorFormat;
        return new ResolvedPipeline(_pipeline, _bindingLayout);
    }

    /// <summary>
    /// Composes the caller's color target with an internally-owned depth attachment sized to match it, entirely
    /// through the generic <see cref="IRekallAgeRenderingDevice"/> contract (no backend-specific canvas API): the
    /// caller's color target is inspected for its existing color attachments/dimensions via
    /// <see cref="IRekallAgeRenderingDevice.InspectResources"/>, and a depth texture plus a new render target
    /// combining both are created (or resized) only when the caller's color target handle or size actually changes
    /// -- including across a browser resize, since the caller creates a new color target handle on resize.
    /// </summary>
    public RekallAgeGraphicsResourceHandle? ResolveRenderTarget(
        RekallAgeGraphicsResourceHandle colorTarget,
        List<RekallAgeGraphicsDiagnostic> diagnostics)
    {
        var inspection = _device.InspectResources().FirstOrDefault(resource => resource.Handle == colorTarget);
        if (inspection?.Descriptor is not RekallAgeRenderTargetDescriptor colorDescriptor)
        {
            diagnostics.Add(new RekallAgeGraphicsDiagnostic(
                "REKALL_RENDERING_DEVICE_SCENE_COLOR_TARGET_NOT_FOUND",
                "The color target passed to the scene renderer was not found on the device.",
                colorTarget.ToString()));
            return null;
        }

        if (_composedTarget.IsValid
            && _composedTargetSourceColorTarget == colorTarget
            && _composedWidth == colorDescriptor.Width
            && _composedHeight == colorDescriptor.Height)
        {
            return _composedTarget;
        }

        if (_composedTarget.IsValid) _device.Destroy(_composedTarget);
        if (_depthTexture.IsValid) _device.Destroy(_depthTexture);
        _composedTarget = default;
        _depthTexture = default;

        var depth = _device.CreateTexture(new RekallAgeTextureDescriptor(
            RekallAgeTextureDimension.Texture2D, colorDescriptor.Width, colorDescriptor.Height, 1, 1, 1, 1,
            DepthFormat, RekallAgeTextureUsage.DepthStencilAttachment, "rekall-scene-depth"));
        if (!depth.Created)
        {
            diagnostics.AddRange(depth.Diagnostics);
            return null;
        }

        var composed = _device.CreateRenderTarget(new RekallAgeRenderTargetDescriptor(
            colorDescriptor.ColorAttachments,
            new RekallAgeRenderTargetAttachment(depth.Handle),
            colorDescriptor.Width,
            colorDescriptor.Height,
            "rekall-scene-target"));
        if (!composed.Created)
        {
            diagnostics.AddRange(composed.Diagnostics);
            _device.Destroy(depth.Handle);
            return null;
        }

        _depthTexture = depth.Handle;
        _composedTarget = composed.Handle;
        _composedTargetSourceColorTarget = colorTarget;
        _composedWidth = colorDescriptor.Width;
        _composedHeight = colorDescriptor.Height;
        return _composedTarget;
    }

    public readonly record struct GeometryBuffers(
        RekallAgeGraphicsResourceHandle VertexBuffer,
        RekallAgeGraphicsResourceHandle IndexBuffer);

    public GeometryBuffers? WriteGeometry(
        RekallAgeVulkanSceneGeometryUpload geometry,
        List<RekallAgeGraphicsDiagnostic> diagnostics)
    {
        var hash = Convert.ToHexString(SHA256.HashData([.. geometry.VertexBytes, .. geometry.IndexBytes]));
        if (hash == _geometryContentHash && _vertexBuffer.IsValid && _indexBuffer.IsValid)
        {
            return new GeometryBuffers(_vertexBuffer, _indexBuffer);
        }

        if (!EnsureBufferCapacity(
                ref _vertexBuffer, ref _vertexBufferCapacity, (ulong)geometry.VertexBytes.Length,
                RekallAgeBufferUsage.Vertex | RekallAgeBufferUsage.TransferDestination, "rekall-scene-vertices", diagnostics))
        {
            return null;
        }

        if (!EnsureBufferCapacity(
                ref _indexBuffer, ref _indexBufferCapacity, (ulong)geometry.IndexBytes.Length,
                RekallAgeBufferUsage.Index | RekallAgeBufferUsage.TransferDestination, "rekall-scene-indices", diagnostics))
        {
            return null;
        }

        var vertexWrite = _device.WriteBuffer(_vertexBuffer, 0, geometry.VertexBytes);
        if (!vertexWrite.Valid)
        {
            diagnostics.AddRange(vertexWrite.Diagnostics);
            return null;
        }

        var indexWrite = _device.WriteBuffer(_indexBuffer, 0, geometry.IndexBytes);
        if (!indexWrite.Valid)
        {
            diagnostics.AddRange(indexWrite.Diagnostics);
            return null;
        }

        _geometryContentHash = hash;
        return new GeometryBuffers(_vertexBuffer, _indexBuffer);
    }

    public RekallAgeGraphicsResourceHandle? WriteFrameUniform(
        RekallAgeVulkanSceneFrameUniform frame,
        List<RekallAgeGraphicsDiagnostic> diagnostics)
    {
        if (!_frameUniformBuffer.IsValid)
        {
            var buffer = _device.CreateBuffer(new RekallAgeBufferDescriptor(
                FrameUniformSizeBytes,
                RekallAgeBufferUsage.Uniform | RekallAgeBufferUsage.TransferDestination,
                Label: "rekall-scene-frame-uniform"));
            if (!buffer.Created)
            {
                diagnostics.AddRange(buffer.Diagnostics);
                return null;
            }

            _frameUniformBuffer = buffer.Handle;
        }

        var gpuFrame = RekallAgeVulkanSceneUniformUploadBuilder.BuildFrameUniform(frame);
        var bytes = MemoryMarshal.AsBytes(new[] { gpuFrame }.AsSpan()).ToArray();
        var write = _device.WriteBuffer(_frameUniformBuffer, 0, bytes);
        if (!write.Valid)
        {
            diagnostics.AddRange(write.Diagnostics);
            return null;
        }

        return _frameUniformBuffer;
    }

    public IReadOnlyList<RekallAgeGraphicsResourceHandle>? WriteDrawUniformsAndBindingSets(
        IReadOnlyList<RekallAgeVulkanScenePreparedDraw> draws,
        RekallAgeGraphicsResourceHandle frameUniformBuffer,
        RekallAgeGraphicsResourceHandle bindingLayout,
        List<RekallAgeGraphicsDiagnostic> diagnostics)
    {
        while (_drawUniformBuffers.Count < draws.Count)
        {
            var buffer = _device.CreateBuffer(new RekallAgeBufferDescriptor(
                DrawUniformSizeBytes,
                RekallAgeBufferUsage.Uniform | RekallAgeBufferUsage.TransferDestination,
                Label: $"rekall-scene-draw-uniform-{_drawUniformBuffers.Count}"));
            if (!buffer.Created)
            {
                diagnostics.AddRange(buffer.Diagnostics);
                return null;
            }

            _drawUniformBuffers.Add(buffer.Handle);
            _drawBindingSets.Add(default);
        }

        var bindingSets = new RekallAgeGraphicsResourceHandle[draws.Count];
        for (var i = 0; i < draws.Count; i++)
        {
            var gpuDraw = BuildDrawUniform(draws[i]);
            var bytes = MemoryMarshal.AsBytes(new[] { gpuDraw }.AsSpan()).ToArray();
            var write = _device.WriteBuffer(_drawUniformBuffers[i], 0, bytes);
            if (!write.Valid)
            {
                diagnostics.AddRange(write.Diagnostics);
                return null;
            }

            if (!_drawBindingSets[i].IsValid)
            {
                var bindingSet = _device.CreateBindingSet(new RekallAgeBindingSetDescriptor(
                    bindingLayout,
                    [
                        new RekallAgeBindingSetEntry(0, frameUniformBuffer, 0, FrameUniformSizeBytes),
                        new RekallAgeBindingSetEntry(1, _drawUniformBuffers[i], 0, DrawUniformSizeBytes)
                    ],
                    $"rekall-scene-draw-binding-{i}"));
                if (!bindingSet.Created)
                {
                    diagnostics.AddRange(bindingSet.Diagnostics);
                    return null;
                }

                _drawBindingSets[i] = bindingSet.Handle;
            }

            bindingSets[i] = _drawBindingSets[i];
        }

        return bindingSets;
    }

    private bool EnsureBufferCapacity(
        ref RekallAgeGraphicsResourceHandle handle,
        ref ulong capacity,
        ulong requiredBytes,
        RekallAgeBufferUsage usage,
        string label,
        List<RekallAgeGraphicsDiagnostic> diagnostics)
    {
        if (handle.IsValid && capacity >= requiredBytes)
        {
            return true;
        }

        if (handle.IsValid)
        {
            _device.Destroy(handle);
        }

        var sizeBytes = Math.Max(requiredBytes, 1);
        var result = _device.CreateBuffer(new RekallAgeBufferDescriptor(sizeBytes, usage, Label: label));
        if (!result.Created)
        {
            diagnostics.AddRange(result.Diagnostics);
            handle = default;
            capacity = 0;
            return false;
        }

        handle = result.Handle;
        capacity = sizeBytes;
        return true;
    }

    private static RekallAgeRenderingDeviceSceneGpuDrawUniform BuildDrawUniform(RekallAgeVulkanScenePreparedDraw draw)
    {
        return new RekallAgeRenderingDeviceSceneGpuDrawUniform(
            RekallAgeVulkanSceneUniformUploadBuilder.ToGpuMatrix(draw.Model),
            draw.MaterialFactors.X,
            draw.MaterialFactors.Y,
            draw.MaterialFactors.Z,
            draw.MaterialFactors.W,
            draw.EmissiveFactors.X,
            draw.EmissiveFactors.Y,
            draw.EmissiveFactors.Z,
            draw.EmissiveFactors.W);
    }
}

[StructLayout(LayoutKind.Sequential)]
public readonly record struct RekallAgeRenderingDeviceSceneGpuDrawUniform(
    RekallAgeVulkanSceneGpuMatrix4x4 Model,
    float MetallicFactor,
    float RoughnessFactor,
    float NormalScale,
    float OcclusionStrength,
    float EmissiveR,
    float EmissiveG,
    float EmissiveB,
    float EmissiveStrength);
