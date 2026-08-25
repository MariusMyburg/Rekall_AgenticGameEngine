using Rekall.Age.Agent.LanguageModels;
using Rekall.Age.Core.Commands;
using System.Text.Json.Serialization;

namespace Rekall.Age.Workflows;

public sealed record RekallAgeLanguageModelProviderDescriptor(
    string Id,
    string DisplayName,
    string DefaultModel,
    string AuthenticationKind);

public sealed class RekallAgeLanguageModelProviderSettings
{
    public string? OllamaUrl { get; init; }

    [JsonIgnore]
    public string? OpenAiApiKey { get; init; }

    public string? OpenAiUrl { get; init; }

    public override string ToString() => "Language model provider settings.";
}

public sealed class RekallAgeLanguageModelProviderCatalog
{
    private static readonly IReadOnlyList<RekallAgeLanguageModelProviderDescriptor> DefaultProviders =
    [
        new("ollama", "Ollama", "qwen3.5:35b", "none"),
        new("openai", "OpenAI", "gpt-5.6-sol", "api-key")
    ];

    private readonly RekallAgeLanguageModelProviderSettings _settings;
    private readonly Func<HttpClient> _httpClientFactory;

    public RekallAgeLanguageModelProviderCatalog(
        RekallAgeLanguageModelProviderSettings? settings = null,
        Func<HttpClient>? httpClientFactory = null)
    {
        _settings = settings ?? ReadEnvironmentSettings();
        _httpClientFactory = httpClientFactory ?? CreateHttpClient;
    }

    public IReadOnlyList<RekallAgeLanguageModelProviderDescriptor> Providers => DefaultProviders;

    public RekallAgeLanguageModelProviderLease Acquire(
        string providerId,
        RekallAgeCommandRegistry registry,
        RekallAgeLanguageModelProviderSettings? sessionSettings = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentNullException.ThrowIfNull(registry);
        var settings = sessionSettings ?? _settings;
        var normalizedProviderId = providerId.Trim().ToLowerInvariant();
        if (!Providers.Any(provider => provider.Id == normalizedProviderId))
        {
            throw new RekallAgeLanguageModelProviderException(
                "REKALL_LANGUAGE_MODEL_PROVIDER_UNSUPPORTED",
                normalizedProviderId,
                "The requested language-model provider is unsupported.",
                requestedValue: normalizedProviderId,
                resolvedValue: string.Join(',', Providers.Select(provider => provider.Id)));
        }

        if (normalizedProviderId == "openai" && string.IsNullOrWhiteSpace(settings.OpenAiApiKey))
        {
            throw new RekallAgeLanguageModelProviderException(
                "REKALL_OPENAI_API_KEY_MISSING",
                "openai",
                "OpenAI requires OPENAI_API_KEY for this session.");
        }

        var httpClient = _httpClientFactory();
        try
        {
            IRekallAgeLanguageModelClient client = normalizedProviderId switch
            {
                "ollama" => new RekallAgeOllamaLanguageModelClient(httpClient, ResolveOllamaUrl(settings)),
                "openai" => new RekallAgeOpenAiLanguageModelClient(
                    httpClient,
                    settings.OpenAiApiKey!,
                    ResolveOpenAiUrl(settings)),
                _ => throw new InvalidOperationException("Validated provider selection was not handled.")
            };
            return new RekallAgeLanguageModelProviderLease(
                normalizedProviderId,
                httpClient,
                client,
                new RekallAgeLanguageModelProjectAgentRunner(client, registry));
        }
        catch
        {
            httpClient.Dispose();
            throw;
        }
    }

    private static RekallAgeLanguageModelProviderSettings ReadEnvironmentSettings() => new()
    {
        OllamaUrl = Environment.GetEnvironmentVariable("REKALL_AGE_OLLAMA_URL"),
        OpenAiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY"),
        OpenAiUrl = Environment.GetEnvironmentVariable("REKALL_AGE_OPENAI_URL")
    };

    private static HttpClient CreateHttpClient() => new() { Timeout = TimeSpan.FromMinutes(30) };

    private static Uri ResolveOllamaUrl(RekallAgeLanguageModelProviderSettings settings) =>
        new(string.IsNullOrWhiteSpace(settings.OllamaUrl) ? "http://127.0.0.1:11434" : settings.OllamaUrl, UriKind.Absolute);

    private static Uri? ResolveOpenAiUrl(RekallAgeLanguageModelProviderSettings settings) =>
        string.IsNullOrWhiteSpace(settings.OpenAiUrl)
            ? null
            : new Uri(settings.OpenAiUrl, UriKind.Absolute);
}
