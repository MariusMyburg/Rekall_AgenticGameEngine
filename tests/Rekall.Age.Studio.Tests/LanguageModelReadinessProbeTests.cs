using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Rekall.Age.Agent.Codex;
using Rekall.Age.Agent.LanguageModels;
using Rekall.Age.Workflows;

namespace Rekall.Age.Studio.Tests;

public sealed class LanguageModelReadinessProbeTests
{
    private const string SentinelKey = "sentinel-super-secret-key";

    [Fact]
    public async Task LocalOllamaWithoutExecutableIsBlockedBeforeEndpointAccess()
    {
        var fixture = new ProbeFixture { ExecutablePath = null };

        var result = await fixture.ProbeAsync("ollama");

        AssertResult(result, "ollama", RekallAgeLanguageModelReadinessState.Blocked,
            "REKALL_ONBOARDING_OLLAMA_RUNTIME_MISSING", "open-ollama-download", canRetry: true);
        Assert.Equal(0, fixture.IdentityCalls);
        Assert.Equal(0, fixture.LeaseCalls);
    }

    [Fact]
    public async Task LocalOllamaWithExecutableAndRefusedServiceIsDistinctFromMissingRuntime()
    {
        var fixture = new ProbeFixture
        {
            IdentityFailure = new HttpRequestException(
                "private refusal detail",
                new SocketException((int)SocketError.ConnectionRefused))
        };

        var result = await fixture.ProbeAsync("ollama");

        AssertResult(result, "ollama", RekallAgeLanguageModelReadinessState.Blocked,
            "REKALL_ONBOARDING_OLLAMA_SERVICE_STOPPED", "start-ollama", canRetry: true);
    }

    [Fact]
    public async Task CustomOllamaEndpointDoesNotRequireLocalExecutableAndReportsUnreachable()
    {
        var fixture = new ProbeFixture
        {
            ExecutablePath = null,
            IdentityFailure = new HttpRequestException("private network detail")
        };

        var result = await fixture.ProbeAsync(
            "ollama",
            settings: new RekallAgeLanguageModelProviderSettings { OllamaUrl = "https://models.example.test" });

        AssertResult(result, "ollama", RekallAgeLanguageModelReadinessState.Blocked,
            "REKALL_ONBOARDING_OLLAMA_ENDPOINT_UNREACHABLE", "edit-endpoint", canRetry: true);
        Assert.Equal(0, fixture.ExecutableLookups);
    }

    [Fact]
    public async Task GgufImportRequiresLocalExecutableEvenWithACustomOllamaEndpoint()
    {
        var fixture = new ProbeFixture { ExecutablePath = null };

        var result = await fixture.ProbeAsync(
            "gguf",
            settings: new RekallAgeLanguageModelProviderSettings { OllamaUrl = "https://models.example.test" });

        AssertResult(result, "gguf", RekallAgeLanguageModelReadinessState.Blocked,
            "REKALL_ONBOARDING_OLLAMA_RUNTIME_MISSING", "open-ollama-download", canRetry: true);
        Assert.Equal(0, fixture.IdentityCalls);
        Assert.Equal(0, fixture.LeaseCalls);
    }

    [Fact]
    public async Task NonOllamaEndpointIsReportedAsInvalidWithoutResponseDetail()
    {
        var fixture = new ProbeFixture
        {
            IdentityFailure = ProviderFailure("REKALL_OLLAMA_ENDPOINT_INVALID", "private provider body")
        };

        var result = await fixture.ProbeAsync("ollama");

        AssertResult(result, "ollama", RekallAgeLanguageModelReadinessState.Blocked,
            "REKALL_ONBOARDING_OLLAMA_ENDPOINT_INVALID", "edit-endpoint", canRetry: false);
        Assert.DoesNotContain("private provider body", ResultText(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OllamaHttpStatusResponseIsInvalidEndpointRatherThanUnreachable()
    {
        const string privateDetail = "private status response detail";
        var fixture = new ProbeFixture
        {
            IdentityFailure = new HttpRequestException(
                privateDetail,
                inner: null,
                HttpStatusCode.NotFound)
        };

        var result = await fixture.ProbeAsync("ollama");

        AssertResult(result, "ollama", RekallAgeLanguageModelReadinessState.Blocked,
            "REKALL_ONBOARDING_OLLAMA_ENDPOINT_INVALID", "edit-endpoint", canRetry: false);
        Assert.DoesNotContain(privateDetail, ResultText(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task IdentityDependencyTimeoutIsTypedFailureWhenCallerRemainsActive()
    {
        const string privateDetail = "private dependency timeout";
        var fixture = new ProbeFixture
        {
            IdentityFailure = new TaskCanceledException(privateDetail)
        };

        var result = await fixture.ProbeAsync(
            "ollama",
            settings: new RekallAgeLanguageModelProviderSettings
            {
                OllamaUrl = "https://models.example.test"
            },
            cancellationToken: CancellationToken.None);

        AssertResult(result, "ollama", RekallAgeLanguageModelReadinessState.Blocked,
            "REKALL_ONBOARDING_OLLAMA_ENDPOINT_UNREACHABLE", "edit-endpoint", canRetry: true);
        Assert.DoesNotContain(privateDetail, ResultText(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OllamaWithoutModelsIsBlocked()
    {
        var fixture = new ProbeFixture { Models = [] };

        var result = await fixture.ProbeAsync("ollama");

        AssertResult(result, "ollama", RekallAgeLanguageModelReadinessState.Blocked,
            "REKALL_ONBOARDING_NO_MODELS", "download-default-model", canRetry: true);
    }

    [Fact]
    public async Task OllamaRequiresKnownToolCapableCompletionModel()
    {
        var fixture = new ProbeFixture
        {
            Models =
            [
                new("unknown:latest", 0),
                new("chat-only:latest", 0, SupportsTools: false, SupportsCompletion: true),
                new("tools-no-completion:latest", 0, SupportsTools: true, SupportsCompletion: false)
            ]
        };

        var result = await fixture.ProbeAsync("ollama");

        AssertResult(result, "ollama", RekallAgeLanguageModelReadinessState.Blocked,
            "REKALL_ONBOARDING_NO_TOOL_MODEL", "download-default-model", canRetry: true);
        Assert.Empty(result.CompatibleModels);
    }

    [Fact]
    public async Task CompatibleFallbackProducesWarningWhenPreferredModelIsMissing()
    {
        var fixture = new ProbeFixture
        {
            Models = [Compatible("other-tools:latest")]
        };

        var result = await fixture.ProbeAsync("ollama", preferredModel: "qwen3.8:27b");

        AssertResult(result, "ollama", RekallAgeLanguageModelReadinessState.Warning,
            "REKALL_ONBOARDING_DEFAULT_MODEL_MISSING", "select-compatible-model", canRetry: false);
        Assert.Equal(["other-tools:latest"], result.CompatibleModels);
    }

    [Fact]
    public async Task RecommendedOllamaModelIsReady()
    {
        var fixture = new ProbeFixture
        {
            Models = [Compatible("qwen3.8:27b"), Compatible("other-tools:latest")]
        };

        var result = await fixture.ProbeAsync("ollama", preferredModel: "qwen3.8:27b");

        AssertResult(result, "ollama", RekallAgeLanguageModelReadinessState.Ready,
            "REKALL_ONBOARDING_READY", recommendedActionId: null, canRetry: false);
        Assert.Equal(["other-tools:latest", "qwen3.8:27b"], result.CompatibleModels);
    }

    [Theory]
    [InlineData("openai")]
    [InlineData("kimi")]
    public async Task ApiProviderWithoutCredentialIsBlockedWithoutAcquiringLease(string providerId)
    {
        var fixture = new ProbeFixture();

        var result = await fixture.ProbeAsync(providerId);

        AssertResult(result, providerId, RekallAgeLanguageModelReadinessState.Blocked,
            "REKALL_ONBOARDING_API_KEY_REQUIRED", "enter-api-key", canRetry: false);
        Assert.Equal(0, fixture.LeaseCalls);
    }

    [Theory]
    [InlineData("openai", "OPENAI_API_KEY")]
    [InlineData("kimi", "KIMI_API_KEY")]
    public async Task EnvironmentCredentialParticipatesWithoutAppearingInResult(string providerId, string variableName)
    {
        var fixture = new ProbeFixture { Models = [Compatible("api-tools-model")] };
        fixture.Environment[variableName] = SentinelKey;

        var result = await fixture.ProbeAsync(providerId, preferredModel: "api-tools-model");

        Assert.Equal(RekallAgeLanguageModelReadinessState.Ready, result.State);
        Assert.DoesNotContain(SentinelKey, ResultText(result), StringComparison.Ordinal);
        Assert.Equal(SentinelKey, providerId == "openai"
            ? fixture.AcquiredSettings!.OpenAiApiKey
            : fixture.AcquiredSettings!.KimiApiKey);
    }

    [Theory]
    [InlineData("openai", HttpStatusCode.Unauthorized, "REKALL_ONBOARDING_AUTH_REJECTED", false)]
    [InlineData("openai", HttpStatusCode.Forbidden, "REKALL_ONBOARDING_AUTH_REJECTED", false)]
    [InlineData("kimi", HttpStatusCode.Unauthorized, "REKALL_ONBOARDING_AUTH_REJECTED", false)]
    [InlineData("kimi", HttpStatusCode.Forbidden, "REKALL_ONBOARDING_AUTH_REJECTED", false)]
    [InlineData("openai", HttpStatusCode.TooManyRequests, "REKALL_ONBOARDING_PROVIDER_RATE_LIMITED", true)]
    [InlineData("kimi", HttpStatusCode.TooManyRequests, "REKALL_ONBOARDING_PROVIDER_RATE_LIMITED", true)]
    [InlineData("openai", HttpStatusCode.ServiceUnavailable, "REKALL_ONBOARDING_PROVIDER_UNAVAILABLE", true)]
    [InlineData("kimi", HttpStatusCode.ServiceUnavailable, "REKALL_ONBOARDING_PROVIDER_UNAVAILABLE", true)]
    public async Task ApiProviderHttpFailuresHaveStableRedactedClassification(
        string providerId,
        HttpStatusCode statusCode,
        string expectedCode,
        bool canRetry)
    {
        var fixture = new ProbeFixture
        {
            ModelFailure = new HttpRequestException(SentinelKey, null, statusCode)
        };

        var result = await fixture.ProbeAsync(providerId, settings: SettingsWithKey(providerId));

        Assert.Equal(expectedCode, result.Code);
        Assert.Equal(canRetry, result.CanRetry);
        Assert.DoesNotContain(SentinelKey, ResultText(result), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("openai")]
    [InlineData("kimi")]
    public async Task ApiProviderNetworkFailureIsStableAndRedacted(string providerId)
    {
        var fixture = new ProbeFixture { ModelFailure = new HttpRequestException(SentinelKey) };

        var result = await fixture.ProbeAsync(providerId, settings: SettingsWithKey(providerId));

        AssertResult(result, providerId, RekallAgeLanguageModelReadinessState.Blocked,
            "REKALL_ONBOARDING_NETWORK_UNREACHABLE", "retry", canRetry: true);
        Assert.DoesNotContain(SentinelKey, ResultText(result), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("openai", "gpt-5.6-sol")]
    [InlineData("kimi", "kimi-k3")]
    public async Task ApiProviderWithToolCapableCompletionModelIsReady(string providerId, string modelId)
    {
        var fixture = new ProbeFixture { Models = [Compatible(modelId)] };

        var result = await fixture.ProbeAsync(
            providerId,
            preferredModel: modelId,
            settings: SettingsWithKey(providerId));

        Assert.Equal(RekallAgeLanguageModelReadinessState.Ready, result.State);
        Assert.Equal("REKALL_ONBOARDING_READY", result.Code);
    }

    [Fact]
    public async Task CodexAuthenticationRequiredPreservesActionableCodexCode()
    {
        var fixture = new ProbeFixture
        {
            ModelFailure = ProviderFailure(RekallAgeCodexErrorCodes.AuthenticationRequired, SentinelKey)
        };

        var result = await fixture.ProbeAsync("codex");

        AssertResult(result, "codex", RekallAgeLanguageModelReadinessState.Blocked,
            RekallAgeCodexErrorCodes.AuthenticationRequired, "sign-in-codex", canRetry: true);
        Assert.DoesNotContain(SentinelKey, ResultText(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CodexRequiresExactProjectAgentModel()
    {
        var fixture = new ProbeFixture { Models = [new("gpt-other", 0)] };

        var result = await fixture.ProbeAsync("codex");

        AssertResult(result, "codex", RekallAgeLanguageModelReadinessState.Blocked,
            RekallAgeCodexErrorCodes.ModelUnavailable, "retry", canRetry: true);
    }

    [Fact]
    public async Task CodexWithRequiredModelIsReady()
    {
        var fixture = new ProbeFixture { Models = [new("gpt-5.6-sol", 0)] };

        var result = await fixture.ProbeAsync("codex");

        Assert.Equal(RekallAgeLanguageModelReadinessState.Ready, result.State);
        Assert.Equal(["gpt-5.6-sol"], result.CompatibleModels);
    }

    [Theory]
    [InlineData(true, "REKALL_ONBOARDING_OLLAMA_RUNTIME_MISSING")]
    [InlineData(false, "REKALL_ONBOARDING_OLLAMA_ENDPOINT_UNREACHABLE")]
    public async Task GgufInheritsOllamaPrerequisiteFailures(bool defaultEndpoint, string expectedCode)
    {
        var fixture = new ProbeFixture
        {
            ExecutablePath = defaultEndpoint ? null : "C:\\Tools\\ollama.exe",
            IdentityFailure = defaultEndpoint ? null : new HttpRequestException("unreachable")
        };
        var settings = new RekallAgeLanguageModelProviderSettings
        {
            OllamaUrl = defaultEndpoint ? null : "https://models.example.test"
        };

        var result = await fixture.ProbeAsync("gguf", settings: settings);

        Assert.Equal("gguf", result.ProviderId);
        Assert.Equal(expectedCode, result.Code);
    }

    [Fact]
    public async Task CancellationPropagatesRatherThanBecomingFailureResult()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var fixture = new ProbeFixture { IdentityFailure = new OperationCanceledException(cancellation.Token) };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await fixture.ProbeAsync("ollama", cancellationToken: cancellation.Token));
    }

    [Theory]
    [InlineData("openai")]
    [InlineData("kimi")]
    public async Task EveryApiFailureResultOmitsSuppliedSentinelKey(string providerId)
    {
        var fixture = new ProbeFixture
        {
            ModelFailure = ProviderFailure("REKALL_PROVIDER_FAILURE", SentinelKey, HttpStatusCode.BadGateway)
        };

        var result = await fixture.ProbeAsync(providerId, settings: SettingsWithKey(providerId));

        Assert.DoesNotContain(SentinelKey, ResultText(result), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("openai")]
    [InlineData("kimi")]
    public async Task ProviderControlledModelIdentityCannotEchoSuppliedKeyIntoResult(string providerId)
    {
        var fixture = new ProbeFixture { Models = [Compatible(SentinelKey)] };

        var result = await fixture.ProbeAsync(providerId, settings: SettingsWithKey(providerId));

        Assert.DoesNotContain(SentinelKey, ResultText(result), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.CompatibleModels);
    }

    private static RekallAgeLanguageModelInfo Compatible(string id) =>
        new(id, 0, SupportsTools: true, SupportsCompletion: true);

    private static RekallAgeLanguageModelProviderSettings SettingsWithKey(string providerId) =>
        providerId == "openai"
            ? new RekallAgeLanguageModelProviderSettings { OpenAiApiKey = SentinelKey }
            : new RekallAgeLanguageModelProviderSettings { KimiApiKey = SentinelKey };

    private static RekallAgeLanguageModelProviderException ProviderFailure(
        string code,
        string message,
        HttpStatusCode? statusCode = null) => new(
            code,
            "test-provider",
            message,
            httpStatus: statusCode is null ? null : (int)statusCode,
            sensitiveValues: [SentinelKey]);

    private static string ResultText(RekallAgeLanguageModelReadinessResult result) => string.Join(
        "|",
        [
            result.ToString(),
            result.Summary,
            result.Code,
            result.RecommendedActionId ?? string.Empty,
            .. result.Checks.SelectMany(check => new[] { check.Id, check.Summary, check.ActionId ?? string.Empty }),
            .. result.CompatibleModels
        ]);

    private static void AssertResult(
        RekallAgeLanguageModelReadinessResult result,
        string providerId,
        RekallAgeLanguageModelReadinessState state,
        string code,
        string? recommendedActionId,
        bool canRetry)
    {
        Assert.Equal(providerId, result.ProviderId);
        Assert.Equal(state, result.State);
        Assert.Equal(code, result.Code);
        Assert.Equal(recommendedActionId, result.RecommendedActionId);
        Assert.Equal(canRetry, result.CanRetry);
        Assert.NotEmpty(result.Checks);
    }

    private sealed class ProbeFixture
    {
        public string? ExecutablePath { get; init; } = "C:\\Tools\\ollama.exe";
        public Exception? IdentityFailure { get; init; }
        public Exception? ModelFailure { get; init; }
        public IReadOnlyList<RekallAgeLanguageModelInfo> Models { get; init; } = [Compatible("qwen3.8:27b")];
        public Dictionary<string, string?> Environment { get; } = new(StringComparer.Ordinal);
        public int ExecutableLookups => _executableLocator.Calls;
        public int IdentityCalls => _identityProbe.Calls;
        public int LeaseCalls => _leaseSource.Calls;
        public RekallAgeLanguageModelProviderSettings? AcquiredSettings => _leaseSource.AcquiredSettings;

        private readonly FakeExecutableLocator _executableLocator;
        private readonly FakeOllamaIdentityProbe _identityProbe;
        private readonly FakeLeaseSource _leaseSource;
        private readonly FakeEnvironment _environment;

        public ProbeFixture()
        {
            _executableLocator = new FakeExecutableLocator(() => ExecutablePath);
            _identityProbe = new FakeOllamaIdentityProbe(() => IdentityFailure);
            _leaseSource = new FakeLeaseSource(() => (Models, ModelFailure));
            _environment = new FakeEnvironment(Environment);
        }

        public ValueTask<RekallAgeLanguageModelReadinessResult> ProbeAsync(
            string providerId,
            string? preferredModel = null,
            RekallAgeLanguageModelProviderSettings? settings = null,
            CancellationToken cancellationToken = default)
        {
            var probe = new RekallAgeLanguageModelReadinessProbe(
                _leaseSource,
                _executableLocator,
                new FakeProcessLauncher(),
                _identityProbe,
                _environment);
            return probe.ProbeAsync(
                new RekallAgeLanguageModelReadinessRequest(
                    providerId,
                    preferredModel,
                    settings ?? new RekallAgeLanguageModelProviderSettings()),
                cancellationToken);
        }
    }

    private sealed class FakeExecutableLocator(Func<string?> resolve) : IRekallAgeExecutableLocator
    {
        public int Calls { get; private set; }

        public string? FindOllamaExecutable()
        {
            Calls++;
            return resolve();
        }
    }

    private sealed class FakeProcessLauncher : IRekallAgeOllamaProcessLauncher
    {
        public ValueTask StartAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class FakeOllamaIdentityProbe(Func<Exception?> failure) : IRekallAgeOllamaIdentityProbe
    {
        public int Calls { get; private set; }

        public ValueTask<string> GetVersionAsync(
            RekallAgeLanguageModelProviderSettings settings,
            CancellationToken cancellationToken)
        {
            Calls++;
            return failure() is { } exception
                ? ValueTask.FromException<string>(exception)
                : ValueTask.FromResult("0.33.2");
        }
    }

    private sealed class FakeLeaseSource(Func<(IReadOnlyList<RekallAgeLanguageModelInfo> Models, Exception? Failure)> resolve)
        : IRekallAgeLanguageModelReadinessLeaseSource
    {
        public int Calls { get; private set; }
        public RekallAgeLanguageModelProviderSettings? AcquiredSettings { get; private set; }

        public ValueTask<IRekallAgeLanguageModelReadinessLease> AcquireAsync(
            string providerId,
            RekallAgeLanguageModelProviderSettings settings,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            AcquiredSettings = settings;
            var state = resolve();
            return ValueTask.FromResult<IRekallAgeLanguageModelReadinessLease>(
                new FakeLease(new FakeClient(providerId, state.Models, state.Failure)));
        }
    }

    private sealed class FakeLease(IRekallAgeLanguageModelClient modelClient)
        : IRekallAgeLanguageModelReadinessLease
    {
        public IRekallAgeLanguageModelClient ModelClient { get; } = modelClient;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeClient(
        string providerId,
        IReadOnlyList<RekallAgeLanguageModelInfo> models,
        Exception? failure) : IRekallAgeLanguageModelClient
    {
        public string ProviderId { get; } = providerId;

        public ValueTask<IReadOnlyList<RekallAgeLanguageModelInfo>> ListModelsAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return failure is null
                ? ValueTask.FromResult(models)
                : ValueTask.FromException<IReadOnlyList<RekallAgeLanguageModelInfo>>(failure);
        }

        public ValueTask<RekallAgeLanguageModelResponse> ChatAsync(
            RekallAgeLanguageModelRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeEnvironment(IReadOnlyDictionary<string, string?> values)
        : IRekallAgeEnvironmentValueSource
    {
        public string? GetValue(string name) => values.GetValueOrDefault(name);
    }
}
