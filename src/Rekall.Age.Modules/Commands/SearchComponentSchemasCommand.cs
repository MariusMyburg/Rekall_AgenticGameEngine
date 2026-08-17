using System.Reflection;
using System.Text.Json.Serialization;
using Rekall.Age.Core.Commands;

namespace Rekall.Age.Modules.Commands;

public sealed record SearchComponentSchemasRequest(string Query, string? ProjectRoot = null, int Limit = 12);

public sealed record SearchComponentSchemasResult(IReadOnlyList<RekallAgeCompactComponentSchema> Components);

public sealed record RekallAgeCompactComponentSchema(
    string TypeName,
    string DisplayName,
    IReadOnlyList<RekallAgeCompactPropertySchema> Properties)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }
}

public sealed record RekallAgeCompactPropertySchema(string Name, string Kind)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AssetKind { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Minimum { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Maximum { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? AllowedValues { get; init; }
}

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
        "Searches agent-readable built-in and project component schemas by runtime type, display name, description, or property name. Put every needed component concept in one space-separated Query and raise Limit if needed; do not spend one call per concept.",
        typeof(SearchComponentSchemasRequest).FullName!,
        typeof(SearchComponentSchemasResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<SearchComponentSchemasResult>> ExecuteAsync(
        SearchComponentSchemasRequest request,
        RekallAgeCommandContext context)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            var error = new RekallAgeCommandError(
                "REKALL_COMPONENT_SCHEMA_QUERY_REQUIRED",
                "Component schema search requires a non-empty query containing all needed component concepts.",
                Name);
            return RekallAgeCommandResult<SearchComponentSchemasResult>.Failure(
                new SearchComponentSchemasResult([]),
                error.Message,
                [error]);
        }

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
                    + FamilyScore(item.Component.TypeName, terms)
            })
            .Where(item => terms.Length == 0 || item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Component.TypeName, StringComparer.Ordinal)
            .Take(Math.Clamp(request.Limit, 1, 64))
            .Select(item => Compact(item.Component))
            .ToArray();
        return RekallAgeCommandResult<SearchComponentSchemasResult>.Success(
            new SearchComponentSchemasResult(matches),
            $"Found {matches.Length} component schemas for '{request.Query}'.");
    }

    private static int FamilyScore(string typeName, IReadOnlyList<string> terms)
    {
        var physics = terms.Any(term => term.Equals("physics", StringComparison.OrdinalIgnoreCase));
        var visible = terms.Any(term => term.Equals("visible", StringComparison.OrdinalIgnoreCase)
            || term.StartsWith("render", StringComparison.OrdinalIgnoreCase));
        var camera = terms.Any(term => term.StartsWith("camera", StringComparison.OrdinalIgnoreCase));
        var lighting = terms.Any(term => term.StartsWith("light", StringComparison.OrdinalIgnoreCase));
        var score = 0;
        if (physics && (typeName.Contains("Rigidbody", StringComparison.Ordinal)
            || typeName.Contains("Collider", StringComparison.Ordinal)
            || typeName.Contains("Physics", StringComparison.Ordinal)))
        {
            score += 5;
        }
        if (physics && typeName.Contains("Transform", StringComparison.Ordinal))
        {
            score += 4;
        }
        if (visible && typeName == "Rekall.MeshRenderer")
        {
            score += 50;
        }
        else if (visible && typeName is "Rekall.SpriteRenderer" or "Rekall.GeometryPrimitive")
        {
            score += 15;
        }
        if (camera && typeName.Contains("Camera", StringComparison.Ordinal))
        {
            score += 4;
        }
        if (lighting && typeName.Contains("Light", StringComparison.Ordinal))
        {
            score += 4;
        }

        return score;
    }

    private static RekallAgeCompactComponentSchema Compact(RekallAgeComponentSchema component)
    {
        return new RekallAgeCompactComponentSchema(
            component.TypeName,
            component.DisplayName,
            component.Properties.Select(property => new RekallAgeCompactPropertySchema(property.Name, property.Kind)
            {
                AssetKind = property.AssetKind,
                Minimum = property.Minimum,
                Maximum = property.Maximum,
                Description = property.Description,
                AllowedValues = property.AllowedValues.Count == 0 ? null : property.AllowedValues
            }).ToArray())
        {
            Description = component.Description
        };
    }
}
