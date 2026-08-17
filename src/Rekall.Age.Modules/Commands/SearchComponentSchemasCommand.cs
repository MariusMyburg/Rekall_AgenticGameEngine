using System.Reflection;
using Rekall.Age.Core.Commands;

namespace Rekall.Age.Modules.Commands;

public sealed record SearchComponentSchemasRequest(string Query, string? ProjectRoot = null, int Limit = 12);

public sealed record SearchComponentSchemasResult(IReadOnlyList<RekallAgeComponentSchema> Components);

public sealed class SearchComponentSchemasCommand
    : IRekallAgeCommand<SearchComponentSchemasRequest, SearchComponentSchemasResult>
{
    private readonly Assembly[] _assemblies;

    public SearchComponentSchemasCommand()
        : this(AppDomain.CurrentDomain.GetAssemblies())
    {
    }

    public SearchComponentSchemasCommand(params Assembly[] assemblies)
    {
        _assemblies = assemblies;
    }

    public string Name => "rekall.module.search_component_schemas";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Searches agent-readable built-in and project component schemas by runtime type, display name, description, or property name.",
        typeof(SearchComponentSchemasRequest).FullName!,
        typeof(SearchComponentSchemasResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<SearchComponentSchemasResult>> ExecuteAsync(
        SearchComponentSchemasRequest request,
        RekallAgeCommandContext context)
    {
        var listed = await new ListComponentSchemasCommand(_assemblies).ExecuteAsync(
            new ListComponentSchemasRequest(ProjectRoot: request.ProjectRoot),
            context);
        if (!listed.Ok)
        {
            return RekallAgeCommandResult<SearchComponentSchemasResult>.Failure(
                new SearchComponentSchemasResult([]),
                listed.Summary,
                listed.Errors);
        }

        var terms = request.Query.Split([' ', '.', '_', '-', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var matches = listed.Value.Components
            .Select(component => new
            {
                Component = component,
                SearchText = string.Join(' ',
                    component.TypeName,
                    component.DisplayName,
                    component.Description,
                    string.Join(' ', component.Properties.Select(property => $"{property.Name} {property.Kind} {property.Description}")))
            })
            .Select(item => new
            {
                item.Component,
                Score = terms.Sum(term => item.SearchText.Contains(term, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            })
            .Where(item => terms.Length == 0 || item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Component.TypeName, StringComparer.Ordinal)
            .Take(Math.Clamp(request.Limit, 1, 64))
            .Select(item => item.Component)
            .ToArray();
        return RekallAgeCommandResult<SearchComponentSchemasResult>.Success(
            new SearchComponentSchemasResult(matches),
            $"Found {matches.Length} component schemas for '{request.Query}'.");
    }
}
