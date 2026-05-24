using Rekall.Age.Core.Commands;
using Rekall.Age.Project;
using Rekall.Age.World;

namespace Rekall.Age.GameTemplates.Commands;

public sealed record CreateGameFromTemplateRequest(
    string ProjectRoot,
    string ProjectName,
    string TemplateId);

public sealed record CreateGameFromTemplateResult(
    RekallAgeGameTemplate Template,
    RekallAgeProjectManifest Manifest,
    RekallAgeSceneDocument Scene);

public sealed class CreateGameFromTemplateCommand
    : IRekallAgeCommand<CreateGameFromTemplateRequest, CreateGameFromTemplateResult>
{
    private readonly RekallAgeGameTemplateCatalog _catalog;
    private readonly RekallAgeProjectStore _projectStore = new();
    private readonly RekallAgeSceneStore _sceneStore = new();

    public CreateGameFromTemplateCommand()
        : this(RekallAgeGameTemplateCatalog.CreateDefault())
    {
    }

    public CreateGameFromTemplateCommand(RekallAgeGameTemplateCatalog catalog)
    {
        _catalog = catalog;
    }

    public string Name => "rekall.workflow.create_game_from_template";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Creates a complete starter game project from a built-in Rekall AGE template.",
        typeof(CreateGameFromTemplateRequest).FullName!,
        typeof(CreateGameFromTemplateResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<CreateGameFromTemplateResult>> ExecuteAsync(
        CreateGameFromTemplateRequest request,
        RekallAgeCommandContext context)
    {
        var template = _catalog.GetRequired(request.TemplateId);
        var manifest = RekallAgeProjectManifest.Create(request.ProjectName, template.Capabilities)
            with
            {
                SourceTemplateId = template.Id
            };
        var scene = RekallAgeSceneDocument.Create("Main", template.Capabilities);
        foreach (var entity in template.Entities)
        {
            scene = scene.AddEntity(entity);
        }

        await _projectStore.SaveAsync(request.ProjectRoot, manifest, context.CancellationToken);
        await _sceneStore.SaveAsync(request.ProjectRoot, scene, context.CancellationToken);

        context.Transaction.RecordChangedResource(Path.Combine(request.ProjectRoot, RekallAgeProjectStore.ManifestFileName));
        context.Transaction.RecordChangedResource(_sceneStore.GetScenePath(request.ProjectRoot, scene.Name));

        return RekallAgeCommandResult<CreateGameFromTemplateResult>.Success(
            new CreateGameFromTemplateResult(template, manifest, scene),
            $"Created {template.Id} game '{manifest.Name}'.");
    }
}
