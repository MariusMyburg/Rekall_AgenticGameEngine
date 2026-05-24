namespace Rekall.Age.Runtime;

public sealed record RekallAgeRuntimeObservation(
    int Frame,
    string EntityId,
    string EntityName,
    string System,
    string Message);
