using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace Rekall.Age.Core.Diagnostics;

public sealed class RekallAgeFailureReportStore
{
    public const string DiagnosticsDirectoryVariable = "REKALL_AGE_DIAGNOSTICS_DIR";

    private const string MalformedCode = "REKALL_FAILURE_REPORT_MALFORMED";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> RootLocks =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _root;
    private readonly RekallAgeFailureReportStoreLimits _limits;
    private readonly Func<string, FileAttributes> _readAttributes;
    private readonly SemaphoreSlim _rootLock;

    public RekallAgeFailureReportStore(
        string? root = null,
        RekallAgeFailureReportStoreLimits? limits = null,
        Func<string, FileAttributes>? readAttributes = null)
    {
        _root = Path.GetFullPath(root ?? ResolveDefaultRoot());
        _limits = limits ?? new RekallAgeFailureReportStoreLimits();
        _readAttributes = readAttributes ?? File.GetAttributes;
        ValidateLimits(_limits);
        _rootLock = RootLocks.GetOrAdd(_root, static _ => new SemaphoreSlim(1, 1));
    }

    public string Root => _root;

    public async ValueTask<string> WriteAsync(
        RekallAgeFailureReport report,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        await _rootLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? temporaryPath = null;
        try
        {
            EnsureSafeRoot();
            ValidateReport(report);
            var json = JsonSerializer.Serialize(report, JsonOptions);
            if (Encoding.UTF8.GetByteCount(json) > _limits.MaximumReportBytes)
            {
                throw Failure(
                    "REKALL_FAILURE_REPORT_BOUNDS_EXCEEDED",
                    $"Failure report exceeds the {_limits.MaximumReportBytes}-byte limit.",
                    _root);
            }

            var timestamp = report.TimestampUtc.UtcDateTime.ToString("yyyyMMdd'T'HHmmssfffffff'Z'");
            var destinationPath = Path.Combine(_root, $"failure-{timestamp}-{report.ReportId}.json");
            temporaryPath = Path.Combine(_root, $".{Path.GetFileName(destinationPath)}.tmp-{Guid.NewGuid():N}");
            await File.WriteAllTextAsync(temporaryPath, json, new UTF8Encoding(false), cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporaryPath, destinationPath, overwrite: false);
            temporaryPath = null;
            EnforceRetention();
            return destinationPath;
        }
        catch (RekallAgeFailureReportStoreException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw Failure(
                "REKALL_FAILURE_REPORT_WRITE_FAILED",
                "The failure report could not be written atomically.",
                _root,
                exception);
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            _rootLock.Release();
        }
    }

    public async ValueTask<RekallAgeFailureReportInspection> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        await _rootLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!Directory.Exists(_root))
            {
                return new RekallAgeFailureReportInspection([], []);
            }

            RejectReparsePoint(_root, "REKALL_FAILURE_REPORT_ROOT_REPARSE_POINT");
            var reports = new List<RekallAgeFailureReport>();
            var issues = new List<RekallAgeFailureReportIssue>();
            var reportPaths = new Dictionary<string, string>(StringComparer.Ordinal);
            var paths = Directory.EnumerateFiles(_root, "failure-*.json", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Take(_limits.MaximumEntries)
                .ToArray();

            foreach (var path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    RejectReparsePoint(path, "REKALL_FAILURE_REPORT_FILE_REPARSE_POINT");
                    if (new FileInfo(path).Length > _limits.MaximumReportBytes)
                    {
                        issues.Add(new RekallAgeFailureReportIssue(
                            "REKALL_FAILURE_REPORT_BOUNDS_EXCEEDED",
                            "Failure report exceeds the configured size limit.",
                            path));
                        continue;
                    }

                    await using var stream = new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        bufferSize: 16 * 1024,
                        FileOptions.Asynchronous | FileOptions.SequentialScan);
                    var report = await JsonSerializer.DeserializeAsync<RekallAgeFailureReport>(
                        stream,
                        JsonOptions,
                        cancellationToken).ConfigureAwait(false);
                    if (report is null)
                    {
                        throw new JsonException("The report was empty.");
                    }

                    ValidateReport(report);
                    reports.Add(report);
                    reportPaths[report.ReportId] = path;
                }
                catch (Exception exception) when (
                    exception is JsonException or IOException or UnauthorizedAccessException or
                    RekallAgeFailureReportStoreException)
                {
                    issues.Add(new RekallAgeFailureReportIssue(
                        MalformedCode,
                        "Failure report was rejected: " + exception.Message,
                        path));
                }
            }

            return new RekallAgeFailureReportInspection(
                reports.OrderByDescending(report => report.TimestampUtc).Take(_limits.MaximumReports).ToArray(),
                issues,
                reportPaths);
        }
        finally
        {
            _rootLock.Release();
        }
    }

    private static string ResolveDefaultRoot()
    {
        var configured = Environment.GetEnvironmentVariable(DiagnosticsDirectoryVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Rekall AGE",
            "Diagnostics");
    }

    private void EnsureSafeRoot()
    {
        Directory.CreateDirectory(_root);
        RejectReparsePoint(_root, "REKALL_FAILURE_REPORT_ROOT_REPARSE_POINT");
    }

    private void RejectReparsePoint(string path, string code)
    {
        if ((_readAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw Failure(code, "Diagnostic report paths must not be reparse points.", path);
        }
    }

    private void EnforceRetention()
    {
        var paths = Directory.EnumerateFiles(_root, "failure-*.json", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.Name, StringComparer.Ordinal)
            .ToArray();
        foreach (var file in paths.Skip(_limits.MaximumReports))
        {
            RejectReparsePoint(file.FullName, "REKALL_FAILURE_REPORT_FILE_REPARSE_POINT");
            file.Delete();
        }
    }

    private void ValidateReport(RekallAgeFailureReport report)
    {
        if (report.SchemaVersion != RekallAgeFailureReport.CurrentSchemaVersion ||
            !Guid.TryParseExact(report.ReportId, "N", out _) ||
            report.Attempts < 0 || report.CompletedFrames < 0 || report.RequestedFrames < 0)
        {
            throw Failure(
                "REKALL_FAILURE_REPORT_INVALID",
                "Failure report identity, schema, or counters are invalid.",
                _root);
        }

        ValidateStrings(report, _limits.MaximumStringChars, _limits.MaximumStackChars);
        ValidateList(report.Limitations, nameof(report.Limitations));
        ValidateList(report.NextActions, nameof(report.NextActions));
    }

    private void ValidateList(IReadOnlyList<string>? values, string name)
    {
        if (values is null || values.Count > _limits.MaximumEntries ||
            values.Any(value => value is null || value.Length > _limits.MaximumStringChars))
        {
            throw Failure(
                "REKALL_FAILURE_REPORT_BOUNDS_EXCEEDED",
                $"Failure report field '{name}' exceeds configured bounds.",
                _root);
        }
    }

    private void ValidateStrings(RekallAgeFailureReport report, int maximum, int maximumStack)
    {
        var values = new[]
        {
            report.ReportId,
            report.ProductVersion,
            report.ProductChannel,
            report.Component,
            report.Outcome,
            report.Category,
            report.Code,
            report.RecoveryMode,
            report.ExceptionType,
            report.ExceptionMessage,
            report.Backend,
            report.ProjectRoot,
            report.SceneName
        };
        if (values.Any(value => value is null || value.Length > maximum) ||
            report.StackExcerpt is null || report.StackExcerpt.Length > maximumStack)
        {
            throw Failure(
                "REKALL_FAILURE_REPORT_BOUNDS_EXCEEDED",
                "Failure report string fields exceed configured bounds.",
                _root);
        }
    }

    private static void ValidateLimits(RekallAgeFailureReportStoreLimits limits)
    {
        if (limits.MaximumReports <= 0 || limits.MaximumEntries <= 0 ||
            limits.MaximumReportBytes <= 0 || limits.MaximumStringChars <= 0 ||
            limits.MaximumStackChars <= 0 || limits.MaximumReports > limits.MaximumEntries)
        {
            throw new ArgumentOutOfRangeException(nameof(limits), "Failure report limits must be positive and coherent.");
        }
    }

    private static RekallAgeFailureReportStoreException Failure(
        string code,
        string message,
        string target,
        Exception? innerException = null) =>
        new(code, message, target, innerException);
}
