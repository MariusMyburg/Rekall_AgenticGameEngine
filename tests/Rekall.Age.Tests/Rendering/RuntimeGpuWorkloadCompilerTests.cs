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
    public void CompilesStructuredStorageAndIndirectDispatchWithoutNativeHandles()
    {
        using var device = new RekallAgeInMemoryRenderingDevice(
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("conformance"));
        var workload = ComputeWorkload() with
        {
            Buffers =
            [
                new("particles", 4_096, RekallAgeRuntimeGpuBufferUsage.Storage) { StructureByteStride = 16 },
                new("dispatch-args", 12, RekallAgeRuntimeGpuBufferUsage.Indirect)
            ],
            Commands =
            [
                new(RekallAgeRuntimeGpuCommandKind.BeginComputePass),
                new(RekallAgeRuntimeGpuCommandKind.SetComputePipeline) { Resource = "simulation" },
                new(RekallAgeRuntimeGpuCommandKind.DispatchIndirect) { Resource = "dispatch-args" },
                new(RekallAgeRuntimeGpuCommandKind.EndComputePass)
            ]
        };

        using var compiled = new RekallAgeRuntimeGpuWorkloadCompiler().Compile(workload, device);

        Assert.True(compiled.Valid, string.Join(Environment.NewLine, compiled.Diagnostics.Select(item => item.Message)));
        var storage = Assert.IsType<RekallAgeBufferDescriptor>(device.InspectResources().Single(item => item.Label == "particles").Descriptor);
        Assert.Equal(16U, storage.StructureByteStride);
        Assert.Contains(compiled.CommandBuffer!.Commands, command => command is RekallAgeDispatchIndirectCommand);
        Assert.True(device.Submit(compiled.CommandBuffer).Valid);
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
    public void InitialAssetUploadRequiresAnExplicitResolverBeforeAllocating()
    {
        using var device = new RekallAgeInMemoryRenderingDevice(
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("conformance"));
        var workload = ComputeWorkload() with
        {
            Buffers = [new("particles", 4_096, RekallAgeRuntimeGpuBufferUsage.Storage) { InitialDataAsset = "particles.bin" }]
        };

        using var compiled = new RekallAgeRuntimeGpuWorkloadCompiler().Compile(workload, device);

        Assert.Contains(compiled.Diagnostics, item => item.Code == "REKALL_GPU_ASSET_RESOLVER_REQUIRED");
        Assert.Empty(device.InspectResources());
    }

    [Fact]
    public void UploadsResolvedInitialBufferAndTextureData()
    {
        using var device = new RekallAgeInMemoryRenderingDevice(
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("conformance"));
        var workload = new RekallAgeRuntimeGpuWorkload("asset-data")
        {
            Buffers =
            [
                new("vertices", 16, RekallAgeRuntimeGpuBufferUsage.Vertex)
                {
                    InitialDataAsset = "asset:vertices"
                }
            ],
            Textures =
            [
                new("pixels", RekallAgeRuntimeGpuTextureDimension.Texture2D, 2, 2, 1,
                    "rgba8-unorm", RekallAgeRuntimeGpuTextureUsage.Sampled)
                {
                    InitialDataAsset = "asset:pixels"
                }
            ]
        };
        var resolver = new DictionaryAssetDataResolver(new Dictionary<string, byte[]>
        {
            ["asset:vertices"] = [1, 2, 3, 4],
            ["asset:pixels"] = Enumerable.Range(0, 16).Select(value => (byte)value).ToArray()
        });

        using var compiled = new RekallAgeRuntimeGpuWorkloadCompiler().Compile(
            workload, device, assetDataResolver: resolver);

        Assert.True(compiled.Valid, string.Join(Environment.NewLine, compiled.Diagnostics.Select(item => item.Message)));
        var resources = device.InspectResources().ToDictionary(resource => resource.Label!);
        Assert.Equal(4UL, resources["vertices"].UploadedBytes);
        Assert.Equal(16UL, resources["pixels"].UploadedBytes);
        var buffer = Assert.IsType<RekallAgeBufferDescriptor>(resources["vertices"].Descriptor);
        var texture = Assert.IsType<RekallAgeTextureDescriptor>(resources["pixels"].Descriptor);
        Assert.True(buffer.Usage.HasFlag(RekallAgeBufferUsage.TransferDestination));
        Assert.True(texture.Usage.HasFlag(RekallAgeTextureUsage.CopyDestination));
    }

    [Fact]
    public void RejectsInitialDataLargerThanItsDeclaredBufferWithoutRetainingAllocations()
    {
        using var device = new RekallAgeInMemoryRenderingDevice(
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("conformance"));
        var workload = new RekallAgeRuntimeGpuWorkload("oversized-upload")
        {
            Buffers =
            [
                new("vertices", 4, RekallAgeRuntimeGpuBufferUsage.Vertex)
                {
                    InitialDataAsset = "asset:vertices"
                }
            ]
        };
        var resolver = new DictionaryAssetDataResolver(new Dictionary<string, byte[]>
        {
            ["asset:vertices"] = [1, 2, 3, 4, 5]
        });

        using var compiled = new RekallAgeRuntimeGpuWorkloadCompiler().Compile(
            workload, device, assetDataResolver: resolver);

        Assert.Contains(compiled.Diagnostics, item => item.Code == "REKALL_GPU_INITIAL_DATA_TOO_LARGE");
        Assert.Empty(device.InspectResources());
    }

    [Theory]
    [InlineData("depth32-float", 1, 1, "REKALL_GPU_INITIAL_DATA_FORMAT_UNSUPPORTED")]
    [InlineData("rgba8-unorm", 4, 1, "REKALL_GPU_INITIAL_DATA_SAMPLE_COUNT_UNSUPPORTED")]
    [InlineData("rgba8-unorm", 1, 2, "REKALL_GPU_INITIAL_DATA_ARRAY_LAYERS_UNSUPPORTED")]
    public void RejectsInitialTextureLayoutsWithoutPortableRawPayloadSemantics(
        string format,
        int sampleCount,
        int arrayLayers,
        string expectedCode)
    {
        using var device = new RekallAgeInMemoryRenderingDevice(
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("conformance"));
        var workload = new RekallAgeRuntimeGpuWorkload("texture-upload")
        {
            Textures = [new("pixels", RekallAgeRuntimeGpuTextureDimension.Texture2D, 2, 2, 1, format, RekallAgeRuntimeGpuTextureUsage.Sampled)
            {
                SampleCount = sampleCount,
                ArrayLayers = arrayLayers,
                InitialDataAsset = "asset:pixels"
            }]
        };
        var resolver = new DictionaryAssetDataResolver(new Dictionary<string, byte[]> { ["asset:pixels"] = new byte[16] });

        using var compiled = new RekallAgeRuntimeGpuWorkloadCompiler().Compile(workload, device, assetDataResolver: resolver);

        Assert.Contains(compiled.Diagnostics, item => item.Code == expectedCode);
        Assert.Empty(device.InspectResources());
    }

    [Fact]
    public void TextureBudgetCountsAllMipsLayersSamplesAndActualFormatSize()
    {
        using var device = new RekallAgeInMemoryRenderingDevice(
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("conformance"));
        var workload = new RekallAgeRuntimeGpuWorkload("texture-budget")
        {
            Textures = [new("large", RekallAgeRuntimeGpuTextureDimension.Texture2D, 8192, 8192, 1,
                "r8-unorm", RekallAgeRuntimeGpuTextureUsage.Sampled)
            {
                MipLevels = 14,
                ArrayLayers = 2,
                SampleCount = 4
            }]
        };

        using var compiled = new RekallAgeRuntimeGpuWorkloadCompiler().Compile(workload, device);

        Assert.Contains(compiled.Diagnostics, item => item.Code == "REKALL_GPU_WORKLOAD_MEMORY_LIMIT");
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
        var pipeline = Assert.IsType<RekallAgeGraphicsPipelineDescriptor>(device.InspectResources()
            .Single(resource => resource.Handle == compiled.Resources["scene-pipeline"]).Descriptor);
        var vertexLayout = Assert.Single(pipeline.VertexBuffers);
        Assert.Equal(32, vertexLayout.StrideBytes);
        Assert.Equal(["Position", "Normal", "UV"], vertexLayout.Attributes.Select(attribute => attribute.Name));
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

    [Fact]
    public void ResolvesExplicitExternalResourcesWithoutTakingOwnership()
    {
        using var device = new RekallAgeInMemoryRenderingDevice(
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("conformance"));
        var color = device.CreateTexture(new(
            RekallAgeTextureDimension.Texture2D, 640, 360, 1, 1, 1, 1,
            RekallAgeTextureFormat.Rgba8Unorm,
            RekallAgeTextureUsage.Sampled | RekallAgeTextureUsage.ColorAttachment,
            "engine.scene-color")).Handle;
        var outputTexture = device.CreateTexture(new(
            RekallAgeTextureDimension.Texture2D, 640, 360, 1, 1, 1, 1,
            RekallAgeTextureFormat.Rgba8Unorm,
            RekallAgeTextureUsage.ColorAttachment | RekallAgeTextureUsage.Present,
            "engine.output-color")).Handle;
        var output = device.CreateRenderTarget(new(
            [new(outputTexture)], null, 640, 360, "engine.output")).Handle;
        var workload = RenderWorkload() with
        {
            Textures = [],
            RenderTargets = [],
            Commands = RenderWorkload().Commands
                .Select(command => command.Kind == RekallAgeRuntimeGpuCommandKind.BeginRenderPass
                    ? command with { Resource = "engine.output", ClearDepth = null }
                    : command)
                .ToArray()
        };

        using (var compiled = new RekallAgeRuntimeGpuWorkloadCompiler().Compile(
            workload,
            device,
            new Dictionary<string, RekallAgeGraphicsResourceHandle>
            {
                ["engine.scene-color"] = color,
                ["engine.output"] = output
            }))
        {
            Assert.True(compiled.Valid, string.Join(Environment.NewLine, compiled.Diagnostics.Select(item => item.Message)));
            Assert.Equal(color, compiled.Resources["engine.scene-color"]);
            Assert.Equal(output, compiled.Resources["engine.output"]);
        }

        Assert.Contains(device.InspectResources(), resource => resource.Handle == color);
        Assert.Contains(device.InspectResources(), resource => resource.Handle == output);
    }

    [Fact]
    public void RejectsExternalResourceIdCollisionBeforeAllocation()
    {
        using var device = new RekallAgeInMemoryRenderingDevice(
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("conformance"));
        var imported = device.CreateBuffer(new(64, RekallAgeBufferUsage.Storage, Label: "engine.buffer") { StructureByteStride = 16 }).Handle;

        using var compiled = new RekallAgeRuntimeGpuWorkloadCompiler().Compile(
            ComputeWorkload(),
            device,
            new Dictionary<string, RekallAgeGraphicsResourceHandle> { ["particles"] = imported });

        Assert.False(compiled.Valid);
        Assert.Contains(compiled.Diagnostics, diagnostic => diagnostic.Code == "REKALL_GPU_WORKLOAD_IMPORT_COLLISION");
        Assert.Single(device.InspectResources());
    }

    private static RekallAgeRuntimeGpuWorkload ComputeWorkload() => new("particles")
    {
        Buffers = [new("particles", 4_096, RekallAgeRuntimeGpuBufferUsage.Storage) { StructureByteStride = 16 }],
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
                DepthStencilFormat = "depth24-stencil8",
                VertexBuffers =
                [
                    new(32, RekallAgeRuntimeGpuVertexStepMode.Vertex,
                    [
                        new("Position", 0, RekallAgeRuntimeGpuVertexFormat.Float32x3, 0),
                        new("Normal", 1, RekallAgeRuntimeGpuVertexFormat.Float32x3, 12),
                        new("UV", 2, RekallAgeRuntimeGpuVertexFormat.Float32x2, 24)
                    ])
                ]
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

    private sealed class DictionaryAssetDataResolver(IReadOnlyDictionary<string, byte[]> assets)
        : IRekallAgeGpuAssetDataResolver
    {
        public RekallAgeGpuAssetDataResolution Resolve(string assetId) =>
            assets.TryGetValue(assetId, out var data)
                ? new(data, [])
                : new(null, [new("REKALL_GPU_ASSET_NOT_FOUND", $"Asset '{assetId}' was not found.", assetId)]);
    }
}
