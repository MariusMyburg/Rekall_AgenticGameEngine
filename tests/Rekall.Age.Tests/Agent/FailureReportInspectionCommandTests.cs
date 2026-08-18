using Rekall.Age.Agent.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Diagnostics;
using Rekall.Age.Core.Transactions;

namespace Rekall.Age.Tests.Agent;

public sealed class FailureReportInspectionCommandTests
{
    [Fact]
    public async Task CommandFiltersNewestReportsAndReturnsPathsWithoutStackFlooding()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeFailureReportStore(root);
        await store.WriteAsync(CreateReport("player.windows", "fatal", "OLD", "old stack", 1), CancellationToken.None);
        await store.WriteAsync(CreateReport("studio", "fatal", "STUDIO", "studio stack", 2), CancellationToken.None);
        await store.WriteAsync(CreateReport("player.windows", "recovered", "RECOVERED", new string('s', 2000), 3), CancellationToken.None);
        var transaction = RekallAgeTransaction.Begin("inspect failures");

        var result = await new InspectFailureReportsCommand().ExecuteAsync(
            new InspectFailureReportsRequest(root, Component: "player.windows", MaximumReports: 1),
            new RekallAgeCommandContext("agent", transaction, CancellationToken.None));
        var codeFiltered = await new InspectFailureReportsCommand().ExecuteAsync(
            new InspectFailureReportsRequest(root, Outcome: "fatal", Code: "STUDIO"),
            new RekallAgeCommandContext("agent", transaction, CancellationToken.None));

        Assert.True(result.Ok, result.Summary);
        var report = Assert.Single(result.Value.Reports);
        Assert.Equal("RECOVERED", report.Code);
        Assert.Equal("recovered", report.Outcome);
        Assert.True(File.Exists(report.Path));
        Assert.DoesNotContain("Stack", report.GetType().GetProperties().Select(property => property.Name));
        Assert.Equal("studio", Assert.Single(codeFiltered.Value.Reports).Component);
        Assert.Empty(transaction.ChangedResources);
    }

    [Fact]
    public async Task CommandReturnsMalformedIssuesAndRejectsUnboundedRequests()
    {
        var root = TestPaths.CreateTempDirectory();
        await File.WriteAllTextAsync(Path.Combine(root, "failure-bad.json"), "{");
        var context = new RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("inspect failures"),
            CancellationToken.None);

        var malformed = await new InspectFailureReportsCommand().ExecuteAsync(
            new InspectFailureReportsRequest(root),
            context);
        var unbounded = await new InspectFailureReportsCommand().ExecuteAsync(
            new InspectFailureReportsRequest(root, MaximumReports: 51),
            context);

        Assert.True(malformed.Ok, malformed.Summary);
        Assert.Contains(malformed.Value.Issues, issue => issue.Code == "REKALL_FAILURE_REPORT_MALFORMED");
        Assert.False(unbounded.Ok);
        Assert.Contains(unbounded.Errors, error => error.Code == "REKALL_DIAGNOSTICS_REQUEST_INVALID");
    }

    private static RekallAgeFailureReport CreateReport(
        string component,
        string outcome,
        string code,
        string stack,
        int hour) =>
        RekallAgeFailureReport.Create(
            component,
            outcome,
            "graphics.device-lost",
            code,
            "cold-session-restart",
            2,
            10,
            10,
            "System.Exception",
            "failure message",
            stack,
            "vulkan",
            "F:/Game",
            "Main",
            ["state not preserved"],
            ["rekall.diagnostics.inspect_failures"],
            DateTimeOffset.Parse($"2026-08-18T{hour:00}:00:00Z"));
}
