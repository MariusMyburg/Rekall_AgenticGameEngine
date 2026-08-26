using System.Security.Cryptography;
using System.Text;

namespace Rekall.Age.Agent.LanguageModels;

internal sealed class RekallAgeOpenAiToolNameMap
{
    private const int MaximumAliasCharacters = 64;
    private const int HashCharacters = 12;
    private const int MaximumPrefixCharacters = MaximumAliasCharacters - HashCharacters - 1;
    private readonly IReadOnlyDictionary<string, string> _aliasesByCanonicalName;
    private readonly IReadOnlyDictionary<string, string> _canonicalNamesByAlias;

    private RekallAgeOpenAiToolNameMap(
        IReadOnlyList<string> aliases,
        IReadOnlyDictionary<string, string> aliasesByCanonicalName,
        IReadOnlyDictionary<string, string> canonicalNamesByAlias)
    {
        Aliases = aliases;
        _aliasesByCanonicalName = aliasesByCanonicalName;
        _canonicalNamesByAlias = canonicalNamesByAlias;
    }

    public IReadOnlyList<string> Aliases { get; }

    public static RekallAgeOpenAiToolNameMap Create(IReadOnlyList<string> canonicalNames)
    {
        ArgumentNullException.ThrowIfNull(canonicalNames);
        var aliases = new string[canonicalNames.Count];
        var aliasesByCanonicalName = new Dictionary<string, string>(canonicalNames.Count, StringComparer.Ordinal);
        var canonicalNamesByAlias = new Dictionary<string, string>(canonicalNames.Count, StringComparer.Ordinal);

        for (var index = 0; index < canonicalNames.Count; index++)
        {
            var canonicalName = canonicalNames[index];
            if (string.IsNullOrWhiteSpace(canonicalName)
                || !aliasesByCanonicalName.TryAdd(canonicalName, string.Empty))
            {
                throw new ArgumentException(
                    "Canonical tool names must be non-empty and unique.",
                    nameof(canonicalNames));
            }

            var alias = BuildAlias(canonicalName);
            if (!canonicalNamesByAlias.TryAdd(alias, canonicalName))
            {
                throw new ArgumentException(
                    "Canonical tool names produced a duplicate provider alias.",
                    nameof(canonicalNames));
            }

            aliases[index] = alias;
            aliasesByCanonicalName[canonicalName] = alias;
        }

        return new RekallAgeOpenAiToolNameMap(aliases, aliasesByCanonicalName, canonicalNamesByAlias);
    }

    public string ToAlias(string canonicalName) => _aliasesByCanonicalName[canonicalName];

    public string ToCanonical(string alias) => _canonicalNamesByAlias[alias];

    private static string BuildAlias(string canonicalName)
    {
        var sanitized = new StringBuilder(Math.Min(canonicalName.Length, MaximumPrefixCharacters));
        foreach (var character in canonicalName)
        {
            sanitized.Append(IsAllowed(character) ? character : '_');
            if (sanitized.Length == MaximumPrefixCharacters)
            {
                break;
            }
        }

        if (sanitized.Length == 0)
        {
            sanitized.Append("tool");
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalName)))
            .ToLowerInvariant()[..HashCharacters];
        return $"{sanitized}_{hash}";
    }

    private static bool IsAllowed(char character) =>
        character is >= 'A' and <= 'Z'
            or >= 'a' and <= 'z'
            or >= '0' and <= '9'
            or '_'
            or '-';
}
