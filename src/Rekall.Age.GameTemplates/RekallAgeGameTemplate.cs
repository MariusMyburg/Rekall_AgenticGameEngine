using Rekall.Age.World;

namespace Rekall.Age.GameTemplates;

public sealed record RekallAgeGameTemplate(
    string Id,
    string DisplayName,
    string Description,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<RekallAgeEntityDocument> Entities)
{
    public IReadOnlyList<RekallAgeTemplateDrawCommand> DrawCommands { get; init; } = [];
}

public sealed record RekallAgeTemplateDrawCommand(
    string Id,
    string Kind,
    string Purpose);
