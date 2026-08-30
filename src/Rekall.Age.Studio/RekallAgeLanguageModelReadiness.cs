using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Rekall.Age.Agent.Codex;
using Rekall.Age.Agent.LanguageModels;
using Rekall.Age.Core.Commands;
using Rekall.Age.Workflows;

namespace Rekall.Age.Studio;

internal enum RekallAgeLanguageModelReadinessState
{
    Ready,
    Warning,
    Blocked
}

internal sealed record RekallAgeLanguageModelReadinessCheck(
    string Id,
    RekallAgeLanguageModelReadinessState State,
    string Summary,
    string? ActionId = null);

internal sealed record RekallAgeLanguageModelReadinessResult(
    string ProviderId,
    RekallAgeLanguageModelReadinessState State,
    string Code,
    string Summary,
    IReadOnlyList<RekallAgeLanguageModelReadinessCheck> Checks,
    IReadOnlyList<string> CompatibleModels,
    string? RecommendedActionId,
    bool CanRetry);

internal sealed record RekallAgeLanguageModelReadinessRequest(
    string ProviderId,
    string? PreferredModel,
    RekallAgeLanguageModelProviderSettings Settings);

internal interface IRekallAgeLanguageModelReadinessProbe
{
    ValueTask<RekallAgeLanguageModelReadinessResult> ProbeAsync(
        RekallAgeLanguageModelReadinessRequest request,
        CancellationToken cancellationToken);
}

internal interface IRekallAgeExecutableLocator
{
    string? FindOllamaExecutable();
}

internal interface IRekallAgeOllamaProcessLauncher
{
    ValueTask StartAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken);
}

internal interface IRekallAgeOllamaIdentityProbe
{
    ValueTask<string> GetVersionAsync(
        RekallAgeLanguageModelProviderSettings settings,
        CancellationToken cancellationToken);
}

internal interface IRekallAgeEnvironmentValueSource
{
    string? GetValue(string name);
}

internal interface IRekallAgeLanguageModelReadinessLease : IAsyncDisposable
{
    IRekallAgeLanguageModelClient ModelClient { get; }
}

internal interface IRekallAgeLanguageModelReadinessLeaseSource
{
    ValueTask<IRekallAgeLanguageModelReadinessLease> AcquireAsync(
        string providerId,
        RekallAgeLanguageModelProviderSettings settings,
        CancellationToken cancellationToken);
}

internal sealed class RekallAgeLanguageModelReadinessProbe : IRekallAgeLanguageModelReadinessProbe
{
    private const string DefaultOllamaUrl = "http://127.0.0.1:11434";
    private const string ReadyCode = "REKALL_ONBOARDING_READY";
    private readonly IRekallAgeLanguageModelReadinessLeaseSource _leaseSource;
    private readonly IRekallAgeExecutableLocator _executableLocator;
    private readonly IRekallAgeOllamaIdentityProbe _ollamaIdentityProbe;
    private readonly IRekallAgeEnvironmentValueSource _environment;

    public RekallAgeLanguageModelReadinessProbe(RekallAgeLanguageModelProviderCatalog catalog)
        : this(
            new CatalogLeaseSource(catalog),
            new ExecutableLocator(),
            new DefaultOllamaProcessLauncher(),
            new OllamaIdentityProbe(),
            new EnvironmentValueSource())
    {
    }

    internal RekallAgeLanguageModelReadinessProbe(
        IRekallAgeLanguageModelReadinessLeaseSource leaseSource,
        IRekallAgeExecutableLocator executableLocator,
        IRekallAgeOllamaProcessLauncher ollamaProcessLauncher,
        IRekallAgeOllamaIdentityProbe ollamaIdentityProbe,
        IRekallAgeEnvironmentValueSource environment)
    {
        _leaseSource = leaseSource ?? throw new ArgumentNullException(nameof(leaseSource));
        _executableLocator = executableLocator ?? throw new ArgumentNullException(nameof(executableLocator));
        OllamaProcessLauncher = ollamaProcessLauncher ?? throw new ArgumentNullException(nameof(ollamaProcessLauncher));
        _ollamaIdentityProbe = ollamaIdentityProbe ?? throw new ArgumentNullException(nameof(ollamaIdentityProbe));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    internal IRekallAgeOllamaProcessLauncher OllamaProcessLauncher { get; }

    public async ValueTask<RekallAgeLanguageModelReadinessResult> ProbeAsync(
        RekallAgeLanguageModelReadinessRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProviderId);
        ArgumentNullException.ThrowIfNull(request.Settings);
        cancellationToken.ThrowIfCancellationRequested();

        var providerId = request.ProviderId.Trim().ToLowerInvariant();
        var settings = ResolveEnvironmentCredentials(request.Settings);
        var checks = new List<RekallAgeLanguageModelReadinessCheck>();

        if (providerId is "ollama" or "gguf")
        {
            var prerequisiteFailure = await ProbeOllamaPrerequisitesAsync(
                providerId,
                settings,
                checks,
                cancellationToken).ConfigureAwait(false);
            if (prerequisiteFailure is not null)
            {
                return prerequisiteFailure;
            }
        }
        else if (providerId is "openai" or "kimi" && !HasApiKey(providerId, settings))
        {
            return Failure(
                providerId,
                "REKALL_ONBOARDING_API_KEY_REQUIRED",
                "An API key is required before this provider can be checked.",
                checks,
                "credential",
                "enter-api-key",
                canRetry: false);
        }

        try
        {
            await using var lease = await _leaseSource.AcquireAsync(
                providerId,
                settings,
                cancellationToken).ConfigureAwait(false);
            var models = await lease.ModelClient.ListModelsAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return EvaluateModels(
                providerId,
                request.PreferredModel,
                models,
                checks,
                SensitiveValues(settings));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return ClassifyProviderFailure(providerId, exception, checks);
        }
    }

    private async ValueTask<RekallAgeLanguageModelReadinessResult?> ProbeOllamaPrerequisitesAsync(
        string providerId,
        RekallAgeLanguageModelProviderSettings settings,
        List<RekallAgeLanguageModelReadinessCheck> checks,
        CancellationToken cancellationToken)
    {
        var isDefaultEndpoint = IsDefaultOllamaEndpoint(settings.OllamaUrl);
        if (isDefaultEndpoint)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(_executableLocator.FindOllamaExecutable()))
            {
                return Failure(
                    providerId,
                    "REKALL_ONBOARDING_OLLAMA_RUNTIME_MISSING",
                    "Ollama is not installed on this PC.",
                    checks,
                    "ollama-runtime",
                    "open-ollama-download",
                    canRetry: true);
            }

            checks.Add(Check("ollama-runtime", "Ollama is installed."));
        }

        try
        {
            var version = await _ollamaIdentityProbe.GetVersionAsync(settings, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(version))
            {
                return Failure(
                    providerId,
                    "REKALL_ONBOARDING_OLLAMA_ENDPOINT_INVALID",
                    "The configured endpoint is not an Ollama service.",
                    checks,
                    "ollama-endpoint",
                    "edit-endpoint",
                    canRetry: false);
            }

            checks.Add(Check("ollama-endpoint", "The Ollama service is reachable and identified."));
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (RekallAgeLanguageModelProviderException exception)
            when (exception.Code == "REKALL_OLLAMA_ENDPOINT_INVALID")
        {
            return Failure(
                providerId,
                "REKALL_ONBOARDING_OLLAMA_ENDPOINT_INVALID",
                "The configured endpoint is not an Ollama service.",
                checks,
                "ollama-endpoint",
                "edit-endpoint",
                canRetry: false);
        }
        catch (HttpRequestException exception)
        {
            var stopped = isDefaultEndpoint && IsConnectionRefused(exception);
            return Failure(
                providerId,
                stopped
                    ? "REKALL_ONBOARDING_OLLAMA_SERVICE_STOPPED"
                    : "REKALL_ONBOARDING_OLLAMA_ENDPOINT_UNREACHABLE",
                stopped
                    ? "Ollama is installed, but its local service is not running."
                    : "The configured Ollama endpoint could not be reached.",
                checks,
                "ollama-endpoint",
                stopped ? "start-ollama" : "edit-endpoint",
                canRetry: true);
        }
        catch (Exception exception) when (exception is InvalidDataException or System.Text.Json.JsonException)
        {
            return Failure(
                providerId,
                "REKALL_ONBOARDING_OLLAMA_ENDPOINT_INVALID",
                "The configured endpoint is not an Ollama service.",
                checks,
                "ollama-endpoint",
                "edit-endpoint",
                canRetry: false);
        }
    }

    private static RekallAgeLanguageModelReadinessResult EvaluateModels(
        string providerId,
        string? preferredModel,
        IReadOnlyList<RekallAgeLanguageModelInfo> models,
        List<RekallAgeLanguageModelReadinessCheck> checks,
        IReadOnlyList<string> sensitiveValues)
    {
        var safeModels = models
            .Where(model => !string.IsNullOrWhiteSpace(model.Id) && model.Id.Length <= 256)
            .Where(model => !sensitiveValues.Any(sensitiveValue =>
                model.Id.Contains(sensitiveValue, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (providerId == "codex")
        {
            var requiredModel = RekallAgeCodexProjectAgentRunner.RequiredModel;
            if (!safeModels.Any(model => string.Equals(model.Id, requiredModel, StringComparison.Ordinal)))
            {
                return Failure(
                    providerId,
                    RekallAgeCodexErrorCodes.ModelUnavailable,
                    "The exact Codex project model is unavailable.",
                    checks,
                    "models",
                    "retry",
                    canRetry: true);
            }

            checks.Add(Check("models", "The required Codex project model is available."));
            return Ready(providerId, checks, [requiredModel]);
        }

        if (safeModels.Length == 0 && models.Count == 0)
        {
            return Failure(
                providerId,
                "REKALL_ONBOARDING_NO_MODELS",
                "No models are available from this provider.",
                checks,
                "models",
                providerId is "ollama" or "gguf" ? "download-default-model" : "retry",
                canRetry: true);
        }

        var compatibleModels = safeModels
            .Where(model => model.SupportsCompletion is not false && model.SupportsTools is true)
            .Select(model => model.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        if (compatibleModels.Length == 0)
        {
            return Failure(
                providerId,
                "REKALL_ONBOARDING_NO_TOOL_MODEL",
                "No available completion model has confirmed tool support.",
                checks,
                "models",
                providerId is "ollama" or "gguf" ? "download-default-model" : "retry",
                canRetry: true);
        }

        checks.Add(Check("models", "At least one tool-capable completion model is available."));
        var desiredModel = string.IsNullOrWhiteSpace(preferredModel)
            ? DefaultModel(providerId)
            : preferredModel.Trim();
        if (!string.IsNullOrEmpty(desiredModel)
            && !compatibleModels.Contains(desiredModel, StringComparer.Ordinal))
        {
            checks.Add(new RekallAgeLanguageModelReadinessCheck(
                "preferred-model",
                RekallAgeLanguageModelReadinessState.Warning,
                "The preferred model is unavailable; choose a compatible model.",
                "select-compatible-model"));
            return new RekallAgeLanguageModelReadinessResult(
                providerId,
                RekallAgeLanguageModelReadinessState.Warning,
                "REKALL_ONBOARDING_DEFAULT_MODEL_MISSING",
                "The preferred model is unavailable, but a compatible alternative is ready.",
                checks.ToArray(),
                compatibleModels,
                "select-compatible-model",
                CanRetry: false);
        }

        checks.Add(Check("preferred-model", "The preferred model is available."));
        return Ready(providerId, checks, compatibleModels);
    }

    private static RekallAgeLanguageModelReadinessResult ClassifyProviderFailure(
        string providerId,
        Exception exception,
        List<RekallAgeLanguageModelReadinessCheck> checks)
    {
        if (providerId == "codex"
            && exception is RekallAgeLanguageModelProviderException codexFailure
            && codexFailure.Code == RekallAgeCodexErrorCodes.AuthenticationRequired)
        {
            return Failure(
                providerId,
                RekallAgeCodexErrorCodes.AuthenticationRequired,
                "Codex authentication is required.",
                checks,
                "authentication",
                "sign-in-codex",
                canRetry: true);
        }

        var statusCode = StatusCode(exception);
        var providerCode = (exception as RekallAgeLanguageModelProviderException)?.Code;
        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            || IsAuthenticationCode(providerCode))
        {
            return Failure(
                providerId,
                "REKALL_ONBOARDING_AUTH_REJECTED",
                "The provider rejected the configured credential.",
                checks,
                "authentication",
                "update-api-key",
                canRetry: false);
        }

        if (statusCode == HttpStatusCode.TooManyRequests || IsRateLimitCode(providerCode))
        {
            return Failure(
                providerId,
                "REKALL_ONBOARDING_PROVIDER_RATE_LIMITED",
                "The provider is rate limited. Retry later.",
                checks,
                "provider",
                "retry",
                canRetry: true);
        }

        if (statusCode == HttpStatusCode.RequestTimeout
            || statusCode is not null && (int)statusCode >= 500
            || IsUnavailableCode(providerCode))
        {
            return Failure(
                providerId,
                "REKALL_ONBOARDING_PROVIDER_UNAVAILABLE",
                "The provider is temporarily unavailable.",
                checks,
                "provider",
                "retry",
                canRetry: true);
        }

        if (exception is HttpRequestException)
        {
            return Failure(
                providerId,
                "REKALL_ONBOARDING_NETWORK_UNREACHABLE",
                "The provider could not be reached from this PC.",
                checks,
                "network",
                "retry",
                canRetry: true);
        }

        return Failure(
            providerId,
            "REKALL_ONBOARDING_PROVIDER_UNAVAILABLE",
            "The provider could not be checked.",
            checks,
            "provider",
            "retry",
            canRetry: true);
    }

    private RekallAgeLanguageModelProviderSettings ResolveEnvironmentCredentials(
        RekallAgeLanguageModelProviderSettings settings)
    {
        var openAiApiKey = FirstNonEmpty(settings.OpenAiApiKey, _environment.GetValue("OPENAI_API_KEY"));
        var kimiApiKey = FirstNonEmpty(
            settings.KimiApiKey,
            _environment.GetValue("KIMI_API_KEY"),
            _environment.GetValue("MOONSHOT_API_KEY"));
        return new RekallAgeLanguageModelProviderSettings
        {
            OllamaUrl = settings.OllamaUrl,
            OpenAiApiKey = openAiApiKey,
            OpenAiUrl = settings.OpenAiUrl,
            KimiApiKey = kimiApiKey,
            KimiUrl = settings.KimiUrl,
            CodexApprovalPolicy = settings.CodexApprovalPolicy
        };
    }

    private static RekallAgeLanguageModelReadinessResult Failure(
        string providerId,
        string code,
        string summary,
        List<RekallAgeLanguageModelReadinessCheck> checks,
        string checkId,
        string actionId,
        bool canRetry)
    {
        checks.Add(new RekallAgeLanguageModelReadinessCheck(
            checkId,
            RekallAgeLanguageModelReadinessState.Blocked,
            summary,
            actionId));
        return new RekallAgeLanguageModelReadinessResult(
            providerId,
            RekallAgeLanguageModelReadinessState.Blocked,
            code,
            summary,
            checks.ToArray(),
            [],
            actionId,
            canRetry);
    }

    private static RekallAgeLanguageModelReadinessResult Ready(
        string providerId,
        List<RekallAgeLanguageModelReadinessCheck> checks,
        IReadOnlyList<string> compatibleModels) => new(
            providerId,
            RekallAgeLanguageModelReadinessState.Ready,
            ReadyCode,
            "This provider is ready for agent authoring.",
            checks.ToArray(),
            compatibleModels,
            RecommendedActionId: null,
            CanRetry: false);

    private static RekallAgeLanguageModelReadinessCheck Check(string id, string summary) => new(
        id,
        RekallAgeLanguageModelReadinessState.Ready,
        summary);

    private static bool HasApiKey(string providerId, RekallAgeLanguageModelProviderSettings settings) =>
        !string.IsNullOrWhiteSpace(providerId == "openai" ? settings.OpenAiApiKey : settings.KimiApiKey);

    private static bool IsDefaultOllamaEndpoint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var endpoint)
            && string.Equals(
                endpoint.AbsoluteUri.TrimEnd('/'),
                DefaultOllamaUrl,
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsConnectionRefused(HttpRequestException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SocketException { SocketErrorCode: SocketError.ConnectionRefused })
            {
                return true;
            }
        }

        return false;
    }

    private static HttpStatusCode? StatusCode(Exception exception) => exception switch
    {
        RekallAgeLanguageModelProviderException { HttpStatus: { } status }
            when Enum.IsDefined(typeof(HttpStatusCode), status) => (HttpStatusCode)status,
        HttpRequestException { StatusCode: { } status } => status,
        _ => null
    };

    private static bool IsAuthenticationCode(string? code) =>
        code?.Contains("AUTHENTICATION", StringComparison.Ordinal) == true
        || code?.Contains("API_KEY_INVALID", StringComparison.Ordinal) == true;

    private static bool IsRateLimitCode(string? code) =>
        code?.Contains("RATE_LIMIT", StringComparison.Ordinal) == true
        || code?.Contains("RATE_LIMITED", StringComparison.Ordinal) == true;

    private static bool IsUnavailableCode(string? code) =>
        code?.Contains("UNAVAILABLE", StringComparison.Ordinal) == true
        || code?.Contains("TIMEOUT", StringComparison.Ordinal) == true;

    private static string? DefaultModel(string providerId) => providerId switch
    {
        "ollama" or "gguf" => "qwen3.8:27b",
        "kimi" => "kimi-k3",
        "openai" => "gpt-5.6-sol",
        _ => null
    };

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static IReadOnlyList<string> SensitiveValues(RekallAgeLanguageModelProviderSettings settings) =>
        new[] { settings.OpenAiApiKey, settings.KimiApiKey }
            .Where(value => !string.IsNullOrEmpty(value))
            .Select(value => value!)
            .ToArray();

    private sealed class CatalogLeaseSource(RekallAgeLanguageModelProviderCatalog catalog)
        : IRekallAgeLanguageModelReadinessLeaseSource
    {
        private readonly RekallAgeLanguageModelProviderCatalog _catalog =
            catalog ?? throw new ArgumentNullException(nameof(catalog));

        public ValueTask<IRekallAgeLanguageModelReadinessLease> AcquireAsync(
            string providerId,
            RekallAgeLanguageModelProviderSettings settings,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lease = _catalog.Acquire(providerId, new RekallAgeCommandRegistry(), settings);
            return ValueTask.FromResult<IRekallAgeLanguageModelReadinessLease>(new CatalogLease(lease));
        }
    }

    private sealed class CatalogLease(RekallAgeLanguageModelProviderLease lease)
        : IRekallAgeLanguageModelReadinessLease
    {
        private readonly RekallAgeLanguageModelProviderLease _lease =
            lease ?? throw new ArgumentNullException(nameof(lease));

        public IRekallAgeLanguageModelClient ModelClient => _lease.ModelClient;

        public ValueTask DisposeAsync() => _lease.DisposeAsync();
    }

    private sealed class OllamaIdentityProbe : IRekallAgeOllamaIdentityProbe
    {
        public async ValueTask<string> GetVersionAsync(
            RekallAgeLanguageModelProviderSettings settings,
            CancellationToken cancellationToken)
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var endpoint = new Uri(
                string.IsNullOrWhiteSpace(settings.OllamaUrl) ? DefaultOllamaUrl : settings.OllamaUrl,
                UriKind.Absolute);
            var client = new RekallAgeOllamaLanguageModelClient(httpClient, endpoint);
            return await client.GetVersionAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class ExecutableLocator : IRekallAgeExecutableLocator
    {
        public string? FindOllamaExecutable()
        {
            var executableName = OperatingSystem.IsWindows() ? "ollama.exe" : "ollama";
            foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var candidate = Path.Combine(directory, executableName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            if (OperatingSystem.IsWindows())
            {
                var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var candidate = Path.Combine(localApplicationData, "Programs", "Ollama", executableName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }
    }

    private sealed class DefaultOllamaProcessLauncher : IRekallAgeOllamaProcessLauncher
    {
        public ValueTask StartAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
            ArgumentNullException.ThrowIfNull(arguments);
            cancellationToken.ThrowIfCancellationRequested();
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            _ = Process.Start(startInfo)
                ?? throw new InvalidOperationException("The Ollama process could not be started.");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class EnvironmentValueSource : IRekallAgeEnvironmentValueSource
    {
        public string? GetValue(string name) => Environment.GetEnvironmentVariable(name);
    }
}
