using Rekall.Age.Rendering.Abstractions;

namespace Rekall.Age.Rendering;

/// <summary>
/// Builds the backend-neutral fullscreen pass used to present a rendered scene texture.
/// Native backends execute the returned immutable command stream instead of owning pass semantics.
/// </summary>
public sealed class RekallAgePresentPassCommandPlanner : IDisposable
{
    private readonly RekallAgeInMemoryRenderingDevice _device;
    private readonly RekallAgeGraphicsResourceHandle _pipeline;
    private readonly RekallAgeGraphicsResourceHandle _sceneTextureSet;
    private readonly RekallAgeGraphicsResourceHandle _postProcessSet;
    private RekallAgeGraphicsResourceHandle _presentTexture;
    private RekallAgeGraphicsResourceHandle _renderTarget;
    private int _width;
    private int _height;
    private bool _disposed;

    public RekallAgePresentPassCommandPlanner(string backend)
    {
        _device = new(RekallAgeRenderingDeviceCapabilities.DesktopBaseline(
            string.IsNullOrWhiteSpace(backend) ? "portable" : backend.Trim()));

        var sampledTexture = Create(_device.CreateTexture(new(
            RekallAgeTextureDimension.Texture2D, 1, 1, 1, 1, 1, 1,
            RekallAgeTextureFormat.Rgba8Unorm,
            RekallAgeTextureUsage.Sampled,
            "present scene input")));
        var sampler = Create(_device.CreateSampler(new(Label: "present sampler")));
        var textureLayout = Create(_device.CreateBindingLayout(new(
        [
            new(0, RekallAgeBindingType.SampledTexture, RekallAgeShaderStage.Fragment),
            new(1, RekallAgeBindingType.Sampler, RekallAgeShaderStage.Fragment)
        ], "present texture layout")));
        _sceneTextureSet = Create(_device.CreateBindingSet(new(
            textureLayout,
            [new(0, sampledTexture), new(1, sampler)],
            "present scene set")));

        var uniform = Create(_device.CreateBuffer(new(
            256, RekallAgeBufferUsage.Uniform, RekallAgeMemoryAccess.Upload, "post-process uniform")));
        var uniformLayout = Create(_device.CreateBindingLayout(new(
            [new(0, RekallAgeBindingType.UniformBuffer, RekallAgeShaderStage.Fragment, 16)],
            "post-process layout")));
        _postProcessSet = Create(_device.CreateBindingSet(new(
            uniformLayout,
            [new(0, uniform, 0, 256)],
            "post-process set")));

        var vertex = Create(_device.CreateShaderModule(new(
            RekallAgeShaderStage.Vertex,
            RekallAgeShaderSourceLanguage.Glsl,
            "#version 450\nvoid main(){ gl_Position = vec4(0.0); }",
            Label: "fullscreen present vertex")));
        var fragment = Create(_device.CreateShaderModule(new(
            RekallAgeShaderStage.Fragment,
            RekallAgeShaderSourceLanguage.Glsl,
            "#version 450\nlayout(location=0) out vec4 color; void main(){ color=vec4(1.0); }",
            Label: "fullscreen present fragment")));
        _pipeline = Create(_device.CreateGraphicsPipeline(new(
            vertex,
            fragment,
            [textureLayout, uniformLayout],
            [new(RekallAgeTextureFormat.Bgra8UnormSrgb)],
            CullMode: RekallAgeCullMode.None,
            Label: "fullscreen present pipeline")));
    }

    public int SubmissionCount => _device.SubmissionCount;

    public RekallAgeGraphicsCommandBuffer Plan(int width, int height, RekallAgeColorClearValue clearColor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentNullException.ThrowIfNull(clearColor);
        EnsureTarget(width, height);

        using var encoder = _device.BeginCommandEncoder("fullscreen present");
        RequireValid(encoder.BeginRenderPass(new(
            _renderTarget,
            [clearColor],
            Label: "swapchain present")));
        RequireValid(encoder.SetRenderPipeline(_pipeline));
        RequireValid(encoder.SetBindingSet(0, _sceneTextureSet));
        RequireValid(encoder.SetBindingSet(1, _postProcessSet));
        RequireValid(encoder.Draw(3));
        RequireValid(encoder.EndRenderPass());
        var commandBuffer = encoder.Finish();
        RequireValid(_device.Submit(commandBuffer));
        return commandBuffer;
    }

    public IReadOnlyList<RekallAgeGraphicsResourceInspection> InspectResources()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _device.InspectResources();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _device.Dispose();
        _disposed = true;
    }

    private void EnsureTarget(int width, int height)
    {
        if (_renderTarget.IsValid && width == _width && height == _height) return;
        if (_renderTarget.IsValid) RequireValid(_device.Destroy(_renderTarget));
        if (_presentTexture.IsValid) RequireValid(_device.Destroy(_presentTexture));
        _presentTexture = Create(_device.CreateTexture(new(
            RekallAgeTextureDimension.Texture2D,
            width, height, 1, 1, 1, 1,
            RekallAgeTextureFormat.Bgra8UnormSrgb,
            RekallAgeTextureUsage.ColorAttachment | RekallAgeTextureUsage.Present,
            "swapchain color")));
        _renderTarget = Create(_device.CreateRenderTarget(new(
            [new(_presentTexture)], null, width, height, "swapchain")));
        _width = width;
        _height = height;
    }

    private static RekallAgeGraphicsResourceHandle Create(RekallAgeGraphicsResourceCreationResult result)
    {
        if (!result.Created)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics.Select(item => $"{item.Code}: {item.Message}")));
        }
        return result.Handle;
    }

    private static void RequireValid(RekallAgeGraphicsValidationResult result)
    {
        if (!result.Valid)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics.Select(item => $"{item.Code}: {item.Message}")));
        }
    }
}
