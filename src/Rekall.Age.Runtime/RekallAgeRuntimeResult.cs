namespace Rekall.Age.Runtime;

public sealed record RekallAgeRuntimeResult(
    bool Ok,
    int FramesSimulated,
    TimeSpan Duration,
    IReadOnlyList<string> Errors,
    IReadOnlyList<RekallAgeRuntimeObservation> Observations)
{
    public IReadOnlyList<string> ActiveSystems =>
        Observations
            .Select(observation => observation.System)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(system => system, StringComparer.Ordinal)
            .ToArray();
}
