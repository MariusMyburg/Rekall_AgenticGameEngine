using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Modules;

public interface IRekallAgeRuntimeModuleSystem
{
    string Id { get; }

    int Priority { get; }

    ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
        RekallAgeRuntimeWorld world,
        RekallAgeRuntimeModuleFrameContext context);
}

public sealed record RekallAgeRuntimeModuleFrameContext(
    int FrameIndex,
    TimeSpan DeltaTime,
    TimeSpan ElapsedTime,
    CancellationToken CancellationToken)
{
    public RekallAgeRuntimeInputState Input { get; init; } = RekallAgeRuntimeInputState.Empty;
}
