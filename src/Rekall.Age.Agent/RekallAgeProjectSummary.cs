namespace Rekall.Age.Agent;

public sealed record RekallAgeProjectSummary(
    string Project,
    string? SourceTemplateId,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> PlayableScenes,
    IReadOnlyList<RekallAgeProjectArtifact> Artifacts,
    RekallAgeProjectHealth Health,
    IReadOnlyList<string> RecommendedNextActions);

public sealed record RekallAgeProjectArtifact(
    string Kind,
    string Path,
    long SizeBytes);

public sealed record RekallAgeProjectHealth(
    string Status,
    IReadOnlyList<string> BlockingIssues);
