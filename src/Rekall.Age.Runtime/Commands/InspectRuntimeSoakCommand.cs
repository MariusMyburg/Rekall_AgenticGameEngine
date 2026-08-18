using System.Diagnostics;
using Rekall.Age.Core.Commands;
using Rekall.Age.Runtime.Abstractions;
using Rekall.Age.World;

namespace Rekall.Age.Runtime.Commands;

public sealed record InspectRuntimeSoakRequest(
    string ProjectRoot,
    string SceneName,
    int Frames = 3_600,
    int CheckpointInterval = 600,
    double MinimumFramesPerSecond = 0,
    long MaximumRetainedManagedMemoryGrowthBytes = -1,
    int MaximumEntityGrowth = 0,
    int MaximumObservationsPerCheckpoint = 128,
    int MaximumEventsPerCheckpoint = 1_024,
    bool RequireStableSystems = true);

public sealed record RuntimeSoakCheckpoint(
    int CompletedFrames,
    int FrameIndex,
    double ElapsedSeconds,
    double WallMilliseconds,
    double FramesPerSecond,
    int EntityCount,
    int ComponentCount,
    int ObservationCount,
    int EventCount,
    int SystemCount,
    long SampledManagedMemoryBytes,
    int PhysicsBodyCount,
    int AudioVoiceCount,
    int AnimationPlayerCount,
    int UiElementCount,
    int RenderableCount,
    int XrPoseCount,
    IReadOnlyList<string> SystemsRun);

public sealed record RuntimeSoakCheck(
    string Name,
    bool Passed,
    double MeasuredValue,
    double? Limit,
    string Message);

public sealed record InspectRuntimeSoakResult(
    string SceneName,
    int RequestedFrames,
    int CompletedFrames,
    int InitialFrameIndex,
    int FinalFrameIndex,
    double InitialElapsedSeconds,
    double FinalElapsedSeconds,
    double WallMilliseconds,
    double FramesPerSecond,
    long BaselineManagedMemoryBytes,
    long FinalRetainedManagedMemoryBytes,
    long PeakSampledManagedMemoryBytes,
    long RetainedManagedMemoryGrowthBytes,
    IReadOnlyList<string> SystemsRun,
    IReadOnlyList<RuntimeSoakCheckpoint> Checkpoints,
    IReadOnlyList<RuntimeSoakCheck> Checks);

public sealed class InspectRuntimeSoakCommand
    : IRekallAgeCommand<InspectRuntimeSoakRequest, InspectRuntimeSoakResult>
{
    public const int MaximumFrames = 1_000_000;
    public const int MaximumCheckpoints = 10_000;

    public string Name => "rekall.runtime.inspect_soak";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Runs one authored scene through a bounded fixed-step soak and returns deterministic continuity, bounded-growth, managed-memory, subsystem, and throughput evidence.",
        typeof(InspectRuntimeSoakRequest).FullName!,
        typeof(InspectRuntimeSoakResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<InspectRuntimeSoakResult>> ExecuteAsync(
        InspectRuntimeSoakRequest request,
        RekallAgeCommandContext context)
    {
        var validationError = Validate(request);
        if (validationError is not null)
        {
            return RekallAgeCommandResult<InspectRuntimeSoakResult>.Failure(
                EmptyResult(request),
                validationError,
                [new RekallAgeCommandError("REKALL_RUNTIME_SOAK_INVALID_REQUEST", validationError, request.SceneName)]);
        }

        var scene = await new RekallAgeSceneStore().LoadAsync(
            request.ProjectRoot,
            request.SceneName,
            context.CancellationToken);
        var initialWorld = new RekallAgeRuntimeWorldBuilder().Build(scene);
        var world = initialWorld;
        var loop = RekallAgeRuntimeExecutionLoop.CreateDefault(request.ProjectRoot);
        var checkpoints = new List<RuntimeSoakCheckpoint>();
        var baselineMemory = GC.GetTotalMemory(forceFullCollection: true);
        var peakSampledMemory = baselineMemory;
        var completedFrames = 0;
        var stopwatch = Stopwatch.StartNew();

        while (completedFrames < request.Frames)
        {
            var chunkFrames = Math.Min(request.CheckpointInterval, request.Frames - completedFrames);
            var run = await loop.RunAsync(world, chunkFrames, context.CancellationToken);
            world = run.World;
            completedFrames += run.FramesSimulated;
            var sampledMemory = GC.GetTotalMemory(forceFullCollection: false);
            peakSampledMemory = Math.Max(peakSampledMemory, sampledMemory);
            checkpoints.Add(ToCheckpoint(world, completedFrames, stopwatch.Elapsed, sampledMemory));
        }

        stopwatch.Stop();
        var finalMemory = GC.GetTotalMemory(forceFullCollection: true);
        peakSampledMemory = Math.Max(peakSampledMemory, finalMemory);
        var retainedGrowth = finalMemory - baselineMemory;
        var wallMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
        var throughput = wallMilliseconds <= 0
            ? double.PositiveInfinity
            : completedFrames / stopwatch.Elapsed.TotalSeconds;
        var checks = BuildChecks(
            request,
            initialWorld,
            world,
            completedFrames,
            throughput,
            retainedGrowth,
            checkpoints);
        var result = new InspectRuntimeSoakResult(
            world.SceneName,
            request.Frames,
            completedFrames,
            initialWorld.FrameIndex,
            world.FrameIndex,
            initialWorld.ElapsedTime.TotalSeconds,
            world.ElapsedTime.TotalSeconds,
            wallMilliseconds,
            throughput,
            baselineMemory,
            finalMemory,
            peakSampledMemory,
            retainedGrowth,
            world.SystemsRun,
            checkpoints,
            checks);
        var failedChecks = checks.Where(check => !check.Passed).ToArray();
        if (failedChecks.Length > 0)
        {
            return RekallAgeCommandResult<InspectRuntimeSoakResult>.Failure(
                result,
                $"Runtime soak exceeded {failedChecks.Length} configured or deterministic budget(s): {string.Join(", ", failedChecks.Select(check => check.Name))}.",
                [new RekallAgeCommandError(
                    "REKALL_RUNTIME_SOAK_BUDGET_EXCEEDED",
                    string.Join(" ", failedChecks.Select(check => check.Message)),
                    request.SceneName)]);
        }

        return RekallAgeCommandResult<InspectRuntimeSoakResult>.Success(
            result,
            $"Runtime soak completed {completedFrames} frames at {throughput:F1} frames/second with {retainedGrowth} bytes retained managed-memory growth.");
    }

    private static string? Validate(InspectRuntimeSoakRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectRoot))
        {
            return "Runtime soak requires a project root.";
        }

        if (string.IsNullOrWhiteSpace(request.SceneName))
        {
            return "Runtime soak requires a scene name.";
        }

        if (request.Frames is < 1 or > MaximumFrames)
        {
            return $"Runtime soak frames must be between 1 and {MaximumFrames}.";
        }

        if (request.CheckpointInterval < 1 || request.CheckpointInterval > request.Frames)
        {
            return "Runtime soak checkpoint interval must be between 1 and the requested frame count.";
        }

        if ((request.Frames + (long)request.CheckpointInterval - 1) / request.CheckpointInterval > MaximumCheckpoints)
        {
            return $"Runtime soak may contain at most {MaximumCheckpoints} checkpoints.";
        }

        if (!double.IsFinite(request.MinimumFramesPerSecond) || request.MinimumFramesPerSecond < 0)
        {
            return "Runtime soak minimum frames per second must be finite and non-negative.";
        }

        if (request.MaximumRetainedManagedMemoryGrowthBytes < -1)
        {
            return "Runtime soak maximum retained managed-memory growth must be -1 (disabled) or non-negative.";
        }

        if (request.MaximumEntityGrowth < 0
            || request.MaximumObservationsPerCheckpoint < 0
            || request.MaximumEventsPerCheckpoint < 0)
        {
            return "Runtime soak entity, observation, and event limits must be non-negative.";
        }

        return null;
    }

    private static RuntimeSoakCheckpoint ToCheckpoint(
        RekallAgeRuntimeWorld world,
        int completedFrames,
        TimeSpan wallTime,
        long sampledMemory)
    {
        var rendering = world.Subsystems.Rendering;
        return new RuntimeSoakCheckpoint(
            completedFrames,
            world.FrameIndex,
            world.ElapsedTime.TotalSeconds,
            wallTime.TotalMilliseconds,
            wallTime <= TimeSpan.Zero ? double.PositiveInfinity : completedFrames / wallTime.TotalSeconds,
            world.Entities.Count,
            world.Entities.Sum(entity => entity.Components.Count),
            world.Observations.Count,
            world.Subsystems.Events.Events.Count,
            world.SystemsRun.Count,
            sampledMemory,
            world.Subsystems.Physics.RigidBodies.Count,
            world.Subsystems.Audio.Voices.Count,
            world.Subsystems.Animation.Players.Count,
            world.Subsystems.Ui.Elements.Count,
            rendering.Sprites.Count + rendering.Meshes.Count + rendering.Lights.Count + rendering.UiLayers.Count,
            world.Subsystems.Xr.Poses.Count,
            world.SystemsRun.ToArray());
    }

    private static IReadOnlyList<RuntimeSoakCheck> BuildChecks(
        InspectRuntimeSoakRequest request,
        RekallAgeRuntimeWorld initialWorld,
        RekallAgeRuntimeWorld finalWorld,
        int completedFrames,
        double throughput,
        long retainedGrowth,
        IReadOnlyList<RuntimeSoakCheckpoint> checkpoints)
    {
        var expectedFrame = initialWorld.FrameIndex + request.Frames;
        var expectedElapsed = initialWorld.ElapsedTime + TimeSpan.FromSeconds(request.Frames / 60.0);
        var maximumEntities = checkpoints.Max(checkpoint => checkpoint.EntityCount);
        var entityGrowth = maximumEntities - initialWorld.Entities.Count;
        var maximumObservations = checkpoints.Max(checkpoint => checkpoint.ObservationCount);
        var maximumEvents = checkpoints.Max(checkpoint => checkpoint.EventCount);
        var stableSystems = checkpoints
            .Select(checkpoint => checkpoint.SystemsRun)
            .Skip(1)
            .All(systems => systems.SequenceEqual(checkpoints[0].SystemsRun, StringComparer.Ordinal));

        return
        [
            Check("complete-execution", completedFrames == request.Frames, completedFrames, request.Frames,
                $"Completed {completedFrames} of {request.Frames} requested frames."),
            Check("frame-continuity", finalWorld.FrameIndex == expectedFrame, finalWorld.FrameIndex, expectedFrame,
                $"Final frame {finalWorld.FrameIndex}; expected {expectedFrame}."),
            Check("elapsed-continuity", finalWorld.ElapsedTime == expectedElapsed, finalWorld.ElapsedTime.TotalSeconds, expectedElapsed.TotalSeconds,
                $"Final elapsed time {finalWorld.ElapsedTime.TotalSeconds:F9}s; expected {expectedElapsed.TotalSeconds:F9}s."),
            Check("stable-systems", !request.RequireStableSystems || stableSystems, stableSystems ? 1 : 0, request.RequireStableSystems ? 1 : null,
                request.RequireStableSystems
                    ? $"Runtime system order was {(stableSystems ? "stable" : "not stable")} across {checkpoints.Count} checkpoints."
                    : "Stable runtime system order check was disabled."),
            Check("throughput", request.MinimumFramesPerSecond <= 0 || throughput >= request.MinimumFramesPerSecond, throughput,
                request.MinimumFramesPerSecond > 0 ? request.MinimumFramesPerSecond : null,
                request.MinimumFramesPerSecond > 0
                    ? $"Measured {throughput:F1} frames/second; minimum {request.MinimumFramesPerSecond:F1}."
                    : $"Measured {throughput:F1} frames/second; blocking throughput budget was disabled."),
            Check("retained-managed-memory", request.MaximumRetainedManagedMemoryGrowthBytes < 0 || retainedGrowth <= request.MaximumRetainedManagedMemoryGrowthBytes,
                retainedGrowth, request.MaximumRetainedManagedMemoryGrowthBytes >= 0 ? request.MaximumRetainedManagedMemoryGrowthBytes : null,
                request.MaximumRetainedManagedMemoryGrowthBytes >= 0
                    ? $"Retained managed-memory growth was {retainedGrowth} bytes; maximum {request.MaximumRetainedManagedMemoryGrowthBytes}."
                    : $"Retained managed-memory growth was {retainedGrowth} bytes; blocking memory budget was disabled."),
            Check("entity-growth", entityGrowth <= request.MaximumEntityGrowth, entityGrowth, request.MaximumEntityGrowth,
                $"Maximum entity-count growth was {entityGrowth}; maximum {request.MaximumEntityGrowth}."),
            Check("checkpoint-observations", maximumObservations <= request.MaximumObservationsPerCheckpoint, maximumObservations,
                request.MaximumObservationsPerCheckpoint,
                $"Maximum checkpoint observations were {maximumObservations}; maximum {request.MaximumObservationsPerCheckpoint}."),
            Check("checkpoint-events", maximumEvents <= request.MaximumEventsPerCheckpoint, maximumEvents,
                request.MaximumEventsPerCheckpoint,
                $"Maximum checkpoint events were {maximumEvents}; maximum {request.MaximumEventsPerCheckpoint}.")
        ];
    }

    private static RuntimeSoakCheck Check(
        string name,
        bool passed,
        double measured,
        double? limit,
        string message)
    {
        return new RuntimeSoakCheck(name, passed, measured, limit, message);
    }

    private static InspectRuntimeSoakResult EmptyResult(InspectRuntimeSoakRequest request)
    {
        return new InspectRuntimeSoakResult(
            request.SceneName,
            request.Frames,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            Array.Empty<string>(),
            Array.Empty<RuntimeSoakCheckpoint>(),
            Array.Empty<RuntimeSoakCheck>());
    }
}
