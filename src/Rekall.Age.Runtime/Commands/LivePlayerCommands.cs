using System.Text.Json;
using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.Runtime.Live;
using Rekall.Age.World.Commands;

namespace Rekall.Age.Runtime.Commands;

public sealed record LivePlayerStatusRequest(
    string ProjectRoot,
    string SceneName,
    string? PipeName = null,
    int TimeoutMilliseconds = 2000);

public sealed record LivePlayerReloadSceneRequest(
    string ProjectRoot,
    string SceneName,
    string? PipeName = null,
    bool ReloadAssets = true,
    int TimeoutMilliseconds = 5000);

public sealed record LivePlayerReloadAssetsRequest(
    string ProjectRoot,
    string SceneName,
    string? PipeName = null,
    int TimeoutMilliseconds = 5000);

public sealed record LivePlayerApplySceneBlueprintRequest(
    string ProjectRoot,
    string SceneName,
    IReadOnlyList<RekallAgeSceneBlueprintEntity> Entities,
    string? PipeName = null,
    bool ClearExisting = false,
    bool PersistToProject = false,
    bool ReloadAssets = true,
    int TimeoutMilliseconds = 5000);

public sealed record LivePlayerApplySceneDiffRequest(
    string ProjectRoot,
    string SceneName,
    IReadOnlyList<RekallAgeSceneBlueprintEntity>? UpsertEntities = null,
    IReadOnlyList<string>? DeleteEntityIds = null,
    IReadOnlyList<string>? DeleteEntityNames = null,
    string? PipeName = null,
    bool ClearExisting = false,
    bool PersistToProject = false,
    bool ReloadAssets = true,
    int TimeoutMilliseconds = 5000);

public sealed record LivePlayerCommandResult(
    bool Connected,
    string PipeName,
    string? SessionId,
    string Operation,
    bool Applied,
    int FrameIndex,
    int EntityCount,
    int RenderableCount,
    int SceneRevision,
    int AssetRevision,
    string Message);

public sealed class LivePlayerStatusCommand
    : RekallAgeLivePlayerCommandBase<LivePlayerStatusRequest>
{
    public override string Name => "rekall.live.status";

    protected override string Operation => "status";

    protected override JsonObject? CreatePayload(LivePlayerStatusRequest request)
    {
        return null;
    }

    protected override (string ProjectRoot, string SceneName, string? PipeName, int TimeoutMilliseconds) GetConnection(
        LivePlayerStatusRequest request)
    {
        return (request.ProjectRoot, request.SceneName, request.PipeName, request.TimeoutMilliseconds);
    }
}

public sealed class LivePlayerReloadSceneCommand
    : RekallAgeLivePlayerCommandBase<LivePlayerReloadSceneRequest>
{
    public override string Name => "rekall.live.reload_scene";

    protected override string Operation => "reload_scene";

    protected override JsonObject? CreatePayload(LivePlayerReloadSceneRequest request)
    {
        return new JsonObject { ["reloadAssets"] = request.ReloadAssets };
    }

    protected override (string ProjectRoot, string SceneName, string? PipeName, int TimeoutMilliseconds) GetConnection(
        LivePlayerReloadSceneRequest request)
    {
        return (request.ProjectRoot, request.SceneName, request.PipeName, request.TimeoutMilliseconds);
    }
}

public sealed class LivePlayerReloadAssetsCommand
    : RekallAgeLivePlayerCommandBase<LivePlayerReloadAssetsRequest>
{
    public override string Name => "rekall.live.reload_assets";

    protected override string Operation => "reload_assets";

    protected override JsonObject? CreatePayload(LivePlayerReloadAssetsRequest request)
    {
        return null;
    }

    protected override (string ProjectRoot, string SceneName, string? PipeName, int TimeoutMilliseconds) GetConnection(
        LivePlayerReloadAssetsRequest request)
    {
        return (request.ProjectRoot, request.SceneName, request.PipeName, request.TimeoutMilliseconds);
    }
}

public sealed class LivePlayerApplySceneBlueprintCommand
    : RekallAgeLivePlayerCommandBase<LivePlayerApplySceneBlueprintRequest>
{
    public override string Name => "rekall.live.apply_scene_blueprint";

    protected override string Operation => "apply_scene_blueprint";

    protected override JsonObject? CreatePayload(LivePlayerApplySceneBlueprintRequest request)
    {
        return JsonSerializer.SerializeToNode(
            new LiveApplySceneBlueprintPayload(
                request.Entities,
                request.ClearExisting,
                request.PersistToProject,
                request.ReloadAssets),
            JsonOptions)!.AsObject();
    }

    protected override (string ProjectRoot, string SceneName, string? PipeName, int TimeoutMilliseconds) GetConnection(
        LivePlayerApplySceneBlueprintRequest request)
    {
        return (request.ProjectRoot, request.SceneName, request.PipeName, request.TimeoutMilliseconds);
    }

    private sealed record LiveApplySceneBlueprintPayload(
        IReadOnlyList<RekallAgeSceneBlueprintEntity> Entities,
        bool ClearExisting,
        bool PersistToProject,
        bool ReloadAssets);
}

public sealed class LivePlayerApplySceneDiffCommand
    : RekallAgeLivePlayerCommandBase<LivePlayerApplySceneDiffRequest>
{
    public override string Name => "rekall.live.apply_scene_diff";

    protected override string Operation => "apply_scene_diff";

    protected override JsonObject? CreatePayload(LivePlayerApplySceneDiffRequest request)
    {
        return JsonSerializer.SerializeToNode(
            new LiveApplySceneDiffPayload(
                request.UpsertEntities ?? [],
                request.DeleteEntityIds ?? [],
                request.DeleteEntityNames ?? [],
                request.ClearExisting,
                request.PersistToProject,
                request.ReloadAssets),
            JsonOptions)!.AsObject();
    }

    protected override (string ProjectRoot, string SceneName, string? PipeName, int TimeoutMilliseconds) GetConnection(
        LivePlayerApplySceneDiffRequest request)
    {
        return (request.ProjectRoot, request.SceneName, request.PipeName, request.TimeoutMilliseconds);
    }

    private sealed record LiveApplySceneDiffPayload(
        IReadOnlyList<RekallAgeSceneBlueprintEntity> UpsertEntities,
        IReadOnlyList<string> DeleteEntityIds,
        IReadOnlyList<string> DeleteEntityNames,
        bool ClearExisting,
        bool PersistToProject,
        bool ReloadAssets);
}

public abstract class RekallAgeLivePlayerCommandBase<TRequest>
    : IRekallAgeCommand<TRequest, LivePlayerCommandResult>
{
    protected static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly RekallAgeLivePlayerClient _client;

    protected RekallAgeLivePlayerCommandBase()
        : this(new RekallAgeLivePlayerClient())
    {
    }

    protected RekallAgeLivePlayerCommandBase(RekallAgeLivePlayerClient client)
    {
        _client = client;
    }

    public abstract string Name { get; }

    protected abstract string Operation { get; }

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Targets a running Rekall AGE player session over local IPC for live scene and asset edits.",
        typeof(TRequest).FullName!,
        typeof(LivePlayerCommandResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<LivePlayerCommandResult>> ExecuteAsync(
        TRequest request,
        RekallAgeCommandContext context)
    {
        var connection = GetConnection(request);
        var pipeName = RekallAgeLivePlayerEndpoint.ResolvePipeName(
            connection.ProjectRoot,
            connection.SceneName,
            connection.PipeName);
        var response = await _client.SendAsync(
            pipeName,
            new RekallAgeLivePlayerRequestEnvelope(
                Operation,
                context.Transaction.Id,
                connection.ProjectRoot,
                connection.SceneName,
                CreatePayload(request)),
            TimeSpan.FromMilliseconds(Math.Max(1, connection.TimeoutMilliseconds)),
            context.CancellationToken);

        if (!response.Ok)
        {
            var value = Empty(pipeName, Operation, response.ErrorMessage ?? "Live player request failed.");
            var error = new RekallAgeCommandError(
                response.ErrorCode ?? "REKALL_LIVE_PLAYER_UNAVAILABLE",
                response.ErrorMessage ?? "Live player request failed.",
                pipeName);
            return RekallAgeCommandResult<LivePlayerCommandResult>.Failure(value, error.Message, [error]);
        }

        var result = ToResult(pipeName, Operation, response.Payload);
        return RekallAgeCommandResult<LivePlayerCommandResult>.Success(
            result,
            $"Live player '{Operation}' applied through pipe '{pipeName}'.");
    }

    protected abstract JsonObject? CreatePayload(TRequest request);

    protected abstract (string ProjectRoot, string SceneName, string? PipeName, int TimeoutMilliseconds) GetConnection(
        TRequest request);

    private static LivePlayerCommandResult Empty(string pipeName, string operation, string message)
    {
        return new LivePlayerCommandResult(
            false,
            pipeName,
            null,
            operation,
            false,
            0,
            0,
            0,
            0,
            0,
            message);
    }

    private static LivePlayerCommandResult ToResult(
        string pipeName,
        string operation,
        JsonObject? payload)
    {
        return new LivePlayerCommandResult(
            true,
            pipeName,
            ReadString(payload, "sessionId"),
            operation,
            ReadBoolean(payload, "applied", true),
            ReadInt32(payload, "frameIndex"),
            ReadInt32(payload, "entityCount"),
            ReadInt32(payload, "renderableCount"),
            ReadInt32(payload, "sceneRevision"),
            ReadInt32(payload, "assetRevision"),
            ReadString(payload, "message") ?? "OK");
    }

    private static string? ReadString(JsonObject? payload, string name)
    {
        return payload is not null
            && payload.TryGetPropertyValue(name, out var node)
            && node is JsonValue value
            && value.TryGetValue<string>(out var text)
            ? text
            : null;
    }

    private static int ReadInt32(JsonObject? payload, string name)
    {
        if (payload is null
            || !payload.TryGetPropertyValue(name, out var node)
            || node is not JsonValue value)
        {
            return 0;
        }

        if (value.TryGetValue<int>(out var integer))
        {
            return integer;
        }

        return value.TryGetValue<long>(out var longValue) ? checked((int)longValue) : 0;
    }

    private static bool ReadBoolean(JsonObject? payload, string name, bool fallback)
    {
        return payload is not null
            && payload.TryGetPropertyValue(name, out var node)
            && node is JsonValue value
            && value.TryGetValue<bool>(out var boolean)
            ? boolean
            : fallback;
    }
}
