using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Compatibility;
using Rekall.Age.Core.Product;
using Rekall.Age.Project;

namespace Rekall.Age.Agent.Commands;

public sealed record InspectProjectCompatibilityRequest(
    string ProjectRoot,
    int MaximumDocuments = 512);

public sealed record RekallAgeCompatibilityDocument(
    string Path,
    string RelativePath,
    string Kind,
    string Status,
    string Code,
    int? DetectedVersion,
    int CurrentVersion,
    bool CanMigrate,
    string Message);

public sealed record RekallAgeCompatibilityBlocker(
    string Code,
    string Message,
    string Target);

public sealed record InspectProjectCompatibilityResult(
    string ProjectRoot,
    bool IsCurrent,
    bool CanMigrate,
    IReadOnlyList<RekallAgeCompatibilityDocument> Documents,
    IReadOnlyList<RekallAgeCompatibilityBlocker> Blockers,
    IReadOnlyList<string> Limitations,
    IReadOnlyList<RekallAgeSuggestedCommand> NextActions);

public sealed class InspectProjectCompatibilityCommand
    : IRekallAgeCommand<InspectProjectCompatibilityRequest, InspectProjectCompatibilityResult>
{
    public string Name => "rekall.compatibility.inspect_project";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Inspects bounded project and scene schema compatibility without changing files or executing project code.",
        typeof(InspectProjectCompatibilityRequest).FullName!,
        typeof(InspectProjectCompatibilityResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<InspectProjectCompatibilityResult>> ExecuteAsync(
        InspectProjectCompatibilityRequest request,
        RekallAgeCommandContext context)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectRoot) || request.MaximumDocuments is < 1 or > 2048)
        {
            return Invalid(request.ProjectRoot, "ProjectRoot is required and MaximumDocuments must be between 1 and 2048.");
        }

        try
        {
            var result = await RekallAgeProjectCompatibilityInspector.InspectAsync(
                request.ProjectRoot,
                request.MaximumDocuments,
                context.CancellationToken).ConfigureAwait(false);
            return RekallAgeCommandResult<InspectProjectCompatibilityResult>.Success(
                result,
                $"Inspected {result.Documents.Count} persisted document(s); {result.Blockers.Count} compatibility blocker(s) found.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            var root = SafeFullPath(request.ProjectRoot);
            var result = Empty(root);
            return RekallAgeCommandResult<InspectProjectCompatibilityResult>.Failure(
                result,
                "Project compatibility could not be inspected safely.",
                [new RekallAgeCommandError("REKALL_COMPATIBILITY_INSPECTION_FAILED", error.Message, root)]);
        }
    }

    private static RekallAgeCommandResult<InspectProjectCompatibilityResult> Invalid(string root, string message)
    {
        var result = Empty(SafeFullPath(root));
        return RekallAgeCommandResult<InspectProjectCompatibilityResult>.Failure(
            result,
            message,
            [new RekallAgeCommandError("REKALL_COMPATIBILITY_REQUEST_INVALID", message, nameof(InspectProjectCompatibilityRequest))]);
    }

    private static InspectProjectCompatibilityResult Empty(string root) =>
        new(root, IsCurrent: false, CanMigrate: false, [], [], [], []);

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

public static class RekallAgeProjectCompatibilityInspector
{
    private const string CurrentCode = "REKALL_DOCUMENT_SCHEMA_CURRENT";
    private const string LegacyCode = "REKALL_DOCUMENT_SCHEMA_LEGACY";

    public static async ValueTask<InspectProjectCompatibilityResult> InspectAsync(
        string projectRoot,
        int maximumDocuments,
        CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(projectRoot);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Project root '{root}' does not exist.");
        }

        var documents = new List<RekallAgeCompatibilityDocument>();
        var blockers = new List<RekallAgeCompatibilityBlocker>();
        if (IsReparsePoint(root))
        {
            AddBlockedPath(documents, blockers, root, ".", "project-root", "REKALL_COMPATIBILITY_REPARSE_REJECTED",
                "Project root is a reparse point and cannot be inspected as a trusted compatibility boundary.");
            return Complete(root, documents, blockers);
        }

        var manifestPath = Path.Combine(root, RekallAgeProjectStore.ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            const string message = "The project manifest is missing.";
            documents.Add(new RekallAgeCompatibilityDocument(
                manifestPath,
                RekallAgeProjectStore.ManifestFileName,
                "project",
                "missing",
                "REKALL_PROJECT_MANIFEST_MISSING",
                null,
                RekallAgeProductInfo.Current.ProjectSchemaVersion,
                CanMigrate: false,
                message));
            blockers.Add(new RekallAgeCompatibilityBlocker("REKALL_PROJECT_MANIFEST_MISSING", message, manifestPath));
        }
        else
        {
            await InspectDocumentAsync(root, manifestPath, "project", documents, blockers, cancellationToken)
                .ConfigureAwait(false);
        }

        var scenesDirectory = Path.Combine(root, "Scenes");
        if (Directory.Exists(scenesDirectory))
        {
            if (IsReparsePoint(scenesDirectory))
            {
                AddBlockedPath(documents, blockers, scenesDirectory, "Scenes", "scene-directory",
                    "REKALL_COMPATIBILITY_REPARSE_REJECTED",
                    "The Scenes directory is a reparse point and was not traversed.");
            }
            else
            {
                var scenePaths = Directory.EnumerateFiles(scenesDirectory, "*.age.scene.json", SearchOption.TopDirectoryOnly)
                    .Select(Path.GetFullPath)
                    .OrderBy(path => Relative(root, path), StringComparer.Ordinal)
                    .ToArray();
                if (scenePaths.Length + documents.Count > maximumDocuments)
                {
                    var message = $"Compatibility inspection found more than the {maximumDocuments} document limit.";
                    AddBlockedPath(documents, blockers, scenesDirectory, "Scenes", "scene-directory",
                        "REKALL_COMPATIBILITY_DOCUMENT_LIMIT_EXCEEDED", message);
                }
                else
                {
                    foreach (var scenePath in scenePaths)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await InspectDocumentAsync(root, scenePath, "scene", documents, blockers, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
            }
        }

        return Complete(root, documents, blockers);
    }

    private static async ValueTask InspectDocumentAsync(
        string root,
        string path,
        string kind,
        List<RekallAgeCompatibilityDocument> documents,
        List<RekallAgeCompatibilityBlocker> blockers,
        CancellationToken cancellationToken)
    {
        var relativePath = Relative(root, path);
        if (IsReparsePoint(path))
        {
            AddBlockedPath(documents, blockers, path, relativePath, kind, "REKALL_COMPATIBILITY_REPARSE_REJECTED",
                $"{kind} document '{relativePath}' is a reparse point and was not read.");
            return;
        }

        try
        {
            var schema = await RekallAgeDocumentSchemaProbe.ReadAsync(
                path,
                kind,
                RekallAgeProductInfo.Current.ProjectSchemaVersion,
                cancellationToken).ConfigureAwait(false);
            var status = schema.IsLegacy ? "legacy" : "current";
            var code = schema.IsLegacy ? LegacyCode : CurrentCode;
            var message = schema.IsLegacy
                ? $"{kind} document uses legacy schema {schema.DetectedVersion} and has a deterministic migration to schema {schema.CurrentVersion}."
                : $"{kind} document uses current schema {schema.CurrentVersion}.";
            documents.Add(new RekallAgeCompatibilityDocument(
                path,
                relativePath,
                kind,
                status,
                code,
                schema.DetectedVersion,
                schema.CurrentVersion,
                CanMigrate: schema.IsLegacy,
                message));
        }
        catch (RekallAgeDocumentCompatibilityException error)
        {
            var status = error.Code == "REKALL_DOCUMENT_SCHEMA_FUTURE" ? "future" : "malformed";
            documents.Add(new RekallAgeCompatibilityDocument(
                path,
                relativePath,
                kind,
                status,
                error.Code,
                error.DetectedVersion,
                error.CurrentVersion,
                CanMigrate: false,
                error.Message));
            blockers.Add(new RekallAgeCompatibilityBlocker(error.Code, error.Message, path));
        }
    }

    private static InspectProjectCompatibilityResult Complete(
        string root,
        List<RekallAgeCompatibilityDocument> documents,
        List<RekallAgeCompatibilityBlocker> blockers)
    {
        var ordered = documents
            .OrderBy(item => item.Kind == "project" ? 0 : 1)
            .ThenBy(item => item.RelativePath, StringComparer.Ordinal)
            .ToArray();
        var orderedBlockers = blockers
            .OrderBy(item => Path.GetFileName(item.Target).Equals(RekallAgeProjectStore.ManifestFileName, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(item => Relative(root, item.Target), StringComparer.Ordinal)
            .ToArray();
        var legacy = ordered.Any(item => item.Status == "legacy");
        var canMigrate = legacy && orderedBlockers.Length == 0;
        IReadOnlyList<RekallAgeSuggestedCommand> nextActions = canMigrate
            ? [new RekallAgeSuggestedCommand(
                "rekall.compatibility.migrate_project",
                new Dictionary<string, object?> { ["ProjectRoot"] = root, ["Apply"] = false })]
            : orderedBlockers.Length == 0
                ? [new RekallAgeSuggestedCommand(
                    "rekall.validation.project",
                    new Dictionary<string, object?> { ["ProjectRoot"] = root })]
                : [];
        return new InspectProjectCompatibilityResult(
            root,
            IsCurrent: ordered.Length > 0 && ordered.All(item => item.Status == "current"),
            CanMigrate: canMigrate,
            Documents: ordered,
            Blockers: orderedBlockers,
            Limitations:
            [
                "Inspection covers the engine-owned project manifest and top-level scene documents only.",
                "Package, module SDK, receipt, animation clip, and diagnostic schemas are independent compatibility contracts."
            ],
            NextActions: nextActions);
    }

    private static void AddBlockedPath(
        List<RekallAgeCompatibilityDocument> documents,
        List<RekallAgeCompatibilityBlocker> blockers,
        string path,
        string relativePath,
        string kind,
        string code,
        string message)
    {
        documents.Add(new RekallAgeCompatibilityDocument(
            path,
            relativePath.Replace('\\', '/'),
            kind,
            "malformed",
            code,
            null,
            RekallAgeProductInfo.Current.ProjectSchemaVersion,
            CanMigrate: false,
            message));
        blockers.Add(new RekallAgeCompatibilityBlocker(code, message, path));
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');
}
