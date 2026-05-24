using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;

namespace Rekall.Age.Mcp;

public sealed class RekallAgeMcpJsonRpcServer
{
    public const string ProtocolVersion = "2025-06-18";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly RekallAgeCommandRegistry _registry;

    public RekallAgeMcpJsonRpcServer(RekallAgeCommandRegistry registry)
    {
        _registry = registry;
    }

    public async ValueTask<string?> HandleJsonLineAsync(
        string line,
        RekallAgeCommandContext context)
    {
        line = line.TrimStart('\uFEFF');
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        var id = root.TryGetProperty("id", out var idElement) ? idElement.Clone() : (JsonElement?)null;
        var method = root.GetProperty("method").GetString();

        if (id is null)
        {
            return null;
        }

        return method switch
        {
            "initialize" => SerializeResponse(id.Value, CreateInitializeResult()),
            "ping" => SerializeResponse(id.Value, new { }),
            "tools/list" => SerializeResponse(id.Value, new { tools = CreateTools() }),
            "tools/call" => await HandleToolCallAsync(id.Value, root, context),
            _ => SerializeError(id.Value, -32601, $"Method not found: {method}")
        };
    }

    public async Task RunStdioAsync(
        TextReader input,
        TextWriter output,
        RekallAgeCommandContext context)
    {
        while (await input.ReadLineAsync(context.CancellationToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var response = await HandleJsonLineAsync(line, context);
            if (response is not null)
            {
                await output.WriteLineAsync(response);
                await output.FlushAsync();
            }
        }
    }

    private async ValueTask<string> HandleToolCallAsync(
        JsonElement id,
        JsonElement root,
        RekallAgeCommandContext context)
    {
        if (!root.TryGetProperty("params", out var parameters)
            || !parameters.TryGetProperty("name", out var nameElement)
            || nameElement.GetString() is not { Length: > 0 } name)
        {
            return SerializeError(id, -32602, "Tool call is missing a tool name.");
        }

        if (!_registry.RegisteredCommands.Any(command => command.Schema.Name.Equals(name, StringComparison.Ordinal)))
        {
            return SerializeError(id, -32602, $"Unknown tool: {name}");
        }

        var argumentsJson = parameters.TryGetProperty("arguments", out var arguments)
            ? arguments.GetRawText()
            : "{}";
        var commandResult = await _registry.ExecuteJsonAsync(name, argumentsJson, context);
        var result = new
        {
            content = new[]
            {
                new
                {
                    type = "text",
                    text = JsonSerializer.Serialize(commandResult, JsonOptions)
                }
            },
            structuredContent = commandResult,
            isError = !commandResult.Ok
        };

        return SerializeResponse(id, result);
    }

    private object CreateInitializeResult()
    {
        return new
        {
            protocolVersion = ProtocolVersion,
            capabilities = new
            {
                tools = new
                {
                    listChanged = false
                }
            },
            serverInfo = new
            {
                name = "rekall-age",
                title = "Rekall AGE",
                version = "0.1.0"
            },
            instructions = "Use Rekall AGE tools to create, inspect, validate, run, and capture agent-authored games."
        };
    }

    private IReadOnlyList<object> CreateTools()
    {
        return _registry.RegisteredCommands
            .Select(command => new
            {
                name = command.Schema.Name,
                title = ToTitle(command.Schema.Name),
                description = command.Schema.Description,
                inputSchema = CreateInputSchema(command.RequestType)
            })
            .ToArray();
    }

    private static JsonObject CreateInputSchema(Type requestType)
    {
        var properties = new JsonObject();
        var required = new JsonArray();

        var requestProperties = requestType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.GetMethod is not null)
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToArray();
        var requiredNames = GetRequiredPropertyNames(requestType, requestProperties);

        foreach (var property in requestProperties)
        {
            var name = ToCamelCase(property.Name);
            properties[name] = CreatePropertySchema(property.PropertyType, []);
            if (requiredNames.Contains(name))
            {
                required.Add(name);
            }
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required
        };
    }

    private static IReadOnlySet<string> GetRequiredPropertyNames(Type requestType, IReadOnlyList<PropertyInfo> properties)
    {
        var constructor = requestType.GetConstructors()
            .OrderByDescending(item => item.GetParameters().Length)
            .FirstOrDefault();
        if (constructor is null)
        {
            return properties.Select(property => ToCamelCase(property.Name)).ToHashSet(StringComparer.Ordinal);
        }

        return constructor.GetParameters()
            .Where(parameter => !parameter.HasDefaultValue)
            .Select(parameter => parameter.Name ?? string.Empty)
            .Where(name => name.Length > 0)
            .Select(ToCamelCase)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static JsonObject CreatePropertySchema(Type type, HashSet<Type> visited)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (typeof(JsonNode).IsAssignableFrom(underlying))
        {
            return [];
        }

        if (underlying == typeof(string))
        {
            return new JsonObject { ["type"] = "string" };
        }

        if (underlying == typeof(bool))
        {
            return new JsonObject { ["type"] = "boolean" };
        }

        if (underlying == typeof(int)
            || underlying == typeof(long)
            || underlying == typeof(short)
            || underlying == typeof(uint)
            || underlying == typeof(ulong)
            || underlying == typeof(ushort))
        {
            return new JsonObject { ["type"] = "integer" };
        }

        if (underlying == typeof(float)
            || underlying == typeof(double)
            || underlying == typeof(decimal))
        {
            return new JsonObject { ["type"] = "number" };
        }

        if (underlying != typeof(string)
            && typeof(System.Collections.IEnumerable).IsAssignableFrom(underlying))
        {
            var itemType = GetEnumerableItemType(underlying);
            return new JsonObject
            {
                ["type"] = "array",
                ["items"] = itemType is null ? new JsonObject() : CreatePropertySchema(itemType, visited)
            };
        }

        var objectProperties = underlying.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.GetMethod is not null && property.GetIndexParameters().Length == 0)
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToArray();
        if (objectProperties.Length > 0 && visited.Add(underlying))
        {
            var properties = new JsonObject();
            var required = new JsonArray();
            var requiredNames = GetRequiredPropertyNames(underlying, objectProperties);
            foreach (var property in objectProperties)
            {
                var name = ToCamelCase(property.Name);
                properties[name] = CreatePropertySchema(property.PropertyType, visited);
                if (requiredNames.Contains(name))
                {
                    required.Add(name);
                }
            }

            visited.Remove(underlying);
            return new JsonObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = required
            };
        }

        return new JsonObject { ["type"] = "object" };
    }

    private static Type? GetEnumerableItemType(Type type)
    {
        if (type.IsArray)
        {
            return type.GetElementType();
        }

        return type.GetInterfaces()
            .Prepend(type)
            .FirstOrDefault(candidate =>
                candidate.IsGenericType
                && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            ?.GetGenericArguments()[0];
    }

    private static string SerializeResponse(JsonElement id, object result)
    {
        return JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result }, JsonOptions);
    }

    private static string SerializeError(JsonElement id, int code, string message)
    {
        return JsonSerializer.Serialize(new { jsonrpc = "2.0", id, error = new { code, message } }, JsonOptions);
    }

    private static string ToCamelCase(string value)
    {
        return value.Length == 0 ? value : char.ToLowerInvariant(value[0]) + value[1..];
    }

    private static string ToTitle(string name)
    {
        return name.Replace("rekall.", string.Empty, StringComparison.Ordinal)
            .Replace('.', ' ');
    }
}
