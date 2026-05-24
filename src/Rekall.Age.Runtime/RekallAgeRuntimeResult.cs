namespace Rekall.Age.Runtime;

public sealed record RekallAgeRuntimeResult(
    bool Ok,
    int FramesSimulated,
    TimeSpan Duration,
    IReadOnlyList<string> Errors);
