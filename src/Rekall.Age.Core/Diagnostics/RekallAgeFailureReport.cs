using System.Reflection;

namespace Rekall.Age.Core.Diagnostics;

public sealed record RekallAgeFailureReport(
    int SchemaVersion,
    string ReportId,
    DateTimeOffset TimestampUtc,
    string ProductVersion,
    string ProductChannel,
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
    string StackExcerpt,
    string Backend,
    string ProjectRoot,
    string SceneName,
    IReadOnlyList<string> Limitations,
    IReadOnlyList<string> NextActions)
{
    public const int CurrentSchemaVersion = 1;

    public static RekallAgeFailureReport Create(
        string component,
        string outcome,
        string category,
        string code,
        string recoveryMode,
        int attempts,
        long completedFrames,
        long? requestedFrames,
        string exceptionType,
        string exceptionMessage,
        string stackExcerpt,
        string backend,
        string projectRoot,
        string sceneName,
        IReadOnlyList<string>? limitations = null,
        IReadOnlyList<string>? nextActions = null,
        DateTimeOffset? timestampUtc = null)
    {
        var assembly = typeof(RekallAgeFailureReport).Assembly;
        var productVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? assembly.GetName().Version?.ToString() ?? "unknown";

        return new RekallAgeFailureReport(
            CurrentSchemaVersion,
            Guid.NewGuid().ToString("N"),
            timestampUtc ?? DateTimeOffset.UtcNow,
            productVersion,
            "production",
            component,
            outcome,
            category,
            code,
            recoveryMode,
            attempts,
            completedFrames,
            requestedFrames,
            exceptionType,
            exceptionMessage,
            stackExcerpt,
            backend,
            projectRoot,
            sceneName,
            limitations?.ToArray() ?? [],
            nextActions?.ToArray() ?? []);
    }
}

public sealed record RekallAgeFailureReportStoreLimits(
    int MaximumReports = 50,
    int MaximumEntries = 256,
    int MaximumReportBytes = 1024 * 1024,
    int MaximumStringChars = 4096,
    int MaximumStackChars = 16384);

public sealed record RekallAgeFailureReportIssue(string Code, string Message, string Target);

public sealed record RekallAgeFailureReportInspection(
    IReadOnlyList<RekallAgeFailureReport> Reports,
    IReadOnlyList<RekallAgeFailureReportIssue> Issues);

public sealed class RekallAgeFailureReportStoreException : Exception
{
    public RekallAgeFailureReportStoreException(string code, string message, string target, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        Target = target;
    }

    public string Code { get; }

    public string Target { get; }
}
