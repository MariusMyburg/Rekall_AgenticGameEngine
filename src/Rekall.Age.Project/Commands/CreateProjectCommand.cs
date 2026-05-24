using Rekall.Age.Core.Commands;

namespace Rekall.Age.Project.Commands;

public sealed record CreateProjectRequest(
    string ProjectRoot,
    string Name,
    IReadOnlyList<string> Capabilities);

public sealed record CreateProjectResult(
    string ManifestPath,
    RekallAgeProjectManifest Manifest);

public sealed class CreateProjectCommand : IRekallAgeCommand<CreateProjectRequest, CreateProjectResult>
{
    private readonly RekallAgeProjectStore _store = new();

    public string Name => "rekall.project.create";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Creates a Rekall AGE project manifest.",
        typeof(CreateProjectRequest).FullName!,
        typeof(CreateProjectResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<CreateProjectResult>> ExecuteAsync(
        CreateProjectRequest request,
        RekallAgeCommandContext context)
    {
        var manifest = RekallAgeProjectManifest.Create(request.Name, request.Capabilities);
        await _store.SaveAsync(request.ProjectRoot, manifest, context.CancellationToken);
        var manifestPath = Path.Combine(request.ProjectRoot, RekallAgeProjectStore.ManifestFileName);
        context.Transaction.RecordChangedResource(manifestPath);

        return RekallAgeCommandResult<CreateProjectResult>.Success(
            new CreateProjectResult(manifestPath, manifest),
            $"Created Rekall AGE project '{manifest.Name}'.");
    }
}
