using System.Runtime.CompilerServices;

namespace Rekall.Age.Core.Diagnostics;

public sealed record RekallAgeDesktopFailureRequest(
    string Component,
    string Outcome,
    string Category,
    string Code,
    Exception Exception,
    IReadOnlyList<string>? Limitations = null,
    IReadOnlyList<string>? NextActions = null,
    string Backend = "desktop",
    string ProjectRoot = "",
    string SceneName = "");

public sealed record RekallAgeDesktopFailureResult(
    bool Written,
    bool Duplicate,
    string? Path,
    string Code,
    string Issue);

public sealed class RekallAgeDesktopFailureReporter
{
    private const int MaximumTextChars = 4096;
    private const int MaximumStackChars = 16384;
    private readonly RekallAgeFailureReportStore _store;
    private readonly ConditionalWeakTable<Exception, object> _reported = new();
    private readonly object _reportedGate = new();

    public RekallAgeDesktopFailureReporter(RekallAgeFailureReportStore? store = null)
    {
        _store = store ?? new RekallAgeFailureReportStore();
    }

    public async ValueTask<RekallAgeDesktopFailureResult> ReportAsync(
        RekallAgeDesktopFailureRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Exception);
        lock (_reportedGate)
        {
            if (_reported.TryGetValue(request.Exception, out _))
            {
                return new RekallAgeDesktopFailureResult(
                    false,
                    true,
                    null,
                    request.Code,
                    "The same exception instance was already reported.");
            }

            _reported.Add(request.Exception, new object());
        }

        try
        {
            var exception = request.Exception;
            var report = RekallAgeFailureReport.Create(
                component: Bounded(request.Component, MaximumTextChars),
                outcome: Bounded(request.Outcome, MaximumTextChars),
                category: Bounded(request.Category, MaximumTextChars),
                code: Bounded(request.Code, MaximumTextChars),
                recoveryMode: "none",
                attempts: 1,
                completedFrames: 0,
                requestedFrames: null,
                exceptionType: Bounded(exception.GetType().FullName ?? exception.GetType().Name, MaximumTextChars),
                exceptionMessage: Bounded(exception.Message, MaximumTextChars),
                stackExcerpt: Bounded(exception.StackTrace ?? string.Empty, MaximumStackChars),
                backend: Bounded(request.Backend, MaximumTextChars),
                projectRoot: Bounded(request.ProjectRoot, MaximumTextChars),
                sceneName: Bounded(request.SceneName, MaximumTextChars),
                limitations: Bounded(request.Limitations),
                nextActions: Bounded(request.NextActions));
            var path = await _store.WriteAsync(report, cancellationToken).ConfigureAwait(false);
            return new RekallAgeDesktopFailureResult(true, false, path, request.Code, string.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            RemoveReported(request.Exception);
            throw;
        }
        catch (RekallAgeFailureReportStoreException exception)
        {
            RemoveReported(request.Exception);
            return new RekallAgeDesktopFailureResult(false, false, null, exception.Code, exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            RemoveReported(request.Exception);
            return new RekallAgeDesktopFailureResult(
                false,
                false,
                null,
                "REKALL_FAILURE_REPORT_WRITE_FAILED",
                exception.Message);
        }
    }

    private void RemoveReported(Exception exception)
    {
        lock (_reportedGate)
        {
            _reported.Remove(exception);
        }
    }

    private static string Bounded(string? value, int maximumChars)
    {
        value ??= string.Empty;
        return value.Length <= maximumChars ? value : value[..maximumChars];
    }

    private static IReadOnlyList<string> Bounded(IReadOnlyList<string>? values) =>
        values?.Take(64).Select(value => Bounded(value, MaximumTextChars)).ToArray() ?? [];
}
