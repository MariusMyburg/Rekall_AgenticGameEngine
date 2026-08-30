using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rekall.Age.Studio;

internal sealed record RekallAgeStudioLanguageModelSetup(
    int Version,
    bool IsComplete,
    string ProviderId,
    string ModelId,
    string ReasoningEffort,
    string? OllamaUrl,
    string? OpenAiUrl,
    string? KimiUrl,
    DateTimeOffset? LastSuccessfulCheckUtc,
    int ReadinessVersion)
{
    public const int CurrentVersion = 1;
    public const int CurrentReadinessVersion = 1;

    public static RekallAgeStudioLanguageModelSetup Incomplete { get; } = new(
        CurrentVersion,
        false,
        "ollama",
        "qwen3.8:27b",
        "high",
        null,
        null,
        null,
        null,
        CurrentReadinessVersion);

    internal static RekallAgeStudioLanguageModelSetup? Normalize(RekallAgeStudioLanguageModelSetup? candidate)
    {
        if (candidate is null
            || candidate.Version != CurrentVersion
            || candidate.ReadinessVersion != CurrentReadinessVersion
            || !TryNormalizeProviderId(candidate.ProviderId, out var providerId)
            || !TryNormalizeModelId(candidate.ModelId, out var modelId)
            || !TryNormalizeReasoningEffort(candidate.ReasoningEffort, out var reasoningEffort)
            || !TryNormalizeEndpoint(candidate.OllamaUrl, out var ollamaUrl)
            || !TryNormalizeEndpoint(candidate.OpenAiUrl, out var openAiUrl)
            || !TryNormalizeEndpoint(candidate.KimiUrl, out var kimiUrl))
        {
            return null;
        }

        return candidate with
        {
            ProviderId = providerId,
            ModelId = modelId,
            ReasoningEffort = reasoningEffort,
            OllamaUrl = ollamaUrl,
            OpenAiUrl = openAiUrl,
            KimiUrl = kimiUrl
        };
    }

    private static bool TryNormalizeProviderId(string? value, out string providerId)
    {
        providerId = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return providerId is "ollama" or "gguf" or "kimi" or "openai" or "codex";
    }

    private static bool TryNormalizeModelId(string? value, out string modelId)
    {
        modelId = value?.Trim() ?? string.Empty;
        return modelId.Length > 0;
    }

    private static bool TryNormalizeReasoningEffort(string? value, out string reasoningEffort)
    {
        reasoningEffort = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return reasoningEffort is "none" or "low" or "medium" or "high" or "xhigh" or "max";
    }

    private static bool TryNormalizeEndpoint(string? value, out string? endpoint)
    {
        endpoint = value?.Trim();
        if (string.IsNullOrEmpty(endpoint))
        {
            endpoint = null;
            return true;
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            endpoint = null;
            return false;
        }

        return true;
    }
}

internal interface IRekallAgeStudioLanguageModelSetupStore
{
    ValueTask<RekallAgeStudioLanguageModelSetup> LoadAsync(CancellationToken cancellationToken);

    ValueTask SaveAsync(RekallAgeStudioLanguageModelSetup setup, CancellationToken cancellationToken);
}

internal sealed class RekallAgeStudioLanguageModelSetupStore : IRekallAgeStudioLanguageModelSetupStore
{
    internal const string SetupRootEnvironmentVariable = "REKALL_AGE_STUDIO_SETUP_ROOT";
    private const string SetupFileName = "language-model-setup-v1.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _path;

    public RekallAgeStudioLanguageModelSetupStore()
        : this(System.IO.Path.Combine(ResolveSetupRoot(), SetupFileName))
    {
    }

    internal RekallAgeStudioLanguageModelSetupStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = System.IO.Path.GetFullPath(path);
    }

    internal string Path => _path;

    public async ValueTask<RekallAgeStudioLanguageModelSetup> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return RekallAgeStudioLanguageModelSetup.Incomplete;

        try
        {
            await using var stream = new FileStream(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var candidate = await JsonSerializer.DeserializeAsync<RekallAgeStudioLanguageModelSetup>(
                    stream,
                    JsonOptions,
                    cancellationToken)
                .ConfigureAwait(false);
            return RekallAgeStudioLanguageModelSetup.Normalize(candidate)
                ?? RekallAgeStudioLanguageModelSetup.Incomplete;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return RekallAgeStudioLanguageModelSetup.Incomplete;
        }
    }

    public async ValueTask SaveAsync(RekallAgeStudioLanguageModelSetup setup, CancellationToken cancellationToken)
    {
        var normalized = RekallAgeStudioLanguageModelSetup.Normalize(setup)
            ?? throw new ArgumentException("Language model setup is incomplete or incompatible.", nameof(setup));
        var directory = System.IO.Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("Language model setup directory is unavailable.");
        Directory.CreateDirectory(directory);
        var temporaryPath = System.IO.Path.Combine(
            directory,
            $".{System.IO.Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, normalized, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static string ResolveSetupRoot()
    {
        var overrideRoot = Environment.GetEnvironmentVariable(SetupRootEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            try
            {
                if (System.IO.Path.IsPathFullyQualified(overrideRoot))
                {
                    return System.IO.Path.GetFullPath(overrideRoot);
                }
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // An invalid test/automation override must never redirect production preferences.
            }
        }

        return System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Rekall",
            "AGE",
            "Studio");
    }
}
