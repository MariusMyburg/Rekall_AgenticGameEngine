namespace Rekall.Age.Mcp;

public sealed record RekallAgeMcpTool(
    string Name,
    string Description,
    string RequestType,
    string ResultType,
    string Category,
    bool Recommended,
    int AgentPriority);
