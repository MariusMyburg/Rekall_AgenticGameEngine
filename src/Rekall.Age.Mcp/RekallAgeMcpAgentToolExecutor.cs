using System.Text.Json;
using System.Text.Json.Nodes;
using Rekall.Age.Agent.LanguageModels;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;

namespace Rekall.Age.Mcp;

public sealed class RekallAgeMcpAgentToolExecutor : IRekallAgeAgentToolExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly RekallAgeCommandRegistry _registry;
    private readonly string _actor;
    private readonly IReadOnlyDictionary<string, RekallAgeLanguageModelTool> _allTools;
    private readonly HashSet<string>? _exposedTools;
    private const string SearchToolName = "rekall.tools.search";

    public RekallAgeMcpAgentToolExecutor(
        RekallAgeCommandRegistry registry,
        string actor = "rekall-embedded-agent",
        bool progressiveDiscovery = false)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _actor = string.IsNullOrWhiteSpace(actor) ? "rekall-embedded-agent" : actor.Trim();
        _allTools = registry.RegisteredCommands
            .OrderBy(command => RekallAgeMcpToolClassifier.GetAgentPriority(command.Schema.Name))
            .ThenBy(command => command.Schema.Name, StringComparer.Ordinal)
            .Select(command => new RekallAgeLanguageModelTool(
                command.Schema.Name,
                command.Schema.Description,
                RekallAgeMcpJsonRpcServer.CreateInputSchema(command.RequestType)))
            .ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        if (progressiveDiscovery)
        {
            _exposedTools = new HashSet<string>(StringComparer.Ordinal);
            if (_allTools.ContainsKey("rekall.context.engine_status"))
            {
                _exposedTools.Add("rekall.context.engine_status");
            }
        }
    }

    public IReadOnlyList<RekallAgeLanguageModelTool> Tools => _exposedTools is null
        ? _allTools.Values.OrderBy(tool => tool.Name, StringComparer.Ordinal).ToArray()
        : _exposedTools.Select(name => _allTools[name])
            .Append(SearchTool)
            .OrderBy(tool => tool.Name, StringComparer.Ordinal)
            .ToArray();

    public async ValueTask<JsonNode> ExecuteAsync(
        string name,
        JsonObject arguments,
        CancellationToken cancellationToken)
    {
        if (_exposedTools is not null && name.Equals(SearchToolName, StringComparison.Ordinal))
        {
            return SearchTools(arguments);
        }

        if (!Tools.Any(tool => tool.Name.Equals(name, StringComparison.Ordinal)))
        {
            return new JsonObject
            {
                ["ok"] = false,
                ["summary"] = $"Unknown Rekall AGE tool '{name}'.",
                ["errors"] = new JsonArray(new JsonObject
                {
                    ["code"] = "REKALL_AGENT_TOOL_UNKNOWN",
                    ["message"] = $"Tool '{name}' is not registered."
                })
            };
        }

        var transaction = RekallAgeTransaction.Begin(name);
        var context = new RekallAgeCommandContext(_actor, transaction, cancellationToken);
        var result = await _registry.ExecuteJsonAsync(name, arguments.ToJsonString(), context);
        if (result.Ok && transaction.ChangedResources.Count > 0)
        {
            var projectRoot = RekallAgeTransactionProjectRootResolver.Resolve(transaction.ChangedResources);
            if (projectRoot is not null)
            {
                await new RekallAgeTransactionLogStore().AppendAsync(
                    projectRoot,
                    transaction,
                    _actor,
                    cancellationToken);
            }
        }

        return JsonSerializer.SerializeToNode(result, JsonOptions)
            ?? new JsonObject { ["ok"] = false, ["summary"] = "Tool result serialization failed." };
    }

    private JsonNode SearchTools(JsonObject arguments)
    {
        var query = arguments["query"]?.GetValue<string>()?.Trim() ?? string.Empty;
        var maxResults = arguments["maxResults"] is JsonValue value && value.TryGetValue<int>(out var requested)
            ? Math.Clamp(requested, 1, 32)
            : 12;
        var terms = query.Split([' ', '.', '_', '-', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var matches = _allTools.Values
            .Where(tool => !_exposedTools!.Contains(tool.Name))
            .Select(tool => new
            {
                Tool = tool,
                Score = terms.Length == 0
                    ? 0
                    : terms.Sum(term =>
                        tool.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ? 3 :
                        tool.Description.Contains(term, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            })
            .Where(item => terms.Length == 0 || item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Tool.Name, StringComparer.Ordinal)
            .Take(maxResults)
            .Select(item => item.Tool)
            .ToArray();
        foreach (var tool in matches)
        {
            _exposedTools!.Add(tool.Name);
        }

        return new JsonObject
        {
            ["ok"] = true,
            ["query"] = query,
            ["matched"] = matches.Length,
            ["tools"] = new JsonArray(matches.Select(tool => (JsonNode)new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description
            }).ToArray()),
            ["instruction"] = "Matched tools are available as native tools on the next agent turn."
        };
    }

    private static RekallAgeLanguageModelTool SearchTool { get; } = new(
        SearchToolName,
        "Search Rekall AGE tool schemas by capability or task words. Matching native tools become available on the next turn.",
        new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["query"] = new JsonObject { ["type"] = "string" },
                ["maxResults"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 32 }
            },
            ["required"] = new JsonArray("query")
        });
}
