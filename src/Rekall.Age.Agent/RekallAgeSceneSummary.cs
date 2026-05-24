namespace Rekall.Age.Agent;

public sealed record RekallAgeSceneSummary(
    string Scene,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<RekallAgeEntitySummary> Entities,
    IReadOnlyList<string> ComponentTypes)
{
    public int EntityCount => Entities.Count;
}

public sealed record RekallAgeEntitySummary(
    string Id,
    string Name,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Components);
