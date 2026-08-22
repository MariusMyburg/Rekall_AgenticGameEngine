using System.Text.Json;
using Rekall.Age.Modules;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Tests.Runtime;

public sealed class RuntimeGpuWorkloadSdkTests
{
    [Fact]
    public void RuntimeModuleSdkAddsReplacesListsAndRemovesGpuWorkloadsDeterministically()
    {
        var world = World();
        var rain = ComputeWorkload("rain", 8);
        var bloom = ComputeWorkload("bloom", 4);

        var authored = world
            .WithGpuWorkload(rain)
            .WithGpuWorkload(bloom)
            .WithGpuWorkload(ComputeWorkload("rain", 16));

        Assert.Equal(["bloom", "rain"], authored.GpuWorkloads().Select(item => item.Id));
        Assert.Equal(16U, authored.GpuWorkloads().Single(item => item.Id == "rain").Commands[2].GroupCountX);
        var removed = authored.WithoutGpuWorkload("bloom");
        Assert.Equal("rain", Assert.Single(removed.GpuWorkloads()).Id);
        Assert.Same(removed, removed.WithoutGpuWorkload("missing"));
    }

    [Fact]
    public void GpuWorkloadsRoundTripThroughRuntimeWorldJson()
    {
        var world = World().WithGpuWorkload(ComputeWorkload("rain", 8));

        var json = JsonSerializer.Serialize(world);
        var restored = JsonSerializer.Deserialize<RekallAgeRuntimeWorld>(json)!;
        var workload = Assert.Single(restored.Subsystems.Rendering.GpuWorkloads);

        Assert.Equal("rain", workload.Id);
        Assert.Equal(RekallAgeRuntimeGpuBufferUsage.Storage, Assert.Single(workload.Buffers).Usage);
        Assert.Equal(RekallAgeRuntimeGpuShaderStage.Compute, Assert.Single(workload.Shaders).Stage);
        Assert.Equal(RekallAgeRuntimeGpuCommandKind.Dispatch, workload.Commands[2].Kind);
    }

    [Fact]
    public void RuntimeModuleSdkBoundsWorkloadCountAndRequiresStableIds()
    {
        var world = World();
        Assert.Throws<ArgumentException>(() => world.WithGpuWorkload(ComputeWorkload(" ", 1)));
        for (var index = 0; index < RekallAgeRuntimeModuleSdk.MaximumGpuWorkloads; index++)
        {
            world = world.WithGpuWorkload(ComputeWorkload($"workload-{index:D2}", 1));
        }

        Assert.Throws<InvalidOperationException>(() =>
            world.WithGpuWorkload(ComputeWorkload("one-too-many", 1)));
    }

    [Fact]
    public void RuntimeProjectionPreservesAgentAuthoredGpuWorkloads()
    {
        var authored = World().WithGpuWorkload(ComputeWorkload("rain", 8));

        var projected = new RekallAgeRuntimeProjectionBuilder().Project(authored);

        Assert.Equal("rain", Assert.Single(projected.Subsystems.Rendering.GpuWorkloads).Id);
    }

    [Fact]
    public void ExistingGpuCommandWireValuesRemainStableWhenCommandsAreAppended()
    {
        Assert.Equal(9, (int)RekallAgeRuntimeGpuCommandKind.EndRenderPass);
        Assert.Equal(10, (int)RekallAgeRuntimeGpuCommandKind.BeginComputePass);
        Assert.Equal(11, (int)RekallAgeRuntimeGpuCommandKind.Dispatch);
        Assert.Equal(12, (int)RekallAgeRuntimeGpuCommandKind.EndComputePass);
        Assert.Equal(13, (int)RekallAgeRuntimeGpuCommandKind.DrawIndirect);
    }

    private static RekallAgeRuntimeGpuWorkload ComputeWorkload(string id, uint groups) => new(id)
    {
        Buffers =
        [
            new("particles", 4_096, RekallAgeRuntimeGpuBufferUsage.Storage) { StructureByteStride = 16 }
        ],
        Shaders =
        [
            new("simulate", RekallAgeRuntimeGpuShaderStage.Compute, RekallAgeRuntimeGpuShaderLanguage.Glsl,
                "#version 450\nvoid main(){}")
        ],
        Pipelines =
        [
            new("simulation", RekallAgeRuntimeGpuPipelineKind.Compute) { ComputeShader = "simulate" }
        ],
        Commands =
        [
            new(RekallAgeRuntimeGpuCommandKind.BeginComputePass) { Label = "simulation" },
            new(RekallAgeRuntimeGpuCommandKind.SetComputePipeline) { Resource = "simulation" },
            new(RekallAgeRuntimeGpuCommandKind.Dispatch) { GroupCountX = groups, GroupCountY = 1, GroupCountZ = 1 },
            new(RekallAgeRuntimeGpuCommandKind.EndComputePass)
        ]
    };

    private static RekallAgeRuntimeWorld World() => new(
        "scene", "Scene", 0, TimeSpan.Zero, [], RekallAgeRuntimeSubsystemViews.Empty, []);
}
