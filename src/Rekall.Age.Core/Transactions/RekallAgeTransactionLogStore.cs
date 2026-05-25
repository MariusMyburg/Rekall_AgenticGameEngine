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
    IReadOnlyList<string> ChangedResources)
{
    public IReadOnlyList<RekallAgeTransactionResourceChange> ResourceChanges { get; init; } =
        Array.Empty<RekallAgeTransactionResourceChange>();
}

public sealed record RekallAgeTransactionResourceChange(
    string Path,
    string RelativePath,
    string Kind,
    bool Exists,
    long? SizeBytes);

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
            transaction.ChangedResources.ToArray())
        {
            ResourceChanges = transaction.ChangedResources
                .Select(resource => RekallAgeTransactionResourceChangeSummarizer.Summarize(projectRoot, resource))
                .ToArray()
        };
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
