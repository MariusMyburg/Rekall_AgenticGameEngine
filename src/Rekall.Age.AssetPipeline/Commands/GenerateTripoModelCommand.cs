using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rekall.Age.Assets;
using Rekall.Age.Core.Commands;

namespace Rekall.Age.AssetPipeline.Commands;

public sealed record GenerateTripoModelRequest(
    string ProjectRoot,
    string Prompt,
    string? DisplayName = null,
    string? ApiKey = null,
    bool UseEnvironmentApiKey = true,
    string? ModelVersion = null,
    string? NegativePrompt = null,
    int? ModelSeed = null,
    bool Texture = true,
    int? FaceLimit = null,
    int PollAttempts = 60,
    int PollDelayMilliseconds = 2000);

public sealed record GenerateTripoModelResult(
    string? TaskId,
    string Status,
    string? ModelUrl,
    RekallAgeAssetDocument? Asset,
    int? ConsumedCredit,
    IReadOnlyList<string> NextActions);

public sealed record RekallAgeTripoTextToModelOptions(
    string Prompt,
    string ModelVersion = RekallAgeTripoTaskPayloadBuilder.DefaultTextToModelVersion,
    string? NegativePrompt = null,
    int? ModelSeed = null,
    bool Texture = true,
    int? FaceLimit = null);

public sealed record RekallAgeTripoTaskStatus(
    string TaskId,
    string Type,
    string Status,
    int Progress,
    string? ModelUrl,
    string? BaseModelUrl,
    string? PbrModelUrl,
    string? RenderedImageUrl,
    int? ConsumedCredit)
{
    public bool Finalized => Status is "success" or "failed" or "banned" or "expired" or "cancelled" or "unknown";

    public string? PreferredModelUrl => PbrModelUrl ?? ModelUrl ?? BaseModelUrl;
}

public interface IRekallAgeTripoClient
{
    ValueTask<string> CreateTextToModelTaskAsync(
        RekallAgeTripoTextToModelOptions options,
        string apiKey,
        CancellationToken cancellationToken);

    ValueTask<RekallAgeTripoTaskStatus> GetTaskAsync(
        string taskId,
        string apiKey,
        CancellationToken cancellationToken);

    ValueTask<byte[]> DownloadAsync(string url, CancellationToken cancellationToken);
}

public static class RekallAgeTripoTaskPayloadBuilder
{
    public const string DefaultTextToModelVersion = "Turbo-v1.0-20250506";

    public static JsonObject BuildTextToModel(RekallAgeTripoTextToModelOptions options)
    {
        var payload = new JsonObject
        {
            ["type"] = "text_to_model",
            ["prompt"] = options.Prompt,
            ["model_version"] = string.IsNullOrWhiteSpace(options.ModelVersion)
                ? DefaultTextToModelVersion
                : options.ModelVersion,
            ["texture"] = options.Texture
        };

        if (!string.IsNullOrWhiteSpace(options.NegativePrompt))
        {
            payload["negative_prompt"] = options.NegativePrompt;
        }

        if (options.ModelSeed is not null)
        {
            payload["model_seed"] = options.ModelSeed.Value;
        }

        if (options.FaceLimit is not null)
        {
            payload["face_limit"] = options.FaceLimit.Value;
        }

        return payload;
    }
}

public sealed class GenerateTripoModelCommand
    : IRekallAgeCommand<GenerateTripoModelRequest, GenerateTripoModelResult>
{
    private readonly IRekallAgeTripoClient _client;
    private readonly RekallAgeAssetCatalogStore _assetStore = new();
    private readonly RekallAgeAssetPipelineStore _pipelineStore = new();

    public GenerateTripoModelCommand()
        : this(new RekallAgeHttpTripoClient())
    {
    }

    public GenerateTripoModelCommand(IRekallAgeTripoClient client)
    {
        _client = client;
    }

    public string Name => "rekall.asset.tripo.generate_model";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Generates a Tripo3D text-to-model task, downloads the completed model, and imports it as a generic Rekall AGE model asset.",
        typeof(GenerateTripoModelRequest).FullName!,
        typeof(GenerateTripoModelResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<GenerateTripoModelResult>> ExecuteAsync(
        GenerateTripoModelRequest request,
        RekallAgeCommandContext context)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return Failure(
                "TRIPO_PROMPT_REQUIRED",
                "Tripo model generation requires a non-empty prompt.",
                status: "not-started");
        }

        var apiKey = ResolveApiKey(request);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Failure(
                "TRIPO_API_KEY_MISSING",
                "Set TRIPO_API_KEY or pass an API key in the request before calling Tripo.",
                status: "not-started");
        }

        var options = new RekallAgeTripoTextToModelOptions(
            request.Prompt.Trim(),
            string.IsNullOrWhiteSpace(request.ModelVersion)
                ? RekallAgeTripoTaskPayloadBuilder.DefaultTextToModelVersion
                : request.ModelVersion.Trim(),
            TrimToNull(request.NegativePrompt),
            request.ModelSeed,
            request.Texture,
            request.FaceLimit);
        var taskId = await _client.CreateTextToModelTaskAsync(options, apiKey, context.CancellationToken);
        RekallAgeTripoTaskStatus? task = null;
        var attempts = Math.Max(1, request.PollAttempts);
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            task = await _client.GetTaskAsync(taskId, apiKey, context.CancellationToken);
            if (task.Finalized)
            {
                break;
            }

            if (request.PollDelayMilliseconds > 0)
            {
                await Task.Delay(request.PollDelayMilliseconds, context.CancellationToken);
            }
        }

        if (task is null)
        {
            return Failure("TRIPO_TASK_STATUS_MISSING", "Tripo did not return a task status.", taskId, "unknown");
        }

        if (!task.Status.Equals("success", StringComparison.Ordinal))
        {
            return Failure(
                "TRIPO_TASK_NOT_SUCCESSFUL",
                $"Tripo task '{taskId}' ended with status '{task.Status}'.",
                taskId,
                task.Status,
                task.PreferredModelUrl,
                task.ConsumedCredit);
        }

        var modelUrl = task.PreferredModelUrl;
        if (string.IsNullOrWhiteSpace(modelUrl))
        {
            return Failure(
                "TRIPO_MODEL_URL_MISSING",
                $"Tripo task '{taskId}' succeeded but did not return a model URL.",
                taskId,
                task.Status,
                consumedCredit: task.ConsumedCredit);
        }

        var modelBytes = await _client.DownloadAsync(modelUrl, context.CancellationToken);
        var stagedPath = await WriteGeneratedModelAsync(
            request.ProjectRoot,
            taskId,
            request.DisplayName,
            modelBytes,
            context.CancellationToken);
        var asset = await RekallAgeAssetImporter.ImportAsync(
            request.ProjectRoot,
            stagedPath,
            "model",
            request.DisplayName ?? $"Tripo {taskId}",
            context.CancellationToken);
        var catalog = await _assetStore.LoadAsync(request.ProjectRoot, context.CancellationToken);
        await _assetStore.SaveAsync(request.ProjectRoot, catalog.AddOrReplace(asset), context.CancellationToken);
        var pipeline = await _pipelineStore.LoadAsync(request.ProjectRoot, context.CancellationToken);
        await _pipelineStore.SaveAsync(
            request.ProjectRoot,
            pipeline.AddImport(asset, stagedPath, "model"),
            context.CancellationToken);

        context.Transaction.RecordChangedResource(stagedPath);
        context.Transaction.RecordChangedResource(asset.ImportedPath);
        context.Transaction.RecordChangedResource(_assetStore.GetCatalogPath(request.ProjectRoot));
        context.Transaction.RecordChangedResource(_pipelineStore.GetPath(request.ProjectRoot));

        return RekallAgeCommandResult<GenerateTripoModelResult>.Success(
            new GenerateTripoModelResult(
                taskId,
                task.Status,
                modelUrl,
                asset,
                task.ConsumedCredit,
                ["Use rekall.asset.list to inspect imported model assets.", "Add a Rekall.MeshRenderer or Rekall.MeshSet component that references the imported asset id."]),
            $"Generated Tripo model task '{taskId}' and imported asset '{asset.Id}'.");
    }

    private static string? ResolveApiKey(GenerateTripoModelRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.ApiKey))
        {
            return request.ApiKey.Trim();
        }

        return request.UseEnvironmentApiKey
            ? TrimToNull(Environment.GetEnvironmentVariable("TRIPO_API_KEY"))
            : null;
    }

    private static async ValueTask<string> WriteGeneratedModelAsync(
        string projectRoot,
        string taskId,
        string? displayName,
        byte[] modelBytes,
        CancellationToken cancellationToken)
    {
        var directory = Path.Combine(projectRoot, "Assets", "Generated", "Tripo");
        Directory.CreateDirectory(directory);
        var baseName = ToSlug(displayName ?? taskId, "tripo-model");
        var path = Path.Combine(directory, $"{baseName}-{ToSlug(taskId, "task")}.glb");
        await File.WriteAllBytesAsync(path, modelBytes, cancellationToken);
        return path;
    }

    private static string ToSlug(string value, string fallback)
    {
        var slug = string.Join(
            "-",
            value
                .Trim()
                .ToLowerInvariant()
                .Select(item => char.IsLetterOrDigit(item) ? item : '-')
                .Aggregate(new StringBuilder(), (builder, item) => builder.Append(item))
                .ToString()
                .Split('-', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length == 0 ? fallback : slug;
    }

    private static string? TrimToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static RekallAgeCommandResult<GenerateTripoModelResult> Failure(
        string code,
        string message,
        string? taskId = null,
        string status = "failed",
        string? modelUrl = null,
        int? consumedCredit = null)
    {
        return RekallAgeCommandResult<GenerateTripoModelResult>.Failure(
            new GenerateTripoModelResult(
                taskId,
                status,
                modelUrl,
                null,
                consumedCredit,
                ["Check Tripo task status and retry generation when the provider is ready."]),
            message,
            [new RekallAgeCommandError(code, message, taskId)]);
    }
}

public sealed class RekallAgeHttpTripoClient : IRekallAgeTripoClient
{
    private static readonly Uri TaskEndpoint = new("https://api.tripo3d.ai/v2/openapi/task");
    private readonly HttpClient _httpClient;

    public RekallAgeHttpTripoClient()
        : this(new HttpClient())
    {
    }

    public RekallAgeHttpTripoClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async ValueTask<string> CreateTextToModelTaskAsync(
        RekallAgeTripoTextToModelOptions options,
        string apiKey,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TaskEndpoint)
        {
            Content = new StringContent(
                RekallAgeTripoTaskPayloadBuilder.BuildTextToModel(options).ToJsonString(),
                Encoding.UTF8,
                "application/json")
        };
        AddAuthorization(request, apiKey);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        var root = JsonNode.Parse(body)?.AsObject()
            ?? throw new InvalidOperationException("Tripo task creation response was not JSON.");
        return root["data"]?["task_id"]?.GetValue<string>()
            ?? throw new InvalidOperationException("Tripo task creation response did not include data.task_id.");
    }

    public async ValueTask<RekallAgeTripoTaskStatus> GetTaskAsync(
        string taskId,
        string apiKey,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{TaskEndpoint}/{taskId}");
        AddAuthorization(request, apiKey);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        var data = JsonNode.Parse(body)?["data"]?.AsObject()
            ?? throw new InvalidOperationException("Tripo task status response did not include data.");
        var output = data["output"]?.AsObject();
        return new RekallAgeTripoTaskStatus(
            data["task_id"]?.GetValue<string>() ?? taskId,
            data["type"]?.GetValue<string>() ?? string.Empty,
            data["status"]?.GetValue<string>() ?? "unknown",
            data["progress"]?.GetValue<int>() ?? 0,
            output?["model"]?.GetValue<string>(),
            output?["base_model"]?.GetValue<string>(),
            output?["pbr_model"]?.GetValue<string>(),
            output?["rendered_image"]?.GetValue<string>(),
            data["consumed_credit"]?.GetValue<int>());
    }

    public async ValueTask<byte[]> DownloadAsync(string url, CancellationToken cancellationToken)
    {
        return await _httpClient.GetByteArrayAsync(url, cancellationToken);
    }

    private static void AddAuthorization(HttpRequestMessage request, string apiKey)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }
}
