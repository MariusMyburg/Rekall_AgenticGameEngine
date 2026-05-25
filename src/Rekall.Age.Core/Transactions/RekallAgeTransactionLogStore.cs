using System.Text.Json;

namespace Rekall.Age.Core.Transactions;

public sealed record RekallAgeTransactionLogDocument(
    IReadOnlyList<RekallAgeTransactionLogEntry> Transactions)
{
    public static RekallAgeTransactionLogDocument Empty { get; } = new(Array.Empty<RekallAgeTransactionLogEntry>());
}

public sealed record RekallAgeTransactionLogEntry(
    string Id,
    string Name,
    string Actor,
    DateTimeOffset StartedAtUtc,
    IReadOnlyList<string> ChangedResources);

public sealed class RekallAgeTransactionLogStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string GetPath(string projectRoot)
    {
        return Path.Combine(projectRoot, "Transactions", "transactions.age.json");
    }

    public async ValueTask<RekallAgeTransactionLogDocument> LoadAsync(
        string projectRoot,
        CancellationToken cancellationToken)
    {
        var path = GetPath(projectRoot);
        if (!File.Exists(path))
        {
            return RekallAgeTransactionLogDocument.Empty;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<RekallAgeTransactionLogDocument>(
            stream,
            JsonOptions,
            cancellationToken) ?? RekallAgeTransactionLogDocument.Empty;
    }

    public async ValueTask AppendAsync(
        string projectRoot,
        RekallAgeTransaction transaction,
        string actor,
        CancellationToken cancellationToken)
    {
        var existing = await LoadAsync(projectRoot, cancellationToken);
        var entry = new RekallAgeTransactionLogEntry(
            transaction.Id,
            transaction.Name,
            actor,
            transaction.StartedAtUtc,
            transaction.ChangedResources.ToArray());
        var document = new RekallAgeTransactionLogDocument(
            existing.Transactions
                .Where(item => !item.Id.Equals(entry.Id, StringComparison.Ordinal))
                .Append(entry)
                .OrderByDescending(item => item.StartedAtUtc)
                .ToArray());

        Directory.CreateDirectory(Path.GetDirectoryName(GetPath(projectRoot))!);
        var json = JsonSerializer.Serialize(document, JsonOptions);
        await File.WriteAllTextAsync(GetPath(projectRoot), json + Environment.NewLine, cancellationToken);
    }
}
