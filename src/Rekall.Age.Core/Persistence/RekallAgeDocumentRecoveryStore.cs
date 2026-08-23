using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Compatibility;
using System.Text.Json;

namespace Rekall.Age.Core.Persistence;

public sealed record RekallAgeDocumentRecoveryFileStatus(
    bool Available,
    bool Valid,
    string Revision,
    int? DetectedSchemaVersion,
    string Code);

public sealed record RekallAgeDocumentRecoveryInspection(
    string DocumentPath,
    string PreviousPath,
    RekallAgeDocumentRecoveryFileStatus Primary,
    RekallAgeDocumentRecoveryFileStatus Previous,
    bool Recoverable,
    IReadOnlyList<string> NextActions);

public sealed class RekallAgeDocumentRecoveryException : RekallAgeCodedBoundaryException
{
    public RekallAgeDocumentRecoveryException(string code, string path, string message)
        : base(code, message, path)
    {
    }
}

public static class RekallAgeDocumentRecoveryStore
{
    public const int MaximumQuarantinesPerDocument = 4;

    public static string GetPreviousPath(string projectRoot, string documentPath)
    {
        var (root, document, relative) = ResolveConfined(projectRoot, documentPath);
        _ = document;
        return RekallAgeConfinedPath.Resolve(
            root,
            Path.Combine(root, ".rekall", "recovery", relative + ".previous"),
            "Recovery previous-version path");
    }

    public static string GetQuarantineDirectory(string projectRoot, string documentPath)
    {
        var (root, _, relative) = ResolveConfined(projectRoot, documentPath);
        var relativeDirectory = Path.GetDirectoryName(relative);
        return RekallAgeConfinedPath.Resolve(
            root,
            Path.Combine(root, ".rekall", "recovery", "quarantine", relativeDirectory ?? string.Empty),
            "Recovery quarantine path");
    }

    public static async ValueTask<RekallAgeDocumentRecoveryInspection> InspectAsync(
        string projectRoot,
        string documentPath,
        string documentKind,
        int currentVersion,
        Action<RekallAgeDocumentSnapshot> validate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(validate);
        var (_, document, _) = ResolveConfined(projectRoot, documentPath);
        var previous = GetPreviousPath(projectRoot, document);
        var primaryStatus = await InspectFileAsync(document, documentKind, currentVersion, validate, cancellationToken)
            .ConfigureAwait(false);
        var previousStatus = await InspectFileAsync(previous, documentKind, currentVersion, validate, cancellationToken)
            .ConfigureAwait(false);
        var recoverable = previousStatus.Valid && primaryStatus.Available;
        return new RekallAgeDocumentRecoveryInspection(
            document,
            previous,
            primaryStatus,
            previousStatus,
            recoverable,
            recoverable
                ? ["Restore the previous version using the reported primary revision as expectedRevision."]
                : ["Repair or replace the document explicitly; no validated previous version is available."]);
    }

    public static async ValueTask<string> RestorePreviousAsync(
        string projectRoot,
        string documentPath,
        string documentKind,
        int currentVersion,
        string expectedRevision,
        Action<RekallAgeDocumentSnapshot> validate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(validate);
        var (_, document, _) = ResolveConfined(projectRoot, documentPath);
        var previousPath = GetPreviousPath(projectRoot, document);
        RekallAgeDocumentSnapshot previous;
        try
        {
            previous = await RekallAgeDocumentSchemaProbe.ReadSnapshotAsync(
                previousPath, documentKind, currentVersion, cancellationToken).ConfigureAwait(false);
            validate(previous);
        }
        catch (Exception error) when (error is RekallAgeDocumentCompatibilityException or
                                            RekallAgeBoundedFileSnapshotException or
                                            FileNotFoundException or
                                            DirectoryNotFoundException or
                                            InvalidDataException or
                                            InvalidOperationException or
                                            JsonException)
        {
            throw new RekallAgeDocumentRecoveryException(
                "REKALL_DOCUMENT_RECOVERY_PREVIOUS_INVALID",
                previousPath,
                $"The previous {documentKind} document is unavailable or invalid and cannot be restored: {error.Message}");
        }

        var quarantineDirectory = GetQuarantineDirectory(projectRoot, document);
        var quarantinePath = Path.Combine(
            quarantineDirectory,
            $"{DateTime.UtcNow.Ticks:D19}.{expectedRevision}.{Path.GetFileName(document)}.corrupt");
        var revision = await RekallAgeAtomicFile.WriteAllBytesIfRevisionAsync(
            document,
            previous.File.Bytes,
            RekallAgeDocumentSchemaProbe.MaximumDocumentBytes,
            expectedRevision,
            quarantinePath,
            cancellationToken).ConfigureAwait(false);
        PruneQuarantines(quarantineDirectory, Path.GetFileName(document));
        return revision;
    }

    private static async ValueTask<RekallAgeDocumentRecoveryFileStatus> InspectFileAsync(
        string path,
        string kind,
        int currentVersion,
        Action<RekallAgeDocumentSnapshot> validate,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return new(false, false, RekallAgeDocumentRevision.Missing, null, "REKALL_DOCUMENT_RECOVERY_MISSING");
        }

        try
        {
            var snapshot = await RekallAgeDocumentSchemaProbe.ReadSnapshotAsync(path, kind, currentVersion, cancellationToken)
                .ConfigureAwait(false);
            validate(snapshot);
            return new(true, true, snapshot.File.Revision, snapshot.Schema.DetectedVersion, "REKALL_DOCUMENT_RECOVERY_VALID");
        }
        catch (RekallAgeDocumentCompatibilityException error)
        {
            var revision = await TryReadRevisionAsync(path, cancellationToken).ConfigureAwait(false);
            return new(true, false, revision, error.DetectedVersion, error.Code);
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or JsonException)
        {
            var revision = await TryReadRevisionAsync(path, cancellationToken).ConfigureAwait(false);
            return new(true, false, revision, null, "REKALL_DOCUMENT_SHAPE_INVALID");
        }
    }

    private static async ValueTask<string> TryReadRevisionAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return (await RekallAgeBoundedFileSnapshot.ReadAsync(
                path, RekallAgeDocumentSchemaProbe.MaximumDocumentBytes, cancellationToken).ConfigureAwait(false)).Revision;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return RekallAgeDocumentRevision.Missing;
        }
    }

    private static (string Root, string Document, string Relative) ResolveConfined(string projectRoot, string documentPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);
        var root = Path.GetFullPath(projectRoot);
        var document = RekallAgeConfinedPath.Resolve(root, documentPath, "Recovery document path");
        var relative = Path.GetRelativePath(root, document);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Document '{document}' must be contained by project root '{root}'.", nameof(documentPath));
        }
        return (root, document, relative);
    }

    private static void PruneQuarantines(string directory, string documentFileName)
    {
        var matches = Directory.EnumerateFiles(directory, $"*.{documentFileName}.corrupt")
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .Skip(MaximumQuarantinesPerDocument)
            .ToArray();
        foreach (var path in matches)
        {
            File.Delete(path);
        }
    }
}
