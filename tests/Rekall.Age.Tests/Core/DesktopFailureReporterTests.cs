using Rekall.Age.Core.Diagnostics;

namespace Rekall.Age.Tests.Core;

public sealed class DesktopFailureReporterTests
{
    [Fact]
    public async Task ReporterMapsDesktopFailurePathsToBoundedReportsAndSuppressesDuplicates()
    {
        var root = TestPaths.CreateTempDirectory();
        var reporter = new RekallAgeDesktopFailureReporter(new RekallAgeFailureReportStore(root));
        var startup = new InvalidOperationException("startup");
        var dispatcher = new InvalidOperationException("dispatcher");
        var appDomain = new InvalidOperationException("domain");
        var task = new AggregateException(new InvalidOperationException("task"));

        var results = new[]
        {
            await reporter.ReportAsync(Request("studio.startup", "REKALL_STUDIO_STARTUP_FATAL", startup)),
            await reporter.ReportAsync(Request("studio.dispatcher", "REKALL_STUDIO_DISPATCHER_FATAL", dispatcher)),
            await reporter.ReportAsync(Request("studio.app-domain", "REKALL_STUDIO_APPDOMAIN_FATAL", appDomain)),
            await reporter.ReportAsync(Request("studio.unobserved-task", "REKALL_STUDIO_TASK_UNOBSERVED", task, "observed"))
        };
        var duplicate = await reporter.ReportAsync(
            Request("studio.dispatcher", "REKALL_STUDIO_DISPATCHER_FATAL", dispatcher));
        var inspection = await new RekallAgeFailureReportStore(root).ReadAsync();

        Assert.All(results, result => Assert.True(result.Written, result.Issue));
        Assert.True(duplicate.Duplicate);
        Assert.False(duplicate.Written);
        Assert.Equal(4, inspection.Reports.Count);
        Assert.Contains(inspection.Reports, report => report.Category == "studio.startup" && report.Outcome == "fatal");
        Assert.Contains(inspection.Reports, report => report.Category == "studio.dispatcher" && report.Outcome == "fatal");
        Assert.Contains(inspection.Reports, report => report.Category == "studio.app-domain" && report.Outcome == "fatal");
        Assert.Contains(inspection.Reports, report => report.Category == "studio.unobserved-task" && report.Outcome == "observed");
        Assert.All(inspection.Reports, report =>
        {
            Assert.Equal("studio", report.Component);
            Assert.Equal("none", report.RecoveryMode);
            Assert.Empty(report.ProjectRoot);
            Assert.Empty(report.SceneName);
        });
    }

    [Fact]
    public async Task ReportWriteFailureIsReturnedWithoutThrowingOrCapturingArbitraryState()
    {
        var root = TestPaths.CreateTempDirectory();
        var reporter = new RekallAgeDesktopFailureReporter(
            new RekallAgeFailureReportStore(
                root,
                new RekallAgeFailureReportStoreLimits(MaximumReportBytes: 100)));
        var exception = new Exception("bounded failure");
        exception.Data["secret"] = "must-not-serialize";

        var result = await reporter.ReportAsync(Request("studio.startup", "FAIL", exception));

        Assert.False(result.Written);
        Assert.False(result.Duplicate);
        Assert.Equal("REKALL_FAILURE_REPORT_BOUNDS_EXCEEDED", result.Code);
        Assert.DoesNotContain("must-not-serialize", result.Issue, StringComparison.Ordinal);
    }

    private static RekallAgeDesktopFailureRequest Request(
        string category,
        string code,
        Exception exception,
        string outcome = "fatal") =>
        new(
            Component: "studio",
            Outcome: outcome,
            Category: category,
            Code: code,
            Exception: exception,
            Limitations: ["No automatic restart is attempted."],
            NextActions: ["rekall.diagnostics.inspect_failures"]);
}
