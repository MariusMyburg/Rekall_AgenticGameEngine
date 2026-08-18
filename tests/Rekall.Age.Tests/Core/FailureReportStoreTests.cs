using Rekall.Age.Core.Diagnostics;

namespace Rekall.Age.Tests.Core;

public sealed class FailureReportStoreTests
{
    [Fact]
    public async Task WritesAtomicSchemaOneReportAndReadsNewestFirst()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeFailureReportStore(root);
        var older = CreateReport("older", DateTimeOffset.Parse("2026-08-18T01:00:00Z"));
        var newer = CreateReport("newer", DateTimeOffset.Parse("2026-08-18T02:00:00Z"));

        var olderPath = await store.WriteAsync(older, CancellationToken.None);
        var newerPath = await store.WriteAsync(newer, CancellationToken.None);
        var inspection = await store.ReadAsync(CancellationToken.None);

        Assert.True(File.Exists(olderPath));
        Assert.True(File.Exists(newerPath));
        Assert.Empty(Directory.EnumerateFiles(root, "*.tmp-*", SearchOption.TopDirectoryOnly));
        Assert.Equal(["newer", "older"], inspection.Reports.Select(report => report.Code));
        Assert.All(inspection.Reports, report => Assert.Equal(1, report.SchemaVersion));
        Assert.Empty(inspection.Issues);
    }

    [Fact]
    public async Task BoundedReadReturnsMalformedIssueAndRetentionKeepsNewestReports()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeFailureReportStore(
            root,
            new RekallAgeFailureReportStoreLimits(MaximumReports: 2, MaximumEntries: 8));
        for (var index = 0; index < 3; index++)
        {
            await store.WriteAsync(
                CreateReport($"report-{index}", DateTimeOffset.Parse("2026-08-18T01:00:00Z").AddMinutes(index)),
                CancellationToken.None);
        }
        await File.WriteAllTextAsync(Path.Combine(root, "failure-malformed.json"), "{");

        var inspection = await store.ReadAsync(CancellationToken.None);

        Assert.Equal(["report-2", "report-1"], inspection.Reports.Select(report => report.Code));
        Assert.Contains(inspection.Issues, issue => issue.Code == "REKALL_FAILURE_REPORT_MALFORMED");
        Assert.Equal(3, Directory.EnumerateFiles(root, "failure-*.json", SearchOption.TopDirectoryOnly).Count());
    }

    [Fact]
    public async Task ConcurrentWritesProduceUniqueCompleteReports()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeFailureReportStore(root);

        await Task.WhenAll(Enumerable.Range(0, 12).Select(index =>
            store.WriteAsync(CreateReport($"concurrent-{index}", DateTimeOffset.UtcNow), CancellationToken.None).AsTask()));
        var inspection = await store.ReadAsync(CancellationToken.None);

        Assert.Equal(12, inspection.Reports.Count);
        Assert.Equal(12, inspection.Reports.Select(report => report.ReportId).Distinct(StringComparer.Ordinal).Count());
        Assert.Empty(inspection.Issues);
    }

    [Fact]
    public async Task ReparseRootAndOversizedReportFailClosedWithoutTemporaryFile()
    {
        var root = TestPaths.CreateTempDirectory();
        var reparseStore = new RekallAgeFailureReportStore(
            root,
            readAttributes: path => Path.GetFullPath(path).Equals(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)
                ? FileAttributes.Directory | FileAttributes.ReparsePoint
                : File.GetAttributes(path));
        var boundedStore = new RekallAgeFailureReportStore(
            root,
            new RekallAgeFailureReportStoreLimits(MaximumReportBytes: 100));

        var reparse = await Assert.ThrowsAsync<RekallAgeFailureReportStoreException>(async () =>
            await reparseStore.WriteAsync(CreateReport("reparse", DateTimeOffset.UtcNow), CancellationToken.None));
        var oversized = await Assert.ThrowsAsync<RekallAgeFailureReportStoreException>(async () =>
            await boundedStore.WriteAsync(CreateReport("oversized", DateTimeOffset.UtcNow), CancellationToken.None));

        Assert.Equal("REKALL_FAILURE_REPORT_ROOT_REPARSE_POINT", reparse.Code);
        Assert.Equal("REKALL_FAILURE_REPORT_BOUNDS_EXCEEDED", oversized.Code);
        Assert.Empty(Directory.EnumerateFiles(root, "*.tmp-*", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void ReportContractContainsOnlyExplicitBoundedFacts()
    {
        var properties = typeof(RekallAgeFailureReport).GetProperties().Select(property => property.Name).ToArray();

        Assert.DoesNotContain(properties, name => name.Contains("Environment", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, name => name.Contains("Data", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, name => name.Contains("Source", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("ExceptionMessage", properties);
        Assert.Contains("StackExcerpt", properties);
    }

    private static RekallAgeFailureReport CreateReport(string code, DateTimeOffset timestamp) =>
        RekallAgeFailureReport.Create(
            component: "player.windows",
            outcome: "fatal",
            category: "runtime.unhandled",
            code,
            recoveryMode: "none",
            attempts: 1,
            completedFrames: 3,
            requestedFrames: 10,
            exceptionType: "System.InvalidOperationException",
            exceptionMessage: "bounded test failure",
            stackExcerpt: "at Test.Run()",
            backend: "vulkan",
            projectRoot: "F:/Game",
            sceneName: "Main",
            limitations: ["test limitation"],
            nextActions: ["rekall.diagnostics.inspect_failures"],
            timestampUtc: timestamp);
}
