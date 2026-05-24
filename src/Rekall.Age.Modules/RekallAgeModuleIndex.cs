namespace Rekall.Age.Modules;

public sealed record RekallAgeModuleIndex(IReadOnlyList<RekallAgeModuleMetadata> Modules)
{
    public IReadOnlyList<RekallAgeComponentSchema> Components =>
        Modules
            .SelectMany(module => module.Components)
            .OrderBy(component => component.TypeName, StringComparer.Ordinal)
            .ToArray();
}

public sealed record RekallAgeModuleMetadata(
    string Id,
    string DisplayName,
    string TypeName,
    IReadOnlyList<string> RequiredCapabilities,
    IReadOnlyList<RekallAgeComponentSchema> Components);

public sealed record RekallAgeComponentSchema(
    string TypeName,
    string DisplayName,
    IReadOnlyList<RekallAgePropertySchema> Properties);

public sealed record RekallAgePropertySchema(
    string Name,
    string TypeName,
    string Kind,
    string? AssetKind,
    double? Minimum,
    double? Maximum);
