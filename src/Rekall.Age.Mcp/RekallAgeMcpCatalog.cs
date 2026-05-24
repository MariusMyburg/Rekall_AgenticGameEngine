using Rekall.Age.Core.Commands;

namespace Rekall.Age.Mcp;

public sealed record RekallAgeMcpCatalog(IReadOnlyList<RekallAgeMcpTool> Tools)
{
    public static RekallAgeMcpCatalog FromRegistry(RekallAgeCommandRegistry registry)
    {
        var tools = registry.Schemas
            .Select(schema => new RekallAgeMcpTool(
                schema.Name,
                schema.Description,
                schema.RequestType,
                schema.ResultType))
            .OrderBy(tool => tool.Name, StringComparer.Ordinal)
            .ToArray();

        return new RekallAgeMcpCatalog(tools);
    }
}
