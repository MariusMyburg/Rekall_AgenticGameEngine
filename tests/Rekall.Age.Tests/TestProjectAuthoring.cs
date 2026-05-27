using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.Project.Commands;
using Rekall.Age.World;
using Rekall.Age.World.Commands;

namespace Rekall.Age.Tests;

internal static class TestProjectAuthoring
{
    public static async Task CreateProjectWithSceneAsync(
        string root,
        RekallAgeCommandContext context,
        string projectName = "Agent Authored Game",
        string sceneName = "Main")
    {
        await new CreateProjectCommand().ExecuteAsync(
            new CreateProjectRequest(root, projectName, ["world"]),
            context);
        await new CreateSceneCommand().ExecuteAsync(
            new CreateSceneRequest(root, sceneName, ["world"]),
            context);
    }

    public static async Task CreateRenderableProjectAsync(
        string root,
        RekallAgeCommandContext context,
        string projectName = "Agent Authored Scene",
        string sceneName = "Main")
    {
        await new CreateProjectCommand().ExecuteAsync(
            new CreateProjectRequest(root, projectName, ["world", "rendering2d"]),
            context);

        var scene = RekallAgeSceneDocument.Create(sceneName, ["world", "rendering2d"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera2D", new JsonObject { ["active"] = true })))
            .AddEntity(RekallAgeEntityDocument.Create("Agent Renderable", ["renderable"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform2D", new JsonObject { ["x"] = 4, ["y"] = 8 }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.SpriteRenderer", new JsonObject { ["sprite"] = "agent_sprite" })));
        await new RekallAgeSceneStore().SaveAsync(root, scene, context.CancellationToken);
    }
}
