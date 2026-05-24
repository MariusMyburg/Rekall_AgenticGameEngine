using Rekall.Age.World;

namespace Rekall.Age.GameTemplates;

public sealed record RekallAgeGameTemplate(
    string Id,
    string DisplayName,
    string Description,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<RekallAgeEntityDocument> Entities);
