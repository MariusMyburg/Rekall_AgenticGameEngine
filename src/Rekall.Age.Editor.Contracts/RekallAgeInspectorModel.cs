namespace Rekall.Age.Editor.Contracts;

public sealed record RekallAgeInspectorModel(
    string? SelectedEntityId,
    string? SelectedEntityName,
    IReadOnlyList<RekallAgeInspectorComponentModel> Components);

public sealed record RekallAgeInspectorComponentModel(
    string Type,
    IReadOnlyList<RekallAgeInspectorPropertyModel> Properties);

public sealed record RekallAgeInspectorPropertyModel(
    string Name,
    string Value,
    string ValueKind);
