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
                schema.ResultType,
                RekallAgeMcpToolClassifier.GetCategory(schema.Name),
                RekallAgeMcpToolClassifier.IsRecommended(schema.Name),
                RekallAgeMcpToolClassifier.GetAgentPriority(schema.Name)))
            .OrderBy(tool => tool.AgentPriority)
            .ThenBy(tool => tool.Name, StringComparer.Ordinal)
            .ToArray();

        return new RekallAgeMcpCatalog(tools);
    }
}

internal static class RekallAgeMcpToolClassifier
{
    public static string GetCategory(string name)
    {
        if (name.StartsWith("rekall.context.", StringComparison.Ordinal))
        {
            return "context";
        }

        if (name.StartsWith("rekall.templates.", StringComparison.Ordinal))
        {
            return "templates";
        }

        if (name.StartsWith("rekall.workflow.", StringComparison.Ordinal))
        {
            return "workflow";
        }

        if (name.StartsWith("rekall.render.", StringComparison.Ordinal))
        {
            return "rendering";
        }

        if (name.StartsWith("rekall.module.", StringComparison.Ordinal))
        {
            return "modules";
        }

        if (name.StartsWith("rekall.play", StringComparison.Ordinal))
        {
            return "playtesting";
        }

        if (name.StartsWith("rekall.asset.", StringComparison.Ordinal))
        {
            return "assets";
        }

        if (name.StartsWith("rekall.project.", StringComparison.Ordinal) ||
            name.StartsWith("rekall.scene.", StringComparison.Ordinal) ||
            name.StartsWith("rekall.entity.", StringComparison.Ordinal) ||
            name.StartsWith("rekall.component.", StringComparison.Ordinal))
        {
            return "world";
        }

        return "unknown";
    }

    public static bool IsRecommended(string name)
    {
        return name is
            "rekall.context.engine_status" or
            "rekall.templates.inspect" or
            "rekall.workflow.create_playable_package_from_template";
    }

    public static int GetAgentPriority(string name)
    {
        return name switch
        {
            "rekall.context.engine_status" => 5,
            "rekall.templates.inspect" => 8,
            "rekall.workflow.create_playable_package_from_template" => 10,
            "rekall.templates.verify_mvp" => 20,
            _ when name.StartsWith("rekall.workflow.", StringComparison.Ordinal) => 30,
            _ when name.StartsWith("rekall.playtest.", StringComparison.Ordinal) => 40,
            _ when name.StartsWith("rekall.module.", StringComparison.Ordinal) => 50,
            _ when name.StartsWith("rekall.render.", StringComparison.Ordinal) => 70,
            _ => 100
        };
    }
}
