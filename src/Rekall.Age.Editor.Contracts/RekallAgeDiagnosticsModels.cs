namespace Rekall.Age.Editor.Contracts;

public sealed record RekallAgeValidationPanelModel(
    IReadOnlyList<RekallAgeValidationPanelIssue> Issues);

public sealed record RekallAgeValidationPanelIssue(
    string Code,
    string Severity,
    string Message,
    string Target,
    IReadOnlyList<string> SuggestedTools);

public sealed record RekallAgeTransactionPanelModel(
    IReadOnlyList<RekallAgeTransactionPanelItem> Transactions);

public sealed record RekallAgeTransactionPanelItem(
    string Id,
    string Name,
    IReadOnlyList<string> ChangedResources);

public sealed record RekallAgeImportQueueModel(
    IReadOnlyList<RekallAgeImportQueueItem> Jobs);

public sealed record RekallAgeImportQueueItem(
    string SourcePath,
    string Kind,
    string Status,
    string Summary);
