using Rekall.Age.Core.Commands;

namespace Rekall.Age.World.Commands;

public sealed record CreateSceneRequest(
    string ProjectRoot,
    string Name,
    IReadOnlyList<string> Capabilities);

public sealed record CreateSceneResult(
    string ScenePath,
    RekallAgeSceneDocument Scene);

public sealed class CreateSceneCommand : IRekallAgeCommand<CreateSceneRequest, CreateSceneResult>
{
    private readonly RekallAgeSceneStore _store = new();

    public string Name => "rekall.scene.create";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Creates a Rekall AGE scene.",
        typeof(CreateSceneRequest).FullName!,
        typeof(CreateSceneResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<CreateSceneResult>> ExecuteAsync(
        CreateSceneRequest request,
        RekallAgeCommandContext context)
    {
        var scene = RekallAgeSceneDocument.Create(request.Name, request.Capabilities);
        await _store.SaveAsync(request.ProjectRoot, scene, context.CancellationToken);
        var path = _store.GetScenePath(request.ProjectRoot, scene.Name);
        context.Transaction.RecordChangedResource(path);
        return RekallAgeCommandResult<CreateSceneResult>.Success(
            new CreateSceneResult(path, scene),
            $"Created scene '{scene.Name}'.");
    }
}
