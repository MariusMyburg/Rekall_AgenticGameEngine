using Rekall.Age.AssetPipeline.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using System.IO;
using System.Collections.ObjectModel;
using System.Collections.Concurrent;

namespace Rekall.Age.Studio;

internal sealed record RekallAgeStudioContentImportClassification(
    string SourcePath,
    string Kind,
    bool Accepted,
    string Code,
    string Summary);

internal sealed class RekallAgeStudioContentImportPolicy
{
    private static readonly IReadOnlyDictionary<string, string> Kinds =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".glb"] = "model", [".gltf"] = "model",
            [".png"] = "texture", [".jpg"] = "texture", [".jpeg"] = "texture",
            [".dds"] = "texture", [".ktx2"] = "texture",
            [".wav"] = "audio", [".mp3"] = "audio",
            [".glsl"] = "shader", [".vert"] = "shader", [".frag"] = "shader",
            [".comp"] = "shader", [".hlsl"] = "shader"
        };

    public IReadOnlyList<RekallAgeStudioContentImportClassification> Classify(IEnumerable<string> paths) =>
        paths.Select(Classify).ToArray();

    public RekallAgeStudioContentImportClassification Classify(string path)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".cs", StringComparison.OrdinalIgnoreCase))
            return new(path, "code", false, "REKALL_CONTENT_IMPORT_MODULE_ROUTE_REQUIRED",
                "C# sources must be added through the project module source workflow.");
        if (Kinds.TryGetValue(extension, out var kind))
            return new(path, kind, true, "REKALL_CONTENT_IMPORT_READY", "Ready to import.");
        return new(path, "other", false, "REKALL_CONTENT_IMPORT_UNSUPPORTED",
            "This file type is not supported for content import.");
    }
}

public sealed record RekallAgeStudioContentImportJob(
    string SourcePath,
    string Kind,
    string Status,
    string Code,
    string Summary,
    string? AssetId = null);

internal interface IRekallAgeStudioAssetImportCommand
{
    ValueTask<ImportAssetWithReportResult> ImportAsync(
        string projectRoot, string sourcePath, string kind, CancellationToken cancellationToken);
}

internal interface IRekallAgeStudioContentImportDispatcher
{
    ValueTask InvokeAsync(Action action, CancellationToken cancellationToken);
}

internal sealed class RekallAgeStudioContentImportDispatcher(SynchronizationContext? context = null)
    : IRekallAgeStudioContentImportDispatcher
{
    private readonly SynchronizationContext? _context = context ?? SynchronizationContext.Current;

    public ValueTask InvokeAsync(Action action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        if (_context is null || ReferenceEquals(SynchronizationContext.Current, _context))
        {
            action();
            return ValueTask.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _context.Post(_ =>
        {
            try { action(); completion.TrySetResult(); }
            catch (Exception exception) { completion.TrySetException(exception); }
        }, null);
        return new(completion.Task.WaitAsync(cancellationToken));
    }
}

internal sealed class RekallAgeStudioAssetImportCommand : IRekallAgeStudioAssetImportCommand
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ProjectGates =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public async ValueTask<ImportAssetWithReportResult> ImportAsync(
        string projectRoot, string sourcePath, string kind, CancellationToken cancellationToken)
    {
        var normalizedRoot = NormalizeProjectGateKey(projectRoot);
        var gate = ProjectGates.GetOrAdd(normalizedRoot, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var registry = new RekallAgeCommandRegistry();
            registry.Register(new ImportAssetWithReportCommand());
            var context = new RekallAgeCommandContext(
                "studio-content-browser",
                RekallAgeTransaction.Begin($"Import {Path.GetFileName(sourcePath)}"),
                cancellationToken);
            var result = await registry.ExecuteAsync<ImportAssetWithReportRequest, ImportAssetWithReportResult>(
                "rekall.asset.import_report",
                new ImportAssetWithReportRequest(projectRoot, sourcePath, kind, Path.GetFileNameWithoutExtension(sourcePath)),
                context);
            if (!result.Ok) throw new InvalidOperationException("The canonical asset import command rejected the file.");
            return result.Value;
        }
        finally { gate.Release(); }
    }

    internal static string NormalizeProjectGateKey(string projectRoot)
    {
        var fullRoot = Path.GetFullPath(projectRoot);
        var root = Path.GetPathRoot(fullRoot);
        return root is not null && fullRoot.Equals(root, OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal)
            ? root
            : fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}

internal sealed class RekallAgeStudioContentImportSession(
    IRekallAgeStudioAssetImportCommand importer,
    Func<CancellationToken, ValueTask> refreshContent,
    Func<CancellationToken, ValueTask> invalidateViewport,
    RekallAgeStudioContentImportPolicy? policy = null,
    IRekallAgeStudioContentImportDispatcher? dispatcher = null)
{
    private readonly RekallAgeStudioContentImportPolicy _policy = policy ?? new();
    private readonly IRekallAgeStudioContentImportDispatcher _dispatcher = dispatcher ?? new RekallAgeStudioContentImportDispatcher();
    private int _active;
    public ObservableCollection<RekallAgeStudioContentImportJob> Jobs { get; } = [];

    public async ValueTask<IReadOnlyList<RekallAgeStudioContentImportJob>> ImportAsync(
        string projectRoot,
        IEnumerable<string> sourcePaths,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentNullException.ThrowIfNull(sourcePaths);
        cancellationToken.ThrowIfCancellationRequested();

        if (Interlocked.CompareExchange(ref _active, 1, 0) != 0)
            return [new(string.Empty, "other", "Rejected", "REKALL_CONTENT_IMPORT_ALREADY_ACTIVE",
                "Another content import batch is already running.")];

        try
        {
            var inputs = sourcePaths.ToArray();
            var preparation = await Task.Run(() => Prepare(inputs, cancellationToken), cancellationToken);
            var jobs = preparation.Jobs;
            var accepted = preparation.Accepted;
            await _dispatcher.InvokeAsync(() => Replace(Jobs, jobs), cancellationToken);

            using var concurrency = new SemaphoreSlim(2, 2);
            var tasks = accepted.Select(async item =>
            {
            var entered = false;
            try
            {
                await concurrency.WaitAsync(cancellationToken);
                entered = true;
                await PublishJobAsync(jobs, item.Index, jobs[item.Index] with { Status = "Importing", Code = "REKALL_CONTENT_IMPORT_ACTIVE", Summary = "Importing content." }, cancellationToken);
                var result = await importer.ImportAsync(projectRoot, item.Path, item.Kind, cancellationToken);
                await PublishJobAsync(jobs, item.Index, new(item.Path, item.Kind, "Succeeded", "REKALL_CONTENT_IMPORT_SUCCEEDED", "Content imported.", result.Report.AssetId), CancellationToken.None);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await PublishJobAsync(jobs, item.Index, jobs[item.Index] with { Status = "Cancelled", Code = "REKALL_CONTENT_IMPORT_CANCELLED", Summary = "Import cancelled." }, CancellationToken.None);
                throw;
            }
            catch (Exception)
            {
                await PublishJobAsync(jobs, item.Index, new(item.Path, item.Kind, "Failed", "REKALL_CONTENT_IMPORT_FAILED",
                    "The file could not be imported. Check that it is readable and valid for its content type."), CancellationToken.None);
            }
            finally
            {
                if (entered) concurrency.Release();
            }
            }).ToArray();

            OperationCanceledException? cancellation = null;
            try { await Task.WhenAll(tasks); }
            catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
            {
                cancellation = exception;
            }

            if (jobs.Any(x => x.Status == "Succeeded"))
            {
                var refreshToken = cancellation is null ? cancellationToken : CancellationToken.None;
                await refreshContent(refreshToken);
                await invalidateViewport(refreshToken);
            }
            if (cancellation is not null) throw cancellation;
            return jobs;
        }
        finally { Interlocked.Exchange(ref _active, 0); }
    }

    private Preparation Prepare(string[] inputs, CancellationToken cancellationToken)
    {
        var jobs = new RekallAgeStudioContentImportJob[inputs.Length];
        var accepted = new List<AcceptedImport>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < inputs.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var input = inputs[index];
            if (!Path.IsPathFullyQualified(input))
            {
                jobs[index] = Rejected(input, "REKALL_CONTENT_IMPORT_ABSOLUTE_PATH_REQUIRED", "Only absolute file paths can be imported.");
                continue;
            }
            var normalized = Path.GetFullPath(input);
            if (!seen.Add(normalized)) jobs[index] = Rejected(input, "REKALL_CONTENT_IMPORT_DUPLICATE", "This file already appears in the import batch.");
            else if (Directory.Exists(normalized)) jobs[index] = Rejected(input, "REKALL_CONTENT_IMPORT_DIRECTORY_UNSUPPORTED", "Folder import is not supported.");
            else if (!File.Exists(normalized)) jobs[index] = Rejected(input, "REKALL_CONTENT_IMPORT_FILE_NOT_FOUND", "The selected file no longer exists.");
            else
            {
                var classification = _policy.Classify(normalized);
                if (!classification.Accepted)
                {
                    jobs[index] = new(normalized, classification.Kind, "Rejected", classification.Code, classification.Summary);
                }
                else
                {
                    jobs[index] = new(normalized, classification.Kind, "Queued", "REKALL_CONTENT_IMPORT_QUEUED", "Waiting to import.");
                    accepted.Add(new(index, normalized, classification.Kind));
                }
            }
        }
        return new(jobs, accepted);
    }

    private static RekallAgeStudioContentImportJob Rejected(string path, string code, string summary) =>
        new(path, "other", "Rejected", code, summary);

    private ValueTask PublishJobAsync(RekallAgeStudioContentImportJob[] jobs, int index,
        RekallAgeStudioContentImportJob job, CancellationToken cancellationToken) =>
        _dispatcher.InvokeAsync(() => { jobs[index] = job; Jobs[index] = job; }, cancellationToken);

    private static void Replace(ObservableCollection<RekallAgeStudioContentImportJob> target,
        IEnumerable<RekallAgeStudioContentImportJob> jobs)
    {
        target.Clear();
        foreach (var job in jobs) target.Add(job);
    }

    private sealed record AcceptedImport(int Index, string Path, string Kind);
    private sealed record Preparation(RekallAgeStudioContentImportJob[] Jobs, IReadOnlyList<AcceptedImport> Accepted);
}
