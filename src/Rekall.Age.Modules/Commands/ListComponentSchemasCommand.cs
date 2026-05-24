using System.Reflection;
using Rekall.Age.Core.Commands;

namespace Rekall.Age.Modules.Commands;

public sealed record ListComponentSchemasRequest(string? ModuleId = null, string? ProjectRoot = null);

public sealed record ListComponentSchemasResult(IReadOnlyList<RekallAgeComponentSchema> Components);

public sealed class ListComponentSchemasCommand
    : IRekallAgeCommand<ListComponentSchemasRequest, ListComponentSchemasResult>
{
    private readonly Assembly[] _assemblies;

    public ListComponentSchemasCommand()
        : this(AppDomain.CurrentDomain.GetAssemblies())
    {
    }

    public ListComponentSchemasCommand(params Assembly[] assemblies)
    {
        _assemblies = assemblies;
    }

    public string Name => "rekall.module.component_schemas";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Lists agent-readable component schemas discovered from Rekall AGE modules.",
        typeof(ListComponentSchemasRequest).FullName!,
        typeof(ListComponentSchemasResult).FullName!);

    public ValueTask<RekallAgeCommandResult<ListComponentSchemasResult>> ExecuteAsync(
        ListComponentSchemasRequest request,
        RekallAgeCommandContext context)
    {
        var assemblies = request.ProjectRoot is null
            ? _assemblies
            : _assemblies.Concat(RekallAgeProjectModuleAssemblyLoader.LoadBuiltModuleAssemblies(request.ProjectRoot));
        var index = RekallAgeModuleIndexer.IndexAssemblies(assemblies);
        var components = index.Modules
            .Where(module => request.ModuleId is null || module.Id.Equals(request.ModuleId, StringComparison.Ordinal))
            .SelectMany(module => module.Components)
            .OrderBy(component => component.TypeName, StringComparer.Ordinal)
            .ToArray();

        return ValueTask.FromResult(RekallAgeCommandResult<ListComponentSchemasResult>.Success(
            new ListComponentSchemasResult(components),
            $"Loaded {components.Length} component schemas."));
    }
}
