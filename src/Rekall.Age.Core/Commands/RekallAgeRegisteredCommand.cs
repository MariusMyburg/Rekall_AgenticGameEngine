namespace Rekall.Age.Core.Commands;

public sealed record RekallAgeRegisteredCommand(
    RekallAgeCommandSchema Schema,
    Type RequestType,
    Type ResultType);
