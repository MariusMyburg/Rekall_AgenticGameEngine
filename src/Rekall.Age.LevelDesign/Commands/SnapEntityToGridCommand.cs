using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.World;

namespace Rekall.Age.LevelDesign.Commands;

public sealed record SnapEntityToGridRequest(
    string ProjectRoot,
    string SceneName,
    string EntityId,
    double GridSize);

public sealed record SnapEntityToGridResult(RekallAgeSceneDocument Scene);

public sealed class SnapEntityToGridCommand : IRekallAgeCommand<SnapEntityToGridRequest, SnapEntityToGridResult>
{
    private readonly RekallAgeSceneStore _store = new();

    public string Name => "rekall.level.entity.snap_to_grid";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Snaps Rekall.Transform2D x and y properties to the requested grid size.",
        typeof(SnapEntityToGridRequest).FullName!,
        typeof(SnapEntityToGridResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<SnapEntityToGridResult>> ExecuteAsync(
        SnapEntityToGridRequest request,
        RekallAgeCommandContext context)
    {
        if (request.GridSize <= 0)
        {
            var error = new RekallAgeCommandError(
                "REKALL_GRID_SIZE_INVALID",
                "Grid size must be greater than zero.",
                request.EntityId);
            return RekallAgeCommandResult<SnapEntityToGridResult>.Failure(default!, error.Message, [error]);
        }

        var scene = await _store.LoadAsync(request.ProjectRoot, request.SceneName, context.CancellationToken);
        var updated = scene.UpdateEntity(
            request.EntityId,
            entity => entity.UpdateComponent(
                "Rekall.Transform2D",
                component => component
                    .SetProperty("x", JsonValue.Create(Snap(ReadDouble(component.Properties["x"]), request.GridSize)))
                    .SetProperty("y", JsonValue.Create(Snap(ReadDouble(component.Properties["y"]), request.GridSize)))));
        await _store.SaveAsync(request.ProjectRoot, updated, context.CancellationToken);
        context.Transaction.RecordChangedResource(_store.GetScenePath(request.ProjectRoot, request.SceneName));
        return RekallAgeCommandResult<SnapEntityToGridResult>.Success(
            new SnapEntityToGridResult(updated),
            $"Snapped entity '{request.EntityId}' to grid.");
    }

    private static double ReadDouble(JsonNode? value)
    {
        return value is null ? 0 : value.GetValue<double>();
    }

    private static double Snap(double value, double grid)
    {
        return Math.Round(value / grid, MidpointRounding.AwayFromZero) * grid;
    }
}
