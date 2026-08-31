namespace Rekall.Age.Editor.Contracts;

public static class RekallAgeContentCapability
{
    public const string Open = "open";
    public const string OpenExternal = "open-external";
    public const string Reveal = "reveal";
    public const string Reimport = "reimport";
    public const string Assign = "assign";
    public const string Place = "place";
}

public sealed record RekallAgeContentBrowserModel(
    IReadOnlyList<RekallAgeContentBrowserItem> Items,
    IReadOnlyList<RekallAgeContentBrowserWarning> Warnings)
{
    public static RekallAgeContentBrowserModel Empty { get; } = new([], []);
}

public sealed record RekallAgeContentBrowserItem(
    string Id,
    string DisplayName,
    string Family,
    string Kind,
    string Origin,
    string? Path,
    string? SourcePath,
    string? Revision,
    string EditorRouteId,
    IReadOnlyList<string> Capabilities,
    string Health,
    string? Diagnostic,
    RekallAgeContentPreviewMetadata Preview);

public sealed record RekallAgeContentPreviewMetadata(
    int? Width = null,
    int? Height = null,
    int? MeshCount = null,
    int? MaterialCount = null,
    int? AnimationCount = null);

public sealed record RekallAgeContentBrowserWarning(
    string Code,
    string Family,
    string Summary);
