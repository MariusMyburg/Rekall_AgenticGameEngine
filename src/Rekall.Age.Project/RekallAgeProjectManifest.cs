namespace Rekall.Age.Project;

public sealed record RekallAgeProjectManifest(
    string Name,
    int SchemaVersion,
    IReadOnlyList<string> Capabilities)
{
    public static RekallAgeProjectManifest Create(string name, IEnumerable<string> capabilities)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Project name is required.", nameof(name));
        }

        return new RekallAgeProjectManifest(name.Trim(), 1, NormalizeCapabilities(capabilities));
    }

    public RekallAgeProjectManifest AddCapability(string capability)
    {
        return this with { Capabilities = NormalizeCapabilities(Capabilities.Append(capability)) };
    }

    private static IReadOnlyList<string> NormalizeCapabilities(IEnumerable<string> capabilities)
    {
        return capabilities
            .Select(id => RekallAgeCapability.Create(id).Id)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
    }
}
