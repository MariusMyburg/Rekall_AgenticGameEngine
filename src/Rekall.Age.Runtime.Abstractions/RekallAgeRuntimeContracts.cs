using Rekall.Age.World;

namespace Rekall.Age.Runtime.Abstractions;

public sealed record RekallAgeFrameContext(
    int FrameIndex,
    TimeSpan DeltaTime,
    TimeSpan ElapsedTime,
    CancellationToken CancellationToken);

public interface IRekallAgeRuntimeSystem
{
    string Id { get; }

    ValueTask UpdateAsync(
        RekallAgeSceneDocument scene,
        RekallAgeFrameContext context);
}

public sealed record RekallAgeSubsystemDescriptor(
    string Id,
    string Kind,
    string Status,
    IReadOnlyList<string> Capabilities);

public sealed class RekallAgeSubsystemRegistry
{
    private readonly List<RekallAgeSubsystemDescriptor> _subsystems = [];

    public IReadOnlyList<RekallAgeSubsystemDescriptor> Subsystems => _subsystems;

    public void Register(RekallAgeSubsystemDescriptor descriptor)
    {
        if (_subsystems.Any(item => item.Id.Equals(descriptor.Id, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Subsystem '{descriptor.Id}' is already registered.");
        }

        _subsystems.Add(descriptor);
    }
}
