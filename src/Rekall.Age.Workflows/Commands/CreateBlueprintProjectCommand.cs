using Rekall.Age.Core.Commands;
using Rekall.Age.Project.Commands;
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
    public string Name => "rekall.workflow.create_blueprint_project";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Creates a project and one or many complete scenes from agent-supplied generic entity/component blueprints in one command. For multi-scene games, put every scene in Scenes instead of authoring incrementally.",
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

        var project = await new CreateProjectCommand().ExecuteAsync(
            new CreateProjectRequest(request.ProjectRoot, request.ProjectName, request.ProjectCapabilities),
            context);
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
            var scene = await new CreateSceneCommand().ExecuteAsync(
                new CreateSceneRequest(request.ProjectRoot, requestedScene.Name, requestedScene.Capabilities),
                context);
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
        return RekallAgeCommandResult<CreateBlueprintProjectResult>.Success(
            new CreateBlueprintProjectResult(project.Value, first.Scene, first.Blueprint)
            {
                Scenes = createdScenes
            },
            $"Created project '{request.ProjectName}' with {createdScenes.Count} scene(s) and {requestedScenes.Sum(scene => scene.Entities.Count)} generic blueprint entities.");
    }
}
