using Rekall.Age.Core.Commands;

namespace Rekall.Age.Validation;

public sealed record RekallAgeValidationIssue(
    string Code,
    string Message,
    string Severity,
    string? Target,
    IReadOnlyList<RekallAgeSuggestedCommand>? SuggestedCommands = null);
