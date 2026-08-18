using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Commands;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Runtime;

public sealed class RuntimeSoakCommandTests
{
    [Fact]
    public async Task ExecutionLoopPreservesElapsedTimeAcrossResumedChunks()
    {
        var initial = new RekallAgeRuntimeWorldBuilder().Build(
            RekallAgeSceneDocument.Create("Main", ["world"]));
        var loop = RekallAgeRuntimeExecutionLoop.CreateDefault();

        var continuous = await loop.RunAsync(initial, 125, CancellationToken.None);
        var firstChunk = await loop.RunAsync(initial, 50, CancellationToken.None);
        var secondChunk = await loop.RunAsync(firstChunk.World, 50, CancellationToken.None);
        var resumed = await loop.RunAsync(secondChunk.World, 25, CancellationToken.None);

        Assert.Equal(continuous.World.FrameIndex, resumed.World.FrameIndex);
        Assert.Equal(continuous.World.ElapsedTime, resumed.World.ElapsedTime);
    }

    [Fact]
    public async Task SoakResumesAcrossChunksWithExactDeterministicContinuity()
    {
        var root = await CreateSceneAsync();
        var result = await ExecuteAsync(new InspectRuntimeSoakRequest(
            root,
            "Main",
            Frames: 125,
            CheckpointInterval: 50,
            MinimumFramesPerSecond: 0,
            MaximumRetainedManagedMemoryGrowthBytes: -1,
            MaximumEntityGrowth: 0,
            MaximumObservationsPerCheckpoint: 128,
            MaximumEventsPerCheckpoint: 1024,
            RequireStableSystems: true));

        Assert.True(result.Ok, result.Summary);
        Assert.Equal(125, result.Value.CompletedFrames);
        Assert.Equal([50, 100, 125], result.Value.Checkpoints.Select(item => item.CompletedFrames));
        Assert.Equal(125, result.Value.FinalFrameIndex);
        Assert.Equal(
            TimeSpan.FromSeconds(125.0 / 60.0).TotalSeconds,
            result.Value.FinalElapsedSeconds,
            precision: 10);
        Assert.All(result.Value.Checks, check => Assert.True(check.Passed, check.Message));
    }

    [Fact]
    public async Task ImpossibleThroughputBudgetReturnsStructuredFailureWithEvidence()
    {
        var root = await CreateSceneAsync();
        var result = await ExecuteAsync(new InspectRuntimeSoakRequest(
            root,
            "Main",
            Frames: 12,
            CheckpointInterval: 5,
            MinimumFramesPerSecond: double.MaxValue));

        Assert.False(result.Ok);
        Assert.Equal(12, result.Value.CompletedFrames);
        Assert.Equal(3, result.Value.Checkpoints.Count);
        var throughput = Assert.Single(result.Value.Checks, check => check.Name == "throughput");
        Assert.False(throughput.Passed);
        Assert.True(throughput.MeasuredValue >= 0);
        Assert.Contains(result.Errors, error => error.Code == "REKALL_RUNTIME_SOAK_BUDGET_EXCEEDED");
    }

    public static TheoryData<InspectRuntimeSoakRequest> InvalidRequests => new()
    {
        new InspectRuntimeSoakRequest("Z:\\missing", "Main", Frames: 0),
        new InspectRuntimeSoakRequest("Z:\\missing", "Main", Frames: 1, CheckpointInterval: 0),
        new InspectRuntimeSoakRequest("Z:\\missing", "Main", Frames: 1_000_001),
        new InspectRuntimeSoakRequest("Z:\\missing", "Main", Frames: 1, MaximumEntityGrowth: -1),
        new InspectRuntimeSoakRequest("Z:\\missing", "Main", Frames: 1, MinimumFramesPerSecond: double.NaN)
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task InvalidRequestsFailBeforeSceneLoading(InspectRuntimeSoakRequest request)
    {
        var result = await ExecuteAsync(request);

        Assert.False(result.Ok);
        Assert.Equal(0, result.Value.CompletedFrames);
        Assert.Contains(result.Errors, error => error.Code == "REKALL_RUNTIME_SOAK_INVALID_REQUEST");
        Assert.DoesNotContain(result.Errors, error => error.Code.Contains("SCENE", StringComparison.Ordinal));
    }

    private static async Task<string> CreateSceneAsync()
    {
        var root = TestPaths.CreateTempDirectory();
        var actor = RekallAgeEntityDocument.Create("Actor", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Transform3D",
                new JsonObject { ["Y"] = 3 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Rigidbody3D",
                new JsonObject { ["Mass"] = 1 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.BoxCollider3D",
                new JsonObject { ["Width"] = 1, ["Height"] = 1, ["Depth"] = 1 }));
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "physics3d"]).AddEntity(actor),
            CancellationToken.None);
        return root;
    }

    private static ValueTask<RekallAgeCommandResult<InspectRuntimeSoakResult>> ExecuteAsync(
        InspectRuntimeSoakRequest request)
    {
        return new InspectRuntimeSoakCommand().ExecuteAsync(
            request,
            new RekallAgeCommandContext(
                "test",
                RekallAgeTransaction.Begin("inspect runtime soak"),
                CancellationToken.None));
    }
}
