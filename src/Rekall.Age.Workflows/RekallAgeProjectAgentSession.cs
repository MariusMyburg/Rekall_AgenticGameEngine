using System.Text.Json;
using System.Text.Json.Nodes;
using Rekall.Age.Agent.LanguageModels;
using Rekall.Age.Core.Commands;
using Rekall.Age.Mcp;

namespace Rekall.Age.Workflows;

public sealed record RekallAgeProjectAgentSessionRequest(
    string ProjectRoot,
    string SceneName,
    string Model,
    string Task)
{
    public int MaxTurns { get; init; } = 24;
    public string? Think { get; init; } = "medium";
    public double? Temperature { get; init; }
    public bool RequireCompletionAudit { get; init; } = true;
    public bool TreatGauntletAsTerminalSuccess { get; init; }
}

public sealed record RekallAgeProjectAgentSessionResult(
    bool Succeeded,
    string Summary,
    RekallAgeLanguageModelAgentResult AgentResult);

public sealed class RekallAgeProjectAgentSession
{
    private readonly IRekallAgeLanguageModelClient _modelClient;
    private readonly RekallAgeCommandRegistry _registry;

    public RekallAgeProjectAgentSession(
        IRekallAgeLanguageModelClient modelClient,
        RekallAgeCommandRegistry registry)
    {
        _modelClient = modelClient ?? throw new ArgumentNullException(nameof(modelClient));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public ValueTask<IReadOnlyList<RekallAgeLanguageModelInfo>> ListModelsAsync(CancellationToken cancellationToken) =>
        _modelClient.ListModelsAsync(cancellationToken);

    public async ValueTask<RekallAgeProjectAgentSessionResult> RunAsync(
        RekallAgeProjectAgentSessionRequest request,
        IProgress<RekallAgeLanguageModelAgentProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProjectRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SceneName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Model);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Task);

        var projectRoot = Path.GetFullPath(request.ProjectRoot);
        var tools = new ProjectScopedToolExecutor(
            projectRoot,
            request.SceneName,
            _registry,
            new RekallAgeMcpAgentToolExecutor(_registry, "rekall-studio-agent", progressiveDiscovery: true));
        var agent = new RekallAgeLanguageModelAgent(_modelClient, tools);
        var scopedTask = $"""
            Open project root: {projectRoot}
            Active scene: {request.SceneName}

            {request.Task.Trim()}

            Work only inside the open project root above. Use canonical Rekall AGE tools to inspect and author the game; do not invent engine operations or author outside that root. Preserve generic engine architecture and put game-specific behavior in project modules or authored scene content.
            """;
        var result = await agent.RunAsync(
            new RekallAgeLanguageModelAgentRequest(
                request.Model,
                RekallAgeEmbeddedAgentContract.SystemPrompt,
                scopedTask)
            {
                MaxTurns = request.MaxTurns,
                Think = request.Think,
                Temperature = request.Temperature,
                RequireCompletionAudit = request.RequireCompletionAudit,
                RequireRuntimeBehaviorAssertions = !request.TreatGauntletAsTerminalSuccess,
                Progress = progress,
                CompletionAuditPrimingTools = new HashSet<string>(
                    ["rekall.workflow.audit_playable_package"],
                    StringComparer.Ordinal),
                TerminalSuccessTools = request.TreatGauntletAsTerminalSuccess
                    ? new HashSet<string>(["rekall.workflow.agent_authoring_gauntlet"], StringComparer.Ordinal)
                    : new HashSet<string>(StringComparer.Ordinal)
            },
            cancellationToken);
        var failedTools = result.ToolExecutions.Count(execution => !execution.Succeeded);
        var securityBoundaryFailures = result.ToolExecutions.Count(execution =>
            !execution.Succeeded
            && execution.ResultPreview.Contains(
                "REKALL_AGENT_PROJECT_SCOPE_VIOLATION",
                StringComparison.Ordinal));
        var succeeded = result.Completed
            && securityBoundaryFailures == 0
            && (request.RequireCompletionAudit || failedTools == 0);
        var summary = succeeded
            ? failedTools == 0
                ? $"AI authoring completed in {result.Turns} turns with {result.ToolCallCount} tool calls."
                : $"AI authoring completed its evidence audit after recovering from {failedTools} failed tool call{(failedTools == 1 ? string.Empty : "s")}."
            : result.Completed
                ? $"AI authoring stopped with {failedTools} failed tool call{(failedTools == 1 ? string.Empty : "s")}."
                : $"AI authoring stopped: {result.StopReason}.";
        return new RekallAgeProjectAgentSessionResult(succeeded, summary, result);
    }

    private sealed class ProjectScopedToolExecutor(
        string projectRoot,
        string sceneName,
        RekallAgeCommandRegistry registry,
        IRekallAgeAgentToolExecutor inner) : IRekallAgeAgentToolExecutor
    {
        private readonly string _projectRoot = Normalize(projectRoot);
        private readonly string _sceneName = sceneName;
        private readonly IReadOnlySet<string> _projectScopedTools = ToolsWithProperty(registry, "ProjectRoot");
        private readonly IReadOnlySet<string> _sceneScopedTools = ToolsWithProperty(registry, "SceneName");

        public IReadOnlyList<RekallAgeLanguageModelTool> Tools => inner.Tools;

        public ValueTask<JsonNode> ExecuteAsync(
            string name,
            JsonObject arguments,
            CancellationToken cancellationToken)
        {
            var scopedArguments = (JsonObject)arguments.DeepClone();
            if (_projectScopedTools.Contains(name)
                && (!scopedArguments.TryGetPropertyValue("projectRoot", out var suppliedRoot) || suppliedRoot is null))
            {
                scopedArguments["projectRoot"] = _projectRoot;
            }
            if (_sceneScopedTools.Contains(name)
                && (!scopedArguments.TryGetPropertyValue("sceneName", out var suppliedScene) || suppliedScene is null))
            {
                scopedArguments["sceneName"] = _sceneName;
            }

            foreach (var candidate in FindProjectRoots(scopedArguments))
            {
                string normalized;
                try
                {
                    normalized = Normalize(candidate);
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    return ValueTask.FromResult<JsonNode>(ScopeViolation(candidate));
                }

                if (!normalized.Equals(_projectRoot, PathComparison))
                {
                    return ValueTask.FromResult<JsonNode>(ScopeViolation(candidate));
                }
            }

            return inner.ExecuteAsync(name, scopedArguments, cancellationToken);
        }

        private static IReadOnlySet<string> ToolsWithProperty(
            RekallAgeCommandRegistry registry,
            string propertyName) =>
            registry.RegisteredCommands
                .Where(command => command.RequestType.GetProperties().Any(property =>
                    property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase)))
                .Select(command => command.Schema.Name)
                .ToHashSet(StringComparer.Ordinal);

        private JsonObject ScopeViolation(string candidate) => new()
        {
            ["ok"] = false,
            ["summary"] = "The embedded agent attempted to operate outside the open project.",
            ["errors"] = new JsonArray(new JsonObject
            {
                ["code"] = "REKALL_AGENT_PROJECT_SCOPE_VIOLATION",
                ["message"] = "ProjectRoot arguments must resolve to the project opened in Studio.",
                ["target"] = candidate
            })
        };

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
                        && json.Length <= 1_000_000)
                    {
                        JsonNode? decoded = null;
                        try
                        {
                            decoded = JsonNode.Parse(json);
                        }
                        catch (JsonException)
                        {
                            // The inner executor returns its bounded malformed-argument diagnostic.
                        }

                        if (decoded is not null)
                        {
                            foreach (var nested in FindProjectRoots(decoded)) yield return nested;
                        }
                    }

                    if (property.Value is not null)
                    {
                        foreach (var nested in FindProjectRoots(property.Value)) yield return nested;
                    }
                }
            }
            else if (node is JsonArray array)
            {
                foreach (var item in array.Where(item => item is not null))
                {
                    foreach (var nested in FindProjectRoots(item!)) yield return nested;
                }
            }
        }

        private static string Normalize(string path) =>
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

        private static StringComparison PathComparison => OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }
}
