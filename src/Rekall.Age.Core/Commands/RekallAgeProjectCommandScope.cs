using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rekall.Age.Core.Commands;

public sealed record RekallAgeProjectCommandScopeResult(
    JsonObject Arguments,
    string? ErrorSummary,
    IReadOnlyList<RekallAgeCommandError> Errors)
{
    public bool Succeeded => Errors.Count == 0;
}

public sealed class RekallAgeProjectCommandScope
{
    public const string ScopeViolationCode = "REKALL_AGENT_PROJECT_SCOPE_VIOLATION";

    private const int MaximumEncodedGatewayArgumentsCharacters = 1_000_000;
    private readonly string _projectRoot;
    private readonly string? _sceneName;
    private readonly IReadOnlySet<string> _projectScopedTools;
    private readonly IReadOnlySet<string> _sceneScopedTools;

    public RekallAgeProjectCommandScope(
        string projectRoot,
        RekallAgeCommandRegistry registry,
        string? sceneName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentNullException.ThrowIfNull(registry);
        _projectRoot = NormalizeProjectRoot(projectRoot);
        _sceneName = sceneName;
        _projectScopedTools = ToolsWithProperty(registry, "ProjectRoot");
        _sceneScopedTools = ToolsWithProperty(registry, "SceneName");
    }

    public string ProjectRoot => _projectRoot;

    public RekallAgeProjectCommandScopeResult Apply(string toolName, JsonObject arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(arguments);

        var scopedArguments = (JsonObject)arguments.DeepClone();
        ApplyDefaults(toolName, scopedArguments);
        if (toolName.Equals("rekall.tools.execute", StringComparison.Ordinal)
            && scopedArguments["name"] is JsonValue targetValue
            && targetValue.TryGetValue<string>(out var targetName))
        {
            var gatewayResult = ReadGatewayArguments(scopedArguments["arguments"]);
            if (!gatewayResult.Succeeded)
            {
                return gatewayResult;
            }

            ApplyDefaults(targetName, gatewayResult.Arguments);
            scopedArguments["arguments"] = gatewayResult.Arguments;
        }

        foreach (var candidate in FindProjectRoots(scopedArguments))
        {
            string normalized;
            try
            {
                normalized = NormalizeProjectRoot(candidate);
            }
            catch (Exception exception) when (
                exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return ScopeViolation(scopedArguments, candidate);
            }

            if (!normalized.Equals(_projectRoot, PathComparison))
            {
                return ScopeViolation(scopedArguments, candidate);
            }
        }

        return new RekallAgeProjectCommandScopeResult(scopedArguments, null, []);
    }

    public static string NormalizeProjectRoot(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private void ApplyDefaults(string toolName, JsonObject arguments)
    {
        if (_projectScopedTools.Contains(toolName)
            && (!arguments.TryGetPropertyValue("projectRoot", out var suppliedRoot) || suppliedRoot is null))
        {
            arguments["projectRoot"] = _projectRoot;
        }

        if (_sceneName is not null
            && _sceneScopedTools.Contains(toolName)
            && (!arguments.TryGetPropertyValue("sceneName", out var suppliedScene) || suppliedScene is null))
        {
            arguments["sceneName"] = _sceneName;
        }
    }

    private static RekallAgeProjectCommandScopeResult ReadGatewayArguments(JsonNode? node)
    {
        if (node is JsonObject objectArguments)
        {
            return new RekallAgeProjectCommandScopeResult(
                (JsonObject)objectArguments.DeepClone(),
                null,
                []);
        }

        if (node is JsonValue encoded && encoded.TryGetValue<string>(out var json))
        {
            if (json.Length > MaximumEncodedGatewayArgumentsCharacters)
            {
                return GatewayArgumentError(
                    "REKALL_AGENT_ARGUMENTS_TOO_LARGE",
                    $"Encoded gateway arguments exceed the {MaximumEncodedGatewayArgumentsCharacters:N0}-character agent safety limit.");
            }

            try
            {
                if (JsonNode.Parse(json) is JsonObject decoded)
                {
                    return new RekallAgeProjectCommandScopeResult(decoded, null, []);
                }
            }
            catch (JsonException)
            {
                // Return the same bounded fail-closed diagnostic as other invalid shapes.
            }
        }

        if (node is null)
        {
            return new RekallAgeProjectCommandScopeResult(new JsonObject(), null, []);
        }

        return GatewayArgumentError(
            "REKALL_AGENT_ARGUMENTS_INVALID",
            "Gateway arguments must be a JSON object or a JSON string encoding an object.");
    }

    private static RekallAgeProjectCommandScopeResult GatewayArgumentError(string code, string message) => new(
        new JsonObject(),
        message,
        [new RekallAgeCommandError(code, message, "rekall.tools.execute.arguments")]);

    private static IReadOnlySet<string> ToolsWithProperty(
        RekallAgeCommandRegistry registry,
        string propertyName) =>
        registry.RegisteredCommands
            .Where(command => command.RequestType.GetProperties().Any(property =>
                property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase)))
            .Select(command => command.Schema.Name)
            .ToHashSet(StringComparer.Ordinal);

    private static RekallAgeProjectCommandScopeResult ScopeViolation(JsonObject arguments, string candidate)
    {
        const string message = "ProjectRoot arguments must resolve to the selected project.";
        return new RekallAgeProjectCommandScopeResult(
            arguments,
            "The agent attempted to operate outside the selected project.",
            [new RekallAgeCommandError(ScopeViolationCode, message, candidate)]);
    }

    private static IEnumerable<string> FindProjectRoots(JsonNode node)
    {
        if (node is JsonObject value)
        {
            foreach (var property in value)
            {
                if (property.Key.Equals("projectRoot", StringComparison.OrdinalIgnoreCase)
                    && property.Value is JsonValue scalar
                    && scalar.TryGetValue<string>(out var root)
                    && !string.IsNullOrWhiteSpace(root))
                {
                    yield return root;
                }

                if (property.Key.Equals("arguments", StringComparison.OrdinalIgnoreCase)
                    && property.Value is JsonValue encoded
                    && encoded.TryGetValue<string>(out var json)
                    && json.Length <= MaximumEncodedGatewayArgumentsCharacters)
                {
                    JsonNode? decoded = null;
                    try
                    {
                        decoded = JsonNode.Parse(json);
                    }
                    catch (JsonException)
                    {
                        // The command dispatcher returns its bounded malformed-argument diagnostic.
                    }

                    if (decoded is not null)
                    {
                        foreach (var nested in FindProjectRoots(decoded))
                        {
                            yield return nested;
                        }
                    }
                }

                if (property.Value is not null)
                {
                    foreach (var nested in FindProjectRoots(property.Value))
                    {
                        yield return nested;
                    }
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array.Where(item => item is not null))
            {
                foreach (var nested in FindProjectRoots(item!))
                {
                    yield return nested;
                }
            }
        }
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}
