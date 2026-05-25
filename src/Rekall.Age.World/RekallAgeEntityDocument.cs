namespace Rekall.Age.World;

public sealed record RekallAgeEntityDocument(
    string Id,
    string Name,
    IReadOnlyList<string> Tags,
    IReadOnlyList<RekallAgeComponentDocument> Components)
{
    public string? ParentId { get; init; }

    public string? PrefabSourceId { get; init; }

    public bool Visible { get; init; } = true;

    public bool Locked { get; init; }

    public static RekallAgeEntityDocument Create(string name, IEnumerable<string> tags)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Entity name is required.", nameof(name));
        }

        return new RekallAgeEntityDocument(
            $"ent_{Guid.NewGuid():N}",
            name.Trim(),
            NormalizeTags(tags),
            Array.Empty<RekallAgeComponentDocument>());
    }

    public RekallAgeEntityDocument AddComponent(RekallAgeComponentDocument component)
    {
        var components = Components
            .Where(existing => !existing.Type.Equals(component.Type, StringComparison.Ordinal))
            .Append(component)
            .OrderBy(item => item.Type, StringComparer.Ordinal)
            .ToArray();
        return this with { Components = components };
    }

    public RekallAgeEntityDocument UpdateComponent(
        string componentType,
        Func<RekallAgeComponentDocument, RekallAgeComponentDocument> update)
    {
        var found = false;
        var components = Components.Select(component =>
        {
            if (!component.Type.Equals(componentType, StringComparison.Ordinal))
            {
                return component;
            }

            found = true;
            return update(component);
        }).ToArray();

        if (!found)
        {
            throw new InvalidOperationException($"Component '{componentType}' was not found on entity '{Name}'.");
        }

        return this with
        {
            Components = components.OrderBy(component => component.Type, StringComparer.Ordinal).ToArray()
        };
    }

    private static IReadOnlyList<string> NormalizeTags(IEnumerable<string> tags)
    {
        return tags
            .Select(tag => tag.Trim().ToLowerInvariant())
            .Where(tag => tag.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(tag => tag, StringComparer.Ordinal)
            .ToArray();
    }
}
