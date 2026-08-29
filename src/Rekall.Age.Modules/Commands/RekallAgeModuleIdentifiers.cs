namespace Rekall.Age.Modules.Commands;

internal static class RekallAgeModuleIdentifiers
{
    public static string ToIdentifier(string? value, string fallback)
    {
        var parts = System.Text.RegularExpressions.Regex.Split(value ?? string.Empty, @"[^\p{L}\p{Nd}]+")
            .Where(part => !string.IsNullOrWhiteSpace(part));
        var identifier = string.Concat(parts.Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return fallback;
        }

        return char.IsLetter(identifier[0]) || identifier[0] == '_'
            ? identifier
            : $"{fallback}{identifier}";
    }
}
