using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Runtime.Commands;
using Rekall.Age.Runtime.Live;
using Rekall.Age.World.Commands;

namespace Rekall.Age.Tests.Runtime;

public sealed class LivePlayerIpcTests
{
    [Fact]
    public async Task NamedPipeServerHandlesLiveEditRoundTrip()
    {
        var pipeName = $"rekall-age-test-{Guid.NewGuid():N}";
        await using var server = new RekallAgeLivePlayerNamedPipeServer(
            pipeName,
            async (request, _) =>
            {
                await Task.Yield();
                return new JsonObject
                {
                    ["operation"] = request.Operation,
                    ["sceneName"] = request.SceneName,
                    ["frameIndex"] = 12,
                    ["entityCount"] = 3,
                    ["renderableCount"] = 2
                };
            });
        server.Start();
        await server.WaitUntilListeningAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

        var response = await new RekallAgeLivePlayerClient().SendAsync(
            pipeName,
            new RekallAgeLivePlayerRequestEnvelope(
                "status",
                Guid.NewGuid().ToString("N"),
                "F:/Game",
                "Main",
                null),
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.True(response.Ok, response.ErrorMessage);
        Assert.NotNull(response.Payload);
        Assert.Equal("status", response.Payload["operation"]!.GetValue<string>());
        Assert.Equal(12, response.Payload["frameIndex"]!.GetValue<int>());
    }

    [Fact]
    public async Task LiveStatusCommandTargetsRunningPlayerSession()
    {
        var pipeName = $"rekall-age-test-{Guid.NewGuid():N}";
        await using var server = new RekallAgeLivePlayerNamedPipeServer(
            pipeName,
            (_, _) => ValueTask.FromResult(new JsonObject
            {
                ["sessionId"] = "session-1",
                ["frameIndex"] = 5,
                ["entityCount"] = 7,
                ["renderableCount"] = 4,
                ["assetRevision"] = 2,
                ["sceneRevision"] = 3
            }));
        server.Start();
        await server.WaitUntilListeningAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

        var result = await new LivePlayerStatusCommand().ExecuteAsync(
            new LivePlayerStatusRequest("F:/Game", "Main", pipeName, 5000),
            new RekallAgeCommandContext("test", RekallAgeTransaction.Begin("live status"), CancellationToken.None));

        Assert.True(result.Ok, result.Summary);
        Assert.True(result.Value.Connected);
        Assert.Equal("session-1", result.Value.SessionId);
        Assert.Equal(5, result.Value.FrameIndex);
        Assert.Equal(7, result.Value.EntityCount);
        Assert.Equal(4, result.Value.RenderableCount);
        Assert.Equal(pipeName, result.Value.PipeName);
    }

    [Fact]
    public async Task LiveSceneDiffCommandSendsGenericUpsertAndDeletePayload()
    {
        var pipeName = $"rekall-age-test-{Guid.NewGuid():N}";
        await using var server = new RekallAgeLivePlayerNamedPipeServer(
            pipeName,
            (request, _) =>
            {
                Assert.Equal("apply_scene_diff", request.Operation);
                Assert.NotNull(request.Payload);
                Assert.Single(request.Payload["upsertEntities"]!.AsArray());
                Assert.Equal("old-entity", request.Payload["deleteEntityIds"]![0]!.GetValue<string>());
                return ValueTask.FromResult(new JsonObject
                {
                    ["sessionId"] = "session-2",
                    ["frameIndex"] = 9,
                    ["entityCount"] = 2,
                    ["renderableCount"] = 1,
                    ["assetRevision"] = 4,
                    ["sceneRevision"] = 5,
                    ["applied"] = true
                });
            });
        server.Start();
        await server.WaitUntilListeningAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

        var result = await new LivePlayerApplySceneDiffCommand().ExecuteAsync(
            new LivePlayerApplySceneDiffRequest(
                "F:/Game",
                "Main",
                UpsertEntities: [new RekallAgeSceneBlueprintEntity("New Light")],
                DeleteEntityIds: ["old-entity"],
                PipeName: pipeName,
                TimeoutMilliseconds: 5000),
            new RekallAgeCommandContext("test", RekallAgeTransaction.Begin("live diff"), CancellationToken.None));

        Assert.True(result.Ok, result.Summary);
        Assert.True(result.Value.Applied);
        Assert.Equal("session-2", result.Value.SessionId);
        Assert.Equal(5, result.Value.SceneRevision);
    }
}
