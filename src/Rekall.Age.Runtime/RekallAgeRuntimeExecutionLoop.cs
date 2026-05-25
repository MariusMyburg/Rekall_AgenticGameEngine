using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Runtime;

public sealed class RekallAgeRuntimeExecutionLoop
{
    private static readonly TimeSpan DefaultFixedDeltaTime = TimeSpan.FromSeconds(1.0 / 60.0);
    private const double DefaultFixedDeltaSeconds = 1.0 / 60.0;
    private double _fixedDeltaSeconds;
    private readonly TimeSpan _fixedDeltaTime;
    private readonly IReadOnlyList<IRekallAgeRuntimeWorldSystem> _systems;
    private readonly RekallAgeRuntimeProjectionBuilder _projectionBuilder = new();

    public RekallAgeRuntimeExecutionLoop(
        IEnumerable<IRekallAgeRuntimeWorldSystem> systems,
        TimeSpan fixedDeltaTime)
    {
        _systems = systems
            .OrderBy(system => system.Priority)
            .ThenBy(system => system.Id, StringComparer.Ordinal)
            .ToArray();
        _fixedDeltaTime = fixedDeltaTime <= TimeSpan.Zero
            ? throw new ArgumentOutOfRangeException(nameof(fixedDeltaTime), "Fixed delta time must be greater than zero.")
            : fixedDeltaTime;
        _fixedDeltaSeconds = fixedDeltaTime.TotalSeconds;
    }

    public static RekallAgeRuntimeExecutionLoop CreateDefault(string? projectRoot = null)
    {
        var systems = new List<IRekallAgeRuntimeWorldSystem>();
        if (!string.IsNullOrWhiteSpace(projectRoot))
        {
            systems.AddRange(new RekallAgeProjectRuntimeSystemLoader().Load(projectRoot));
        }

        systems.AddRange(
        [
            new RekallAgeTransformAnimationSystem(),
            new NoOpRuntimeWorldSystem("runtime.audio"),
            new RekallAgeBepuPhysicsSystem(),
            new NoOpRuntimeWorldSystem("runtime.rendering"),
            new NoOpRuntimeWorldSystem("runtime.transform"),
            new NoOpRuntimeWorldSystem("runtime.ui")
        ]);

        var loop = new RekallAgeRuntimeExecutionLoop(
            systems,
            DefaultFixedDeltaTime);
        loop._fixedDeltaSeconds = DefaultFixedDeltaSeconds;
        return loop;
    }

    public async ValueTask<RekallAgeRuntimeRunResult> RunAsync(
        RekallAgeRuntimeWorld initialWorld,
        int frames,
        CancellationToken cancellationToken)
    {
        if (frames < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frames), "Frame count cannot be negative.");
        }

        var world = initialWorld;
        for (var frame = 0; frame < frames; frame++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nextFrameIndex = world.FrameIndex + 1;
            var nextElapsed = initialWorld.ElapsedTime + TimeSpan.FromSeconds((frame + 1) * _fixedDeltaSeconds);
            var context = new RekallAgeRuntimeWorldFrameContext(
                nextFrameIndex,
                _fixedDeltaTime,
                nextElapsed,
                cancellationToken);

            foreach (var system in _systems)
            {
                world = await system.UpdateAsync(world, context);
            }

            world = _projectionBuilder.Project(world with
            {
                FrameIndex = nextFrameIndex,
                ElapsedTime = nextElapsed,
                Observations = Array.Empty<RekallAgeRuntimeObservation>(),
                SystemsRun = _systems.Select(system => system.Id).ToArray()
            });
        }

        return new RekallAgeRuntimeRunResult(
            world,
            frames,
            _fixedDeltaTime,
            _systems.Select(system => system.Id).ToArray());
    }

    private sealed class NoOpRuntimeWorldSystem(string id) : IRekallAgeRuntimeWorldSystem
    {
        public string Id { get; } = id;

        public int Priority => 0;

        public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
            RekallAgeRuntimeWorld world,
            RekallAgeRuntimeWorldFrameContext context)
        {
            return ValueTask.FromResult(world);
        }
    }
}

public sealed record RekallAgeRuntimeWorldFrameContext(
    int FrameIndex,
    TimeSpan DeltaTime,
    TimeSpan ElapsedTime,
    CancellationToken CancellationToken);

public interface IRekallAgeRuntimeWorldSystem
{
    string Id { get; }

    int Priority { get; }

    ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeWorldFrameContext context);
}

public sealed record RekallAgeRuntimeRunResult(
    RekallAgeRuntimeWorld World,
    int FramesSimulated,
    TimeSpan FixedDeltaTime,
    IReadOnlyList<string> SystemsRun);
