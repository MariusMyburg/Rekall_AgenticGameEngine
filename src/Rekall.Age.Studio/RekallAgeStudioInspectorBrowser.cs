using Rekall.Age.Editor.Contracts;

namespace Rekall.Age.Studio;

internal sealed record RekallAgeStudioInspectorBrowserResult(
    IReadOnlyList<RekallAgeInspectorComponentModel> Components,
    RekallAgeInspectorComponentModel? SelectedComponent);

internal static class RekallAgeStudioInspectorBrowser
{
    public static RekallAgeStudioInspectorBrowserResult Project(
        IReadOnlyList<RekallAgeInspectorComponentModel> components,
        string? query,
        string? selectedType)
    {
        ArgumentNullException.ThrowIfNull(components);
        var term = query?.Trim() ?? string.Empty;
        var visible = string.IsNullOrEmpty(term)
            ? components.ToArray()
            : components.Where(component => Matches(component, term)).ToArray();
        var selected = visible.FirstOrDefault(component =>
                component.Type.Equals(selectedType, StringComparison.Ordinal))
            ?? visible.FirstOrDefault();
        return new RekallAgeStudioInspectorBrowserResult(visible, selected);
    }

    private static bool Matches(RekallAgeInspectorComponentModel component, string term) =>
        Contains(component.DisplayName, term)
        || Contains(component.Type, term)
        || Contains(component.Description, term)
        || component.Properties.Any(property =>
            Contains(property.Name, term)
            || Contains(property.Value, term)
            || Contains(property.Description, term));

    private static bool Contains(string? value, string term) =>
        value?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;
}
