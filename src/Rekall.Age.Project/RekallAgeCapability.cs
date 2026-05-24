namespace Rekall.Age.Project;

public sealed record RekallAgeCapability(string Id)
{
    public static RekallAgeCapability Create(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Capability id is required.", nameof(id));
        }

        return new RekallAgeCapability(id.Trim().ToLowerInvariant());
    }
}
