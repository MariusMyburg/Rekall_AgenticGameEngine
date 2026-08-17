using Rekall.Age.Core.Commands;
using Rekall.Age.Project.Commands;
using Rekall.Age.World.Commands;

namespace Rekall.Age.Workflows.Commands;

public sealed record CreateBlueprintProjectRequest(
    string ProjectRoot,
    string ProjectName,
    IReadOnlyList<string> ProjectCapabilities,
    string SceneName,
    IReadOnlyList<string> SceneCapabilities,
    IReadOnlyList<RekallAgeSceneBlueprintEntity> Entities);

public sealed record CreateBlueprintProjectResult(
    CreateProjectResult Project,
    CreateSceneResult Scene,
    ApplySceneBlueprintResult Blueprint);

public sealed class CreateBlueprintProjectCommand
    : IRekallAgeCommand<CreateBlueprintProjectRequest, CreateBlueprintProjectResult>
{
    public string Name => "rekall.workflow.create_blueprint_project";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Atomically creates a project and scene from a complete agent-supplied generic entity/component blueprint.",
        typeof(CreateBlueprintProjectRequest).FullName!,
        typeof(CreateBlueprintProjectResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<CreateBlueprintProjectResult>> ExecuteAsync(
        CreateBlueprintProjectRequest request,
        RekallAgeCommandContext context)
    {
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

        var scene = await new CreateSceneCommand().ExecuteAsync(
            new CreateSceneRequest(request.ProjectRoot, request.SceneName, request.SceneCapabilities),
            context);
        if (!scene.Ok)
        {
            return RekallAgeCommandResult<CreateBlueprintProjectResult>.Failure(default!, scene.Summary, scene.Errors);
        }

        var blueprint = await new ApplySceneBlueprintCommand().ExecuteAsync(
            new ApplySceneBlueprintRequest(request.ProjectRoot, request.SceneName, request.Entities, ClearExisting: true),
            context);
        if (!blueprint.Ok)
        {
            return RekallAgeCommandResult<CreateBlueprintProjectResult>.Failure(default!, blueprint.Summary, blueprint.Errors);
        }

        return RekallAgeCommandResult<CreateBlueprintProjectResult>.Success(
            new CreateBlueprintProjectResult(project.Value, scene.Value, blueprint.Value),
            $"Created project '{request.ProjectName}' and scene '{request.SceneName}' from {request.Entities.Count} generic blueprint entities.");
    }
}
