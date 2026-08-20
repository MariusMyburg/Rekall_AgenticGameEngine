namespace Rekall.Age.Editor.Contracts;

public sealed record RekallAgeInspectorModel(
    string? SelectedEntityId,
    string? SelectedEntityName,
    IReadOnlyList<RekallAgeInspectorComponentModel> Components)
{
    public IReadOnlyList<RekallAgeInspectorComponentSchemaModel> AvailableComponents { get; init; } = [];
}

public sealed record RekallAgeInspectorComponentModel(
    string Type,
    IReadOnlyList<RekallAgeInspectorPropertyModel> Properties)
{
    public string DisplayName { get; init; } = Type;

    public string? Description { get; init; }

    public bool SchemaKnown { get; init; }
}

public sealed record RekallAgeInspectorPropertyModel(
    string Name,
    string Value,
    string ValueKind)
{
    public string TypeName { get; init; } = ValueKind;

    public string EditorKind { get; init; } = "json";

    public string? AssetKind { get; init; }

    public double? Minimum { get; init; }

    public double? Maximum { get; init; }

    public string? Description { get; init; }

    public IReadOnlyList<string> AllowedValues { get; init; } = [];

    public bool IsDefined { get; init; } = true;
}

public sealed record RekallAgeInspectorComponentSchemaModel(
    string Type,
    string DisplayName,
    string? Description,
    IReadOnlyList<RekallAgeInspectorPropertySchemaModel> Properties);

public sealed record RekallAgeInspectorPropertySchemaModel(
    string Name,
    string TypeName,
    string EditorKind,
    string? AssetKind,
    double? Minimum,
    double? Maximum,
    string? Description,
    IReadOnlyList<string> AllowedValues);
