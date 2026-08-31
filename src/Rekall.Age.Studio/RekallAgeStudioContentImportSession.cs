using Rekall.Age.AssetPipeline.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using System.IO;
using System.Collections.ObjectModel;

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

internal sealed class RekallAgeStudioAssetImportCommand : IRekallAgeStudioAssetImportCommand
{
    public async ValueTask<ImportAssetWithReportResult> ImportAsync(
        string projectRoot, string sourcePath, string kind, CancellationToken cancellationToken)
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
}

internal sealed class RekallAgeStudioContentImportSession(
    IRekallAgeStudioAssetImportCommand importer,
    Func<CancellationToken, ValueTask> refreshContent,
    Func<CancellationToken, ValueTask> invalidateViewport,
    RekallAgeStudioContentImportPolicy? policy = null)
{
    private readonly RekallAgeStudioContentImportPolicy _policy = policy ?? new();
    public ObservableCollection<RekallAgeStudioContentImportJob> Jobs { get; } = [];

    public async ValueTask<IReadOnlyList<RekallAgeStudioContentImportJob>> ImportAsync(
        string projectRoot,
        IEnumerable<string> sourcePaths,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentNullException.ThrowIfNull(sourcePaths);
        cancellationToken.ThrowIfCancellationRequested();

        var inputs = sourcePaths.ToArray();
        var preparation = await Task.Run(() => Prepare(inputs, cancellationToken), cancellationToken);
        var jobs = preparation.Jobs;
        var accepted = preparation.Accepted;
        Replace(Jobs, jobs);

        using var concurrency = new SemaphoreSlim(2, 2);
        var tasks = accepted.Select(async item =>
        {
            var entered = false;
            try
            {
                await concurrency.WaitAsync(cancellationToken);
                entered = true;
                UpdateJob(jobs, item.Index, jobs[item.Index] with { Status = "Importing", Code = "REKALL_CONTENT_IMPORT_ACTIVE", Summary = "Importing content." });
                var result = await importer.ImportAsync(projectRoot, item.Path, item.Kind, cancellationToken);
                UpdateJob(jobs, item.Index, new(item.Path, item.Kind, "Succeeded", "REKALL_CONTENT_IMPORT_SUCCEEDED", "Content imported.", result.Report.AssetId));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                UpdateJob(jobs, item.Index, jobs[item.Index] with { Status = "Cancelled", Code = "REKALL_CONTENT_IMPORT_CANCELLED", Summary = "Import cancelled." });
                throw;
            }
            catch (Exception)
            {
                UpdateJob(jobs, item.Index, new(item.Path, item.Kind, "Failed", "REKALL_CONTENT_IMPORT_FAILED",
                    "The file could not be imported. Check that it is readable and valid for its content type."));
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

    private void UpdateJob(RekallAgeStudioContentImportJob[] jobs, int index, RekallAgeStudioContentImportJob job)
    {
        jobs[index] = job;
        Jobs[index] = job;
    }

    private static void Replace(ObservableCollection<RekallAgeStudioContentImportJob> target,
        IEnumerable<RekallAgeStudioContentImportJob> jobs)
    {
        target.Clear();
        foreach (var job in jobs) target.Add(job);
    }

    private sealed record AcceptedImport(int Index, string Path, string Kind);
    private sealed record Preparation(RekallAgeStudioContentImportJob[] Jobs, IReadOnlyList<AcceptedImport> Accepted);
}
