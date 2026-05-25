using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Runtime;

public sealed record RekallAgeRuntimeResult(
    bool Ok,
    int FramesSimulated,
    TimeSpan Duration,
    IReadOnlyList<string> Errors,
    IReadOnlyList<RekallAgeRuntimeObservation> Observations)
{
    public IReadOnlyList<string> SystemsRun { get; init; } = Array.Empty<string>();

    public IReadOnlyList<RekallAgeRuntimeInputAction> InputActions { get; init; } =
        Array.Empty<RekallAgeRuntimeInputAction>();

    public IReadOnlyList<string> ActiveSystems =>
        SystemsRun.Count > 0
            ? SystemsRun
            : Observations
                .Select(observation => observation.System)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(system => system, StringComparer.Ordinal)
                .ToArray();
}
