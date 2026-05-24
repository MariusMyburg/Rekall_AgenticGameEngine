namespace Rekall.Age.World;

public sealed record RekallAgeSceneDocument(
    string Id,
    string Name,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<RekallAgeEntityDocument> Entities)
{
    public static RekallAgeSceneDocument Create(string name, IEnumerable<string> capabilities)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Scene name is required.", nameof(name));
        }

        return new RekallAgeSceneDocument(
            $"scene_{Guid.NewGuid():N}",
            name.Trim(),
            NormalizeCapabilities(capabilities),
            Array.Empty<RekallAgeEntityDocument>());
    }

    public RekallAgeSceneDocument AddEntity(RekallAgeEntityDocument entity)
    {
        return this with { Entities = SortEntities(Entities.Append(entity)) };
    }

    public RekallAgeSceneDocument UpdateEntity(string entityId, Func<RekallAgeEntityDocument, RekallAgeEntityDocument> update)
    {
        var found = false;
        var entities = Entities.Select(entity =>
        {
            if (!entity.Id.Equals(entityId, StringComparison.Ordinal))
            {
                return entity;
            }

            found = true;
            return update(entity);
        }).ToArray();

        if (!found)
        {
            throw new InvalidOperationException($"Entity '{entityId}' was not found in scene '{Name}'.");
        }

        return this with { Entities = SortEntities(entities) };
    }

    private static IReadOnlyList<RekallAgeEntityDocument> SortEntities(IEnumerable<RekallAgeEntityDocument> entities)
    {
        return entities
            .OrderBy(item => item.Name, StringComparer.Ordinal)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<string> NormalizeCapabilities(IEnumerable<string> capabilities)
    {
        return capabilities
            .Select(capability => capability.Trim().ToLowerInvariant())
            .Where(capability => capability.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(capability => capability, StringComparer.Ordinal)
            .ToArray();
    }
}
