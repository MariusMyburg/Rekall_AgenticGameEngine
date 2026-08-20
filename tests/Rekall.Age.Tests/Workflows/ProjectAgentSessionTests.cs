using System.Text.Json.Nodes;
using Rekall.Age.Agent.LanguageModels;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Project.Commands;
using Rekall.Age.Workflows;
using Rekall.Age.World;
using Rekall.Age.World.Commands;

namespace Rekall.Age.Tests.Workflows;

public sealed class ProjectAgentSessionTests
{
    [Fact]
    public async Task SessionUsesCanonicalToolsToMutateOnlyTheOpenProject()
    {
        var root = TestPaths.CreateTempDirectory();
        var registry = CreateRegistry();
        await CreateProjectAsync(registry, root);
        var model = new ScriptedModelClient(
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall("rekall.entity.create", new JsonObject
                {
                    ["projectRoot"] = root,
                    ["sceneName"] = "Main",
                    ["name"] = "Agent Authored",
                    ["tags"] = new JsonArray("agent-authored")
                })],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse("test", "model", "Created the entity.", "", [], "stop", new(1, 1, 1)));
        var progress = new RecordingProgress<RekallAgeLanguageModelAgentProgress>();
        var session = new RekallAgeProjectAgentSession(model, registry);

        var result = await session.RunAsync(
            new RekallAgeProjectAgentSessionRequest(root, "Main", "model", "Add one entity") { MaxTurns = 4 },
            progress,
            CancellationToken.None);

        Assert.True(result.Succeeded, result.Summary);
        Assert.Equal(1, result.AgentResult.ToolCallCount);
        Assert.Contains((await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None)).Entities,
            entity => entity.Name == "Agent Authored");
        Assert.Contains(progress.Values, item => item.Phase == "tool.completed");
    }

    [Fact]
    public async Task SessionRejectsToolArgumentsThatEscapeTheOpenProject()
    {
        var root = TestPaths.CreateTempDirectory();
        var outside = TestPaths.CreateTempDirectory();
        var registry = CreateRegistry();
        await CreateProjectAsync(registry, root);
        await CreateProjectAsync(registry, outside);
        var model = new ScriptedModelClient(
            new RekallAgeLanguageModelResponse(
                "test", "model", "", "",
                [new RekallAgeLanguageModelToolCall("rekall.tools.execute", new JsonObject
                {
                    ["name"] = "rekall.entity.create",
                    ["arguments"] = new JsonObject
                    {
                        ["projectRoot"] = outside,
                        ["sceneName"] = "Main",
                        ["name"] = "Escaped",
                        ["tags"] = new JsonArray()
                    }.ToJsonString()
                })],
                "tool_calls", new(1, 1, 1)),
            new RekallAgeLanguageModelResponse("test", "model", "Done", "", [], "stop", new(1, 1, 1)));

        var result = await new RekallAgeProjectAgentSession(model, registry).RunAsync(
            new RekallAgeProjectAgentSessionRequest(root, "Main", "model", "Try to escape"),
            progress: null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        var failed = Assert.Single(result.AgentResult.ToolExecutions);
        Assert.False(failed.Succeeded);
        Assert.Contains("REKALL_AGENT_PROJECT_SCOPE_VIOLATION", failed.ResultPreview, StringComparison.Ordinal);
        Assert.DoesNotContain((await new RekallAgeSceneStore().LoadAsync(outside, "Main", CancellationToken.None)).Entities,
            entity => entity.Name == "Escaped");
    }

    private static RekallAgeCommandRegistry CreateRegistry()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new CreateProjectCommand());
        registry.Register(new CreateSceneCommand());
        registry.Register(new CreateEntityCommand());
        return registry;
    }

    private static async Task CreateProjectAsync(RekallAgeCommandRegistry registry, string root)
    {
        var context = new RekallAgeCommandContext("test", RekallAgeTransaction.Begin("create"), CancellationToken.None);
        Assert.True((await registry.ExecuteAsync<CreateProjectRequest, CreateProjectResult>(
            "rekall.project.create", new CreateProjectRequest(root, "Game", ["world"]), context)).Ok);
        Assert.True((await registry.ExecuteAsync<CreateSceneRequest, CreateSceneResult>(
            "rekall.scene.create", new CreateSceneRequest(root, "Main", ["world"]), context)).Ok);
    }

    private sealed class ScriptedModelClient(params RekallAgeLanguageModelResponse[] responses) : IRekallAgeLanguageModelClient
    {
        private int _index;
        public string ProviderId => "test";
        public ValueTask<IReadOnlyList<RekallAgeLanguageModelInfo>> ListModelsAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<RekallAgeLanguageModelInfo>>([new("model", 1)]);
        public ValueTask<RekallAgeLanguageModelResponse> ChatAsync(RekallAgeLanguageModelRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(responses[Math.Min(_index++, responses.Length - 1)]);
    }

    private sealed class RecordingProgress<T> : IProgress<T>
    {
        public List<T> Values { get; } = [];
        public void Report(T value) => Values.Add(value);
    }
}
