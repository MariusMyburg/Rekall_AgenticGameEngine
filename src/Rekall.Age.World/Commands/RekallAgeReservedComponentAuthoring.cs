using Rekall.Age.Core.Commands;

namespace Rekall.Age.World.Commands;

internal static class RekallAgeReservedComponentAuthoring
{
    public static RekallAgeCommandError? Validate(string componentType, string target)
    {
        componentType = componentType.Trim();
        if (!RekallAgeBuiltInComponentTypeCatalog.IsUnknownReserved(componentType))
        {
            return null;
        }

        var suggestion = RekallAgeBuiltInComponentTypeCatalog.FindSafeSuggestion(componentType);
        return new RekallAgeCommandError(
            "REKALL_COMPONENT_RESERVED_TYPE_UNKNOWN",
            $"Unknown reserved component '{componentType}'. "
            + (suggestion is null ? string.Empty : $"Did you mean '{suggestion}'? ")
            + "Use the exact type returned by rekall.module.search_component_schemas before mutating the scene.",
            target,
            [
                new RekallAgeSuggestedCommand(
                    "rekall.module.search_component_schemas",
                    new Dictionary<string, object?>
                    {
                        ["query"] = componentType,
                        ["limit"] = 20
                    })
            ]);
    }
}
