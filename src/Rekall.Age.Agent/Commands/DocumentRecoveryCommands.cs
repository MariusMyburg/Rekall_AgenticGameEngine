using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Persistence;
using Rekall.Age.Project;
using Rekall.Age.World;

namespace Rekall.Age.Agent.Commands;

public sealed record InspectDocumentRecoveryRequest(
    string ProjectRoot,
    string DocumentKind,
    string? SceneName = null);

public sealed record InspectDocumentRecoveryResult(
    string ProjectRoot,
    string DocumentKind,
    string? SceneName,
    string DocumentPath,
    string PreviousPath,
    RekallAgeDocumentRecoveryFileStatus Primary,
    RekallAgeDocumentRecoveryFileStatus Previous,
    bool Recoverable,
    IReadOnlyList<RekallAgeSuggestedCommand> NextActions);

public sealed class InspectDocumentRecoveryCommand
    : IRekallAgeCommand<InspectDocumentRecoveryRequest, InspectDocumentRecoveryResult>
{
    public string Name => "rekall.recovery.inspect_document";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Inspects one bounded project manifest or named scene and its retained previous version without changing either file.",
        typeof(InspectDocumentRecoveryRequest).FullName!,
        typeof(InspectDocumentRecoveryResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<InspectDocumentRecoveryResult>> ExecuteAsync(
        InspectDocumentRecoveryRequest request,
        RekallAgeCommandContext context)
    {
        if (!DocumentRecoveryCommandTarget.TryCreate(request.ProjectRoot, request.DocumentKind, request.SceneName, out var target, out var message))
        {
            return RekallAgeCommandResult<InspectDocumentRecoveryResult>.Failure(
                Empty(request),
                message,
                [new("REKALL_DOCUMENT_RECOVERY_REQUEST_INVALID", message, nameof(InspectDocumentRecoveryRequest))]);
        }

        try
        {
            var inspection = target.Kind == "project"
                ? await new RekallAgeProjectStore().InspectRecoveryAsync(target.ProjectRoot, context.CancellationToken).ConfigureAwait(false)
                : await new RekallAgeSceneStore().InspectRecoveryAsync(target.ProjectRoot, target.SceneName!, context.CancellationToken).ConfigureAwait(false);
            IReadOnlyList<RekallAgeSuggestedCommand> nextActions = inspection.Recoverable
                ? [new RekallAgeSuggestedCommand(
                    "rekall.recovery.restore_document",
                    new Dictionary<string, object?>
                    {
                        ["projectRoot"] = target.ProjectRoot,
                        ["documentKind"] = target.Kind,
                        ["sceneName"] = target.SceneName,
                        ["expectedRevision"] = inspection.Primary.Revision
                    })]
                : [new RekallAgeSuggestedCommand(
                    "rekall.compatibility.inspect_project",
                    new Dictionary<string, object?> { ["projectRoot"] = target.ProjectRoot })];
            var result = new InspectDocumentRecoveryResult(
                target.ProjectRoot,
                target.Kind,
                target.SceneName,
                inspection.DocumentPath,
                inspection.PreviousPath,
                inspection.Primary,
                inspection.Previous,
                inspection.Recoverable,
                nextActions);
            return RekallAgeCommandResult<InspectDocumentRecoveryResult>.Success(
                result,
                $"Inspected {target.Kind} recovery state; recoverable={inspection.Recoverable}.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return RekallAgeCommandResult<InspectDocumentRecoveryResult>.Failure(
                Empty(request),
                "Document recovery state could not be inspected safely.",
                [new("REKALL_DOCUMENT_RECOVERY_INSPECTION_FAILED", error.Message, request.ProjectRoot)]);
        }
    }

    private static InspectDocumentRecoveryResult Empty(InspectDocumentRecoveryRequest request)
    {
        var missing = new RekallAgeDocumentRecoveryFileStatus(
            false, false, RekallAgeDocumentRevision.Missing, null, "REKALL_DOCUMENT_RECOVERY_MISSING");
        return new(request.ProjectRoot, request.DocumentKind, request.SceneName, string.Empty, string.Empty, missing, missing, false, []);
    }
}

public sealed record RestoreDocumentRecoveryRequest(
    string ProjectRoot,
    string DocumentKind,
    string ExpectedRevision,
    string? SceneName = null);

public sealed record RestoreDocumentRecoveryResult(
    string ProjectRoot,
    string DocumentKind,
    string? SceneName,
    string RestoredRevision,
    string QuarantineDirectory,
    IReadOnlyList<RekallAgeSuggestedCommand> NextActions);

public sealed class RestoreDocumentRecoveryCommand
    : IRekallAgeCommand<RestoreDocumentRecoveryRequest, RestoreDocumentRecoveryResult>
{
    public string Name => "rekall.recovery.restore_document";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Explicitly restores one validated previous project or scene document at an exact current revision and quarantines the displaced bytes.",
        typeof(RestoreDocumentRecoveryRequest).FullName!,
        typeof(RestoreDocumentRecoveryResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<RestoreDocumentRecoveryResult>> ExecuteAsync(
        RestoreDocumentRecoveryRequest request,
        RekallAgeCommandContext context)
    {
        if (!DocumentRecoveryCommandTarget.TryCreate(request.ProjectRoot, request.DocumentKind, request.SceneName, out var target, out var message) ||
            string.IsNullOrWhiteSpace(request.ExpectedRevision) ||
            request.ExpectedRevision == RekallAgeDocumentRevision.Missing ||
            !RekallAgeDocumentRevision.IsValid(request.ExpectedRevision))
        {
            message = string.IsNullOrWhiteSpace(message)
                ? "ExpectedRevision must be the inspected lowercase SHA-256 revision of an existing primary document."
                : message;
            return RekallAgeCommandResult<RestoreDocumentRecoveryResult>.Failure(
                Empty(request),
                message,
                [new("REKALL_DOCUMENT_RECOVERY_REQUEST_INVALID", message, nameof(RestoreDocumentRecoveryRequest))]);
        }

        try
        {
            string restoredRevision;
            string quarantineDirectory;
            if (target.Kind == "project")
            {
                var store = new RekallAgeProjectStore();
                restoredRevision = (await store.RestorePreviousAsync(
                    target.ProjectRoot, request.ExpectedRevision, context.CancellationToken).ConfigureAwait(false)).Revision;
                quarantineDirectory = store.GetQuarantineDirectory(target.ProjectRoot);
            }
            else
            {
                var store = new RekallAgeSceneStore();
                restoredRevision = (await store.RestorePreviousAsync(
                    target.ProjectRoot, target.SceneName!, request.ExpectedRevision, context.CancellationToken).ConfigureAwait(false)).Revision;
                quarantineDirectory = store.GetQuarantineDirectory(target.ProjectRoot, target.SceneName!);
            }

            var validationTool = target.Kind == "project" ? "rekall.validation.project" : "rekall.validation.scene";
            var arguments = new Dictionary<string, object?> { ["projectRoot"] = target.ProjectRoot };
            if (target.SceneName is not null)
            {
                arguments["sceneName"] = target.SceneName;
            }
            var result = new RestoreDocumentRecoveryResult(
                target.ProjectRoot,
                target.Kind,
                target.SceneName,
                restoredRevision,
                quarantineDirectory,
                [new(validationTool, arguments)]);
            return RekallAgeCommandResult<RestoreDocumentRecoveryResult>.Success(
                result,
                $"Restored {target.Kind} document at revision '{restoredRevision}' and quarantined the displaced bytes.");
        }
        catch (RekallAgeDocumentRevisionException error)
        {
            return Failure(request, error.Code, error.Message, error.Target);
        }
        catch (RekallAgeDocumentRecoveryException error)
        {
            return Failure(request, error.Code, error.Message, error.Target);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Failure(request, "REKALL_DOCUMENT_RECOVERY_RESTORE_FAILED", error.Message, request.ProjectRoot);
        }
    }

    private static RekallAgeCommandResult<RestoreDocumentRecoveryResult> Failure(
        RestoreDocumentRecoveryRequest request,
        string code,
        string message,
        string target) =>
        RekallAgeCommandResult<RestoreDocumentRecoveryResult>.Failure(
            Empty(request),
            "The previous document was not restored.",
            [new(code, message, target, [new RekallAgeSuggestedCommand(
                "rekall.recovery.inspect_document",
                new Dictionary<string, object?>
                {
                    ["projectRoot"] = request.ProjectRoot,
                    ["documentKind"] = request.DocumentKind,
                    ["sceneName"] = request.SceneName
                })])]);

    private static RestoreDocumentRecoveryResult Empty(RestoreDocumentRecoveryRequest request) =>
        new(request.ProjectRoot, request.DocumentKind, request.SceneName, string.Empty, string.Empty, []);
}

internal sealed record DocumentRecoveryCommandTarget(string ProjectRoot, string Kind, string? SceneName)
{
    public static bool TryCreate(
        string projectRoot,
        string documentKind,
        string? sceneName,
        out DocumentRecoveryCommandTarget target,
        out string message)
    {
        target = new(projectRoot, documentKind, sceneName);
        message = string.Empty;
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            message = "ProjectRoot is required.";
            return false;
        }
        var kind = documentKind?.Trim().ToLowerInvariant();
        try
        {
            if (kind == "project" && string.IsNullOrWhiteSpace(sceneName))
            {
                target = new(Path.GetFullPath(projectRoot), kind, null);
                return true;
            }
            if (kind == "scene" && !string.IsNullOrWhiteSpace(sceneName))
            {
                var fullRoot = Path.GetFullPath(projectRoot);
                _ = new RekallAgeSceneStore().GetScenePath(fullRoot, sceneName.Trim());
                target = new(fullRoot, kind, sceneName.Trim());
                return true;
            }
        }
        catch (ArgumentException error)
        {
            message = error.Message;
            return false;
        }
        message = "DocumentKind must be 'project' without SceneName or 'scene' with one SceneName.";
        return false;
    }
}
