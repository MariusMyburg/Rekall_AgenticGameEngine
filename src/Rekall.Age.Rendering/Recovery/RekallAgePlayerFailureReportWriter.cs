using Rekall.Age.Core.Diagnostics;

namespace Rekall.Age.Rendering.Recovery;

public sealed record RekallAgePlayerFailureReportContext(
    string Component,
    string Backend,
    string ProjectRoot,
    string SceneName);

public sealed class RekallAgePlayerFailureReportWriter(
    RekallAgeFailureReportStore store,
    RekallAgePlayerFailureReportContext context) : IRekallAgePlayerSessionEvidenceWriter
{
    private const int MaximumTextChars = 4096;
    private const int MaximumStackChars = 16384;

    public async ValueTask<string?> WriteAsync(
        RekallAgePlayerSessionEvidence evidence,
        CancellationToken cancellationToken)
    {
        var exception = evidence.Exception;
        var report = RekallAgeFailureReport.Create(
            component: Bounded(context.Component, MaximumTextChars),
            outcome: Bounded(evidence.Outcome, MaximumTextChars),
            category: Bounded(evidence.Category, MaximumTextChars),
            code: Bounded(evidence.Code, MaximumTextChars),
            recoveryMode: Bounded(evidence.RecoveryMode, MaximumTextChars),
            attempts: evidence.Attempts,
            completedFrames: evidence.CompletedFrames,
            requestedFrames: evidence.RequestedFrames,
            exceptionType: Bounded(exception.GetType().FullName ?? exception.GetType().Name, MaximumTextChars),
            exceptionMessage: Bounded(exception.Message, MaximumTextChars),
            stackExcerpt: Bounded(exception.StackTrace ?? string.Empty, MaximumStackChars),
            backend: Bounded(context.Backend, MaximumTextChars),
            projectRoot: Bounded(context.ProjectRoot, MaximumTextChars),
            sceneName: Bounded(context.SceneName, MaximumTextChars),
            limitations:
            [
                "Cold-session restart recreates the desktop player and graphics resources.",
                "Arbitrary in-memory module state is not preserved across recovery."
            ],
            nextActions: ["rekall.diagnostics.inspect_failures"]);
        return await store.WriteAsync(report, cancellationToken).ConfigureAwait(false);
    }

    private static string Bounded(string value, int maximumChars) =>
        string.IsNullOrEmpty(value) || value.Length <= maximumChars ? value ?? string.Empty : value[..maximumChars];
}
