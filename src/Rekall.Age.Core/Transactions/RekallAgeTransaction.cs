namespace Rekall.Age.Core.Transactions;

public sealed class RekallAgeTransaction
{
    private readonly List<string> _changedResources = [];
    private readonly List<RekallAgeTransactionResourcePreimage> _resourcePreimages = [];

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

    public IReadOnlyList<RekallAgeTransactionResourcePreimage> ResourcePreimages => _resourcePreimages;

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

    public void CaptureResourcePreimage(string resource)
    {
        if (string.IsNullOrWhiteSpace(resource))
        {
            throw new ArgumentException("Resource preimage path is required.", nameof(resource));
        }

        if (_resourcePreimages.Any(preimage => preimage.Resource.Equals(resource, StringComparison.Ordinal)))
        {
            return;
        }

        var content = File.Exists(resource)
            ? File.ReadAllBytes(resource)
            : Array.Empty<byte>();
        _resourcePreimages.Add(new RekallAgeTransactionResourcePreimage(resource, File.Exists(resource), content));
    }
}

public sealed record RekallAgeTransactionResourcePreimage(
    string Resource,
    bool ExistedBefore,
    byte[] Content)
{
    public string ReadUtf8Text()
    {
        return System.Text.Encoding.UTF8.GetString(Content);
    }
}
