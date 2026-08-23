using Rekall.Age.Core.Commands;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling.Commands;

public sealed record SearchMeshOperationTypesRequest(string Query = "", int MaximumResults = 32);
public sealed record SearchMeshOperationTypesResult(IReadOnlyList<RekallAgeMeshOperationDescriptor> OperationTypes, bool Truncated);
public sealed class SearchMeshOperationTypesCommand : IRekallAgeCommand<SearchMeshOperationTypesRequest, SearchMeshOperationTypesResult>
{
    private readonly RekallAgeMeshOperationExecutor _executor = new(); public string Name => "rekall.mesh.operation_types.search";
    public RekallAgeCommandSchema Schema => new(Name, "Searches generic semantic mesh operation contracts by ID or description, returning domains, possible change masks, and typed parameters; maximumResults must be 1-128.", typeof(SearchMeshOperationTypesRequest).FullName!, typeof(SearchMeshOperationTypesResult).FullName!);
    public ValueTask<RekallAgeCommandResult<SearchMeshOperationTypesResult>> ExecuteAsync(SearchMeshOperationTypesRequest request, RekallAgeCommandContext context)
    { if (request.MaximumResults is < 1 or > 128) throw new ArgumentException("maximumResults must be 1-128."); var q = request.Query?.Trim() ?? ""; var matches = _executor.Descriptors.Where(item => q.Length == 0 || item.OperationId.Contains(q, StringComparison.OrdinalIgnoreCase) || item.Description.Contains(q, StringComparison.OrdinalIgnoreCase)).OrderBy(item => item.OperationId, StringComparer.Ordinal).ToArray(); return ValueTask.FromResult(RekallAgeCommandResult<SearchMeshOperationTypesResult>.Success(new(matches.Take(request.MaximumResults).ToArray(), matches.Length > request.MaximumResults), $"Returned {Math.Min(matches.Length, request.MaximumResults)} of {matches.Length} mesh operation type(s).")); }
}
public sealed record InspectMeshOperationTypeRequest(string OperationId);
public sealed record InspectMeshOperationTypeResult(RekallAgeMeshOperationDescriptor? OperationType);
public sealed class InspectMeshOperationTypeCommand : IRekallAgeCommand<InspectMeshOperationTypeRequest, InspectMeshOperationTypeResult>
{
    private readonly RekallAgeMeshOperationExecutor _executor = new(); public string Name => "rekall.mesh.operation_types.inspect";
    public RekallAgeCommandSchema Schema => new(Name, "Inspects one exact semantic mesh operation contract before preview or apply.", typeof(InspectMeshOperationTypeRequest).FullName!, typeof(InspectMeshOperationTypeResult).FullName!);
    public ValueTask<RekallAgeCommandResult<InspectMeshOperationTypeResult>> ExecuteAsync(InspectMeshOperationTypeRequest request, RekallAgeCommandContext context)
    { var found = _executor.Descriptors.SingleOrDefault(item => item.OperationId == request.OperationId); return ValueTask.FromResult(found is null ? RekallAgeCommandResult<InspectMeshOperationTypeResult>.Failure(new(null), "Mesh operation type was not found.", [new("REKALL_MESH_OPERATION_TYPE_NOT_FOUND", "Mesh operation type was not found.", request.OperationId)]) : RekallAgeCommandResult<InspectMeshOperationTypeResult>.Success(new(found), $"Inspected mesh operation '{found.OperationId}'.")); }
}
