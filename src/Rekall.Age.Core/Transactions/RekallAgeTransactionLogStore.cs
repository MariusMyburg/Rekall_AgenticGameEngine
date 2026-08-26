using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rekall.Age.Core.Persistence;

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
    IReadOnlyList<string> ChangedResources)
{
    public IReadOnlyList<RekallAgeTransactionResourceChange> ResourceChanges { get; init; } =
        Array.Empty<RekallAgeTransactionResourceChange>();

    public IReadOnlyList<RekallAgeTransactionResourcePreimageEntry> ResourcePreimages { get; init; } =
        Array.Empty<RekallAgeTransactionResourcePreimageEntry>();

    public IReadOnlyList<RekallAgeTransactionResourceDeltaEntry> ResourceDeltas { get; init; } =
        Array.Empty<RekallAgeTransactionResourceDeltaEntry>();
}

public sealed record RekallAgeTransactionResourceChange(
    string Path,
    string RelativePath,
    string Kind,
    bool Exists,
    long? SizeBytes);

public sealed record RekallAgeTransactionResourcePreimageEntry(
    string Path,
    string RelativePath,
    bool ExistedBefore,
    string? SnapshotPath,
    long? SizeBytes,
    string? Sha256);

public sealed class RekallAgeTransactionLogStore
{
    private readonly long _maximumLogBytes;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = RekallAgePersistedJson.MaximumDocumentDepth
    };

    public RekallAgeTransactionLogStore(long maximumLogBytes = RekallAgePersistedJson.MaximumDocumentBytes)
    {
        if (maximumLogBytes < 1_024 || maximumLogBytes > RekallAgePersistedJson.MaximumDocumentBytes)
            throw new ArgumentOutOfRangeException(nameof(maximumLogBytes),
                $"Transaction journal budget must be from 1024 through {RekallAgePersistedJson.MaximumDocumentBytes} bytes.");
        _maximumLogBytes = maximumLogBytes;
    }

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

        return (await LoadVersionedAsync(path, _maximumLogBytes, cancellationToken).ConfigureAwait(false)).Value;
    }

    public async ValueTask AppendAsync(
        string projectRoot,
        RekallAgeTransaction transaction,
        string actor,
        CancellationToken cancellationToken)
    {
        var resourcePreimages = await PersistPreimagesAsync(projectRoot, transaction, cancellationToken);
        var resourceDeltas = await BuildResourceDeltasAsync(projectRoot, transaction, cancellationToken);
        var entry = new RekallAgeTransactionLogEntry(
            transaction.Id,
            transaction.Name,
            actor,
            transaction.StartedAtUtc,
            transaction.ChangedResources.ToArray())
        {
            ResourceChanges = transaction.ChangedResources
                .Select(resource => RekallAgeTransactionResourceChangeSummarizer.Summarize(projectRoot, resource))
                .ToArray(),
            ResourcePreimages = resourcePreimages,
            ResourceDeltas = resourceDeltas
        };
        var path = GetPath(projectRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        const int maximumAttempts = 64;
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var existing = await LoadVersionedAsync(path, _maximumLogBytes, cancellationToken).ConfigureAwait(false);
            var json = SerializeBounded(existing.Value, entry);
            try
            {
                await RekallAgeAtomicFile.WriteAllTextIfRevisionAsync(
                    path,
                    json,
                    _maximumLogBytes,
                    existing.Revision,
                    cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (RekallAgeDocumentRevisionException error) when (
                error.Code == "REKALL_DOCUMENT_REVISION_CONFLICT" &&
                attempt < maximumAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(attempt, 4)), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private static async ValueTask<RekallAgeVersionedDocument<RekallAgeTransactionLogDocument>> LoadVersionedAsync(
        string path,
        long maximumLogBytes,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return new RekallAgeVersionedDocument<RekallAgeTransactionLogDocument>(
                RekallAgeTransactionLogDocument.Empty,
                RekallAgeDocumentRevision.Missing);
        }

        var snapshot = await RekallAgeBoundedFileSnapshot.ReadAsync(
            path,
            maximumLogBytes,
            cancellationToken).ConfigureAwait(false);
        var document = JsonSerializer.Deserialize<RekallAgeTransactionLogDocument>(snapshot.Bytes, JsonOptions)
            ?? throw new InvalidOperationException($"Document '{path}' could not be deserialized as a transaction log.");
        return new RekallAgeVersionedDocument<RekallAgeTransactionLogDocument>(document, snapshot.Revision);
    }

    private string SerializeBounded(
        RekallAgeTransactionLogDocument existing,
        RekallAgeTransactionLogEntry entry)
    {
        var history = existing.Transactions
            .Where(item => !item.Id.Equals(entry.Id, StringComparison.Ordinal))
            .OrderByDescending(item => item.StartedAtUtc)
            .ToArray();
        var current = entry;
        var currentOnly = Serialize([current]);
        if (Encoding.UTF8.GetByteCount(currentOnly) > _maximumLogBytes && current.ResourceDeltas.Count > 0)
        {
            current = current with { ResourceDeltas = Array.Empty<RekallAgeTransactionResourceDeltaEntry>() };
            currentOnly = Serialize([current]);
        }
        if (Encoding.UTF8.GetByteCount(currentOnly) > _maximumLogBytes)
            throw new InvalidDataException(
                $"Transaction journal entry is {Encoding.UTF8.GetByteCount(currentOnly)} bytes; the journal budget is {_maximumLogBytes} bytes.");

        var low = 0;
        var high = history.Length;
        var best = currentOnly;
        while (low <= high)
        {
            var count = low + (high - low) / 2;
            var candidate = Serialize(
                history.Take(count).Append(current).OrderByDescending(item => item.StartedAtUtc).ToArray());
            if (Encoding.UTF8.GetByteCount(candidate) <= _maximumLogBytes)
            {
                best = candidate;
                low = count + 1;
            }
            else
            {
                high = count - 1;
            }
        }
        return best;

        static string Serialize(IReadOnlyList<RekallAgeTransactionLogEntry> transactions) =>
            JsonSerializer.Serialize(new RekallAgeTransactionLogDocument(transactions), JsonOptions) + Environment.NewLine;
    }

    private static async ValueTask<IReadOnlyList<RekallAgeTransactionResourcePreimageEntry>> PersistPreimagesAsync(
        string projectRoot,
        RekallAgeTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (transaction.ResourcePreimages.Count == 0)
        {
            return Array.Empty<RekallAgeTransactionResourcePreimageEntry>();
        }

        var snapshotsDirectory = Path.Combine(projectRoot, "Transactions", "Snapshots", transaction.Id);
        Directory.CreateDirectory(snapshotsDirectory);
        var entries = new List<RekallAgeTransactionResourcePreimageEntry>();
        for (var index = 0; index < transaction.ResourcePreimages.Count; index++)
        {
            var preimage = transaction.ResourcePreimages[index];
            var fullPath = Path.GetFullPath(preimage.Resource);
            var relativePath = GetRelativePath(projectRoot, fullPath);
            string? snapshotPath = null;
            string? sha256 = null;
            long? sizeBytes = null;
            if (preimage.ExistedBefore)
            {
                sha256 = Convert.ToHexString(SHA256.HashData(preimage.Content)).ToLowerInvariant();
                sizeBytes = preimage.Content.LongLength;
                var snapshotFileName = $"{index:D4}-{SanitizeFileName(Path.GetFileName(preimage.Resource))}-{sha256[..12]}.preimage";
                var snapshotFullPath = Path.Combine(snapshotsDirectory, snapshotFileName);
                await File.WriteAllBytesAsync(snapshotFullPath, preimage.Content, cancellationToken);
                snapshotPath = Path.GetRelativePath(projectRoot, snapshotFullPath);
            }

            entries.Add(new RekallAgeTransactionResourcePreimageEntry(
                fullPath,
                relativePath,
                preimage.ExistedBefore,
                snapshotPath,
                sizeBytes,
                sha256));
        }

        return entries;
    }

    private static async ValueTask<IReadOnlyList<RekallAgeTransactionResourceDeltaEntry>> BuildResourceDeltasAsync(
        string projectRoot,
        RekallAgeTransaction transaction,
        CancellationToken cancellationToken)
    {
        var entries = new List<RekallAgeTransactionResourceDeltaEntry>();
        foreach (var preimage in transaction.ResourcePreimages.Where(item => item.ExistedBefore))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(preimage.Resource)) continue;
            var after = await RekallAgeBoundedFileSnapshot.ReadAsync(
                preimage.Resource,
                RekallAgePersistedJson.MaximumDocumentBytes,
                cancellationToken).ConfigureAwait(false);
            var fullPath = Path.GetFullPath(preimage.Resource);
            var delta = RekallAgeReversibleJsonDelta.Create(
                fullPath,
                GetRelativePath(projectRoot, fullPath),
                preimage.Content,
                after.Bytes);
            if (delta is not null) entries.Add(delta);
        }
        return entries;
    }

    private static string GetRelativePath(string projectRoot, string fullPath)
    {
        var projectFullPath = Path.GetFullPath(projectRoot);
        var normalizedRoot = projectFullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            ? Path.GetRelativePath(projectFullPath, fullPath)
            : fullPath;
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = fileName.Select(character => invalid.Contains(character) ? '_' : character).ToArray();
        return new string(chars);
    }
}

public static class RekallAgeTransactionResourceChangeSummarizer
{
    public static IReadOnlyList<RekallAgeTransactionResourceChange> Summarize(
        string projectRoot,
        IReadOnlyList<string> resources)
    {
        return resources.Select(resource => Summarize(projectRoot, resource)).ToArray();
    }

    public static RekallAgeTransactionResourceChange Summarize(string projectRoot, string resource)
    {
        try
        {
            var fullPath = Path.GetFullPath(Path.IsPathRooted(resource)
                ? resource
                : Path.Combine(projectRoot, resource));
            var projectFullPath = Path.GetFullPath(projectRoot);
            var relativePath = IsUnderProjectRoot(projectFullPath, fullPath)
                ? Path.GetRelativePath(projectFullPath, fullPath)
                : fullPath;

            if (File.Exists(fullPath))
            {
                var file = new FileInfo(fullPath);
                return new RekallAgeTransactionResourceChange(
                    fullPath,
                    relativePath,
                    GetResourceKind(fullPath, isDirectory: false),
                    Exists: true,
                    file.Length);
            }

            if (Directory.Exists(fullPath))
            {
                return new RekallAgeTransactionResourceChange(
                    fullPath,
                    relativePath,
                    GetResourceKind(fullPath, isDirectory: true),
                    Exists: true,
                    SizeBytes: null);
            }

            return new RekallAgeTransactionResourceChange(
                fullPath,
                relativePath,
                GetResourceKind(fullPath, isDirectory: false),
                Exists: false,
                SizeBytes: null);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return new RekallAgeTransactionResourceChange(
                resource,
                resource,
                "resource",
                Exists: false,
                SizeBytes: null);
        }
    }

    private static bool IsUnderProjectRoot(string projectRoot, string fullPath)
    {
        var normalizedRoot = projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetResourceKind(string path, bool isDirectory)
    {
        if (isDirectory)
        {
            return "directory";
        }

        var fileName = System.IO.Path.GetFileName(path);
        if (fileName.Equals("rekall.project.json", StringComparison.OrdinalIgnoreCase))
        {
            return "project-manifest";
        }

        if (fileName.Equals("transactions.age.json", StringComparison.OrdinalIgnoreCase))
        {
            return "transaction-log";
        }

        if (fileName.Equals("assets.age.catalog.json", StringComparison.OrdinalIgnoreCase))
        {
            return "asset-catalog";
        }

        if (fileName.EndsWith(".age.scene.json", StringComparison.OrdinalIgnoreCase))
        {
            return "scene";
        }

        return System.IO.Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".cs" => "module-source",
            ".csproj" => "module-project",
            ".png" => "image",
            ".zip" => "package",
            ".json" => "json-file",
            _ => "file"
        };
    }
}
