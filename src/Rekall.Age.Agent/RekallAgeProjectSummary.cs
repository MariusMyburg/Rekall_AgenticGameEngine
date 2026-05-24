namespace Rekall.Age.Agent;

public sealed record RekallAgeProjectSummary(
    string Project,
    string? SourceTemplateId,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> PlayableScenes,
    RekallAgeProjectHealth Health,
    IReadOnlyList<string> RecommendedNextActions);

public sealed record RekallAgeProjectHealth(
    string Status,
    IReadOnlyList<string> BlockingIssues);
