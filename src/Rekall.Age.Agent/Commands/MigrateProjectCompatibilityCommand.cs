using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Product;
using Rekall.Age.Project;
using Rekall.Age.World;

namespace Rekall.Age.Agent.Commands;

public sealed record MigrateProjectCompatibilityRequest(
    string ProjectRoot,
    bool Apply = false,
    int MaximumDocuments = 512);

public sealed record RekallAgeCompatibilityMigrationDocument(
    string Path,
    string RelativePath,
    string Kind,
    int FromVersion,
    int ToVersion,
    string OriginalSha256,
    string MigratedSha256);

public sealed record MigrateProjectCompatibilityResult(
    string ProjectRoot,
    bool Applied,
    bool NoOp,
    string? BackupRoot,
    IReadOnlyList<RekallAgeCompatibilityMigrationDocument> Documents,
    IReadOnlyList<RekallAgeCompatibilityBlocker> Blockers,
    IReadOnlyList<string> Limitations,
    IReadOnlyList<RekallAgeSuggestedCommand> NextActions);

public sealed class MigrateProjectCompatibilityCommand
    : IRekallAgeCommand<MigrateProjectCompatibilityRequest, MigrateProjectCompatibilityResult>
{
    private readonly RekallAgeProjectCompatibilityMigrator _migrator;

    public MigrateProjectCompatibilityCommand()
        : this(new RekallAgeProjectCompatibilityMigrator())
    {
    }

    internal MigrateProjectCompatibilityCommand(RekallAgeProjectCompatibilityMigrator migrator)
    {
        _migrator = migrator;
    }

    public string Name => "rekall.compatibility.migrate_project";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Plans or explicitly applies bounded atomic migrations for engine-owned project and scene schemas.",
        typeof(MigrateProjectCompatibilityRequest).FullName!,
        typeof(MigrateProjectCompatibilityResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<MigrateProjectCompatibilityResult>> ExecuteAsync(
        MigrateProjectCompatibilityRequest request,
        RekallAgeCommandContext context)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectRoot) || request.MaximumDocuments is < 1 or > 2048)
        {
            var invalid = Empty(SafeFullPath(request.ProjectRoot));
            return RekallAgeCommandResult<MigrateProjectCompatibilityResult>.Failure(
                invalid,
                "ProjectRoot is required and MaximumDocuments must be between 1 and 2048.",
                [new RekallAgeCommandError(
                    "REKALL_COMPATIBILITY_REQUEST_INVALID",
                    "ProjectRoot is required and MaximumDocuments must be between 1 and 2048.",
                    nameof(MigrateProjectCompatibilityRequest))]);
        }

        try
        {
            var inspection = await RekallAgeProjectCompatibilityInspector.InspectAsync(
                request.ProjectRoot,
                request.MaximumDocuments,
                context.CancellationToken).ConfigureAwait(false);
            if (inspection.Blockers.Count > 0)
            {
                var blocked = new MigrateProjectCompatibilityResult(
                    inspection.ProjectRoot,
                    Applied: false,
                    NoOp: false,
                    BackupRoot: null,
                    Documents: [],
                    Blockers: inspection.Blockers,
                    Limitations: inspection.Limitations,
                    NextActions: []);
                return RekallAgeCommandResult<MigrateProjectCompatibilityResult>.Failure(
                    blocked,
                    "Compatibility migration is blocked; no files were changed.",
                    inspection.Blockers.Select(item => new RekallAgeCommandError(item.Code, item.Message, item.Target)).ToArray());
            }

            if (request.Apply)
            {
                foreach (var document in inspection.Documents.Where(item => item.Status == "legacy"))
                {
                    context.Transaction.CaptureResourcePreimage(document.Path);
                }
            }

            var execution = await _migrator.ExecuteAsync(
                inspection,
                request.Apply,
                context.CancellationToken).ConfigureAwait(false);
            if (execution.Applied)
            {
                foreach (var document in execution.Documents)
                {
                    context.Transaction.RecordChangedResource(document.Path);
                }
            }

            var nextActions = execution.Applied || execution.NoOp
                ? new[]
                {
                    new RekallAgeSuggestedCommand(
                        "rekall.compatibility.inspect_project",
                        new Dictionary<string, object?> { ["ProjectRoot"] = inspection.ProjectRoot })
                }
                : new[]
                {
                    new RekallAgeSuggestedCommand(
                        Name,
                        new Dictionary<string, object?> { ["ProjectRoot"] = inspection.ProjectRoot, ["Apply"] = true })
                };
            var result = new MigrateProjectCompatibilityResult(
                inspection.ProjectRoot,
                execution.Applied,
                execution.NoOp,
                execution.BackupRoot,
                execution.Documents,
                [],
                inspection.Limitations,
                nextActions);
            var summary = execution.NoOp
                ? "Project and scene documents already use the current schema; no migration was needed."
                : execution.Applied
                    ? $"Migrated {execution.Documents.Count} document(s) and preserved exact backups."
                    : $"Planned {execution.Documents.Count} document migration(s); dry-run changed no files.";
            return RekallAgeCommandResult<MigrateProjectCompatibilityResult>.Success(result, summary);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or AggregateException or JsonException)
        {
            var root = SafeFullPath(request.ProjectRoot);
            return RekallAgeCommandResult<MigrateProjectCompatibilityResult>.Failure(
                Empty(root),
                "Compatibility migration failed; replaced files were rolled back where necessary.",
                [new RekallAgeCommandError("REKALL_COMPATIBILITY_MIGRATION_FAILED", error.Message, root)]);
        }
    }

    private static MigrateProjectCompatibilityResult Empty(string root) =>
        new(root, Applied: false, NoOp: false, null, [], [], [], []);

    private static string SafeFullPath(string path)
    {
        try
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFullPath(path);
        }
        catch (ArgumentException)
        {
            return path;
        }
    }
}

internal sealed class RekallAgeProjectCompatibilityMigrator
{
    private const int MaximumBackupSets = 5;
    private readonly Func<int, string, Exception?>? _beforeReplace;

    internal RekallAgeProjectCompatibilityMigrator(Func<int, string, Exception?>? beforeReplace = null)
    {
        _beforeReplace = beforeReplace;
    }

    internal async ValueTask<MigrationExecution> ExecuteAsync(
        InspectProjectCompatibilityResult inspection,
        bool apply,
        CancellationToken cancellationToken)
    {
        var legacyDocuments = inspection.Documents.Where(item => item.Status == "legacy").ToArray();
        if (legacyDocuments.Length == 0)
        {
            return new MigrationExecution(Applied: false, NoOp: true, null, []);
        }

        var plans = new List<MigrationPlan>(legacyDocuments.Length);
        foreach (var document in legacyDocuments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureFileIsNotReparse(document.Path);
            var original = await File.ReadAllBytesAsync(document.Path, cancellationToken).ConfigureAwait(false);
            var migrated = await TransformAsync(inspection.ProjectRoot, document, original, cancellationToken).ConfigureAwait(false);
            plans.Add(new MigrationPlan(
                document,
                original,
                migrated,
                ToPublic(document, original, migrated)));
        }

        var publicPlans = plans.Select(plan => plan.Public).ToArray();
        if (!apply)
        {
            return new MigrationExecution(Applied: false, NoOp: false, null, publicPlans);
        }

        var backupRoot = PrepareBackupRoot(inspection.ProjectRoot);
        var staged = new List<(MigrationPlan Plan, string TempPath)>();
        var replaced = new List<MigrationPlan>();
        try
        {
            foreach (var plan in plans)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var backupPath = Path.Combine(backupRoot, plan.Document.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                EnsureContained(backupRoot, backupPath);
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                await WriteDurableAsync(backupPath, plan.Original, createNew: true, cancellationToken).ConfigureAwait(false);

                var tempPath = plan.Document.Path + $".rekall-migrate-{Guid.NewGuid():N}.tmp";
                await WriteDurableAsync(tempPath, plan.Migrated, createNew: true, cancellationToken).ConfigureAwait(false);
                staged.Add((plan, tempPath));
            }

            for (var index = 0; index < staged.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = staged[index];
                EnsureFileIsNotReparse(item.Plan.Document.Path);
                var current = await File.ReadAllBytesAsync(item.Plan.Document.Path, cancellationToken).ConfigureAwait(false);
                if (!current.AsSpan().SequenceEqual(item.Plan.Original))
                {
                    throw new IOException($"Document '{item.Plan.Document.Path}' changed after migration preflight.");
                }

                var injected = _beforeReplace?.Invoke(index, item.Plan.Document.Path);
                if (injected is not null)
                {
                    throw injected;
                }

                EnsureFileIsNotReparse(item.Plan.Document.Path);
                File.Move(item.TempPath, item.Plan.Document.Path, overwrite: true);
                replaced.Add(item.Plan);
            }

            RetainNewestBackups(Path.GetDirectoryName(backupRoot)!, backupRoot);
            return new MigrationExecution(Applied: true, NoOp: false, backupRoot, publicPlans);
        }
        catch
        {
            var rollbackErrors = new List<Exception>();
            foreach (var plan in replaced.AsEnumerable().Reverse())
            {
                try
                {
                    var rollbackPath = plan.Document.Path + $".rekall-rollback-{Guid.NewGuid():N}.tmp";
                    await WriteDurableAsync(rollbackPath, plan.Original, createNew: true, CancellationToken.None).ConfigureAwait(false);
                    File.Move(rollbackPath, plan.Document.Path, overwrite: true);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    rollbackErrors.Add(error);
                }
            }

            if (rollbackErrors.Count > 0)
            {
                throw new AggregateException("Compatibility migration failed and rollback was incomplete.", rollbackErrors);
            }

            throw;
        }
        finally
        {
            foreach (var item in staged)
            {
                if (File.Exists(item.TempPath))
                {
                    File.Delete(item.TempPath);
                }
            }
        }
    }

    private static async ValueTask<byte[]> TransformAsync(
        string projectRoot,
        RekallAgeCompatibilityDocument document,
        byte[] original,
        CancellationToken cancellationToken)
    {
        if (document.DetectedVersion != 0)
        {
            throw new InvalidOperationException(
                $"No adjacent migration is registered for {document.Kind} schema {document.DetectedVersion}.");
        }

        if (document.Kind == "project")
        {
            var store = new RekallAgeProjectStore();
            await store.LoadAsync(projectRoot, cancellationToken).ConfigureAwait(false);
            return AddCurrentSchema(original);
        }

        if (document.Kind == "scene")
        {
            var store = new RekallAgeSceneStore();
            var sceneName = Path.GetFileName(document.Path)
                .Replace(".age.scene.json", string.Empty, StringComparison.Ordinal);
            await store.LoadAsync(projectRoot, sceneName, cancellationToken).ConfigureAwait(false);
            return AddCurrentSchema(original);
        }

        throw new InvalidOperationException($"Document kind '{document.Kind}' has no migration transform.");
    }

    private static byte[] AddCurrentSchema(byte[] original)
    {
        var source = JsonNode.Parse(original) as JsonObject
            ?? throw new InvalidOperationException("A migratable persisted document must have an object root.");
        var migrated = new JsonObject
        {
            ["schemaVersion"] = RekallAgeProductInfo.Current.ProjectSchemaVersion
        };
        foreach (var property in source)
        {
            if (!property.Key.Equals("schemaVersion", StringComparison.Ordinal))
            {
                migrated.Add(property.Key, property.Value?.DeepClone());
            }
        }

        return Encoding.UTF8.GetBytes(
            migrated.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
    }

    private static string PrepareBackupRoot(string projectRoot)
    {
        var engineRoot = Path.Combine(projectRoot, ".rekall");
        var migrationsRoot = Path.Combine(engineRoot, "migrations");
        EnsureDirectoryIsNotReparse(engineRoot);
        Directory.CreateDirectory(engineRoot);
        EnsureDirectoryIsNotReparse(engineRoot);
        EnsureDirectoryIsNotReparse(migrationsRoot);
        Directory.CreateDirectory(migrationsRoot);
        EnsureDirectoryIsNotReparse(migrationsRoot);
        var backupRoot = Path.Combine(
            migrationsRoot,
            $"migration-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(backupRoot);
        EnsureDirectoryIsNotReparse(backupRoot);
        return backupRoot;
    }

    private static void EnsureDirectoryIsNotReparse(string path)
    {
        if (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException($"Migration path '{path}' is a reparse point.");
        }
    }

    private static void EnsureFileIsNotReparse(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException($"Migration document '{path}' became a reparse point.");
        }
    }

    private static void EnsureContained(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException($"Migration path '{fullPath}' escaped '{root}'.");
        }
    }

    private static async ValueTask WriteDurableAsync(
        string path,
        byte[] bytes,
        bool createNew,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            createNew ? FileMode.CreateNew : FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private static void RetainNewestBackups(string migrationsRoot, string protectedBackupRoot)
    {
        try
        {
            var stale = Directory.EnumerateDirectories(migrationsRoot, "migration-*", SearchOption.TopDirectoryOnly)
                .OrderByDescending(path => path.Equals(protectedBackupRoot, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(path => Path.GetFileName(path), StringComparer.Ordinal)
                .Skip(MaximumBackupSets)
                .ToArray();
            foreach (var path in stale)
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
                {
                    DeleteTreeWithoutFollowingReparse(path, migrationsRoot);
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Retention failure must not turn a completed atomic migration into a false failure.
        }
    }

    private static void DeleteTreeWithoutFollowingReparse(string path, string requiredRoot)
    {
        var fullRoot = Path.GetFullPath(requiredRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException($"Backup retention target '{fullPath}' escaped '{requiredRoot}'.");
        }

        if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
        {
            Directory.Delete(fullPath);
            return;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(fullPath))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    Directory.Delete(entry);
                }
                else
                {
                    DeleteTreeWithoutFollowingReparse(entry, requiredRoot);
                }
            }
            else
            {
                File.Delete(entry);
            }
        }

        Directory.Delete(fullPath);
    }

    private static RekallAgeCompatibilityMigrationDocument ToPublic(
        RekallAgeCompatibilityDocument document,
        byte[] original,
        byte[] migrated) =>
        new(
            document.Path,
            document.RelativePath,
            document.Kind,
            document.DetectedVersion ?? 0,
            document.CurrentVersion,
            Convert.ToHexString(SHA256.HashData(original)),
            Convert.ToHexString(SHA256.HashData(migrated)));

    private sealed record MigrationPlan(
        RekallAgeCompatibilityDocument Document,
        byte[] Original,
        byte[] Migrated,
        RekallAgeCompatibilityMigrationDocument Public);

    internal sealed record MigrationExecution(
        bool Applied,
        bool NoOp,
        string? BackupRoot,
        IReadOnlyList<RekallAgeCompatibilityMigrationDocument> Documents);
}
