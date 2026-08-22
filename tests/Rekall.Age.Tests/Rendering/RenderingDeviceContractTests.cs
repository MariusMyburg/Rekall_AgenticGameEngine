using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Rendering.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;

namespace Rekall.Age.Tests.Rendering;

public sealed class RenderingDeviceContractTests
{
    [Fact]
    public async Task WorkloadInspectionExposesPortableLimitsAndStableDiagnostics()
    {
        var command = new InspectRenderingDeviceWorkloadCommand();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("inspect GPU workload"), CancellationToken.None);
        var result = await command.ExecuteAsync(new(
            "webgpu",
            [new(4_096, RekallAgeBufferUsage.Vertex, Label: "vertices")],
            [new(RekallAgeTextureDimension.Texture2D, 1920, 1080, 1, 1, 1, 1,
                RekallAgeTextureFormat.Rgba8Unorm, RekallAgeTextureUsage.ColorAttachment, "color")],
            [], [], [],
            MaximumDispatchX: 65_536,
            MaximumDispatchY: 1,
            MaximumDispatchZ: 1), context);

        Assert.True(result.Ok);
        Assert.False(result.Value.Valid);
        Assert.Equal("webgpu", result.Value.Capabilities.Backend);
        Assert.Equal(2, result.Value.ResourceDescriptorCount);
        Assert.Equal(4_096UL, result.Value.TotalBufferBytes);
        Assert.Contains("draw-indexed", result.Value.CommandSurface);
        Assert.Contains(result.Value.Diagnostics, item => item.Code == "REKALL_GPU_DISPATCH_RANGE_INVALID");
    }

    [Fact]
    public void ValidatesBufferDescriptorsAndRequiredCapabilities()
    {
        var capabilities = RekallAgeRenderingDeviceCapabilities.DesktopBaseline("test");
        var valid = RekallAgeRenderingDeviceValidator.Validate(
            new RekallAgeBufferDescriptor(
                4096,
                RekallAgeBufferUsage.Vertex | RekallAgeBufferUsage.TransferDestination,
                RekallAgeMemoryAccess.DeviceLocal,
                "scene vertices"),
            capabilities);

        Assert.True(valid.Valid, string.Join(Environment.NewLine, valid.Diagnostics.Select(item => item.Message)));

        var invalid = RekallAgeRenderingDeviceValidator.Validate(
            new RekallAgeBufferDescriptor(
                3,
                RekallAgeBufferUsage.Uniform | RekallAgeBufferUsage.Storage,
                RekallAgeMemoryAccess.Upload,
                "bad"),
            capabilities with { SupportsStorageBuffers = false });

        Assert.False(invalid.Valid);
        Assert.Contains(invalid.Diagnostics, item => item.Code == "REKALL_GPU_BUFFER_ALIGNMENT_INVALID");
        Assert.Contains(invalid.Diagnostics, item => item.Code == "REKALL_GPU_FEATURE_REQUIRED");
    }

    [Fact]
    public void ValidatesTextureShapeUsageAndBudgets()
    {
        var capabilities = RekallAgeRenderingDeviceCapabilities.DesktopBaseline("test") with
        {
            MaximumTextureDimension2D = 4096
        };
        var valid = RekallAgeRenderingDeviceValidator.Validate(
            new RekallAgeTextureDescriptor(
                RekallAgeTextureDimension.Texture2D,
                1920,
                1080,
                1,
                1,
                1,
                1,
                RekallAgeTextureFormat.Rgba8UnormSrgb,
                RekallAgeTextureUsage.Sampled | RekallAgeTextureUsage.CopyDestination,
                "scene color"),
            capabilities);
        Assert.True(valid.Valid);

        var invalid = RekallAgeRenderingDeviceValidator.Validate(
            new RekallAgeTextureDescriptor(
                RekallAgeTextureDimension.Texture2D,
                8192,
                8,
                1,
                1,
                1,
                1,
                RekallAgeTextureFormat.Depth24Stencil8,
                RekallAgeTextureUsage.ColorAttachment | RekallAgeTextureUsage.Storage,
                "bad depth"),
            capabilities);

        Assert.False(invalid.Valid);
        Assert.Contains(invalid.Diagnostics, item => item.Code == "REKALL_GPU_TEXTURE_DIMENSION_LIMIT");
        Assert.Contains(invalid.Diagnostics, item => item.Code == "REKALL_GPU_TEXTURE_USAGE_INVALID");
    }

    [Fact]
    public void ValidatesSamplerShaderAndBindingLayoutDescriptors()
    {
        var capabilities = RekallAgeRenderingDeviceCapabilities.DesktopBaseline("test") with
        {
            SupportsCompute = false,
            SupportsStorageTextures = false,
            MaximumSamplerAnisotropy = 8
        };

        Assert.True(RekallAgeRenderingDeviceValidator.Validate(
            new RekallAgeSamplerDescriptor(
                RekallAgeFilter.Linear,
                RekallAgeFilter.Linear,
                RekallAgeMipmapFilter.Linear,
                MaximumAnisotropy: 8,
                Label: "material sampler"),
            capabilities).Valid);
        var sampler = RekallAgeRenderingDeviceValidator.Validate(
            new RekallAgeSamplerDescriptor(MaximumAnisotropy: 16),
            capabilities);
        Assert.Contains(sampler.Diagnostics, item => item.Code == "REKALL_GPU_SAMPLER_ANISOTROPY_LIMIT");

        var shader = RekallAgeRenderingDeviceValidator.Validate(
            new RekallAgeShaderModuleDescriptor(
                RekallAgeShaderStage.Compute,
                RekallAgeShaderSourceLanguage.Glsl,
                "#version 450\nvoid main(){}",
                "main",
                "agent compute"),
            capabilities);
        Assert.Contains(shader.Diagnostics, item => item.Code == "REKALL_GPU_FEATURE_REQUIRED");

        var layout = RekallAgeRenderingDeviceValidator.Validate(
            new RekallAgeBindingLayoutDescriptor(
            [
                new(0, RekallAgeBindingType.UniformBuffer, RekallAgeShaderStage.Vertex),
                new(0, RekallAgeBindingType.StorageTexture, RekallAgeShaderStage.Fragment)
            ], "bad layout"),
            capabilities);
        Assert.Contains(layout.Diagnostics, item => item.Code == "REKALL_GPU_BINDING_DUPLICATE");
        Assert.Contains(layout.Diagnostics, item => item.Code == "REKALL_GPU_FEATURE_REQUIRED");
    }

    [Fact]
    public void ValidatesGraphicsComputePipelineAndRenderTargetShapes()
    {
        var capabilities = RekallAgeRenderingDeviceCapabilities.DesktopBaseline("test");
        var deviceId = Guid.NewGuid();
        var vertex = new RekallAgeGraphicsResourceHandle(deviceId, RekallAgeGraphicsResourceKind.ShaderModule, 1, 1);
        var fragment = new RekallAgeGraphicsResourceHandle(deviceId, RekallAgeGraphicsResourceKind.ShaderModule, 2, 1);
        var valid = RekallAgeRenderingDeviceValidator.Validate(
            new RekallAgeGraphicsPipelineDescriptor(
                vertex,
                fragment,
                [],
                [new(RekallAgeTextureFormat.Bgra8UnormSrgb)],
                new(RekallAgeTextureFormat.Depth24Stencil8),
                Label: "scene pipeline"),
            capabilities);
        Assert.True(valid.Valid, string.Join(Environment.NewLine, valid.Diagnostics.Select(item => item.Message)));

        var invalidVertexLayout = RekallAgeRenderingDeviceValidator.Validate(
            new RekallAgeGraphicsPipelineDescriptor(
                vertex,
                fragment,
                [],
                [new(RekallAgeTextureFormat.Bgra8UnormSrgb)])
            {
                VertexBuffers =
                [
                    new(16, RekallAgeVertexStepMode.Vertex,
                    [
                        new("Position", 0, RekallAgeVertexFormat.Float32x3, 8),
                        new("Duplicate", 0, RekallAgeVertexFormat.Float32x2, 0)
                    ])
                ]
            },
            capabilities);
        Assert.Contains(invalidVertexLayout.Diagnostics, item => item.Code == "REKALL_GPU_VERTEX_LOCATION_DUPLICATE");
        Assert.Contains(invalidVertexLayout.Diagnostics, item => item.Code == "REKALL_GPU_VERTEX_ATTRIBUTE_INVALID");

        var invalid = RekallAgeRenderingDeviceValidator.Validate(
            new RekallAgeGraphicsPipelineDescriptor(
                vertex,
                default,
                [],
                Enumerable.Range(0, capabilities.MaximumColorAttachments + 1)
                    .Select(_ => new RekallAgeColorTargetDescriptor(RekallAgeTextureFormat.Rgba8Unorm))
                    .ToArray()),
            capabilities);
        Assert.Contains(invalid.Diagnostics, item => item.Code == "REKALL_GPU_PIPELINE_SHADER_INVALID");
        Assert.Contains(invalid.Diagnostics, item => item.Code == "REKALL_GPU_COLOR_ATTACHMENT_LIMIT");

        var compute = RekallAgeRenderingDeviceValidator.Validate(
            new RekallAgeComputePipelineDescriptor(default, [], "missing shader"),
            capabilities);
        Assert.Contains(compute.Diagnostics, item => item.Code == "REKALL_GPU_PIPELINE_SHADER_INVALID");
    }

    [Fact]
    public void OpaqueHandlesRetainDeviceKindSlotAndGeneration()
    {
        var device = Guid.NewGuid();
        var handle = new RekallAgeGraphicsResourceHandle(
            device,
            RekallAgeGraphicsResourceKind.Buffer,
            Slot: 12,
            Generation: 3);

        Assert.True(handle.IsValid);
        Assert.True(handle.BelongsTo(device));
        Assert.False(handle.BelongsTo(Guid.NewGuid()));
        Assert.Equal("buffer:12@3", handle.ToString());
        Assert.False(default(RekallAgeGraphicsResourceHandle).IsValid);
    }

    [Fact]
    public void InMemoryDeviceRejectsStaleForeignAndOutOfRangeCommands()
    {
        using var device = new RekallAgeInMemoryRenderingDevice(
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("conformance"));
        var source = device.CreateBuffer(new(
            64,
            RekallAgeBufferUsage.CopySource,
            RekallAgeMemoryAccess.Upload,
            "source"));
        var destination = device.CreateBuffer(new(
            64,
            RekallAgeBufferUsage.TransferDestination,
            RekallAgeMemoryAccess.DeviceLocal,
            "destination"));
        Assert.True(source.Created);
        Assert.True(destination.Created);

        using var encoder = device.BeginCommandEncoder("copy");
        Assert.True(encoder.CopyBuffer(source.Handle, 0, destination.Handle, 0, 64).Valid);
        var range = encoder.CopyBuffer(source.Handle, 32, destination.Handle, 0, 64);
        Assert.Contains(range.Diagnostics, item => item.Code == "REKALL_GPU_COPY_RANGE_INVALID");

        using var other = new RekallAgeInMemoryRenderingDevice(
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("other"));
        var foreign = encoder.CopyBuffer(source.Handle, 0, other.CreateBuffer(new(
            64,
            RekallAgeBufferUsage.TransferDestination)).Handle, 0, 4);
        Assert.Contains(foreign.Diagnostics, item => item.Code == "REKALL_GPU_HANDLE_FOREIGN");

        Assert.True(device.Destroy(source.Handle).Valid);
        var stale = encoder.CopyBuffer(source.Handle, 0, destination.Handle, 0, 4);
        Assert.Contains(stale.Diagnostics, item => item.Code == "REKALL_GPU_HANDLE_STALE");
    }

    [Fact]
    public void FinishedCommandBuffersAreImmutableInspectableAndSubmittable()
    {
        using var device = new RekallAgeInMemoryRenderingDevice(
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("conformance"));
        var source = device.CreateBuffer(new(16, RekallAgeBufferUsage.CopySource, Label: "source")).Handle;
        var destination = device.CreateBuffer(new(16, RekallAgeBufferUsage.TransferDestination, Label: "destination")).Handle;
        using var encoder = device.BeginCommandEncoder("upload");
        Assert.True(encoder.CopyBuffer(source, 0, destination, 0, 16).Valid);

        var commandBuffer = encoder.Finish();
        Assert.True(commandBuffer.Finished);
        Assert.Single(commandBuffer.Commands);
        Assert.Throws<InvalidOperationException>(() => encoder.CopyBuffer(source, 0, destination, 0, 4));
        var submission = device.Submit(commandBuffer);

        Assert.True(submission.Valid);
        Assert.Equal(1, device.SubmissionCount);
        Assert.Equal(2, device.InspectResources().Count);
        Assert.Equal("upload", commandBuffer.Label);
    }

    [Fact]
    public void BufferWritesAreBoundedAndReportUploadedBytes()
    {
        using var device = new RekallAgeInMemoryRenderingDevice(
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("conformance"));
        var buffer = device.CreateBuffer(new(
            16,
            RekallAgeBufferUsage.Vertex | RekallAgeBufferUsage.TransferDestination,
            Label: "vertices"));
        Assert.True(buffer.Created);

        var write = device.WriteBuffer(buffer.Handle, 4, new byte[] { 1, 2, 3, 4 });

        Assert.True(write.Valid, string.Join(Environment.NewLine, write.Diagnostics.Select(item => item.Message)));
        Assert.Equal(4UL, Assert.Single(device.InspectResources()).UploadedBytes);
        var overflow = device.WriteBuffer(buffer.Handle, 15, new byte[] { 1, 2 });
        Assert.Contains(overflow.Diagnostics, item => item.Code == "REKALL_GPU_WRITE_RANGE_INVALID");
    }

    [Fact]
    public void TextureWritesRequireAnExactWritableSubresource()
    {
        using var device = new RekallAgeInMemoryRenderingDevice(
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("conformance"));
        var texture = device.CreateTexture(new(
            RekallAgeTextureDimension.Texture2D,
            2, 2, 1, 1, 1, 1,
            RekallAgeTextureFormat.Rgba8Unorm,
            RekallAgeTextureUsage.Sampled | RekallAgeTextureUsage.CopyDestination,
            "pixels"));
        Assert.True(texture.Created);

        var write = device.WriteTexture(texture.Handle, Enumerable.Range(0, 16).Select(value => (byte)value).ToArray());

        Assert.True(write.Valid, string.Join(Environment.NewLine, write.Diagnostics.Select(item => item.Message)));
        Assert.Equal(16UL, Assert.Single(device.InspectResources()).UploadedBytes);
        var wrongSize = device.WriteTexture(texture.Handle, new byte[15]);
        Assert.Contains(wrongSize.Diagnostics, item => item.Code == "REKALL_GPU_WRITE_RANGE_INVALID");
        var wrongSubresource = device.WriteTexture(texture.Handle, new byte[16], mipLevel: 1);
        Assert.Contains(wrongSubresource.Diagnostics, item => item.Code == "REKALL_GPU_WRITE_SUBRESOURCE_INVALID");
    }

    [Theory]
    [InlineData(RekallAgeTextureFormat.Depth32Float, 1, "REKALL_GPU_WRITE_FORMAT_UNSUPPORTED")]
    [InlineData(RekallAgeTextureFormat.Rgba8Unorm, 4, "REKALL_GPU_WRITE_SAMPLE_COUNT_UNSUPPORTED")]
    public void TextureWritesRejectLayoutsWithoutPortableRawUploadSemantics(
        RekallAgeTextureFormat format,
        int sampleCount,
        string expectedCode)
    {
        using var device = new RekallAgeInMemoryRenderingDevice(
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("conformance"));
        var texture = device.CreateTexture(new(
            RekallAgeTextureDimension.Texture2D, 2, 2, 1, 1, 1, sampleCount, format,
            (format == RekallAgeTextureFormat.Depth32Float ? RekallAgeTextureUsage.DepthStencilAttachment : RekallAgeTextureUsage.Sampled)
            | RekallAgeTextureUsage.CopyDestination));
        Assert.True(texture.Created);

        var write = device.WriteTexture(texture.Handle, new byte[16]);

        Assert.Contains(write.Diagnostics, item => item.Code == expectedCode);
    }

    [Fact]
    public void TextureCreationRejectsMipCountsBeyondTheFullDimensionChain()
    {
        using var device = new RekallAgeInMemoryRenderingDevice(
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("conformance"));

        var texture = device.CreateTexture(new(
            RekallAgeTextureDimension.Texture2D, 4, 4, 1, 4, 1, 1,
            RekallAgeTextureFormat.Rgba8Unorm, RekallAgeTextureUsage.Sampled));

        Assert.False(texture.Created);
        Assert.Contains(texture.Diagnostics, item => item.Code == "REKALL_GPU_TEXTURE_LAYOUT_INVALID");
    }

    [Fact]
    public void TextureInspectionCountsFormatMipsLayersAndSamples()
    {
        using var device = new RekallAgeInMemoryRenderingDevice(
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("conformance"));
        var texture = device.CreateTexture(new(
            RekallAgeTextureDimension.Texture2D, 4, 4, 1, 3, 2, 4,
            RekallAgeTextureFormat.R8Unorm, RekallAgeTextureUsage.Sampled));

        Assert.True(texture.Created);
        Assert.Equal(168UL, Assert.Single(device.InspectResources()).EstimatedBytes);
    }

    [Fact]
    public void TextureLayoutSizingFailsFastForMipCountsBeyondTheDimensionChain()
    {
        var descriptor = new RekallAgeTextureDescriptor(
            RekallAgeTextureDimension.Texture2D, 4, 4, 1, 34, 1, 1,
            RekallAgeTextureFormat.Rgba8Unorm, RekallAgeTextureUsage.Sampled);

        Assert.Equal(ulong.MaxValue, RekallAgeTextureLayout.TotalBytes(descriptor));
        Assert.Equal(0UL, RekallAgeTextureLayout.SubresourceBytes(descriptor, 32));
    }

    [Fact]
    public void ConformanceDeviceCreatesShaderLayoutsAndPipelinesWithStageChecks()
    {
        using var device = new RekallAgeInMemoryRenderingDevice(
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("conformance"));
        var vertex = device.CreateShaderModule(new(
            RekallAgeShaderStage.Vertex, RekallAgeShaderSourceLanguage.Glsl, "void main(){}", Label: "vertex"));
        var fragment = device.CreateShaderModule(new(
            RekallAgeShaderStage.Fragment, RekallAgeShaderSourceLanguage.Glsl, "void main(){}", Label: "fragment"));
        var layout = device.CreateBindingLayout(new(
            [new(0, RekallAgeBindingType.UniformBuffer, RekallAgeShaderStage.Vertex | RekallAgeShaderStage.Fragment)],
            "frame layout"));
        var sampler = device.CreateSampler(new(Label: "linear sampler"));
        Assert.True(vertex.Created);
        Assert.True(fragment.Created);
        Assert.True(layout.Created);
        Assert.True(sampler.Created);

        var pipeline = device.CreateGraphicsPipeline(new(
            vertex.Handle,
            fragment.Handle,
            [layout.Handle],
            [new(RekallAgeTextureFormat.Bgra8UnormSrgb)],
            Label: "scene"));
        Assert.True(pipeline.Created, string.Join(Environment.NewLine, pipeline.Diagnostics.Select(item => item.Message)));

        var wrongStage = device.CreateComputePipeline(new(vertex.Handle, [layout.Handle], "invalid compute"));
        Assert.False(wrongStage.Created);
        Assert.Contains(wrongStage.Diagnostics, item => item.Code == "REKALL_GPU_SHADER_STAGE_MISMATCH");
        Assert.Equal(5, device.InspectResources().Count);
    }

    [Fact]
    public void ConformanceDeviceCreatesValidatedBindingSetsAndRenderTargets()
    {
        using var device = new RekallAgeInMemoryRenderingDevice(
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("conformance"));
        var layout = device.CreateBindingLayout(new(
            [new(0, RekallAgeBindingType.UniformBuffer, RekallAgeShaderStage.Vertex, 16)], "frame"));
        var uniform = device.CreateBuffer(new(256, RekallAgeBufferUsage.Uniform, Label: "frame data"));
        var bindingSet = device.CreateBindingSet(new(
            layout.Handle,
            [new(0, uniform.Handle, 0, 256)],
            "frame set"));
        Assert.True(bindingSet.Created, string.Join(Environment.NewLine, bindingSet.Diagnostics.Select(item => item.Message)));

        var color = device.CreateTexture(new(
            RekallAgeTextureDimension.Texture2D, 640, 360, 1, 1, 1, 1,
            RekallAgeTextureFormat.Rgba8Unorm,
            RekallAgeTextureUsage.ColorAttachment | RekallAgeTextureUsage.Sampled,
            "color"));
        var depth = device.CreateTexture(new(
            RekallAgeTextureDimension.Texture2D, 640, 360, 1, 1, 1, 1,
            RekallAgeTextureFormat.Depth24Stencil8,
            RekallAgeTextureUsage.DepthStencilAttachment,
            "depth"));
        var target = device.CreateRenderTarget(new(
            [new(color.Handle)],
            new(depth.Handle),
            640,
            360,
            "viewport"));
        Assert.True(target.Created, string.Join(Environment.NewLine, target.Diagnostics.Select(item => item.Message)));

        var missing = device.CreateBindingSet(new(layout.Handle, [], "missing"));
        Assert.Contains(missing.Diagnostics, item => item.Code == "REKALL_GPU_BINDING_SET_INCOMPLETE");
    }

    [Fact]
    public void RecordsInspectableRenderPassAndIndexedDrawCommands()
    {
        using var device = new RekallAgeInMemoryRenderingDevice(
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("conformance"));
        var vertexShader = device.CreateShaderModule(new(
            RekallAgeShaderStage.Vertex, RekallAgeShaderSourceLanguage.Glsl, "void main(){}"));
        var fragmentShader = device.CreateShaderModule(new(
            RekallAgeShaderStage.Fragment, RekallAgeShaderSourceLanguage.Glsl, "void main(){}"));
        var pipeline = device.CreateGraphicsPipeline(new(
            vertexShader.Handle,
            fragmentShader.Handle,
            [],
            [new(RekallAgeTextureFormat.Rgba8Unorm)],
            Label: "scene"));
        var vertices = device.CreateBuffer(new(256, RekallAgeBufferUsage.Vertex, Label: "vertices"));
        var indices = device.CreateBuffer(new(72, RekallAgeBufferUsage.Index, Label: "indices"));
        var color = device.CreateTexture(new(
            RekallAgeTextureDimension.Texture2D, 640, 360, 1, 1, 1, 1,
            RekallAgeTextureFormat.Rgba8Unorm,
            RekallAgeTextureUsage.ColorAttachment,
            "color"));
        var target = device.CreateRenderTarget(new(
            [new(color.Handle)], null, 640, 360, "viewport"));

        using var encoder = device.BeginCommandEncoder("frame");
        Assert.True(encoder.BeginRenderPass(new(target.Handle,
            [new(0.02f, 0.03f, 0.04f, 1f)], Label: "main pass")).Valid);
        Assert.True(encoder.SetRenderPipeline(pipeline.Handle).Valid);
        Assert.True(encoder.SetVertexBuffer(0, vertices.Handle, 0, 256).Valid);
        Assert.True(encoder.SetIndexBuffer(indices.Handle, RekallAgeIndexFormat.UInt16, 0, 72).Valid);
        Assert.True(encoder.DrawIndexed(36, 1, 0, 0, 0).Valid);
        Assert.True(encoder.EndRenderPass().Valid);
        var commands = encoder.Finish();

        Assert.Collection(commands.Commands,
            item => Assert.IsType<RekallAgeBeginRenderPassCommand>(item),
            item => Assert.IsType<RekallAgeSetRenderPipelineCommand>(item),
            item => Assert.IsType<RekallAgeSetVertexBufferCommand>(item),
            item => Assert.IsType<RekallAgeSetIndexBufferCommand>(item),
            item => Assert.IsType<RekallAgeDrawIndexedCommand>(item),
            item => Assert.IsType<RekallAgeEndRenderPassCommand>(item));
        Assert.True(device.Submit(commands).Valid);
    }

    [Fact]
    public void RecordsComputeDispatchAndRejectsInvalidPassState()
    {
        using var device = new RekallAgeInMemoryRenderingDevice(
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("conformance"));
        var shader = device.CreateShaderModule(new(
            RekallAgeShaderStage.Compute, RekallAgeShaderSourceLanguage.Glsl, "void main(){}"));
        var pipeline = device.CreateComputePipeline(new(shader.Handle, [], "simulation"));
        using var encoder = device.BeginCommandEncoder("compute frame");

        var outside = encoder.Draw(3);
        Assert.Contains(outside.Diagnostics, item => item.Code == "REKALL_GPU_PASS_STATE_INVALID");
        Assert.True(encoder.BeginComputePass("simulation pass").Valid);
        Assert.Contains(encoder.BeginComputePass().Diagnostics,
            item => item.Code == "REKALL_GPU_PASS_STATE_INVALID");
        Assert.True(encoder.SetComputePipeline(pipeline.Handle).Valid);
        Assert.Contains(encoder.Dispatch(0, 1, 1).Diagnostics,
            item => item.Code == "REKALL_GPU_DISPATCH_RANGE_INVALID");
        Assert.Contains(encoder.Dispatch(65_536, 1, 1).Diagnostics,
            item => item.Code == "REKALL_GPU_DISPATCH_RANGE_INVALID");
        Assert.Throws<InvalidOperationException>(() => encoder.Finish());
        Assert.True(encoder.Dispatch(8, 4, 1).Valid);
        Assert.True(encoder.EndComputePass().Valid);

        var commands = encoder.Finish();
        Assert.Collection(commands.Commands,
            item => Assert.IsType<RekallAgeBeginComputePassCommand>(item),
            item => Assert.IsType<RekallAgeSetComputePipelineCommand>(item),
            item => Assert.IsType<RekallAgeDispatchCommand>(item),
            item => Assert.IsType<RekallAgeEndComputePassCommand>(item));
        Assert.True(device.Submit(commands).Valid);
    }

    [Fact]
    public void SubmissionRevalidatesResourcesReferencedByRecordedCommands()
    {
        using var device = new RekallAgeInMemoryRenderingDevice(
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("conformance"));
        var shader = device.CreateShaderModule(new(
            RekallAgeShaderStage.Compute, RekallAgeShaderSourceLanguage.Glsl, "void main(){}"));
        var pipeline = device.CreateComputePipeline(new(shader.Handle, [], "simulation"));
        using var encoder = device.BeginCommandEncoder();
        Assert.True(encoder.BeginComputePass().Valid);
        Assert.True(encoder.SetComputePipeline(pipeline.Handle).Valid);
        Assert.True(encoder.Dispatch(1, 1, 1).Valid);
        Assert.True(encoder.EndComputePass().Valid);
        var commands = encoder.Finish();

        Assert.True(device.Destroy(pipeline.Handle).Valid);
        var submission = device.Submit(commands);

        Assert.Contains(submission.Diagnostics, item => item.Code == "REKALL_GPU_HANDLE_STALE");
        Assert.Equal(0, device.SubmissionCount);
    }
}
