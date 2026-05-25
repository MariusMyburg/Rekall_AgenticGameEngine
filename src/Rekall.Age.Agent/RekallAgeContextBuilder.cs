using Rekall.Age.Project;
using Rekall.Age.Validation;
using Rekall.Age.World;

namespace Rekall.Age.Agent;

public sealed class RekallAgeContextBuilder
{
    private readonly RekallAgeProjectStore _projectStore;
    private readonly RekallAgeSceneStore _sceneStore;
    private readonly RekallAgeProjectValidator _validator;

    public RekallAgeContextBuilder(
        RekallAgeProjectStore projectStore,
        RekallAgeSceneStore sceneStore,
        RekallAgeProjectValidator validator)
    {
        _projectStore = projectStore;
        _sceneStore = sceneStore;
        _validator = validator;
    }

    public async ValueTask<RekallAgeProjectSummary> BuildProjectSummaryAsync(
        string projectRoot,
        CancellationToken cancellationToken)
    {
        var manifest = await _projectStore.LoadAsync(projectRoot, cancellationToken);
        var sceneNames = _sceneStore.ListSceneNames(projectRoot);
        var blocking = new List<string>();

        foreach (var sceneName in sceneNames)
        {
            var report = await _validator.ValidateSceneAsync(projectRoot, sceneName, cancellationToken);
            blocking.AddRange(report.BlockingMessages);
        }

        var artifacts = FindArtifacts(projectRoot);
        var status = blocking.Count == 0 ? "ok" : "blocked";
        IReadOnlyList<string> nextActions = blocking.Count != 0
            ? ["rekall.workflow.fix_validation_errors"]
            : artifacts.Any(artifact => artifact.Kind == "playable-package-archive")
                ? ["rekall.workflow.audit_playable_package", "rekall.workflow.run_playable_package"]
                : ["rekall.workflow.package_playable_game", "rekall.capture.screenshot"];

        return new RekallAgeProjectSummary(
            manifest.Name,
            manifest.SourceTemplateId,
            manifest.Capabilities,
            sceneNames,
            artifacts,
            new RekallAgeProjectHealth(status, blocking),
            nextActions);
    }

    public async ValueTask<RekallAgeSceneSummary> BuildSceneSummaryAsync(
        string projectRoot,
        string sceneName,
        CancellationToken cancellationToken)
    {
        var scene = await _sceneStore.LoadAsync(projectRoot, sceneName, cancellationToken);
        var entities = scene.Entities
            .Select(entity => new RekallAgeEntitySummary(
                entity.Id,
                entity.Name,
                entity.Tags,
                entity.Components.Select(component => SimplifyComponentType(component.Type)).ToArray()))
            .ToArray();
        var componentTypes = entities
            .SelectMany(entity => entity.Components)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(component => component, StringComparer.Ordinal)
            .ToArray();

        return new RekallAgeSceneSummary(scene.Name, scene.Capabilities, entities, componentTypes);
    }

    private static string SimplifyComponentType(string componentType)
    {
        const string prefix = "Rekall.";
        return componentType.StartsWith(prefix, StringComparison.Ordinal)
            ? componentType[prefix.Length..]
            : componentType;
    }

    private static IReadOnlyList<RekallAgeProjectArtifact> FindArtifacts(string projectRoot)
    {
        var fullRoot = Path.GetFullPath(projectRoot);
        if (!Directory.Exists(fullRoot))
        {
            return [];
        }

        var artifacts = new List<RekallAgeProjectArtifact>();
        AddArtifacts(
            artifacts,
            fullRoot,
            Directory.EnumerateFiles(fullRoot, "*.zip", SearchOption.AllDirectories),
            "playable-package-archive");
        AddArtifacts(
            artifacts,
            fullRoot,
            Directory.EnumerateFiles(fullRoot, "rekall.package.json", SearchOption.AllDirectories),
            "playable-package-manifest");

        return artifacts
            .OrderBy(artifact => artifact.Kind, StringComparer.Ordinal)
            .ThenBy(artifact => artifact.Path, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddArtifacts(
        List<RekallAgeProjectArtifact> artifacts,
        string fullRoot,
        IEnumerable<string> files,
        string kind)
    {
        foreach (var file in files)
        {
            var fullPath = Path.GetFullPath(file);
            artifacts.Add(new RekallAgeProjectArtifact(
                kind,
                NormalizePath(Path.GetRelativePath(fullRoot, fullPath)),
                new FileInfo(fullPath).Length));
        }
    }

    private static string NormalizePath(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }
}
