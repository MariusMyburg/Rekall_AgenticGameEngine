using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Rendering.Commands;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Tests.Rendering;

public sealed class RuntimeGpuWorkloadInspectionCommandTests
{
    [Fact]
    public async Task InspectionReturnsNamedOpaqueResourcesAndCommandKinds()
    {
        var workload = new RekallAgeRuntimeGpuWorkload("simulation")
        {
            Shaders = [new("shader", RekallAgeRuntimeGpuShaderStage.Compute,
                RekallAgeRuntimeGpuShaderLanguage.Glsl, "void main(){}")],
            Pipelines = [new("pipeline", RekallAgeRuntimeGpuPipelineKind.Compute) { ComputeShader = "shader" }],
            Commands =
            [
                new(RekallAgeRuntimeGpuCommandKind.BeginComputePass),
                new(RekallAgeRuntimeGpuCommandKind.SetComputePipeline) { Resource = "pipeline" },
                new(RekallAgeRuntimeGpuCommandKind.Dispatch) { GroupCountX = 4 },
                new(RekallAgeRuntimeGpuCommandKind.EndComputePass)
            ]
        };
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("inspect GPU"), CancellationToken.None);

        var result = await new InspectRuntimeGpuWorkloadCommand().ExecuteAsync(new(workload, "webgpu"), context);

        Assert.True(result.Ok);
        Assert.True(result.Value.Valid);
        Assert.Equal("webgpu", result.Value.Backend);
        Assert.Contains(result.Value.Resources, item => item.Id == "pipeline" && item.Kind == "ComputePipeline");
        Assert.Equal(["BeginComputePass", "SetComputePipeline", "Dispatch", "EndComputePass"],
            result.Value.Commands.Select(item => item.Kind));
    }
}
