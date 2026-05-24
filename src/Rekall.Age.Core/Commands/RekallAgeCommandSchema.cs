namespace Rekall.Age.Core.Commands;

public sealed record RekallAgeCommandSchema(
    string Name,
    string Description,
    string RequestType,
    string ResultType);
