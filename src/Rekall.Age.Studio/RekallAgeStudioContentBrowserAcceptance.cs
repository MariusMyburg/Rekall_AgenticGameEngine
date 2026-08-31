using System.Numerics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rekall.Age.Editor;
using Rekall.Age.Editor.Contracts;
using Rekall.Age.World;
using Rekall.Age.Workflows;

namespace Rekall.Age.Studio;

internal sealed record RekallAgeStudioContentBrowserAcceptanceEvidence(
    IReadOnlyList<string> ImportedKinds,
    string UnsupportedCode,
    IReadOnlyList<string> OpenedKinds,
    string? PersistedModelAssetId,
    string? PersistedTextureAssetId,
    bool RestartedIndexContainedImports);

internal static class RekallAgeStudioContentBrowserAcceptance
{
    public const string Switch = "--studio-content-browser-acceptance";

    public static async Task<bool> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        string? Read(string name)
        {
            var index = arguments.IndexOf(name);
            return index >= 0 && index + 1 < arguments.Count ? arguments[index + 1] : null;
        }
        var root = Path.GetFullPath(Read("--project") ?? throw new ArgumentException("Missing --project."));
        var evidencePath = Path.GetFullPath(Read("--evidence") ?? throw new ArgumentException("Missing --evidence."));
        var fixtures = (Read("--fixtures") ?? throw new ArgumentException("Missing --fixtures."))
            .Split('|', StringSplitOptions.RemoveEmptyEntries).Select(Path.GetFullPath).ToArray();

        var firstSession = new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create());
        var created = await firstSession.CreateProjectAsync(root, "Content Browser Acceptance", "Main",
            ["world"], ["world", "rendering3d"], "studio-acceptance", cancellationToken);
        if (!created.Ok) throw new InvalidOperationException(created.Summary);

        var import = new RekallAgeStudioContentImportSession(
            new RekallAgeStudioAssetImportCommand(), _ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask);
        var jobs = await import.ImportAsync(root, fixtures, cancellationToken);
        var unsupported = jobs.Single(job => Path.GetExtension(job.SourcePath).Equals(".xyz", StringComparison.OrdinalIgnoreCase));
        var successfulAssetIds = jobs.Where(job => job.Status == "Succeeded" && job.AssetId is not null)
            .Select(job => job.AssetId!).ToArray();

        // This fresh index/session is the deliberate Studio restart boundary.
        var restartedIndex = await RekallAgeStudioContentIndex.CreateDefault().RefreshAsync(root, cancellationToken);
        var imported = restartedIndex.Items.Where(item => successfulAssetIds.Contains(item.Id, StringComparer.Ordinal)).ToArray();
        var openTarget = new RecordingOpenTarget();
        var router = new RekallAgeStudioContentOpenRouter(openTarget);
        foreach (var item in imported.Where(item => item.Family is "model" or "texture" or "audio"))
        {
            var result = await router.OpenAsync(item, cancellationToken);
            if (!result.Opened) throw new InvalidOperationException(result.Code);
        }

        var restartedSession = new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create());
        var opened = await restartedSession.OpenAsync(root, "Main", cancellationToken);
        if (!opened.Ok) throw new InvalidOperationException(opened.Summary);
        var modelItem = imported.Single(item => item.Family == "model");
        var textureItem = imported.Single(item => item.Family == "texture");
        var modelAssetId = await RekallAgeStudioImportedModelPublisher.EnsurePublishedAsync(
            restartedSession, modelItem, cancellationToken);
        var placed = await restartedSession.ExecuteAsync("rekall.scene.instantiate_asset", JsonSerializer.Serialize(new
        {
            projectRoot = root, sceneName = "Main", modelAssetId, name = "Acceptance Model",
            position = new { x = 0, y = 0, z = 5 }, rotationDegrees = new { x = 0, y = 0, z = 0 },
            scale = new { x = 1, y = 1, z = 1 }
        }), "Place imported acceptance model", "studio-acceptance", cancellationToken);
        if (!placed.Ok) throw new InvalidOperationException(placed.Summary);

        var entity = await restartedSession.ExecuteAsync("rekall.entity.create", JsonSerializer.Serialize(new
        {
            projectRoot = root, sceneName = "Main", name = "Textured Acceptance Surface", tags = Array.Empty<string>()
        }), "Create texture acceptance entity", "studio-acceptance", cancellationToken);
        if (!entity.Ok) throw new InvalidOperationException(entity.Summary);
        var entityJson = JsonDocument.Parse(JsonSerializer.Serialize(entity.Value)).RootElement;
        var entityId = (entityJson.TryGetProperty("entityId", out var camelId) ? camelId : entityJson.GetProperty("EntityId")).GetString()!;
        var material = await restartedSession.ExecuteAsync("rekall.component.add", JsonSerializer.Serialize(new
        {
            projectRoot = root, sceneName = "Main", entityId, componentType = "Rekall.Material",
            properties = new JsonObject { ["baseColorTexture"] = textureItem.Id }
        }), "Assign imported acceptance texture", "studio-acceptance", cancellationToken);
        if (!material.Ok) throw new InvalidOperationException(material.Summary);

        var scene = await new RekallAgeSceneStore().LoadAsync(root, "Main", cancellationToken);
        var persistedModel = scene.Entities.SelectMany(value => value.Components)
            .FirstOrDefault(component => component.Type == "Rekall.ModelAssetReference")?
            .Properties["assetId"]?.GetValue<string>();
        var persistedTexture = scene.Entities.SelectMany(value => value.Components)
            .FirstOrDefault(component => component.Type == "Rekall.Material")?
            .Properties["baseColorTexture"]?.GetValue<string>();
        var evidence = new RekallAgeStudioContentBrowserAcceptanceEvidence(
            jobs.Where(job => job.Status == "Succeeded").Select(job => job.Kind).Distinct().Order().ToArray(),
            unsupported.Code,
            openTarget.OpenedKinds.Distinct().Order().ToArray(),
            persistedModel,
            persistedTexture,
            imported.Length == successfulAssetIds.Length);
        Directory.CreateDirectory(Path.GetDirectoryName(evidencePath)!);
        await File.WriteAllTextAsync(evidencePath,
            JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        return evidence.ImportedKinds.Contains("model") && evidence.ImportedKinds.Contains("texture")
            && evidence.ImportedKinds.Contains("audio")
            && evidence.UnsupportedCode == "REKALL_CONTENT_IMPORT_UNSUPPORTED"
            && !string.IsNullOrWhiteSpace(evidence.PersistedModelAssetId)
            && !string.IsNullOrWhiteSpace(evidence.PersistedTextureAssetId)
            && evidence.RestartedIndexContainedImports;
    }

    private sealed class RecordingOpenTarget : IRekallAgeStudioContentOpenTarget
    {
        public List<string> OpenedKinds { get; } = [];
        public ValueTask SelectMeshAsync(RekallAgeContentBrowserItem item, CancellationToken token) => Open(item, token);
        public ValueTask SelectGraphAsync(RekallAgeContentBrowserItem item, CancellationToken token) => Open(item, token);
        public ValueTask SelectMaterialAsync(RekallAgeContentBrowserItem item, CancellationToken token) => Open(item, token);
        public ValueTask SelectModuleSourceAsync(RekallAgeContentBrowserItem item, CancellationToken token) => Open(item, token);
        public ValueTask OpenAssociatedAsync(RekallAgeContentBrowserItem item, CancellationToken token) => Open(item, token);
        private ValueTask Open(RekallAgeContentBrowserItem item, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); OpenedKinds.Add(item.Family); return ValueTask.CompletedTask;
        }
    }
}
