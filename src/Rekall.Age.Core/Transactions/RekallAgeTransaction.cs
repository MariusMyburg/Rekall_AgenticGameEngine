namespace Rekall.Age.Core.Transactions;

public sealed class RekallAgeTransaction
{
    private readonly List<string> _changedResources = [];

    private RekallAgeTransaction(string name)
    {
        Id = $"txn_{Guid.NewGuid():N}";
        Name = name;
        StartedAtUtc = DateTimeOffset.UtcNow;
    }

    public string Id { get; }

    public string Name { get; }

    public DateTimeOffset StartedAtUtc { get; }

    public IReadOnlyList<string> ChangedResources => _changedResources;

    public static RekallAgeTransaction Begin(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Transaction name is required.", nameof(name));
        }

        return new RekallAgeTransaction(name);
    }

    public void RecordChangedResource(string resource)
    {
        if (string.IsNullOrWhiteSpace(resource))
        {
            throw new ArgumentException("Changed resource is required.", nameof(resource));
        }

        if (!_changedResources.Contains(resource, StringComparer.Ordinal))
        {
            _changedResources.Add(resource);
        }
    }
}
