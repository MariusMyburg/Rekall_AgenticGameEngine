using Rekall.Age.Agent.LanguageModels;
using Rekall.Age.Agent.Codex;
using Rekall.Age.Core.Commands;
using System.Text.Json.Serialization;

namespace Rekall.Age.Workflows;

public sealed record RekallAgeLanguageModelProviderDescriptor(
    string Id,
    string DisplayName,
    string DefaultModel,
    string AuthenticationKind)
{
    public string AuthenticationState { get; init; } = "unknown";

    public bool IsAvailable { get; init; } = true;

    public string Availability => IsAvailable ? "available" : "unavailable";

    public IReadOnlyList<RekallAgeLanguageModelProviderDiagnostic> Diagnostics { get; init; } = [];
}

public sealed record RekallAgeLanguageModelProviderDiagnostic(string Code, string Message);

public sealed class RekallAgeLanguageModelProviderSettings
{
    public string? OllamaUrl { get; init; }

    [JsonIgnore]
    public string? OpenAiApiKey { get; init; }

    public string? OpenAiUrl { get; init; }

    public string? CodexApprovalPolicy { get; init; }

    public override string ToString() => "Language model provider settings.";
}

public sealed class RekallAgeLanguageModelProviderCatalog
{
    private readonly RekallAgeLanguageModelProviderSettings _settings;
    private readonly Func<HttpClient> _httpClientFactory;
    private readonly Func<IRekallAgeCodexProjectAgentRunner>? _codexRunnerFactory;

    public RekallAgeLanguageModelProviderCatalog(
        RekallAgeLanguageModelProviderSettings? settings = null,
        Func<HttpClient>? httpClientFactory = null,
        Func<IRekallAgeCodexProjectAgentRunner>? codexRunnerFactory = null)
    {
        _settings = settings ?? ReadEnvironmentSettings();
        _httpClientFactory = httpClientFactory ?? CreateHttpClient;
        _codexRunnerFactory = codexRunnerFactory;
        Providers = DescribeProviders(_settings);
    }

    public IReadOnlyList<RekallAgeLanguageModelProviderDescriptor> Providers { get; }

    public IReadOnlyList<RekallAgeLanguageModelProviderDescriptor> DescribeProviders(
        RekallAgeLanguageModelProviderSettings? sessionSettings = null)
    {
        var settings = sessionSettings ?? _settings;
        var hasOpenAiApiKey = !string.IsNullOrWhiteSpace(settings.OpenAiApiKey);
        return Array.AsReadOnly<RekallAgeLanguageModelProviderDescriptor>(
        [
            new("ollama", "Local Ollama", "qwen3.5:35b", "none")
            {
                AuthenticationState = "not-required",
                IsAvailable = true,
                Diagnostics = []
            },
            new("openai", "OpenAI API", "gpt-5.6-sol", "api-key")
            {
                AuthenticationState = hasOpenAiApiKey ? "configured" : "required",
                IsAvailable = hasOpenAiApiKey,
                Diagnostics = hasOpenAiApiKey
                    ? []
                    : Array.AsReadOnly(
                    [
                        new RekallAgeLanguageModelProviderDiagnostic(
                            "REKALL_OPENAI_API_KEY_MISSING",
                            "OpenAI requires OPENAI_API_KEY or a session-only API key.")
                    ])
            },
            DescribeCodexProvider()
        ]);
    }

    public static RekallAgeLanguageModelProviderDescriptor DescribeCodexProvider(
        RekallAgeCodexAccount? account = null,
        IReadOnlyList<RekallAgeCodexModel>? models = null)
    {
        if (account is null)
        {
            return new RekallAgeLanguageModelProviderDescriptor(
                "codex",
                "Codex App Server",
                RekallAgeCodexProjectAgentRunner.RequiredModel,
                "codex-managed")
            {
                AuthenticationState = "unknown",
                IsAvailable = true,
                Diagnostics = []
            };
        }

        if (!account.IsAuthenticated)
        {
            return new RekallAgeLanguageModelProviderDescriptor(
                "codex",
                "Codex App Server",
                RekallAgeCodexProjectAgentRunner.RequiredModel,
                "codex-managed")
            {
                AuthenticationState = "required",
                IsAvailable = false,
                Diagnostics =
                [
                    new RekallAgeLanguageModelProviderDiagnostic(
                        RekallAgeCodexErrorCodes.AuthenticationRequired,
                        "Codex authentication is required. Sign in through Codex and retry.")
                ]
            };
        }

        var authenticationState = account.AuthenticationType switch
        {
            "apiKey" or "apikey" => "api-key",
            { Length: > 0 } value => value,
            _ => "authenticated"
        };
        var modelAvailable = models is null || models.Any(model =>
            !model.Hidden
            && string.Equals(
                model.Model,
                RekallAgeCodexProjectAgentRunner.RequiredModel,
                StringComparison.Ordinal));
        return new RekallAgeLanguageModelProviderDescriptor(
            "codex",
            "Codex App Server",
            RekallAgeCodexProjectAgentRunner.RequiredModel,
            "codex-managed")
        {
            AuthenticationState = authenticationState,
            IsAvailable = modelAvailable,
            Diagnostics = modelAvailable
                ? []
                :
                [
                    new RekallAgeLanguageModelProviderDiagnostic(
                        RekallAgeCodexErrorCodes.ModelUnavailable,
                        "The exact Codex project model is unavailable.")
                ]
        };
    }

    public RekallAgeLanguageModelProviderLease Acquire(
        string providerId,
        RekallAgeCommandRegistry registry,
        RekallAgeLanguageModelProviderSettings? sessionSettings = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentNullException.ThrowIfNull(registry);
        var settings = sessionSettings ?? _settings;
        var normalizedProviderId = providerId.Trim().ToLowerInvariant();
        var providers = DescribeProviders(settings);
        var descriptor = providers.SingleOrDefault(provider => provider.Id == normalizedProviderId);
        if (descriptor is null)
        {
            throw new RekallAgeLanguageModelProviderException(
                "REKALL_LANGUAGE_MODEL_PROVIDER_UNSUPPORTED",
                normalizedProviderId,
                "The requested language-model provider is unsupported.",
                requestedValue: normalizedProviderId,
                resolvedValue: string.Join(',', providers.Select(provider => provider.Id)));
        }

        if (!descriptor.IsAvailable && descriptor.Diagnostics.FirstOrDefault() is { } diagnostic)
        {
            throw new RekallAgeLanguageModelProviderException(
                diagnostic.Code,
                descriptor.Id,
                diagnostic.Message);
        }

        var httpClient = _httpClientFactory();
        try
        {
            if (normalizedProviderId == "codex")
            {
                var approvalPolicy = string.IsNullOrWhiteSpace(settings.CodexApprovalPolicy)
                    ? "on-request"
                    : settings.CodexApprovalPolicy;
                if (approvalPolicy is not "on-request" and not "never")
                {
                    throw new RekallAgeLanguageModelProviderException(
                        "REKALL_CODEX_APPROVAL_POLICY_INVALID",
                        "codex",
                        "Codex approval policy must be on-request or never.",
                        requestedValue: approvalPolicy,
                        resolvedValue: "on-request,never");
                }
                var codexRunner = _codexRunnerFactory?.Invoke()
                    ?? new RekallAgeCodexProjectAgentRunner(
                        RekallAgeCodexMcpConfiguration.Resolve(),
                        approvalPolicy: approvalPolicy == "never" ? "on-request" : approvalPolicy,
                        approvalCallback: approvalPolicy == "never"
                            ? static (_, _) => ValueTask.FromResult(
                                RekallAgeCodexApprovalDecision.Accept)
                            : null);
                return new RekallAgeLanguageModelProviderLease(
                    normalizedProviderId,
                    httpClient,
                    codexRunner,
                    codexRunner);
            }

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
