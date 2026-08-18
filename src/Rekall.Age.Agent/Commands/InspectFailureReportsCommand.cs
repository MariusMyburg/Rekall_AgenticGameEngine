using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Diagnostics;

namespace Rekall.Age.Agent.Commands;

public sealed record InspectFailureReportsRequest(
    string? Root = null,
    string? Component = null,
    string? Outcome = null,
    string? Code = null,
    int MaximumReports = 20);

public sealed record RekallAgeFailureReportSummary(
    string Path,
    string ReportId,
    DateTimeOffset TimestampUtc,
    string Component,
    string Outcome,
    string Category,
    string Code,
    string RecoveryMode,
    int Attempts,
    long CompletedFrames,
    long? RequestedFrames,
    string ExceptionType,
    string ExceptionMessage,
    string Backend,
    string ProjectRoot,
    string SceneName,
    IReadOnlyList<string> Limitations,
    IReadOnlyList<string> NextActions);

public sealed record InspectFailureReportsResult(
    string Root,
    IReadOnlyList<RekallAgeFailureReportSummary> Reports,
    IReadOnlyList<RekallAgeFailureReportIssue> Issues,
    IReadOnlyList<RekallAgeSuggestedCommand> NextActions);

public sealed class InspectFailureReportsCommand
    : IRekallAgeCommand<InspectFailureReportsRequest, InspectFailureReportsResult>
{
    public string Name => "rekall.diagnostics.inspect_failures";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Reads bounded desktop failure and recovery evidence without changing projects or executing game code.",
        typeof(InspectFailureReportsRequest).FullName!,
        typeof(InspectFailureReportsResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<InspectFailureReportsResult>> ExecuteAsync(
        InspectFailureReportsRequest request,
        RekallAgeCommandContext context)
    {
        if (request.MaximumReports is < 1 or > 50)
        {
            return Invalid(request.Root, "MaximumReports must be between 1 and 50.");
        }

        try
        {
            var store = new RekallAgeFailureReportStore(request.Root);
            var inspection = await store.ReadAsync(context.CancellationToken).ConfigureAwait(false);
            var reports = inspection.Reports
                .Where(report => Matches(report.Component, request.Component))
                .Where(report => Matches(report.Outcome, request.Outcome))
                .Where(report => Matches(report.Code, request.Code))
                .Take(request.MaximumReports)
                .Select(report => ToSummary(
                    report,
                    inspection.ReportPaths?.GetValueOrDefault(report.ReportId) ?? string.Empty))
                .ToArray();
            var nextActions = reports.SelectMany(report => report.NextActions)
                .Distinct(StringComparer.Ordinal)
                .Select(tool => new RekallAgeSuggestedCommand(tool, new Dictionary<string, object?>()))
                .ToArray();
            var result = new InspectFailureReportsResult(store.Root, reports, inspection.Issues, nextActions);
            return RekallAgeCommandResult<InspectFailureReportsResult>.Success(
                result,
                $"Inspected {reports.Length} bounded failure report(s); {inspection.Issues.Count} file issue(s) were isolated.");
        }
        catch (RekallAgeFailureReportStoreException exception)
        {
            var result = new InspectFailureReportsResult(
                request.Root ?? string.Empty,
                [],
                [new RekallAgeFailureReportIssue(exception.Code, exception.Message, exception.Target)],
                []);
            return RekallAgeCommandResult<InspectFailureReportsResult>.Failure(
                result,
                "Failure reports could not be inspected safely.",
                [new RekallAgeCommandError(exception.Code, exception.Message, exception.Target)]);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            var result = new InspectFailureReportsResult(request.Root ?? string.Empty, [], [], []);
            return RekallAgeCommandResult<InspectFailureReportsResult>.Failure(
                result,
                "Failure reports could not be inspected.",
                [new RekallAgeCommandError("REKALL_DIAGNOSTICS_STORE_UNAVAILABLE", exception.Message, request.Root)]);
        }
    }

    private static bool Matches(string actual, string? requested) =>
        string.IsNullOrWhiteSpace(requested) || actual.Equals(requested, StringComparison.OrdinalIgnoreCase);

    private static RekallAgeFailureReportSummary ToSummary(RekallAgeFailureReport report, string path) =>
        new(
            path,
            report.ReportId,
            report.TimestampUtc,
            report.Component,
            report.Outcome,
            report.Category,
            report.Code,
            report.RecoveryMode,
            report.Attempts,
            report.CompletedFrames,
            report.RequestedFrames,
            report.ExceptionType,
            report.ExceptionMessage,
            report.Backend,
            report.ProjectRoot,
            report.SceneName,
            report.Limitations,
            report.NextActions);

    private static RekallAgeCommandResult<InspectFailureReportsResult> Invalid(string? root, string message)
    {
        var result = new InspectFailureReportsResult(root ?? string.Empty, [], [], []);
        return RekallAgeCommandResult<InspectFailureReportsResult>.Failure(
            result,
            message,
            [new RekallAgeCommandError("REKALL_DIAGNOSTICS_REQUEST_INVALID", message, nameof(InspectFailureReportsRequest.MaximumReports))]);
    }
}
