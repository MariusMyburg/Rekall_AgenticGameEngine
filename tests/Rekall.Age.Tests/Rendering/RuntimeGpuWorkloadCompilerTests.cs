using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Tests.Rendering;

public sealed class RuntimeGpuWorkloadCompilerTests
{
    [Fact]
    public void CompilesNamedComputeGraphToOpaqueResourcesAndImmutableCommands()
    {
        using var device = new RekallAgeInMemoryRenderingDevice(
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("conformance"));
        var compiler = new RekallAgeRuntimeGpuWorkloadCompiler();

        using var compiled = compiler.Compile(ComputeWorkload(), device);

        Assert.True(compiled.Valid, string.Join(Environment.NewLine, compiled.Diagnostics.Select(item => item.Message)));
        Assert.Equal(RekallAgeGraphicsResourceKind.Buffer, compiled.Resources["particles"].Kind);
        Assert.Equal(RekallAgeGraphicsResourceKind.ShaderModule, compiled.Resources["simulate"].Kind);
        Assert.Equal(RekallAgeGraphicsResourceKind.ComputePipeline, compiled.Resources["simulation"].Kind);
        Assert.Collection(compiled.CommandBuffer!.Commands,
            item => Assert.IsType<RekallAgeBeginComputePassCommand>(item),
            item => Assert.IsType<RekallAgeSetComputePipelineCommand>(item),
            item => Assert.IsType<RekallAgeDispatchCommand>(item),
            item => Assert.IsType<RekallAgeEndComputePassCommand>(item));
        Assert.True(device.Submit(compiled.CommandBuffer).Valid);
        Assert.Equal(1, device.SubmissionCount);
    }

    [Fact]
    public void RejectsDuplicateAndMissingReferencesBeforeAllocatingAnything()
    {
        using var device = new RekallAgeInMemoryRenderingDevice(
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("conformance"));
        var workload = ComputeWorkload() with
        {
            Buffers =
            [
                new("duplicate", 64, RekallAgeRuntimeGpuBufferUsage.Storage),
                new("duplicate", 64, RekallAgeRuntimeGpuBufferUsage.Storage)
            ],
            Pipelines = [new("simulation", RekallAgeRuntimeGpuPipelineKind.Compute) { ComputeShader = "missing" }]
        };

        using var compiled = new RekallAgeRuntimeGpuWorkloadCompiler().Compile(workload, device);

        Assert.False(compiled.Valid);
        Assert.Contains(compiled.Diagnostics, item => item.Code == "REKALL_GPU_WORKLOAD_ID_DUPLICATE");
        Assert.Contains(compiled.Diagnostics, item => item.Code == "REKALL_GPU_WORKLOAD_REFERENCE_MISSING");
        Assert.Empty(device.InspectResources());
        Assert.Equal(0, device.SubmissionCount);
    }

    [Fact]
    public void RejectsWorkloadBudgetsBeforeAllocatingAnything()
    {
        using var device = new RekallAgeInMemoryRenderingDevice(
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("conformance"));
        var workload = ComputeWorkload() with
        {
            Commands = Enumerable.Range(0, RekallAgeRuntimeGpuWorkloadCompiler.MaximumCommands + 1)
                .Select(_ => new RekallAgeRuntimeGpuCommand(RekallAgeRuntimeGpuCommandKind.BeginComputePass))
                .ToArray()
        };

        using var compiled = new RekallAgeRuntimeGpuWorkloadCompiler().Compile(workload, device);

        Assert.Contains(compiled.Diagnostics, item => item.Code == "REKALL_GPU_WORKLOAD_COMMAND_LIMIT");
        Assert.Empty(device.InspectResources());
    }

    [Fact]
    public void FailsClosedForInitialAssetUploadReservedForTheNextCompilerStage()
    {
        using var device = new RekallAgeInMemoryRenderingDevice(
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("conformance"));
        var workload = ComputeWorkload() with
        {
            Buffers = [new("particles", 4_096, RekallAgeRuntimeGpuBufferUsage.Storage) { InitialDataAsset = "particles.bin" }]
        };

        using var compiled = new RekallAgeRuntimeGpuWorkloadCompiler().Compile(workload, device);

        Assert.Contains(compiled.Diagnostics, item => item.Code == "REKALL_GPU_WORKLOAD_NOT_IMPLEMENTED");
        Assert.Empty(device.InspectResources());
    }

    [Fact]
    public void CompilesBoundRenderGraphBindingsTargetAndIndexedDraw()
    {
        using var device = new RekallAgeInMemoryRenderingDevice(
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("conformance"));
        var workload = RenderWorkload();

        using var compiled = new RekallAgeRuntimeGpuWorkloadCompiler().Compile(workload, device);

        Assert.True(compiled.Valid, string.Join(Environment.NewLine, compiled.Diagnostics.Select(item => item.Message)));
        Assert.Equal(RekallAgeGraphicsResourceKind.Texture, compiled.Resources["color"].Kind);
        Assert.Equal(RekallAgeGraphicsResourceKind.Sampler, compiled.Resources["linear"].Kind);
        Assert.Equal(RekallAgeGraphicsResourceKind.BindingSet, compiled.Resources["frame-set"].Kind);
        Assert.Equal(RekallAgeGraphicsResourceKind.RenderPipeline, compiled.Resources["scene-pipeline"].Kind);
        Assert.Equal(RekallAgeGraphicsResourceKind.RenderTarget, compiled.Resources["viewport"].Kind);
        Assert.Collection(compiled.CommandBuffer!.Commands,
            item => Assert.IsType<RekallAgeBeginRenderPassCommand>(item),
            item => Assert.IsType<RekallAgeSetRenderPipelineCommand>(item),
            item => Assert.IsType<RekallAgeSetBindingSetCommand>(item),
            item => Assert.IsType<RekallAgeSetVertexBufferCommand>(item),
            item => Assert.IsType<RekallAgeSetIndexBufferCommand>(item),
            item => Assert.IsType<RekallAgeDrawIndexedCommand>(item),
            item => Assert.IsType<RekallAgeEndRenderPassCommand>(item));
        Assert.True(device.Submit(compiled.CommandBuffer).Valid);
    }

    [Fact]
    public void CompilesTransferCommandsAlongsideRenderOrComputeIndependentWork()
    {
        using var device = new RekallAgeInMemoryRenderingDevice(
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("conformance"));
        var workload = new RekallAgeRuntimeGpuWorkload("upload")
        {
            Buffers =
            [
                new("staging", 256, RekallAgeRuntimeGpuBufferUsage.CopySource) { MemoryAccess = "upload" },
                new("vertices", 256, RekallAgeRuntimeGpuBufferUsage.CopyDestination | RekallAgeRuntimeGpuBufferUsage.Vertex)
            ],
            Commands =
            [
                new(RekallAgeRuntimeGpuCommandKind.CopyBuffer)
                {
                    Source = "staging", Destination = "vertices", SizeBytes = 256
                }
            ]
        };

        using var compiled = new RekallAgeRuntimeGpuWorkloadCompiler().Compile(workload, device);

        Assert.True(compiled.Valid, string.Join(Environment.NewLine, compiled.Diagnostics.Select(item => item.Message)));
        Assert.IsType<RekallAgeCopyBufferCommand>(Assert.Single(compiled.CommandBuffer!.Commands));
        Assert.True(device.Submit(compiled.CommandBuffer).Valid);
    }

    [Fact]
    public void MalformedDeserializedGraphReturnsDiagnosticsWithoutAllocatingOrThrowing()
    {
        using var device = new RekallAgeInMemoryRenderingDevice(
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("conformance"));
        var workload = RenderWorkload() with
        {
            Textures = [new("bad", RekallAgeRuntimeGpuTextureDimension.Texture2D, 64, 64, 1,
                "unknown-format", RekallAgeRuntimeGpuTextureUsage.Sampled)],
            BindingSets = [new("broken", "frame-layout", null!)],
            Commands = [new(RekallAgeRuntimeGpuCommandKind.BeginRenderPass) { Resource = null }]
        };

        using var compiled = new RekallAgeRuntimeGpuWorkloadCompiler().Compile(workload, device);

        Assert.False(compiled.Valid);
        Assert.Contains(compiled.Diagnostics, item => item.Code == "REKALL_GPU_WORKLOAD_SHAPE_INVALID");
        Assert.Contains(compiled.Diagnostics, item => item.Code == "REKALL_GPU_WORKLOAD_FORMAT_UNSUPPORTED");
        Assert.Contains(compiled.Diagnostics, item => item.Code == "REKALL_GPU_WORKLOAD_COMMAND_OPERAND_REQUIRED");
        Assert.Empty(device.InspectResources());
    }

    [Fact]
    public void AggregateAllocationBudgetIsCheckedBeforeAllocation()
    {
        using var device = new RekallAgeInMemoryRenderingDevice(
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("conformance"));
        var workload = ComputeWorkload() with
        {
            Buffers =
            [
                new("a", RekallAgeRuntimeGpuWorkloadCompiler.MaximumEstimatedBytes, RekallAgeRuntimeGpuBufferUsage.Storage),
                new("b", 1, RekallAgeRuntimeGpuBufferUsage.Storage)
            ]
        };

        using var compiled = new RekallAgeRuntimeGpuWorkloadCompiler().Compile(workload, device);

        Assert.Contains(compiled.Diagnostics, item => item.Code == "REKALL_GPU_WORKLOAD_MEMORY_LIMIT");
        Assert.Empty(device.InspectResources());
    }

    private static RekallAgeRuntimeGpuWorkload ComputeWorkload() => new("particles")
    {
        Buffers = [new("particles", 4_096, RekallAgeRuntimeGpuBufferUsage.Storage)],
        Shaders =
        [
            new("simulate", RekallAgeRuntimeGpuShaderStage.Compute, RekallAgeRuntimeGpuShaderLanguage.Glsl,
                "#version 450\nvoid main(){}")
        ],
        Pipelines = [new("simulation", RekallAgeRuntimeGpuPipelineKind.Compute) { ComputeShader = "simulate" }],
        Commands =
        [
            new(RekallAgeRuntimeGpuCommandKind.BeginComputePass) { Label = "particles" },
            new(RekallAgeRuntimeGpuCommandKind.SetComputePipeline) { Resource = "simulation" },
            new(RekallAgeRuntimeGpuCommandKind.Dispatch) { GroupCountX = 8 },
            new(RekallAgeRuntimeGpuCommandKind.EndComputePass)
        ]
    };

    private static RekallAgeRuntimeGpuWorkload RenderWorkload() => new("scene")
    {
        Buffers =
        [
            new("vertices", 1_024, RekallAgeRuntimeGpuBufferUsage.Vertex),
            new("indices", 256, RekallAgeRuntimeGpuBufferUsage.Index),
            new("frame", 256, RekallAgeRuntimeGpuBufferUsage.Uniform) { MemoryAccess = "upload" }
        ],
        Textures =
        [
            new("color", RekallAgeRuntimeGpuTextureDimension.Texture2D, 640, 360, 1,
                "rgba8-unorm", RekallAgeRuntimeGpuTextureUsage.ColorAttachment),
            new("depth", RekallAgeRuntimeGpuTextureDimension.Texture2D, 640, 360, 1,
                "depth24-stencil8", RekallAgeRuntimeGpuTextureUsage.DepthStencilAttachment)
        ],
        Samplers = [new("linear")],
        Shaders =
        [
            new("vertex", RekallAgeRuntimeGpuShaderStage.Vertex, RekallAgeRuntimeGpuShaderLanguage.Glsl, "void main(){}"),
            new("fragment", RekallAgeRuntimeGpuShaderStage.Fragment, RekallAgeRuntimeGpuShaderLanguage.Glsl, "void main(){}")
        ],
        BindingLayouts =
        [
            new("frame-layout",
            [
                new(0, RekallAgeRuntimeGpuBindingType.UniformBuffer,
                    [RekallAgeRuntimeGpuShaderStage.Vertex, RekallAgeRuntimeGpuShaderStage.Fragment]) { MinimumBindingSize = 16 }
            ])
        ],
        BindingSets = [new("frame-set", "frame-layout", [new(0, "frame") { SizeBytes = 256 }])],
        Pipelines =
        [
            new("scene-pipeline", RekallAgeRuntimeGpuPipelineKind.Render)
            {
                VertexShader = "vertex", FragmentShader = "fragment",
                BindingLayouts = ["frame-layout"], ColorFormats = ["rgba8-unorm"],
                DepthStencilFormat = "depth24-stencil8"
            }
        ],
        RenderTargets = [new("viewport", ["color"], 640, 360) { DepthStencilAttachment = "depth" }],
        Commands =
        [
            new(RekallAgeRuntimeGpuCommandKind.BeginRenderPass)
            {
                Resource = "viewport", ClearColors = [new(0.02f, 0.03f, 0.04f, 1)], ClearDepth = 1
            },
            new(RekallAgeRuntimeGpuCommandKind.SetRenderPipeline) { Resource = "scene-pipeline" },
            new(RekallAgeRuntimeGpuCommandKind.SetBindingSet) { BindingSetIndex = 0, Resource = "frame-set" },
            new(RekallAgeRuntimeGpuCommandKind.SetVertexBuffer) { Slot = 0, Resource = "vertices", SizeBytes = 1_024 },
            new(RekallAgeRuntimeGpuCommandKind.SetIndexBuffer) { Resource = "indices", SizeBytes = 256 },
            new(RekallAgeRuntimeGpuCommandKind.DrawIndexed) { IndexCount = 36 },
            new(RekallAgeRuntimeGpuCommandKind.EndRenderPass)
        ]
    };
}
