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
    private const string ExecuteToolName = "rekall.tools.execute";
    private const int MaximumDirectlyExposedTools = 24;
    private const int MaximumEncodedGatewayArgumentsCharacters = 1_000_000;

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
            .Concat([SearchTool, ExecuteTool])
            .OrderBy(tool => tool.Name, StringComparer.Ordinal)
            .ToArray();

    public async ValueTask<JsonNode> ExecuteAsync(
        string name,
        JsonObject arguments,
        CancellationToken cancellationToken)
    {
        var attemptedName = name;
        var correctedName = TryCanonicalizeToolName(name, out var canonicalName);
        if (correctedName)
        {
            name = canonicalName;
        }

        if (_exposedTools is not null && name.Equals(SearchToolName, StringComparison.Ordinal))
        {
            return WithToolNameCorrection(SearchTools(arguments));
        }

        if (_exposedTools is not null && name.Equals(ExecuteToolName, StringComparison.Ordinal))
        {
            var targetName = arguments["name"]?.GetValue<string>() ?? string.Empty;
            if (!_allTools.ContainsKey(targetName))
            {
                return WithToolNameCorrection(UnknownTool(targetName));
            }
            if (!TryReadTargetArguments(arguments["arguments"], out var targetArguments, out var argumentError))
            {
                return WithToolNameCorrection(argumentError);
            }

            return WithToolNameCorrection(
                await ExecuteRegisteredToolAsync(targetName, targetArguments, cancellationToken));
        }

        if (!_allTools.ContainsKey(name))
        {
            return WithToolNameCorrection(UnknownTool(name));
        }

        if (arguments.ContainsKey("arguments")
            && arguments.All(property => property.Key is "name" or "arguments")
            && (arguments["name"] is null
                || arguments["name"]!.GetValue<string>().Equals(name, StringComparison.Ordinal)))
        {
            if (!TryReadTargetArguments(arguments["arguments"], out var unwrapped, out var argumentError))
            {
                return WithToolNameCorrection(argumentError);
            }

            arguments = unwrapped;
        }

        return WithToolNameCorrection(await ExecuteRegisteredToolAsync(name, arguments, cancellationToken));

        JsonNode WithToolNameCorrection(JsonNode result)
        {
            if (correctedName && result is JsonObject objectResult)
            {
                objectResult["toolNameCorrection"] = new JsonObject
                {
                    ["attempted"] = attemptedName,
                    ["canonical"] = canonicalName,
                    ["editDistance"] = 1
                };
            }

            return result;
        }
    }

    private bool TryCanonicalizeToolName(string attemptedName, out string canonicalName)
    {
        canonicalName = string.Empty;
        if (_allTools.ContainsKey(attemptedName)
            || _exposedTools is not null
            && attemptedName is SearchToolName or ExecuteToolName)
        {
            return false;
        }

        var candidates = _allTools.Keys.AsEnumerable();
        if (_exposedTools is not null)
        {
            candidates = candidates.Concat([SearchToolName, ExecuteToolName]);
        }

        var matches = candidates
            .Distinct(StringComparer.Ordinal)
            .Where(candidate => IsSingleEditAway(attemptedName, candidate))
            .Take(2)
            .ToArray();
        if (matches.Length != 1)
        {
            return false;
        }

        canonicalName = matches[0];
        return true;
    }

    private static bool IsSingleEditAway(string attempted, string candidate)
    {
        if (attempted.Equals(candidate, StringComparison.Ordinal)
            || Math.Abs(attempted.Length - candidate.Length) > 1)
        {
            return false;
        }

        var attemptedIndex = 0;
        var candidateIndex = 0;
        var edits = 0;
        while (attemptedIndex < attempted.Length && candidateIndex < candidate.Length)
        {
            if (attempted[attemptedIndex] == candidate[candidateIndex])
            {
                attemptedIndex++;
                candidateIndex++;
                continue;
            }

            if (++edits > 1)
            {
                return false;
            }

            if (attempted.Length > candidate.Length)
            {
                attemptedIndex++;
            }
            else if (candidate.Length > attempted.Length)
            {
                candidateIndex++;
            }
            else
            {
                attemptedIndex++;
                candidateIndex++;
            }
        }

        if (attemptedIndex < attempted.Length || candidateIndex < candidate.Length)
        {
            edits++;
        }

        return edits == 1;
    }

    private async ValueTask<JsonNode> ExecuteRegisteredToolAsync(
        string name,
        JsonObject arguments,
        CancellationToken cancellationToken)
    {
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

        var node = JsonSerializer.SerializeToNode(result, JsonOptions)
            ?? new JsonObject { ["ok"] = false, ["summary"] = "Tool result serialization failed." };
        var serialized = node.ToJsonString();
        if (serialized.Length <= 12_000)
        {
            return node;
        }

        return new JsonObject
        {
            ["ok"] = result.Ok,
            ["summary"] = result.Summary,
            ["errors"] = JsonSerializer.SerializeToNode(result.Errors, JsonOptions),
            ["valuePreview"] = serialized[..8_000],
            ["valueTruncated"] = true,
            ["originalCharacters"] = serialized.Length
        };
    }

    private JsonNode SearchTools(JsonObject arguments)
    {
        var query = arguments["query"]?.GetValue<string>()?.Trim() ?? string.Empty;
        var maxResults = arguments["maxResults"] is JsonValue value && value.TryGetValue<int>(out var requested)
            ? Math.Clamp(requested, 1, 32)
            : 6;
        var terms = query.Split([' ', '.', '_', '-', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var matches = _allTools.Values
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
        var directExposureBudget = Math.Max(0, MaximumDirectlyExposedTools - _exposedTools!.Count);
        _exposedTools.UnionWith(matches
            .Where(tool => !_exposedTools.Contains(tool.Name))
            .Take(directExposureBudget)
            .Select(tool => tool.Name));
        return new JsonObject
        {
            ["ok"] = true,
            ["query"] = query,
            ["matched"] = matches.Length,
            ["tools"] = new JsonArray(matches.Select(tool => (JsonNode)new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["parameters"] = tool.Parameters.DeepClone(),
                ["directlyExposed"] = _exposedTools.Contains(tool.Name)
            }).ToArray()),
            ["instruction"] = "On the next turn, call the matched native tool directly when directlyExposed is true. When it is false, call rekall.tools.execute with that exact name and an arguments object conforming to the returned parameters schema. Search can rediscover tools throughout long sessions."
        };
    }

    private JsonObject UnknownTool(string name)
    {
        var requestedTerms = ToolTerms(name);
        var suggestions = _allTools.Values
            .Select(tool => new
            {
                Tool = tool,
                Score = ToolTerms(tool.Name).Sum(candidate => requestedTerms.Any(requested =>
                    candidate.Equals(requested, StringComparison.OrdinalIgnoreCase)
                    || candidate.Length >= 5 && requested.Length >= 5
                    && candidate[..5].Equals(requested[..5], StringComparison.OrdinalIgnoreCase)) ? 1 : 0)
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Tool.Name, StringComparer.Ordinal)
            .Take(3)
            .Select(item => (JsonNode)new JsonObject
            {
                ["name"] = item.Tool.Name,
                ["description"] = item.Tool.Description
            })
            .ToArray();
        return new JsonObject
        {
            ["ok"] = false,
            ["summary"] = $"Unknown or unavailable Rekall AGE tool '{name}'.",
            ["errors"] = new JsonArray(new JsonObject
            {
                ["code"] = "REKALL_AGENT_TOOL_UNKNOWN",
                ["message"] = $"Tool '{name}' is not registered or exposed."
            }),
            ["suggestedTools"] = new JsonArray(suggestions),
            ["instruction"] = suggestions.Length == 0
                ? "Search for the required capability with rekall.tools.search."
                : "Retry with an exact suggested name and its discovered parameter schema; do not invent aliases."
        };
    }

    private static string[] ToolTerms(string name) => name.Split(
        ['.', '_', '-', '/'],
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool TryReadTargetArguments(
        JsonNode? node,
        out JsonObject arguments,
        out JsonObject error)
    {
        if (node is null)
        {
            arguments = [];
            error = [];
            return true;
        }
        if (node is JsonObject value)
        {
            arguments = value;
            error = [];
            return true;
        }
        if (node is JsonValue scalar && scalar.TryGetValue<string>(out var json))
        {
            if (json.Length > MaximumEncodedGatewayArgumentsCharacters)
            {
                arguments = [];
                error = new JsonObject
                {
                    ["ok"] = false,
                    ["summary"] = $"rekall.tools.execute encoded arguments exceed the {MaximumEncodedGatewayArgumentsCharacters:N0}-character safety limit.",
                    ["errors"] = new JsonArray(new JsonObject
                    {
                        ["code"] = "REKALL_AGENT_ARGUMENTS_TOO_LARGE",
                        ["message"] = "Provide a smaller target-tool arguments object. Large content must use a purpose-built bounded authoring operation."
                    })
                };
                return false;
            }

            try
            {
                if (JsonNode.Parse(json) is JsonObject parsed)
                {
                    arguments = parsed;
                    error = [];
                    return true;
                }
            }
            catch (JsonException)
            {
                // Return the same bounded structured error for malformed or non-object JSON.
            }
        }

        arguments = [];
        error = new JsonObject
        {
            ["ok"] = false,
            ["summary"] = "rekall.tools.execute arguments must be a JSON object or a JSON string encoding an object.",
            ["errors"] = new JsonArray(new JsonObject
            {
                ["code"] = "REKALL_AGENT_ARGUMENTS_INVALID",
                ["message"] = "Provide the target tool arguments as an object conforming to its discovered schema."
            })
        };
        return false;
    }

    private static RekallAgeLanguageModelTool SearchTool { get; } = new(
        SearchToolName,
        "Search or rediscover Rekall AGE tool schemas by capability or task words. Results state whether to call the native tool directly or through the bounded execution gateway.",
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

    private static RekallAgeLanguageModelTool ExecuteTool { get; } = new(
        ExecuteToolName,
        "Execute an exact Rekall AGE tool returned by rekall.tools.search using arguments that conform to the returned parameter schema.",
        new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["name"] = new JsonObject { ["type"] = "string" },
                ["arguments"] = new JsonObject { ["type"] = "object" }
            },
            ["required"] = new JsonArray("name", "arguments")
        });
}
