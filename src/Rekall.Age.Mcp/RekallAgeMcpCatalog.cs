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

        if (name.StartsWith("rekall.transaction.", StringComparison.Ordinal))
        {
            return "transactions";
        }

        if (name.StartsWith("rekall.render.", StringComparison.Ordinal))
        {
            return "rendering";
        }

        if (name.StartsWith("rekall.shader.", StringComparison.Ordinal))
        {
            return "shaders";
        }

        if (name.StartsWith("rekall.module.", StringComparison.Ordinal))
        {
            return "modules";
        }

        if (name.StartsWith("rekall.live.", StringComparison.Ordinal))
        {
            return "live";
        }

        if (name.StartsWith("rekall.multiplayer.", StringComparison.Ordinal))
        {
            return "multiplayer";
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
            name.StartsWith("rekall.geometry.", StringComparison.Ordinal) ||
            name.StartsWith("rekall.planet.", StringComparison.Ordinal) ||
            name.StartsWith("rekall.solar.", StringComparison.Ordinal) ||
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
            "rekall.live.status" or
            "rekall.live.reload_scene" or
            "rekall.live.reload_assets" or
            "rekall.live.apply_scene_blueprint" or
            "rekall.live.apply_scene_diff" or
            "rekall.multiplayer.status" or
            "rekall.multiplayer.connect" or
            "rekall.multiplayer.submit_input" or
            "rekall.multiplayer.snapshot" or
            "rekall.multiplayer.delta" or
            "rekall.render.visibility.inspect_scene" or
            "rekall.solar.import_ksa_system" or
            "rekall.templates.inspect" or
            "rekall.workflow.create_playable_package_from_template" or
            "rekall.workflow.audit_playable_package";
    }

    public static int GetAgentPriority(string name)
    {
        return name switch
        {
            "rekall.context.engine_status" => 5,
            "rekall.templates.inspect" => 8,
            "rekall.workflow.create_playable_package_from_template" => 10,
            "rekall.workflow.audit_playable_package" => 15,
            "rekall.live.status" => 16,
            "rekall.live.apply_scene_blueprint" => 17,
            "rekall.live.apply_scene_diff" => 18,
            "rekall.live.reload_scene" => 19,
            "rekall.live.reload_assets" => 20,
            "rekall.multiplayer.status" => 21,
            "rekall.multiplayer.connect" => 22,
            "rekall.multiplayer.submit_input" => 23,
            "rekall.multiplayer.tick" => 24,
            "rekall.multiplayer.snapshot" => 25,
            "rekall.multiplayer.delta" => 26,
            "rekall.render.visibility.inspect_scene" => 26,
            "rekall.solar.import_ksa_system" => 27,
            "rekall.templates.verify_mvp" => 20,
            "rekall.scene.apply_blueprint" => 42,
            _ when name.StartsWith("rekall.workflow.", StringComparison.Ordinal) => 30,
            _ when name.StartsWith("rekall.transaction.", StringComparison.Ordinal) => 35,
            _ when name.StartsWith("rekall.playtest.", StringComparison.Ordinal) => 40,
            _ when name.StartsWith("rekall.geometry.", StringComparison.Ordinal) => 45,
            _ when name.StartsWith("rekall.module.", StringComparison.Ordinal) => 50,
            _ when name.StartsWith("rekall.shader.", StringComparison.Ordinal) => 60,
            _ when name.StartsWith("rekall.render.", StringComparison.Ordinal) => 70,
            _ when name.StartsWith("rekall.multiplayer.", StringComparison.Ordinal) => 75,
            _ => 100
        };
    }
}
