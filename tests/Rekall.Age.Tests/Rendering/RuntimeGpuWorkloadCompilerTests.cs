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
    public void FailsClosedForDeclarationsReservedForTheNextCompilerStage()
    {
        using var device = new RekallAgeInMemoryRenderingDevice(
            RekallAgeRenderingDeviceCapabilities.DesktopBaseline("conformance"));
        var workload = ComputeWorkload() with
        {
            Textures = [new("color", RekallAgeRuntimeGpuTextureDimension.Texture2D, 64, 64, 1,
                "rgba8-unorm", RekallAgeRuntimeGpuTextureUsage.Storage)]
        };

        using var compiled = new RekallAgeRuntimeGpuWorkloadCompiler().Compile(workload, device);

        Assert.Contains(compiled.Diagnostics, item => item.Code == "REKALL_GPU_WORKLOAD_NOT_IMPLEMENTED");
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
}
