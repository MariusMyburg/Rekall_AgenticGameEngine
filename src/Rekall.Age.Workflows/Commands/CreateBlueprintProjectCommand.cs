using Rekall.Age.Core.Commands;
using Rekall.Age.Project;
using Rekall.Age.Project.Commands;
using Rekall.Age.World;
using Rekall.Age.World.Commands;

namespace Rekall.Age.Workflows.Commands;

public sealed record CreateBlueprintProjectRequest(
    string ProjectRoot,
    string ProjectName,
    IReadOnlyList<string> ProjectCapabilities,
    string SceneName = "Main",
    IReadOnlyList<string>? SceneCapabilities = null,
    IReadOnlyList<RekallAgeSceneBlueprintEntity>? Entities = null,
    IReadOnlyList<RekallAgeProjectBlueprintScene>? Scenes = null);

public sealed record RekallAgeProjectBlueprintScene(
    string Name,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<RekallAgeSceneBlueprintEntity> Entities);

public sealed record RekallAgeCreatedBlueprintScene(
    CreateSceneResult Scene,
    ApplySceneBlueprintResult Blueprint);

public sealed record CreateBlueprintProjectResult(
    CreateProjectResult Project,
    CreateSceneResult Scene,
    ApplySceneBlueprintResult Blueprint)
{
    public IReadOnlyList<RekallAgeCreatedBlueprintScene> Scenes { get; init; } = [];
}

public sealed class CreateBlueprintProjectCommand
    : IRekallAgeCommand<CreateBlueprintProjectRequest, CreateBlueprintProjectResult>
{
    private readonly RekallAgeProjectStore _projectStore = new();
    private readonly RekallAgeSceneStore _sceneStore = new();

    public string Name => "rekall.workflow.create_blueprint_project";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Creates a project and one or many scenes from agent-supplied generic entity/component blueprints in one command. For multi-scene games, put every scene in Scenes; an empty entities array is a valid named scene scaffold for later authoring recovery. Exact compact shape: {\"projectRoot\":\"...\",\"projectName\":\"Game\",\"projectCapabilities\":[],\"scenes\":[{\"name\":\"Main\",\"capabilities\":[],\"entities\":[{\"name\":\"Entity\",\"components\":[{\"type\":\"Rekall.Transform3D\",\"properties\":{\"X\":0}}]}]}]}. scenes, entities, and components are JSON arrays, never a JSON string; each component uses type and properties.",
        typeof(CreateBlueprintProjectRequest).FullName!,
        typeof(CreateBlueprintProjectResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<CreateBlueprintProjectResult>> ExecuteAsync(
        CreateBlueprintProjectRequest request,
        RekallAgeCommandContext context)
    {
        var requestedScenes = request.Scenes is { Count: > 0 }
            ? request.Scenes
            :
            [
                new RekallAgeProjectBlueprintScene(
                    request.SceneName,
                    request.SceneCapabilities ?? [],
                    request.Entities ?? [])
            ];
        var duplicateScene = requestedScenes
            .GroupBy(scene => scene.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateScene is not null)
        {
            var error = new RekallAgeCommandError(
                "REKALL_BLUEPRINT_SCENE_DUPLICATE",
                $"Project blueprint scene names must be unique; '{duplicateScene.Key}' appears more than once.",
                duplicateScene.Key);
            return RekallAgeCommandResult<CreateBlueprintProjectResult>.Failure(default!, error.Message, [error]);
        }

        var validationErrors = requestedScenes
            .SelectMany(scene => ApplySceneBlueprintCommand.ValidateBlueprint(scene.Name, scene.Entities))
            .ToArray();
        if (validationErrors.Length > 0)
        {
            return RekallAgeCommandResult<CreateBlueprintProjectResult>.Failure(
                default!,
                $"Project blueprint validation failed with {validationErrors.Length} error(s); no project files were written.",
                validationErrors);
        }

        var project = await EnsureProjectAsync(request, context);
        if (!project.Ok)
        {
            return RekallAgeCommandResult<CreateBlueprintProjectResult>.Failure(
                default!,
                project.Summary,
                project.Errors);
        }

        var createdScenes = new List<RekallAgeCreatedBlueprintScene>();
        foreach (var requestedScene in requestedScenes)
        {
            var scene = await EnsureSceneAsync(request.ProjectRoot, requestedScene, context);
            if (!scene.Ok)
            {
                return RekallAgeCommandResult<CreateBlueprintProjectResult>.Failure(default!, scene.Summary, scene.Errors);
            }

            var blueprint = await new ApplySceneBlueprintCommand().ExecuteAsync(
                new ApplySceneBlueprintRequest(
                    request.ProjectRoot,
                    requestedScene.Name,
                    requestedScene.Entities,
                    ClearExisting: true),
                context);
            if (!blueprint.Ok)
            {
                return RekallAgeCommandResult<CreateBlueprintProjectResult>.Failure(default!, blueprint.Summary, blueprint.Errors);
            }

            createdScenes.Add(new RekallAgeCreatedBlueprintScene(scene.Value, blueprint.Value));
        }

        var first = createdScenes[0];
        var reusedExistingDocuments = !context.Transaction.ChangedResources.Contains(
            project.Value.ManifestPath,
            StringComparer.OrdinalIgnoreCase);
        return RekallAgeCommandResult<CreateBlueprintProjectResult>.Success(
            new CreateBlueprintProjectResult(project.Value, first.Scene, first.Blueprint)
            {
                Scenes = createdScenes
            },
            $"{(reusedExistingDocuments ? "Updated existing" : "Created")} project '{project.Value.Manifest.Name}' with {createdScenes.Count} scene(s) and {requestedScenes.Sum(scene => scene.Entities.Count)} generic blueprint entities.");
    }

    private async ValueTask<RekallAgeCommandResult<CreateProjectResult>> EnsureProjectAsync(
        CreateBlueprintProjectRequest request,
        RekallAgeCommandContext context)
    {
        var manifestPath = Path.Combine(request.ProjectRoot, RekallAgeProjectStore.ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return await new CreateProjectCommand().ExecuteAsync(
                new CreateProjectRequest(request.ProjectRoot, request.ProjectName, request.ProjectCapabilities),
                context);
        }

        var manifest = await _projectStore.LoadAsync(request.ProjectRoot, context.CancellationToken);
        var missingCapabilities = MissingCapabilities(request.ProjectCapabilities, manifest.Capabilities);
        if (missingCapabilities.Length > 0)
        {
            return RekallAgeCommandResult<CreateProjectResult>.Failure(
                new CreateProjectResult(manifestPath, manifest),
                "The existing project is missing capabilities required by the supplied blueprint.",
                [new RekallAgeCommandError(
                    "REKALL_BLUEPRINT_PROJECT_CAPABILITY_MISSING",
                    $"Existing project requires capabilities: {string.Join(", ", missingCapabilities)}.",
                    manifestPath)]);
        }

        return RekallAgeCommandResult<CreateProjectResult>.Success(
            new CreateProjectResult(manifestPath, manifest),
            $"Using existing Rekall AGE project '{manifest.Name}'.");
    }

    private async ValueTask<RekallAgeCommandResult<CreateSceneResult>> EnsureSceneAsync(
        string projectRoot,
        RekallAgeProjectBlueprintScene requestedScene,
        RekallAgeCommandContext context)
    {
        var scenePath = _sceneStore.GetScenePath(projectRoot, requestedScene.Name);
        if (!File.Exists(scenePath))
        {
            return await new CreateSceneCommand().ExecuteAsync(
                new CreateSceneRequest(projectRoot, requestedScene.Name, requestedScene.Capabilities),
                context);
        }

        var scene = await _sceneStore.LoadAsync(projectRoot, requestedScene.Name, context.CancellationToken);
        var missingCapabilities = MissingCapabilities(requestedScene.Capabilities, scene.Capabilities);
        if (missingCapabilities.Length > 0)
        {
            return RekallAgeCommandResult<CreateSceneResult>.Failure(
                new CreateSceneResult(scenePath, scene),
                $"The existing scene '{scene.Name}' is missing capabilities required by the supplied blueprint.",
                [new RekallAgeCommandError(
                    "REKALL_BLUEPRINT_SCENE_CAPABILITY_MISSING",
                    $"Existing scene requires capabilities: {string.Join(", ", missingCapabilities)}.",
                    scenePath)]);
        }

        return RekallAgeCommandResult<CreateSceneResult>.Success(
            new CreateSceneResult(scenePath, scene),
            $"Using existing scene '{scene.Name}'.");
    }

    private static string[] MissingCapabilities(
        IEnumerable<string> requested,
        IReadOnlyList<string> available) =>
        requested
            .Where(required => !available.Contains(required, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(capability => capability, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
