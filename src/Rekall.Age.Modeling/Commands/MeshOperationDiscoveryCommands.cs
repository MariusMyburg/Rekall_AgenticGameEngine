using Rekall.Age.Core.Commands;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling.Commands;

public sealed record SearchMeshOperationTypesRequest(string ProjectRoot, string Query = "", int MaximumResults = 32);
public sealed record SearchMeshOperationTypesResult(IReadOnlyList<RekallAgeMeshOperationDescriptor> OperationTypes, bool Truncated);
public sealed class SearchMeshOperationTypesCommand : IRekallAgeCommand<SearchMeshOperationTypesRequest, SearchMeshOperationTypesResult>
{
    public string Name => "rekall.mesh.operation_types.search";
    public RekallAgeCommandSchema Schema => new(Name, "Searches generic semantic mesh operation contracts (built-in and project-registered) by ID or description, returning domains, possible change masks, and typed parameters; maximumResults must be 1-128.", typeof(SearchMeshOperationTypesRequest).FullName!, typeof(SearchMeshOperationTypesResult).FullName!);
    public ValueTask<RekallAgeCommandResult<SearchMeshOperationTypesResult>> ExecuteAsync(SearchMeshOperationTypesRequest request, RekallAgeCommandContext context)
    {
        if (request.MaximumResults is < 1 or > 128) throw new ArgumentException("maximumResults must be 1-128.");
        var plugins = new RekallAgeProjectMeshPluginLoader().Load(request.ProjectRoot);
        var executor = new RekallAgeMeshOperationExecutor(plugins.Operations);
        var q = request.Query?.Trim() ?? "";
        var matches = executor.Descriptors.Where(item => q.Length == 0 || item.OperationId.Contains(q, StringComparison.OrdinalIgnoreCase) || item.Description.Contains(q, StringComparison.OrdinalIgnoreCase)).OrderBy(item => item.OperationId, StringComparer.Ordinal).ToArray();
        return ValueTask.FromResult(RekallAgeCommandResult<SearchMeshOperationTypesResult>.Success(new(matches.Take(request.MaximumResults).ToArray(), matches.Length > request.MaximumResults), $"Returned {Math.Min(matches.Length, request.MaximumResults)} of {matches.Length} mesh operation type(s)."));
    }
}
public sealed record InspectMeshOperationTypeRequest(string ProjectRoot, string OperationId);
public sealed record InspectMeshOperationTypeResult(RekallAgeMeshOperationDescriptor? OperationType);
public sealed class InspectMeshOperationTypeCommand : IRekallAgeCommand<InspectMeshOperationTypeRequest, InspectMeshOperationTypeResult>
{
    public string Name => "rekall.mesh.operation_types.inspect";
    public RekallAgeCommandSchema Schema => new(Name, "Inspects one exact semantic mesh operation contract (built-in or project-registered) before preview or apply.", typeof(InspectMeshOperationTypeRequest).FullName!, typeof(InspectMeshOperationTypeResult).FullName!);
    public ValueTask<RekallAgeCommandResult<InspectMeshOperationTypeResult>> ExecuteAsync(InspectMeshOperationTypeRequest request, RekallAgeCommandContext context)
    {
        var plugins = new RekallAgeProjectMeshPluginLoader().Load(request.ProjectRoot);
        var executor = new RekallAgeMeshOperationExecutor(plugins.Operations);
        var found = executor.Descriptors.SingleOrDefault(item => item.OperationId == request.OperationId);
        return ValueTask.FromResult(found is null ? RekallAgeCommandResult<InspectMeshOperationTypeResult>.Failure(new(null), "Mesh operation type was not found.", [new("REKALL_MESH_OPERATION_TYPE_NOT_FOUND", "Mesh operation type was not found.", request.OperationId)]) : RekallAgeCommandResult<InspectMeshOperationTypeResult>.Success(new(found), $"Inspected mesh operation '{found.OperationId}'."));
    }
}

public sealed record RekallAgeFractureAlgorithmSummary(string AlgorithmId, string Description);
public sealed record ListFractureAlgorithmsRequest(string ProjectRoot);
public sealed record ListFractureAlgorithmsResult(IReadOnlyList<RekallAgeFractureAlgorithmSummary> Algorithms);
public sealed class ListFractureAlgorithmsCommand : IRekallAgeCommand<ListFractureAlgorithmsRequest, ListFractureAlgorithmsResult>
{
    public string Name => "rekall.mesh.fracture_algorithms.list";
    public RekallAgeCommandSchema Schema => new(Name, "Lists the built-in Voronoi-style fracture algorithm and any project-registered fracture algorithm plugins, for use as rekall.mesh.fracture's algorithmId.", typeof(ListFractureAlgorithmsRequest).FullName!, typeof(ListFractureAlgorithmsResult).FullName!);
    public ValueTask<RekallAgeCommandResult<ListFractureAlgorithmsResult>> ExecuteAsync(ListFractureAlgorithmsRequest request, RekallAgeCommandContext context)
    {
        var plugins = new RekallAgeProjectMeshPluginLoader().Load(request.ProjectRoot);
        var algorithms = new List<RekallAgeFractureAlgorithmSummary>
        {
            new(RekallAgeMeshFractureExecutor.BuiltInVoronoiAlgorithmId, "Built-in Voronoi-style CSG fracture (splits a closed manifold mesh into N chunks around random seed points).")
        };
        algorithms.AddRange(plugins.FractureAlgorithms
            .OrderBy(item => item.AlgorithmId, StringComparer.Ordinal)
            .Select(item => new RekallAgeFractureAlgorithmSummary(item.AlgorithmId, item.GetType().FullName ?? item.AlgorithmId)));
        return ValueTask.FromResult(RekallAgeCommandResult<ListFractureAlgorithmsResult>.Success(
            new(algorithms),
            $"Listed {algorithms.Count} fracture algorithm(s)."));
    }
}
