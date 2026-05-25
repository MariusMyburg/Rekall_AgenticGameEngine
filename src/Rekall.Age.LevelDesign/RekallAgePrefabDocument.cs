using Rekall.Age.World;

namespace Rekall.Age.LevelDesign;

public sealed record RekallAgePrefabDocument(
    string Id,
    string Name,
    RekallAgeEntityDocument RootEntity)
{
    public static RekallAgePrefabDocument Create(string name, RekallAgeEntityDocument entity)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Prefab name is required.", nameof(name));
        }

        return new RekallAgePrefabDocument($"prefab_{Guid.NewGuid():N}", name.Trim(), entity with { ParentId = null });
    }
}
