using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Rekall.Age.Editor;
using Rekall.Age.Editor.Contracts;
using Rekall.Age.World;
using Rekall.Age.Workflows;

namespace Rekall.Age.Studio;

internal sealed record RekallAgeStudioContentBrowserAcceptanceManifest(
    string Nonce, int PhaseOneProcessId, long PhaseOneStartUtcTicks,
    IReadOnlyList<string> ImportedKinds, string UnsupportedCode, IReadOnlyList<string> AssetIds);

internal sealed record RekallAgeStudioContentBrowserAcceptanceEvidence(
    string Nonce, int PhaseOneProcessId, int PhaseTwoProcessId,
    long PhaseOneStartUtcTicks, long PhaseTwoStartUtcTicks,
    IReadOnlyList<string> ImportedKinds, string UnsupportedCode,
    IReadOnlyList<string> OpenedKinds, IReadOnlyList<string> OpenCodes,
    IReadOnlyList<string> WorkspaceSurfaces, IReadOnlyList<string> ExternalRouteOutcomes,
    string PlacementCode, string AssignmentCode,
    string? PlacementTransactionId, string? AssignmentTransactionId,
    string? PersistedModelAssetId, string? PersistedTextureAssetId,
    bool RestartedIndexContainedImports);

internal static class RekallAgeStudioContentBrowserAcceptance
{
    public const string Switch = "--studio-content-browser-acceptance";

    public static async Task<bool> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        string Read(string name)
        {
            var index = arguments.IndexOf(name);
            return index >= 0 && index + 1 < arguments.Count ? arguments[index + 1]
                : throw new ArgumentException($"Missing {name}.");
        }
        var root = Path.GetFullPath(Read("--project"));
        var evidencePath = Path.GetFullPath(Read("--evidence"));
        var manifestPath = Path.GetFullPath(Read("--phase-manifest"));
        var nonce = Read("--phase-nonce");
        return Read("--phase") switch
        {
            "1" => await RunPhaseOneAsync(root, evidencePath, manifestPath, nonce,
                Read("--fixtures").Split('|', StringSplitOptions.RemoveEmptyEntries).Select(Path.GetFullPath).ToArray(), cancellationToken),
            "2" => await RunPhaseTwoAsync(root, evidencePath, manifestPath, nonce, cancellationToken),
            _ => throw new ArgumentException("--phase must be 1 or 2.")
        };
    }

    private static async Task<bool> RunPhaseOneAsync(string root, string evidencePath, string manifestPath,
        string nonce, IReadOnlyList<string> fixtures, CancellationToken cancellationToken)
    {
        var session = new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create());
        var created = await session.CreateProjectAsync(root, "Content Browser Acceptance", "Main",
            ["world"], ["world", "rendering3d"], "studio-acceptance", cancellationToken);
        if (!created.Ok) throw new InvalidOperationException(created.Summary);
        var import = new RekallAgeStudioContentImportSession(
            new RekallAgeStudioAssetImportCommand(), _ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask);
        var jobs = await import.ImportAsync(root, fixtures, cancellationToken);
        var manifest = new RekallAgeStudioContentBrowserAcceptanceManifest(
            nonce, Environment.ProcessId, Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks,
            jobs.Where(job => job.Status == "Succeeded").Select(job => job.Kind).Distinct().Order().ToArray(),
            jobs.Single(job => Path.GetExtension(job.SourcePath).Equals(".xyz", StringComparison.OrdinalIgnoreCase)).Code,
            jobs.Where(job => job.Status == "Succeeded" && job.AssetId is not null).Select(job => job.AssetId!).ToArray());
        await WriteJsonAsync(manifestPath, manifest, cancellationToken);
        await WriteJsonAsync(evidencePath + ".phase1", manifest, cancellationToken);
        return manifest.ImportedKinds.Contains("model") && manifest.ImportedKinds.Contains("texture")
            && manifest.ImportedKinds.Contains("audio") && manifest.UnsupportedCode == "REKALL_CONTENT_IMPORT_UNSUPPORTED";
    }

    private static async Task<bool> RunPhaseTwoAsync(string root, string evidencePath, string manifestPath,
        string nonce, CancellationToken cancellationToken)
    {
        var manifest = JsonSerializer.Deserialize<RekallAgeStudioContentBrowserAcceptanceManifest>(
            await File.ReadAllTextAsync(manifestPath, cancellationToken))
            ?? throw new InvalidDataException("Phase-one manifest is empty.");
        var process = Process.GetCurrentProcess();
        if (manifest.Nonce != nonce || manifest.PhaseOneProcessId == Environment.ProcessId)
            throw new InvalidDataException("Acceptance phases did not cross a distinct Studio process boundary.");

        var session = new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create());
        var validatedExternalPaths = new List<string>();
        await using var viewModel = new RekallAgeStudioViewModel(session,
            new RekallAgeStudioValidatingExternalContentLauncher(validatedExternalPaths.Add));
        await viewModel.InitializeAsync(root, "Main");
        var imported = viewModel.ContentItems.Where(item => manifest.AssetIds.Contains(item.Id, StringComparer.Ordinal)).ToArray();
        var router = new RekallAgeStudioContentOpenRouter((IRekallAgeStudioContentOpenTarget)viewModel);
        var openResults = new List<(string Kind, RekallAgeStudioContentOpenResult Result)>();
        foreach (var item in imported.Where(item => item.Family is "model" or "texture" or "audio"))
            openResults.Add((item.Family, await router.OpenAsync(item, cancellationToken)));
        if (openResults.Any(result => !result.Result.Opened))
            throw new InvalidOperationException("A production content route did not open.");

        var createdEntity = await session.ExecuteAsync("rekall.entity.create", JsonSerializer.Serialize(new
        {
            projectRoot = root, sceneName = "Main", name = "Textured Acceptance Surface", tags = Array.Empty<string>()
        }), "Create texture acceptance entity", "studio-acceptance", cancellationToken);
        if (!createdEntity.Ok) throw new InvalidOperationException(createdEntity.Summary);
        var entityJson = JsonDocument.Parse(JsonSerializer.Serialize(createdEntity.Value)).RootElement;
        var entityId = (entityJson.TryGetProperty("entityId", out var camelId) ? camelId : entityJson.GetProperty("EntityId")).GetString()!;
        var material = await session.ExecuteAsync("rekall.component.add", JsonSerializer.Serialize(new
        {
            projectRoot = root, sceneName = "Main", entityId, componentType = "Rekall.Material", properties = new { }
        }), "Create texture assignment target", "studio-acceptance", cancellationToken);
        if (!material.Ok) throw new InvalidOperationException(material.Summary);
        await viewModel.InitializeAsync(root, "Main");
        var targetNode = viewModel.EntityNodes.SelectMany(Flatten).Single(node => node.EntityId == entityId);
        await viewModel.SelectEntityAsync(targetNode);
        var row = viewModel.InspectorPropertyEditors.Single(editor =>
            editor.ComponentType == "Rekall.Material" && editor.Name == "baseColorTexture");

        var texture = imported.Single(item => item.Family == "texture");
        var model = imported.Single(item => item.Family == "model");
        var texturePayload = RekallAgeStudioContentDragPayload.FromJson(RekallAgeStudioContentDragPayload.FromItem(texture).ToJson());
        var modelPayload = RekallAgeStudioContentDragPayload.FromJson(RekallAgeStudioContentDragPayload.FromItem(model).ToJson());
        var assignment = await viewModel.AssignContentAsync(texturePayload, row, cancellationToken);
        var placement = await viewModel.PlaceContentAsync(modelPayload, .5, .5, 16d / 9d, cancellationToken);
        if (!assignment.Applied || !placement.Applied) throw new InvalidOperationException("Production content drop was rejected.");

        var scene = await new RekallAgeSceneStore().LoadAsync(root, "Main", cancellationToken);
        var persistedModel = scene.Entities.SelectMany(value => value.Components)
            .FirstOrDefault(component => component.Type == "Rekall.ModelAssetReference")?.Properties["assetId"]?.GetValue<string>();
        var persistedTexture = scene.Entities.SelectMany(value => value.Components)
            .FirstOrDefault(component => component.Type == "Rekall.Material")?.Properties["baseColorTexture"]?.GetValue<string>();
        var evidence = new RekallAgeStudioContentBrowserAcceptanceEvidence(
            nonce, manifest.PhaseOneProcessId, Environment.ProcessId, manifest.PhaseOneStartUtcTicks,
            process.StartTime.ToUniversalTime().Ticks, manifest.ImportedKinds, manifest.UnsupportedCode,
            openResults.Select(result => result.Kind).Distinct().Order().ToArray(),
            openResults.Select(result => result.Result.Code).ToArray(),
            openResults.Where(result => result.Result.WorkspaceId != "external")
                .Select(result => $"{result.Result.WorkspaceId}/{result.Result.SurfaceId}").ToArray(),
            validatedExternalPaths.Select(path => $"validated:{Path.GetExtension(path).ToLowerInvariant()}").ToArray(),
            placement.Code, assignment.Code, placement.TransactionId, assignment.TransactionId,
            persistedModel, persistedTexture, imported.Length == manifest.AssetIds.Count);
        await WriteJsonAsync(evidencePath, evidence, cancellationToken);
        return evidence.PhaseOneProcessId != evidence.PhaseTwoProcessId
            && evidence.PhaseOneStartUtcTicks != evidence.PhaseTwoStartUtcTicks
            && evidence.OpenCodes.All(code => code == "REKALL_CONTENT_OPENED")
            && evidence.ExternalRouteOutcomes.Count == openResults.Count(result => result.Result.WorkspaceId == "external")
            && evidence.PlacementCode == "REKALL_CONTENT_DROP_APPLIED"
            && evidence.AssignmentCode == "REKALL_CONTENT_DROP_APPLIED"
            && !string.IsNullOrWhiteSpace(evidence.PlacementTransactionId)
            && !string.IsNullOrWhiteSpace(evidence.AssignmentTransactionId)
            && !string.IsNullOrWhiteSpace(evidence.PersistedModelAssetId)
            && !string.IsNullOrWhiteSpace(evidence.PersistedTextureAssetId)
            && evidence.RestartedIndexContainedImports;
    }

    private static IEnumerable<RekallAgeSceneEntityNode> Flatten(RekallAgeSceneEntityNode node) =>
        new[] { node }.Concat(node.Children.SelectMany(Flatten));

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path,
            JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }
}
