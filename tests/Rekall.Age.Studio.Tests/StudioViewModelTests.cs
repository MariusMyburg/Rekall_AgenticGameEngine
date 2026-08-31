using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rekall.Age.Agent.Codex;
using Rekall.Age.Agent.LanguageModels;
using Rekall.Age.Assets;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Editor;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;
using Rekall.Age.Project;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Rendering.Commands;
using Rekall.Age.Rendering.Windows;
using Rekall.Age.Studio;
using Rekall.Age.Workflows;
using Rekall.Age.Workflows.Commands;
using Rekall.Age.World;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Rekall.Age.Studio.Tests;

public sealed class StudioViewModelTests
{
    [Fact]
    public async Task ChangingWorldViewportStyleImmediatelyPresentsTheCurrentVulkanFrame()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-style-" + Guid.NewGuid().ToString("N"));
        try
        {
            var preview = new RecordingPreviewSession();
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                preview)
            {
                ProjectPathInput = root,
                ProjectNameInput = "Style Test",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);

            viewModel.WorldViewportRenderStyle = "Wireframe";
            await WaitForAsync(() => preview.PresentCurrentCount == 1);

            Assert.Equal(RekallAgeStudioViewportRenderStyle.Wireframe, preview.RenderStyle);
            Assert.Equal(1, preview.PresentCurrentCount);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AgentMessageFeedbackKeepsTheCompleteMessageInItsDedicatedStream()
    {
        await using var viewModel = new RekallAgeStudioViewModel(
            new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
            new EmptyModel());
        var message = new string('m', 3_000);
        var report = typeof(RekallAgeStudioViewModel).GetMethod(
            "ReportAgentProgress",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        report.Invoke(viewModel, [new RekallAgeLanguageModelAgentProgress(2, "agent.message", message)]);
        report.Invoke(viewModel, [new RekallAgeLanguageModelAgentProgress(2, "tool.start", "not feedback")]);

        Assert.Equal([message], viewModel.AgentMessageLines);
    }

    [Fact]
    public async Task InteractiveStudioDoesNotOptimisticallyReportUncheckedOllamaAsReady()
    {
        await using var viewModel = new RekallAgeStudioViewModel(
            new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
            new EmptyModel());

        Assert.Equal("deterministic test session ready.", viewModel.ProviderStatus);

        await using var ordinary = new RekallAgeStudioViewModel();
        Assert.Equal("Local Ollama selected; setup not checked.", ordinary.ProviderStatus);
    }

    [Fact]
    public async Task RestoreLanguageModelSetupSelectsOnlyADiscoveredSavedModelAndRestoresReasoning()
    {
        var handlers = new Queue<ProviderLifecycleHandler>(
        [
            new ProviderLifecycleHandler(blockOllamaChat: false),
            new ProviderLifecycleHandler(blockOllamaChat: false, openAiModels: ["gpt-5.6-sol", "gpt-5.6-sol-preview"]),
            new ProviderLifecycleHandler(blockOllamaChat: false, openAiModels: ["gpt-5.6-sol", "gpt-5.6-sol-preview"])
        ]);
        var catalog = new RekallAgeLanguageModelProviderCatalog(
            new RekallAgeLanguageModelProviderSettings { OpenAiApiKey = " " },
            () => new HttpClient(handlers.Dequeue(), disposeHandler: true));
        await using var viewModel = new RekallAgeStudioViewModel(
            new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
            catalog,
            new RecordingPreviewSession());
        var setup = RekallAgeStudioLanguageModelSetup.Incomplete with
        {
            ProviderId = "openai",
            ModelId = "gpt-5.6-sol-preview",
            ReasoningEffort = "xhigh"
        };

        await viewModel.RestoreLanguageModelSetupAsync(
            setup,
            "restore-session-openai-key",
            null,
            CancellationToken.None);

        Assert.Equal("openai", viewModel.SelectedLanguageModelProvider.Id);
        Assert.Equal("gpt-5.6-sol-preview", viewModel.SelectedLanguageModel);
        Assert.Equal("xhigh", viewModel.SelectedReasoningEffort);

        await viewModel.RestoreLanguageModelSetupAsync(
            setup with { ModelId = "invented-model" },
            "restore-session-openai-key",
            null,
            CancellationToken.None);
        Assert.NotEqual("invented-model", viewModel.SelectedLanguageModel);
    }

    [Fact]
    public void CodexApprovalSummaryShowsAllowlistedActionFactsAndOmitsCredentialLikeFields()
    {
        using var document = JsonDocument.Parse(
            """{"command":["dotnet","build"],"cwd":"C:\\Game","reason":"Build modules","apiKey":"secret-key","token":"secret-token","changes":[{"path":"Scenes/Main.age.scene.json"}],"mcpServer":"rekall-age","toolName":"rekall.build.modules","message":"Compile authored modules"}""");
        var request = new RekallAgeCodexApprovalRequest(
            "mcpServer/elicitation/request",
            document.RootElement.Clone());

        Assert.True(RekallAgeCodexApprovalPresenter.TryFormat(request, out var summary));
        Assert.Contains("dotnet build", summary, StringComparison.Ordinal);
        Assert.Contains("C:\\Game", summary, StringComparison.Ordinal);
        Assert.Contains("Scenes/Main.age.scene.json", summary, StringComparison.Ordinal);
        Assert.Contains("rekall-age", summary, StringComparison.Ordinal);
        Assert.Contains("rekall.build.modules", summary, StringComparison.Ordinal);
        Assert.Contains("Compile authored modules", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-key", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token", summary, StringComparison.Ordinal);
        Assert.True(summary.Length <= 1_200);
    }

    [Fact]
    public void CodexApprovalSummaryFailsClosedForUnknownMethodsOrUninformativeShapes()
    {
        using var unknown = JsonDocument.Parse("""{"command":"dangerous"}""");
        Assert.False(RekallAgeCodexApprovalPresenter.TryFormat(
            new RekallAgeCodexApprovalRequest("unknown/request", unknown.RootElement.Clone()), out _));
        Assert.False(RekallAgeCodexApprovalPresenter.TryFormat(
            new RekallAgeCodexApprovalRequest("unknown/commandExecution/requestApproval", unknown.RootElement.Clone()), out _));
        Assert.False(RekallAgeCodexApprovalPresenter.TryFormat(
            new RekallAgeCodexApprovalRequest("item/commandExecution/requestApproval/unsupported", unknown.RootElement.Clone()), out _));
        using var secretOnly = JsonDocument.Parse("""{"apiKey":"secret"}""");
        Assert.False(RekallAgeCodexApprovalPresenter.TryFormat(
            new RekallAgeCodexApprovalRequest("item/commandExecution/requestApproval", secretOnly.RootElement.Clone()), out _));
    }

    [Fact]
    public void CodexApprovalSessionCanRememberOneMcpToolWithoutApprovingOthers()
    {
        var session = new RekallAgeCodexApprovalSession();
        using var firstDocument = JsonDocument.Parse(
            """{"serverName":"rekall-age","message":"Allow the rekall-age MCP server to run tool 'rekall.context.scene_summary'?"}""");
        using var secondDocument = JsonDocument.Parse(
            """{"serverName":"rekall-age","message":"Allow the rekall-age MCP server to run tool 'rekall.validation.scene'?"}""");
        var first = new RekallAgeCodexApprovalRequest("mcpServer/elicitation/request", firstDocument.RootElement.Clone());
        var second = new RekallAgeCodexApprovalRequest("mcpServer/elicitation/request", secondDocument.RootElement.Clone());

        Assert.False(session.IsApproved(first));
        session.ApproveAction(first);

        Assert.True(session.IsApproved(first));
        Assert.False(session.IsApproved(second));
    }

    [Fact]
    public void CodexApprovalSessionCanPreapproveEveryActionUntilCleared()
    {
        var session = new RekallAgeCodexApprovalSession { ApproveAll = true };
        using var document = JsonDocument.Parse("""{"command":["dotnet","build"],"cwd":"C:\\Game"}""");
        var request = new RekallAgeCodexApprovalRequest(
            "item/commandExecution/requestApproval",
            document.RootElement.Clone());

        Assert.True(session.IsApproved(request));

        session.Clear();
        Assert.False(session.ApproveAll);
        Assert.False(session.IsApproved(request));
    }

    [Fact]
    public async Task StudioViewModelExposesFailClosedCodexSignInAndCancellationActions()
    {
        var viewModel = new RekallAgeStudioViewModel();
        try
        {
            Assert.False(viewModel.SignInCodexCommand.CanExecute(null));
            Assert.False(viewModel.CancelCodexSignInCommand.CanExecute(null));
        }
        finally
        {
            await viewModel.DisposeAsync();
        }
    }

    [Fact]
    public async Task FreshUnauthenticatedCodexSelectionRetainsRunnerAndRecoversThroughStudioSignIn()
    {
        var runner = new UnauthenticatedCodexRunner();
        var catalog = new RekallAgeLanguageModelProviderCatalog(
            new RekallAgeLanguageModelProviderSettings(),
            () => new HttpClient(new ProviderLifecycleHandler(false), disposeHandler: true),
            () => runner);
        var root = Path.Combine(Path.GetTempPath(), "rekall-studio-codex-signin-" + Guid.NewGuid().ToString("N"));
        await using var viewModel = new RekallAgeStudioViewModel(
            new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
            catalog,
            new RecordingPreviewSession());
        try
        {
            viewModel.SelectedLanguageModelProvider = viewModel.LanguageModelProviders.Single(item => item.Id == "codex");
            await viewModel.WaitForLanguageModelProviderTransitionAsync();

            Assert.Contains(RekallAgeCodexErrorCodes.AuthenticationRequired, viewModel.ProviderStatus, StringComparison.Ordinal);
            Assert.True(viewModel.SignInCodexCommand.CanExecute(null));
            viewModel.CodexAuthenticationLauncher = uri =>
            {
                runner.LaunchedUri = uri;
                return ValueTask.CompletedTask;
            };
            await ExecuteAsync(viewModel.SignInCodexCommand);

            Assert.Equal("https://chatgpt.com/sign-in", runner.LaunchedUri?.AbsoluteUri);
            Assert.Equal([RekallAgeCodexProjectAgentRunner.RequiredModel], viewModel.LanguageModels);
            Assert.Equal(RekallAgeCodexProjectAgentRunner.RequiredModel, viewModel.SelectedLanguageModel);
            viewModel.ProjectPathInput = root;
            viewModel.ProjectNameInput = "Codex Sign-In";
            viewModel.SceneNameInput = "Main";
            viewModel.AgentTaskInput = "Author a small game.";
            await ExecuteAsync(viewModel.CreateCommand);
            Assert.True(viewModel.RunAgentCommand.CanExecute(null));

            viewModel.SelectedLanguageModelProvider = viewModel.LanguageModelProviders.Single(item => item.Id == "ollama");
            await viewModel.WaitForLanguageModelProviderTransitionAsync();
            Assert.Equal(1, runner.DisposeCount);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CodexApprovalRequestsRouteToTheStudioHandlerAndDefaultToDecline()
    {
        await using var viewModel = new RekallAgeStudioViewModel(
            new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
            new EmptyModel());
        using var parameters = JsonDocument.Parse("{\"itemId\":\"change-1\"}");
        var request = new RekallAgeCodexApprovalRequest(
            "item/fileChange/requestApproval",
            parameters.RootElement.Clone());

        Assert.Equal(
            RekallAgeCodexApprovalDecision.Decline,
            await viewModel.RouteCodexApprovalAsync(request, CancellationToken.None));

        RekallAgeCodexApprovalRequest? observed = null;
        viewModel.CodexApprovalHandler = (candidate, _) =>
        {
            observed = candidate;
            return ValueTask.FromResult(RekallAgeCodexApprovalDecision.AcceptForSession);
        };

        Assert.Equal(
            RekallAgeCodexApprovalDecision.AcceptForSession,
            await viewModel.RouteCodexApprovalAsync(request, CancellationToken.None));
        Assert.Equal(request, observed);
    }

    [Fact]
    public async Task ProviderPresentationShowsOnlyTheSelectedProviderConfiguration()
    {
        var catalog = new RekallAgeLanguageModelProviderCatalog(
            new RekallAgeLanguageModelProviderSettings { OpenAiApiKey = "session-test-key" },
            () => new HttpClient(new ProviderLifecycleHandler(blockOllamaChat: false), disposeHandler: true));
        await using var viewModel = new RekallAgeStudioViewModel(
            new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
            catalog,
            new RecordingPreviewSession());

        Assert.True(viewModel.IsOllamaSelected);
        Assert.False(viewModel.IsOpenAiSelected);
        Assert.False(viewModel.IsCodexSelected);

        viewModel.SelectedLanguageModelProvider = viewModel.LanguageModelProviders.Single(provider => provider.Id == "codex");
        await viewModel.WaitForLanguageModelProviderTransitionAsync();

        Assert.False(viewModel.IsOllamaSelected);
        Assert.False(viewModel.IsOpenAiSelected);
        Assert.True(viewModel.IsCodexSelected);
    }

    [Fact]
    public async Task ProviderSelectionExposesStableMissingOpenAiCredentialGateWithoutRetainingOllamaModels()
    {
        var ollama = new ProviderLifecycleHandler(blockOllamaChat: false);
        var catalog = new RekallAgeLanguageModelProviderCatalog(
            new RekallAgeLanguageModelProviderSettings { OpenAiApiKey = " " },
            () => new HttpClient(ollama, disposeHandler: true));
        await using var viewModel = new RekallAgeStudioViewModel(
            new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
            catalog,
            new RecordingPreviewSession());

        Assert.Equal(["ollama", "gguf", "kimi", "openai", "codex"], viewModel.LanguageModelProviders.Select(provider => provider.Id));
        Assert.Equal(["none", "low", "medium", "high", "xhigh", "max"], viewModel.ReasoningEfforts);
        Assert.Equal("medium", viewModel.SelectedReasoningEffort);
        Assert.Equal("ollama", viewModel.SelectedLanguageModelProvider.Id);
        await ExecuteAsync(viewModel.RefreshLanguageModelsCommand);
        Assert.Equal(["gemma3:latest", "qwen3.8:27b"], viewModel.LanguageModels);
        Assert.Equal("qwen3.8:27b", viewModel.SelectedLanguageModel);

        viewModel.SelectedLanguageModelProvider = viewModel.LanguageModelProviders.Single(provider => provider.Id == "openai");
        await viewModel.WaitForLanguageModelProviderTransitionAsync();

        Assert.Empty(viewModel.LanguageModels);
        Assert.Empty(viewModel.SelectedLanguageModel);
        Assert.Equal(
            "REKALL_OPENAI_API_KEY_MISSING: OpenAI requires OPENAI_API_KEY or a session-only API key.",
            viewModel.ProviderStatus);
        Assert.False(viewModel.RefreshLanguageModelsCommand.CanExecute(null));
        Assert.DoesNotContain("qwen", viewModel.ProviderStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProviderSwitchCancelsAndAwaitsTheCurrentRunBeforeDisposingItsLeaseAndLoadingTheExactDefault()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-provider-switch-" + Guid.NewGuid().ToString("N"));
        var ollama = new ProviderLifecycleHandler(blockOllamaChat: true);
        var openAi = new ProviderLifecycleHandler(blockOllamaChat: false);
        var handlers = new Queue<ProviderLifecycleHandler>([ollama, openAi]);
        var catalog = new RekallAgeLanguageModelProviderCatalog(
            new RekallAgeLanguageModelProviderSettings { OpenAiApiKey = "session-test-key" },
            () => new HttpClient(handlers.Dequeue(), disposeHandler: true));
        try
        {
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                catalog,
                new RecordingPreviewSession())
            {
                ProjectPathInput = root,
                ProjectNameInput = "Provider Switch",
                SceneNameInput = "Main",
                AgentTaskInput = "Inspect the open project."
            };
            await ExecuteAsync(viewModel.CreateCommand);
            await ExecuteAsync(viewModel.RefreshLanguageModelsCommand);

            var run = ExecuteAsync(viewModel.RunAgentCommand);
            await ollama.WaitForChatAsync();
            viewModel.SelectedLanguageModelProvider = viewModel.LanguageModelProviders.Single(provider => provider.Id == "openai");
            await viewModel.WaitForLanguageModelProviderTransitionAsync();
            await run;

            Assert.Equal(["chat-cancelled", "lease-disposed"], ollama.Events);
            Assert.Equal("openai", viewModel.SelectedLanguageModelProvider.Id);
            Assert.Equal(["gpt-5.6-sol", "gpt-5.6-sol-preview"], viewModel.LanguageModels);
            Assert.Equal("gpt-5.6-sol", viewModel.SelectedLanguageModel);
            Assert.Equal("OpenAI API ready with 2 models.", viewModel.ProviderStatus);
            Assert.False(viewModel.IsAgentRunning);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task OpenAiSessionKeyUnlocksTheSelectedProviderWithoutAppearingInInspectableStudioState()
    {
        const string secret = "studio-session-secret-must-stay-private";
        var handlers = new Queue<ProviderLifecycleHandler>(
            [new(blockOllamaChat: false), new(blockOllamaChat: false)]);
        var catalog = new RekallAgeLanguageModelProviderCatalog(
            new RekallAgeLanguageModelProviderSettings { OpenAiApiKey = " " },
            () => new HttpClient(handlers.Dequeue(), disposeHandler: true));
        await using var viewModel = new RekallAgeStudioViewModel(
            new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
            catalog,
            new RecordingPreviewSession());
        var missingCredentialDescriptor = viewModel.LanguageModelProviders.Single(
            provider => provider.Id == "openai");
        Assert.Equal("required", missingCredentialDescriptor.AuthenticationState);
        Assert.False(missingCredentialDescriptor.IsAvailable);
        viewModel.SelectedLanguageModelProvider = viewModel.LanguageModelProviders.Single(provider => provider.Id == "openai");
        await viewModel.WaitForLanguageModelProviderTransitionAsync();

        await viewModel.ApplyOpenAiApiKeyAsync(secret);

        Assert.True(viewModel.HasSessionOpenAiCredential);
        var configuredDescriptor = viewModel.LanguageModelProviders.Single(provider => provider.Id == "openai");
        Assert.Equal("configured", configuredDescriptor.AuthenticationState);
        Assert.True(configuredDescriptor.IsAvailable);
        Assert.Empty(configuredDescriptor.Diagnostics);
        Assert.Equal(configuredDescriptor, viewModel.SelectedLanguageModelProvider);
        Assert.Equal("gpt-5.6-sol", viewModel.SelectedLanguageModel);
        var inspectable = string.Join('\n',
            [viewModel.ProviderStatus, viewModel.StatusText, .. viewModel.ValidationLines, .. viewModel.AgentLines]);
        Assert.DoesNotContain(secret, inspectable, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProviderSwitchCancelsAndAwaitsModelRefreshBeforeDisposingItsLease()
    {
        var ollama = new ProviderLifecycleHandler(blockOllamaChat: false, blockOllamaModels: true);
        var openAi = new ProviderLifecycleHandler(blockOllamaChat: false);
        var handlers = new Queue<ProviderLifecycleHandler>([ollama, openAi]);
        var catalog = new RekallAgeLanguageModelProviderCatalog(
            new RekallAgeLanguageModelProviderSettings { OpenAiApiKey = "session-test-key" },
            () => new HttpClient(handlers.Dequeue(), disposeHandler: true));
        await using var viewModel = new RekallAgeStudioViewModel(
            new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
            catalog,
            new RecordingPreviewSession());

        var refresh = ExecuteAsync(viewModel.RefreshLanguageModelsCommand);
        await ollama.WaitForModelsAsync();
        viewModel.SelectedLanguageModelProvider = viewModel.LanguageModelProviders.Single(provider => provider.Id == "openai");
        await viewModel.WaitForLanguageModelProviderTransitionAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await refresh;

        Assert.Equal(["models-cancelled", "lease-disposed"], ollama.Events);
        Assert.Equal("gpt-5.6-sol", viewModel.SelectedLanguageModel);
    }

    [Fact]
    public async Task ProviderDefaultAbsenceSelectsDiscoveredFallbackAndRejectsInventedModelIds()
    {
        var handlers = new Queue<ProviderLifecycleHandler>(
        [
            new(blockOllamaChat: false),
            new(blockOllamaChat: false, openAiModels: ["gpt-5.6-sol-preview"])
        ]);
        var catalog = new RekallAgeLanguageModelProviderCatalog(
            new RekallAgeLanguageModelProviderSettings { OpenAiApiKey = "session-test-key" },
            () => new HttpClient(handlers.Dequeue(), disposeHandler: true));
        await using var viewModel = new RekallAgeStudioViewModel(
            new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
            catalog,
            new RecordingPreviewSession());

        viewModel.SelectedLanguageModelProvider = viewModel.LanguageModelProviders.Single(
            provider => provider.Id == "openai");
        await viewModel.WaitForLanguageModelProviderTransitionAsync();

        Assert.Equal(["gpt-5.6-sol-preview"], viewModel.LanguageModels);
        Assert.Equal("gpt-5.6-sol-preview", viewModel.SelectedLanguageModel);
        Assert.True(viewModel.HasUsableLanguageModel);
        Assert.Equal(
            "Configured default gpt-5.6-sol unavailable; using gpt-5.6-sol-preview.",
            viewModel.ProviderDisplayStatus);
        Assert.Contains(
            viewModel.ValidationLines,
            line => line.Contains("REKALL_LANGUAGE_MODEL_DEFAULT_UNAVAILABLE", StringComparison.Ordinal)
                && line.Contains("Requested: gpt-5.6-sol", StringComparison.Ordinal)
                && line.Contains("Resolved: gpt-5.6-sol-preview", StringComparison.Ordinal));

        viewModel.SelectedLanguageModel = "invented-model";

        Assert.Equal("gpt-5.6-sol-preview", viewModel.SelectedLanguageModel);
    }

    [Fact]
    public async Task OllamaFallbackPrefersAnInstalledModelOverACloudProxy()
    {
        var catalog = new RekallAgeLanguageModelProviderCatalog(
            httpClientFactory: () => new HttpClient(new ProviderLifecycleHandler(
                blockOllamaChat: false,
                ollamaModels: ["deepseek-v3.1:671b-cloud", "qwen3.5:35b"]), disposeHandler: true));
        await using var viewModel = new RekallAgeStudioViewModel(
            new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
            catalog,
            new RecordingPreviewSession());

        await ExecuteAsync(viewModel.RefreshLanguageModelsCommand);

        Assert.Equal(["deepseek-v3.1:671b-cloud", "qwen3.5:35b"], viewModel.LanguageModels);
        Assert.Equal("qwen3.5:35b", viewModel.SelectedLanguageModel);
        Assert.Contains("using qwen3.5:35b", viewModel.ProviderDisplayStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OllamaDiscoveryExcludesModelsWithoutToolsAndPrefersTheLargestLocalAuthoringModel()
    {
        var catalog = new RekallAgeLanguageModelProviderCatalog(
            httpClientFactory: () => new HttpClient(new ProviderLifecycleHandler(
                blockOllamaChat: false,
                ollamaModels: ["dolphin-llama3:latest", "llama3.2:latest", "qwen3.5:35b"]), disposeHandler: true));
        await using var viewModel = new RekallAgeStudioViewModel(
            new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
            catalog,
            new RecordingPreviewSession());

        await ExecuteAsync(viewModel.RefreshLanguageModelsCommand);

        Assert.Equal(["llama3.2:latest", "qwen3.5:35b"], viewModel.LanguageModels);
        Assert.Equal("qwen3.5:35b", viewModel.SelectedLanguageModel);
        Assert.Contains("using qwen3.5:35b", viewModel.ProviderDisplayStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SelectingADiscoveredModelClearsAStaleFallbackWarning()
    {
        var catalog = new RekallAgeLanguageModelProviderCatalog(
            httpClientFactory: () => new HttpClient(new ProviderLifecycleHandler(
                blockOllamaChat: false,
                ollamaModels: ["gemma4:latest", "qwen3.5:35b"]), disposeHandler: true));
        await using var viewModel = new RekallAgeStudioViewModel(
            new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
            catalog,
            new RecordingPreviewSession());

        await ExecuteAsync(viewModel.RefreshLanguageModelsCommand);
        Assert.Contains("Configured default", viewModel.ProviderDisplayStatus, StringComparison.Ordinal);

        viewModel.SelectedLanguageModel = "gemma4:latest";

        Assert.DoesNotContain("Configured default", viewModel.ProviderDisplayStatus, StringComparison.Ordinal);
        Assert.Equal("Using gemma4:latest with Local Ollama.", viewModel.ProviderDisplayStatus);
    }

    [Fact]
    public async Task InteractiveStudioUsesABoundedAuthoringTurnLimit()
    {
        await using var viewModel = new RekallAgeStudioViewModel();

        Assert.Equal(64, viewModel.AgentMaxTurns);
    }

    [Fact]
    public async Task RapidProviderSwitchDoesNotPublishStaleModelsWhenTheFinalProviderFails()
    {
        var initialOllama = new ProviderLifecycleHandler(blockOllamaChat: false);
        var staleOpenAi = new ProviderLifecycleHandler(blockOllamaChat: false, pauseModelResponse: true);
        var failingOllama = new ProviderLifecycleHandler(
            blockOllamaChat: false,
            pauseModelResponse: true,
            modelFailure: new RekallAgeLanguageModelProviderException(
                "REKALL_TEST_FINAL_PROVIDER_FAILED",
                "ollama",
                "The final provider failed model discovery."));
        var handlers = new Queue<ProviderLifecycleHandler>([initialOllama, staleOpenAi, failingOllama]);
        var catalog = new RekallAgeLanguageModelProviderCatalog(
            new RekallAgeLanguageModelProviderSettings { OpenAiApiKey = "session-test-key" },
            () => new HttpClient(handlers.Dequeue(), disposeHandler: true));
        await using var viewModel = new RekallAgeStudioViewModel(
            new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
            catalog,
            new RecordingPreviewSession());

        viewModel.SelectedLanguageModelProvider = viewModel.LanguageModelProviders.Single(
            provider => provider.Id == "openai");
        var staleTransition = viewModel.WaitForLanguageModelProviderTransitionAsync();
        await staleOpenAi.WaitForModelsAsync();
        viewModel.SelectedLanguageModelProvider = viewModel.LanguageModelProviders.Single(
            provider => provider.Id == "ollama");
        var finalTransition = viewModel.WaitForLanguageModelProviderTransitionAsync();
        staleOpenAi.ReleaseModels();
        await staleTransition;
        await failingOllama.WaitForModelsAsync();

        Assert.Empty(viewModel.LanguageModels);
        Assert.Empty(viewModel.SelectedLanguageModel);

        failingOllama.ReleaseModels();
        await finalTransition;

        Assert.Equal("ollama", viewModel.SelectedLanguageModelProvider.Id);
        Assert.Empty(viewModel.LanguageModels);
        Assert.Empty(viewModel.SelectedLanguageModel);
        Assert.Equal(
            "REKALL_TEST_FINAL_PROVIDER_FAILED: The final provider failed model discovery.",
            viewModel.ProviderStatus);
        Assert.Equal(["lease-disposed"], staleOpenAi.Events);
    }

    [Fact]
    public async Task ProviderSwitchAfterFaultedAgentRunReleasesOldLeaseAndLoadsNewProvider()
    {
        const string upstreamPayload = "opaque-switch-after-run-payload";
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-switch-after-run-" + Guid.NewGuid().ToString("N"));
        var ollama = new ProviderLifecycleHandler(
            blockOllamaChat: false,
            chatFailure: new InvalidDataException(upstreamPayload));
        var openAi = new ProviderLifecycleHandler(blockOllamaChat: false);
        var handlers = new Queue<ProviderLifecycleHandler>([ollama, openAi]);
        var catalog = new RekallAgeLanguageModelProviderCatalog(
            new RekallAgeLanguageModelProviderSettings { OpenAiApiKey = "session-test-key" },
            () => new HttpClient(handlers.Dequeue(), disposeHandler: true));
        var viewModel = new RekallAgeStudioViewModel(
            new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
            catalog,
            new RecordingPreviewSession())
        {
            ProjectPathInput = root,
            ProjectNameInput = "Switch After Failed Run",
            SceneNameInput = "Main",
            AgentTaskInput = "Inspect the project."
        };
        try
        {
            await ExecuteAsync(viewModel.CreateCommand);
            await ExecuteAsync(viewModel.RunAgentCommand);

            viewModel.SelectedLanguageModelProvider = viewModel.LanguageModelProviders.Single(
                provider => provider.Id == "openai");
            await viewModel.WaitForLanguageModelProviderTransitionAsync();

            Assert.Equal("openai", viewModel.SelectedLanguageModelProvider.Id);
            Assert.Equal(["gpt-5.6-sol", "gpt-5.6-sol-preview"], viewModel.LanguageModels);
            Assert.Equal("gpt-5.6-sol", viewModel.SelectedLanguageModel);
            Assert.Equal(["lease-disposed"], ollama.Events);
            var inspectable = string.Join('\n', [viewModel.ProviderStatus, viewModel.StatusText, .. viewModel.ValidationLines]);
            Assert.DoesNotContain(upstreamPayload, inspectable, StringComparison.Ordinal);
        }
        finally
        {
            await viewModel.DisposeAsync();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ProviderSwitchAfterFaultedModelRefreshReleasesOldLeaseAndLoadsNewProvider()
    {
        const string upstreamPayload = "opaque-switch-after-refresh-payload";
        var ollama = new ProviderLifecycleHandler(
            blockOllamaChat: false,
            modelFailure: new InvalidDataException(upstreamPayload));
        var openAi = new ProviderLifecycleHandler(blockOllamaChat: false);
        var handlers = new Queue<ProviderLifecycleHandler>([ollama, openAi]);
        var catalog = new RekallAgeLanguageModelProviderCatalog(
            new RekallAgeLanguageModelProviderSettings { OpenAiApiKey = "session-test-key" },
            () => new HttpClient(handlers.Dequeue(), disposeHandler: true));
        await using var viewModel = new RekallAgeStudioViewModel(
            new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
            catalog,
            new RecordingPreviewSession());

        await ExecuteAsync(viewModel.RefreshLanguageModelsCommand);
        viewModel.SelectedLanguageModelProvider = viewModel.LanguageModelProviders.Single(
            provider => provider.Id == "openai");
        await viewModel.WaitForLanguageModelProviderTransitionAsync();

        Assert.Equal("openai", viewModel.SelectedLanguageModelProvider.Id);
        Assert.Equal(["gpt-5.6-sol", "gpt-5.6-sol-preview"], viewModel.LanguageModels);
        Assert.Equal("gpt-5.6-sol", viewModel.SelectedLanguageModel);
        Assert.Equal(["lease-disposed"], ollama.Events);
        var inspectable = string.Join('\n', [viewModel.ProviderStatus, viewModel.StatusText, .. viewModel.ValidationLines]);
        Assert.DoesNotContain(upstreamPayload, inspectable, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShutdownCleansUpProviderAndSessionCredentialAfterFaultingAgentRun()
    {
        const string sessionCredential = "studio-shutdown-run-session-credential";
        const string upstreamPayload = "opaque-upstream-run-payload";
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-faulting-run-" + Guid.NewGuid().ToString("N"));
        var model = new BlockingFaultingModel(upstreamPayload);
        var preview = new RecordingPreviewSession();
        RekallAgeStudioViewModel? viewModel = null;
        try
        {
            viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                model,
                preview)
            {
                ProjectPathInput = root,
                ProjectNameInput = "Faulting Agent Shutdown",
                SceneNameInput = "Main",
                AgentTaskInput = "Inspect the project."
            };
            await viewModel.ApplyOpenAiApiKeyAsync(sessionCredential);
            await ExecuteAsync(viewModel.CreateCommand);
            var run = ExecuteAsync(viewModel.RunAgentCommand);
            await model.WaitForChatAsync();

            model.ReleaseChat();
            await run;
            await viewModel.DisposeAsync();

            Assert.True(preview.IsDisposed);
            Assert.False(viewModel.HasSessionOpenAiCredential);
            Assert.False(viewModel.RefreshLanguageModelsCommand.CanExecute(null));
            var inspectable = string.Join('\n', [viewModel.ProviderStatus, viewModel.StatusText, .. viewModel.ValidationLines]);
            Assert.Contains("REKALL_STUDIO_LANGUAGE_MODEL_SHUTDOWN_FAILED", inspectable, StringComparison.Ordinal);
            Assert.DoesNotContain(sessionCredential, inspectable, StringComparison.Ordinal);
            Assert.DoesNotContain(upstreamPayload, inspectable, StringComparison.Ordinal);
        }
        finally
        {
            if (viewModel is not null)
            {
                try { await viewModel.DisposeAsync(); }
                catch { }
            }
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ShutdownCleansUpProviderAndSessionCredentialAfterFaultingModelRefresh()
    {
        const string sessionCredential = "studio-shutdown-refresh-session-credential";
        const string upstreamPayload = "opaque-upstream-refresh-payload";
        var ollama = new ProviderLifecycleHandler(
            blockOllamaChat: false,
            pauseModelResponse: true,
            modelFailure: new InvalidDataException(upstreamPayload));
        var catalog = new RekallAgeLanguageModelProviderCatalog(
            new RekallAgeLanguageModelProviderSettings(),
            () => new HttpClient(ollama, disposeHandler: true));
        var preview = new RecordingPreviewSession();
        var viewModel = new RekallAgeStudioViewModel(
            new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
            catalog,
            preview);
        try
        {
            await viewModel.ApplyOpenAiApiKeyAsync(sessionCredential);
            var refresh = ExecuteAsync(viewModel.RefreshLanguageModelsCommand);
            await ollama.WaitForModelsAsync();
            ollama.ReleaseModels();
            await refresh;
            await viewModel.DisposeAsync();

            Assert.True(preview.IsDisposed);
            Assert.False(viewModel.HasSessionOpenAiCredential);
            Assert.False(viewModel.RefreshLanguageModelsCommand.CanExecute(null));
            Assert.Contains("lease-disposed", ollama.Events);
            var inspectable = string.Join('\n', [viewModel.ProviderStatus, viewModel.StatusText, .. viewModel.ValidationLines]);
            Assert.Contains("REKALL_STUDIO_LANGUAGE_MODEL_SHUTDOWN_FAILED", inspectable, StringComparison.Ordinal);
            Assert.DoesNotContain(sessionCredential, inspectable, StringComparison.Ordinal);
            Assert.DoesNotContain(upstreamPayload, inspectable, StringComparison.Ordinal);
        }
        finally
        {
            try { await viewModel.DisposeAsync(); }
            catch { }
        }
    }

    [Fact]
    public async Task ShutdownCleansUpWhenAgentCancellationCallbackThrows()
    {
        const string sessionCredential = "studio-shutdown-cancellation-session-credential";
        const string upstreamPayload = "opaque-cancellation-callback-payload";
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-throwing-cancellation-" + Guid.NewGuid().ToString("N"));
        var model = new BlockingFaultingModel(upstreamPayload);
        var preview = new RecordingPreviewSession();
        RekallAgeStudioViewModel? viewModel = null;
        try
        {
            viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                model,
                preview)
            {
                ProjectPathInput = root,
                ProjectNameInput = "Cancellation Cleanup",
                SceneNameInput = "Main",
                AgentTaskInput = "Inspect the project."
            };
            await viewModel.ApplyOpenAiApiKeyAsync(sessionCredential);
            await ExecuteAsync(viewModel.CreateCommand);
            var run = ExecuteAsync(viewModel.RunAgentCommand);
            await model.WaitForChatAsync();

            var agentCancellation = Assert.IsType<CancellationTokenSource>(
                typeof(RekallAgeStudioViewModel)
                    .GetField("_agentCancellation", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .GetValue(viewModel));
            using var throwingRegistration = agentCancellation.Token.Register(() =>
            {
                model.ReleaseChat();
                throw new InvalidDataException(upstreamPayload);
            });
            var shutdown = viewModel.DisposeAsync().AsTask();
            await Task.WhenAll(run, shutdown);

            Assert.True(preview.IsDisposed);
            Assert.False(viewModel.HasSessionOpenAiCredential);
            Assert.False(viewModel.RefreshLanguageModelsCommand.CanExecute(null));
            var inspectable = string.Join('\n', [viewModel.ProviderStatus, viewModel.StatusText, .. viewModel.ValidationLines]);
            Assert.Contains("REKALL_STUDIO_LANGUAGE_MODEL_SHUTDOWN_FAILED", inspectable, StringComparison.Ordinal);
            Assert.DoesNotContain(sessionCredential, inspectable, StringComparison.Ordinal);
            Assert.DoesNotContain(upstreamPayload, inspectable, StringComparison.Ordinal);
        }
        finally
        {
            if (viewModel is not null)
            {
                try { await viewModel.DisposeAsync(); }
                catch { }
            }
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PartialComparisonFailureKeepsUsableTypedEvidenceVisibleWithItsError()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-partial-comparison-" + Guid.NewGuid().ToString("N"));
        try
        {
            await CreateQualityProjectAsync(root);
            var registry = new RekallAgeCommandRegistry();
            registry.Register(new PartialQualityComparisonCommand());
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(registry),
                new EmptyModel(),
                new RecordingPreviewSession())
            {
                ProjectPathInput = root,
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.OpenCommand);

            await ExecuteAsync(viewModel.CompareQualityCommand);

            var comparison = Assert.Single(viewModel.RenderQualityComparisons);
            Assert.Equal("D:/captures/partial-high.png", comparison.ScreenshotPath);
            Assert.Equal("High", viewModel.ResolvedQualityPreset);
            Assert.Single(viewModel.RenderDebugViews);
            Assert.Contains(viewModel.ValidationLines, line =>
                line.Contains("REKALL_TEST_PARTIAL_COMPARISON", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DisposeCancelsAndAwaitsActiveQualityCaptureBeforePreviewDependencies()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-cancel-quality-" + Guid.NewGuid().ToString("N"));
        RekallAgeStudioViewModel? viewModel = null;
        Task? captureTask = null;
        Task? disposeTask = null;
        var command = new CancellationBlockingCaptureCommand();
        var preview = new RecordingPreviewSession();
        preview.BlockDispose();
        try
        {
            await CreateQualityProjectAsync(root);
            var registry = new RekallAgeCommandRegistry();
            registry.Register(command);
            viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(registry),
                new EmptyModel(),
                preview)
            {
                ProjectPathInput = root,
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.OpenCommand);

            captureTask = ExecuteAsync(viewModel.CaptureQualityCommand);
            await command.WaitForStartAsync();
            var cancellationObserved = command.WaitForCancellationAsync();
            var previewDisposeEntered = preview.WaitForDisposeAsync();
            disposeTask = viewModel.DisposeAsync().AsTask();

            var firstLifecycleSignal = await Task.WhenAny(cancellationObserved, previewDisposeEntered);

            Assert.Same(cancellationObserved, firstLifecycleSignal);
            Assert.False(previewDisposeEntered.IsCompleted);
        }
        finally
        {
            command.Release();
            preview.ReleaseDispose();
            if (captureTask is not null) await captureTask.WaitAsync(TimeSpan.FromSeconds(5));
            if (disposeTask is not null) await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));
            else if (viewModel is not null) await viewModel.DisposeAsync();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }

        Assert.DoesNotContain(viewModel!.ValidationLines, line =>
            line.Contains("REKALL_STUDIO_UNEXPECTED_FAILURE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AttachQualityProfileUsesTheSharedBuiltInComponentContract()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-attach-quality-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var viewModel = new RekallAgeStudioViewModel
            {
                ProjectPathInput = root,
                ProjectNameInput = "Attach Quality",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);
            await ExecuteAsync(viewModel.AddEntityCommand);

            Assert.True(viewModel.AttachQualityProfileCommand.CanExecute(null));
            await ExecuteAsync(viewModel.AttachQualityProfileCommand);

            Assert.True(viewModel.ApplyQualityCommand.CanExecute(null), viewModel.StatusText);
            Assert.Contains(viewModel.ComponentSchemas, schema => schema.Type == "Rekall.RenderQualityProfile");
            var scene = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
            var profile = Assert.Single(
                Assert.Single(scene.Entities).Components,
                component => component.Type == "Rekall.RenderQualityProfile");
            Assert.Equal("High", profile.Properties["preset"]!.GetValue<string>());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task QualityControlsPersistGenericComponentMutationsWithoutChangingGameplayState()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-quality-" + Guid.NewGuid().ToString("N"));
        try
        {
            await new RekallAgeProjectStore().SaveAsync(
                root,
                RekallAgeProjectManifest.Create("Quality Controls", ["world", "rendering3d"]),
                CancellationToken.None);
            var quality = RekallAgeEntityDocument.Create("Render Settings", ["rendering"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.RenderQualityProfile",
                    new JsonObject { ["preset"] = "High" }));
            var gameplay = RekallAgeEntityDocument.Create("Runtime State", ["gameplay"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Game.RuntimeState",
                    new JsonObject { ["score"] = 41, ["active"] = true }));
            await new RekallAgeSceneStore().SaveAsync(
                root,
                RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
                    .AddEntity(quality)
                    .AddEntity(gameplay),
                CancellationToken.None);

            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                new RecordingPreviewSession())
            {
                ProjectPathInput = root,
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.OpenCommand);
            Assert.Equal(["Performance", "Low", "Medium", "High", "Ultra", "Epic"], viewModel.QualityPresets);
            Assert.Equal("High", viewModel.RequestedQualityPreset);
            Assert.Equal("Unavailable", viewModel.ResolvedQualityPreset);
            Assert.Equal("Unavailable", viewModel.TotalGpuMillisecondsText);
            viewModel.SelectedQualityPreset = "Epic";
            viewModel.QualityResolutionScaleInput = "0.8";
            viewModel.QualityShadowCascadeCountInput = "4";
            viewModel.QualityShadowResolutionInput = "4096";
            viewModel.QualityFogModeInput = "froxel-high";
            viewModel.QualityBloomOverride = false;
            viewModel.QualitySsaoOverride = true;
            viewModel.QualityMaximumActiveParticlesInput = "128000";

            await ExecuteAsync(viewModel.ApplyQualityCommand);

            var scene = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
            var savedQuality = Assert.Single(
                scene.GetRequiredEntity(quality.Id).Components,
                component => component.Type == "Rekall.RenderQualityProfile");
            Assert.Equal("Epic", savedQuality.Properties["preset"]!.GetValue<string>());
            Assert.Equal(0.8, savedQuality.Properties["resolutionScale"]!.GetValue<double>());
            Assert.Equal(4, savedQuality.Properties["shadowCascadeCount"]!.GetValue<int>());
            Assert.Equal(4096, savedQuality.Properties["shadowResolution"]!.GetValue<int>());
            Assert.Equal("froxel-high", savedQuality.Properties["fogMode"]!.GetValue<string>());
            Assert.False(savedQuality.Properties["bloom"]!.GetValue<bool>());
            Assert.True(savedQuality.Properties["ssao"]!.GetValue<bool>());
            Assert.Equal(128000, savedQuality.Properties["maximumActiveParticles"]!.GetValue<int>());
            var savedGameplay = Assert.Single(
                scene.GetRequiredEntity(gameplay.Id).Components,
                component => component.Type == "Game.RuntimeState");
            Assert.Equal(41, savedGameplay.Properties["score"]!.GetValue<int>());
            Assert.True(savedGameplay.Properties["active"]!.GetValue<bool>());

            var transactions = await new RekallAgeTransactionLogStore().LoadAsync(root, CancellationToken.None);
            Assert.Contains(transactions.Transactions, transaction =>
                transaction.Actor == "studio" && transaction.Name.StartsWith("Set render quality", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PublishedAndPlacedStudioModelSurvivesWindowsPackagingAndPlayableAudit()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-model-package-" + Guid.NewGuid().ToString("N"));
        try
        {
            var gauntlet = await new RunAgentAuthoringGauntletCommand().ExecuteAsync(
                new RunAgentAuthoringGauntletRequest(
                    root,
                    "Packaged Model Test",
                    "Main",
                    Path.Combine(root, "Builds", "InitialPackage")),
                new Rekall.Age.Core.Commands.RekallAgeCommandContext(
                    "studio-model-package-test",
                    RekallAgeTransaction.Begin("create playable Studio model fixture"),
                    CancellationToken.None));
            Assert.True(gauntlet.Ok, gauntlet.Summary);

            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                new RecordingPreviewSession())
            {
                ProjectPathInput = root,
                ProjectNameInput = "Packaged Model Test",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.OpenCommand);
            viewModel.MeshPrimitiveAssetIdInput = "package-mesh";
            await ExecuteAsync(viewModel.CreateMeshPrimitiveCommand);
            viewModel.ModelAssetIdInput = "package-model";
            viewModel.ModelAssetDisplayNameInput = "Package Model";
            viewModel.ModelEntityNameInput = "Packaged Instance";
            viewModel.ModelPositionZ = 5;
            await ExecuteAsync(viewModel.PublishAndPlaceModelCommand);

            var model = await new RekallAgeModelAssetStore().LoadAsync(root, "package-model", CancellationToken.None);
            await ExecuteAsync(viewModel.PackageCommand);

            Assert.Equal(RekallAgePlayablePackageTargets.Windows, viewModel.SelectedPackageTarget);
            Assert.True(viewModel.LastPackageOutputDirectory is not null,
                viewModel.StatusText + Environment.NewLine + string.Join(Environment.NewLine, viewModel.ValidationLines));
            Assert.NotNull(viewModel.LastPackagePath);
            Assert.True(File.Exists(Path.Combine(viewModel.LastPackageOutputDirectory!, "Play.exe")));
            Assert.True(File.Exists(Path.Combine(viewModel.LastPackageOutputDirectory!, "Play.bat")));
            Assert.True(File.Exists(viewModel.LastPackagePath));

            using var archive = ZipFile.OpenRead(viewModel.LastPackagePath!);
            var entryPaths = archive.Entries.Select(entry => entry.FullName.Replace('\\', '/')).ToHashSet(StringComparer.Ordinal);
            Assert.Contains("Play.exe", entryPaths);
            Assert.Contains("Play.bat", entryPaths);
            Assert.Contains("Game/Scenes/Main.age.scene.json", entryPaths);
            Assert.Contains("Game/Assets/Models/package-model.age.model.json", entryPaths);
            Assert.Contains("Game/" + model.LastSuccessfulBuild!.CompiledMeshPath, entryPaths);
            using (var sceneReader = new StreamReader(archive.GetEntry("Game/Scenes/Main.age.scene.json")!.Open()))
            {
                var sceneJson = await sceneReader.ReadToEndAsync();
                Assert.Contains("package-model", sceneJson, StringComparison.Ordinal);
                Assert.Contains("Rekall.ModelAssetReference", sceneJson, StringComparison.Ordinal);
            }

            Assert.True(viewModel.AuditPackageCommand.CanExecute(null));
            await ExecuteAsync(viewModel.AuditPackageCommand);
            Assert.DoesNotContain(viewModel.ValidationLines, line => line.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PublishAndPlaceTurnsTheSelectedEditableMeshIntoASelectedSceneEntity()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-place-model-" + Guid.NewGuid().ToString("N"));
        try
        {
            var preview = new RecordingPreviewSession();
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                preview)
            {
                ProjectPathInput = root,
                ProjectNameInput = "Place Model Test",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);
            viewModel.SelectedMeshPrimitive = "box";
            viewModel.MeshPrimitiveAssetIdInput = "hero-box";
            await ExecuteAsync(viewModel.CreateMeshPrimitiveCommand);
            viewModel.ModelAssetIdInput = "hero-model";
            viewModel.ModelAssetDisplayNameInput = "Hero Model";
            viewModel.ModelEntityIdInput = "hero-instance";
            viewModel.ModelEntityNameInput = "Hero Instance";
            viewModel.ModelPositionX = 1.25;
            viewModel.ModelPositionY = -2.5;
            viewModel.ModelPositionZ = 3.75;
            viewModel.ModelRotationY = 45;
            viewModel.ModelScaleX = 0.5;
            viewModel.ModelScaleY = 2;
            viewModel.ModelScaleZ = 3;

            Assert.True(viewModel.PublishAndPlaceModelCommand.CanExecute(null));
            var previewResetsBeforePlacement = preview.ResetCount;
            await ExecuteAsync(viewModel.PublishAndPlaceModelCommand);

            var published = await new RekallAgeModelAssetStore().LoadAsync(root, "hero-model", CancellationToken.None);
            Assert.Equal("Hero Model", published.DisplayName);
            Assert.Equal(RekallAgeModelSourceKind.Mesh, published.Source.Kind);
            Assert.Equal("hero-box", published.Source.AssetId);
            Assert.NotNull(published.LastSuccessfulBuild);
            Assert.True(File.Exists(Path.Combine(root, published.LastSuccessfulBuild!.CompiledMeshPath)));

            var entity = Assert.Single((await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None)).Entities);
            Assert.Equal("hero-instance", entity.Id);
            Assert.Equal("Hero Instance", entity.Name);
            var reference = Assert.Single(entity.Components, component => component.Type == "Rekall.ModelAssetReference");
            Assert.Equal("hero-model", reference.Properties["assetId"]!.GetValue<string>());
            var transform = Assert.Single(entity.Components, component => component.Type == "Rekall.Transform3D");
            Assert.Equal(1.25, transform.Properties["x"]!.GetValue<double>());
            Assert.Equal(-2.5, transform.Properties["y"]!.GetValue<double>());
            Assert.Equal(3.75, transform.Properties["z"]!.GetValue<double>());
            Assert.Equal(45, transform.Properties["yaw"]!.GetValue<double>());
            Assert.Equal(0.5, transform.Properties["scaleX"]!.GetValue<double>());
            Assert.Equal(2, transform.Properties["scaleY"]!.GetValue<double>());
            Assert.Equal(3, transform.Properties["scaleZ"]!.GetValue<double>());
            Assert.Equal(entity.Id, viewModel.SelectedEntityId);
            Assert.Equal("hero-model", viewModel.LastPublishedModelAssetId);
            Assert.Equal(entity.Id, viewModel.LastPlacedModelEntityId);
            Assert.True(preview.ResetCount > previewResetsBeforePlacement);

            Assert.True(viewModel.CanEditSelectedLinkedModel);
            viewModel.SelectedMeshAssetId = null;
            Assert.True(await viewModel.OpenSelectedLinkedModelInModelingAsync());
            Assert.Equal("hero-box", viewModel.SelectedMeshAssetId);
            Assert.Equal("hero-model", viewModel.ModelAssetIdInput);
            Assert.Equal("Hero Model", viewModel.ModelAssetDisplayNameInput);
            Assert.Contains("8 points", viewModel.MeshSummary, StringComparison.Ordinal);

            viewModel.MeshEditDomain = RekallAgeGeometryDomain.Face;
            viewModel.SelectedMeshElementId = viewModel.MeshElementIds[0];
            await ExecuteAsync(viewModel.SelectMeshElementCommand);
            viewModel.SelectedMeshOperationId = "extrude_faces";
            await ExecuteAsync(viewModel.ApplyMeshOperationCommand);
            var previewResetsBeforeRebuild = preview.ResetCount;
            await ExecuteAsync(viewModel.PublishModelCommand);

            var rebuilt = await new RekallAgeModelAssetStore().LoadAsync(root, "hero-model", default);
            Assert.Equal(2, rebuilt.Revision);
            Assert.Equal(2, rebuilt.LastSuccessfulBuild!.SourceLogicalRevision);
            Assert.NotEqual(published.LastSuccessfulBuild!.CompiledContentHash, rebuilt.LastSuccessfulBuild.CompiledContentHash);
            Assert.False(File.Exists(new RekallAgeModelAssetStore().GetModelPath(root, "hero-box")));
            Assert.True(preview.ResetCount > previewResetsBeforeRebuild);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SeparatePublishRebuildAndPlaceActionsPreserveTheLiveLinkedModelWorkflow()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-model-actions-" + Guid.NewGuid().ToString("N"));
        try
        {
            var preview = new RecordingPreviewSession();
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                preview)
            {
                ProjectPathInput = root,
                ProjectNameInput = "Model Actions Test",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);
            viewModel.MeshPrimitiveAssetIdInput = "display-mesh";
            await ExecuteAsync(viewModel.CreateMeshPrimitiveCommand);

            Assert.True(viewModel.PublishModelCommand.CanExecute(null));
            Assert.False(viewModel.PlaceModelCommand.CanExecute(null));
            var previewResetsBeforePublish = preview.ResetCount;
            await ExecuteAsync(viewModel.PublishModelCommand);
            Assert.True(preview.ResetCount > previewResetsBeforePublish);

            var store = new RekallAgeModelAssetStore();
            var first = await store.LoadAsync(root, "display-mesh", CancellationToken.None);
            Assert.Equal(1, first.Revision);
            Assert.Empty((await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None)).Entities);
            Assert.True(viewModel.PlaceModelCommand.CanExecute(null));

            viewModel.ModelAssetDisplayNameInput = "Renamed Display Model";
            previewResetsBeforePublish = preview.ResetCount;
            await ExecuteAsync(viewModel.PublishModelCommand);
            Assert.True(preview.ResetCount > previewResetsBeforePublish);
            var rebuilt = await store.LoadAsync(root, "display-mesh", CancellationToken.None);
            Assert.Equal(2, rebuilt.Revision);
            Assert.Equal("Renamed Display Model", rebuilt.DisplayName);
            Assert.Equal("display-mesh", rebuilt.Source.AssetId);

            viewModel.ModelEntityNameInput = "Display Instance";
            await ExecuteAsync(viewModel.PlaceModelCommand);
            var entity = Assert.Single((await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None)).Entities);
            Assert.Equal("Display Instance", entity.Name);
            Assert.Equal(entity.Id, viewModel.SelectedEntityId);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task StaleModelPlacementSurfacesSuccessfulWarningsInStudioDiagnostics()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-stale-model-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                new RecordingPreviewSession())
            {
                ProjectPathInput = root,
                ProjectNameInput = "Stale Model Test",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);
            viewModel.MeshPrimitiveAssetIdInput = "stale-mesh";
            await ExecuteAsync(viewModel.CreateMeshPrimitiveCommand);
            await ExecuteAsync(viewModel.PublishModelCommand);

            var meshStore = new RekallAgeMeshAssetStore();
            var loaded = await meshStore.LoadVersionedAsync(root, "stale-mesh", CancellationToken.None);
            var replacement = await new RekallAgeMeshPrimitiveFactory().CreateAsync(
                "sphere",
                "stale-mesh",
                "Stale Mesh",
                CancellationToken.None);
            await meshStore.SaveIfRevisionAsync(
                root,
                replacement with { Revision = loaded.Value.Revision + 1 },
                loaded.Revision,
                CancellationToken.None);

            await ExecuteAsync(viewModel.PlaceModelCommand);

            Assert.Contains(
                viewModel.ValidationLines,
                line => line.Contains("warning: REKALL_MODEL_SOURCE_STALE", StringComparison.OrdinalIgnoreCase));
            Assert.Single((await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None)).Entities);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExistingModelAssetRejectsASelectedMeshSourceMismatchWithoutPlacingTheWrongGeometry()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-model-source-mismatch-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                new RecordingPreviewSession())
            {
                ProjectPathInput = root,
                ProjectNameInput = "Model Source Test",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);
            viewModel.MeshPrimitiveAssetIdInput = "first-mesh";
            await ExecuteAsync(viewModel.CreateMeshPrimitiveCommand);
            await ExecuteAsync(viewModel.PublishModelCommand);

            viewModel.MeshPrimitiveAssetIdInput = "second-mesh";
            await ExecuteAsync(viewModel.CreateMeshPrimitiveCommand);
            viewModel.ModelAssetIdInput = "first-mesh";
            viewModel.ModelAssetDisplayNameInput = "First Mesh";
            await ExecuteAsync(viewModel.PublishAndPlaceModelCommand);

            Assert.Empty((await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None)).Entities);
            Assert.Null(viewModel.LastPlacedModelEntityId);
            Assert.Contains(
                viewModel.ValidationLines,
                line => line.Contains("REKALL_STUDIO_MODEL_SOURCE_MISMATCH", StringComparison.Ordinal));
            var published = await new RekallAgeModelAssetStore().LoadAsync(root, "first-mesh", CancellationToken.None);
            Assert.Equal("first-mesh", published.Source.AssetId);
            Assert.Equal(1, published.Revision);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InvalidPlacementScaleSurfacesTheCanonicalDiagnosticWithoutMutatingTheScene()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-invalid-model-placement-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                new RecordingPreviewSession())
            {
                ProjectPathInput = root,
                ProjectNameInput = "Invalid Placement Test",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);
            viewModel.MeshPrimitiveAssetIdInput = "invalid-scale-mesh";
            await ExecuteAsync(viewModel.CreateMeshPrimitiveCommand);
            viewModel.ModelScaleX = 0;

            await ExecuteAsync(viewModel.PublishAndPlaceModelCommand);

            Assert.Empty((await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None)).Entities);
            Assert.Contains(
                viewModel.ValidationLines,
                line => line.Contains("REKALL_MODEL_PLACEMENT_TRANSFORM_INVALID", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InvalidModelAssetIdDisablesPlacementWithoutThrowingFromCommandEvaluation()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-invalid-model-id-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                new RecordingPreviewSession())
            {
                ProjectPathInput = root,
                ProjectNameInput = "Invalid Model ID Test",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);
            await ExecuteAsync(viewModel.CreateMeshPrimitiveCommand);
            viewModel.ModelAssetIdInput = "bad/id";

            var exception = Record.Exception(() => viewModel.PlaceModelCommand.CanExecute(null));

            Assert.Null(exception);
            Assert.False(viewModel.PlaceModelCommand.CanExecute(null));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ViewportPickSelectsTheMappedSceneEntityAndRejectsLetterboxSpace()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-pick-" + Guid.NewGuid().ToString("N"));
        try
        {
            var preview = new RecordingPreviewSession();
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                preview)
            {
                ProjectPathInput = root,
                ProjectNameInput = "Viewport Pick Test",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);
            await ExecuteAsync(viewModel.AddEntityCommand);
            var entity = Assert.Single(viewModel.EntityNodes);
            preview.Regions.Add(new(entity.EntityId, RekallAgeStudioViewportRegionKind.World, 390, 215, 20, 20, 2, 0));

            Assert.True(await viewModel.SelectViewportEntityAsync(200, 100, 100, 50));
            Assert.Equal(entity.EntityId, viewModel.SelectedEntityId);
            Assert.False(await viewModel.SelectViewportEntityAsync(200, 100, 5, 50));
            Assert.Equal(entity.EntityId, viewModel.SelectedEntityId);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SelectingEntityKeepsEquivalentHierarchyNodesStableForTreeViewSelection()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-tree-selection-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                new RecordingPreviewSession())
            {
                ProjectPathInput = root,
                ProjectNameInput = "Tree Selection Test",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);
            await ExecuteAsync(viewModel.AddEntityCommand);
            var entity = Assert.Single(viewModel.EntityNodes);

            await viewModel.SelectEntityAsync(entity);

            Assert.Equal(entity.EntityId, viewModel.SelectedEntityId);
            Assert.Same(entity, Assert.Single(viewModel.EntityNodes));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SceneGizmoDragPersistsAsOneUndoableTransformTransaction()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-gizmo-" + Guid.NewGuid().ToString("N"));
        try
        {
            var preview = new RecordingPreviewSession();
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                preview)
            {
                ProjectPathInput = root,
                ProjectNameInput = "Scene Gizmo Test",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);
            await ExecuteAsync(viewModel.AddEntityCommand);
            var entity = Assert.Single(viewModel.EntityNodes);
            viewModel.ComponentTypeInput = "Rekall.Transform3D";
            await ExecuteAsync(viewModel.AddComponentCommand);
            preview.Regions.Add(new(entity.EntityId, RekallAgeStudioViewportRegionKind.World, 510, 215, 20, 20, 2, 0));
            await viewModel.SelectEntityAsync(entity);
            var transactionsBefore = viewModel.TransactionLines.Count;

            Assert.True(viewModel.BeginSceneTransform(800, 450, 525, 225));
            Assert.True(viewModel.UpdateSceneTransform(800, 450, 550, 225));
            Assert.True(await viewModel.CompleteSceneTransformAsync());

            var scene = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
            var transform = Assert.Single(Assert.Single(scene.Entities).Components, component => component.Type == "Rekall.Transform3D");
            Assert.Equal(0.5, transform.Properties["x"]!.GetValue<double>(), 6);
            Assert.Equal(transactionsBefore + 1, viewModel.TransactionLines.Count);

            await ExecuteAsync(viewModel.UndoCommand);
            scene = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
            transform = Assert.Single(Assert.Single(scene.Entities).Components, component => component.Type == "Rekall.Transform3D");
            Assert.False(transform.Properties.ContainsKey("x"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RichSceneCommandsRenameDuplicateHideLockAndReparentThroughCanonicalTransactions()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-scene-tools-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                new RecordingPreviewSession())
            {
                ProjectPathInput = root,
                ProjectNameInput = "Scene Tools Test",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);
            await ExecuteAsync(viewModel.AddEntityCommand);
            var parent = Assert.Single(viewModel.EntityNodes);
            await ExecuteAsync(viewModel.AddEntityCommand);
            var child = viewModel.EntityNodes.Single(entity => entity.EntityId != parent.EntityId);
            await viewModel.SelectEntityAsync(child);

            viewModel.EntityNameInput = "Playable Hero";
            await ExecuteAsync(viewModel.RenameEntityCommand);
            viewModel.ParentEntityIdInput = parent.EntityId;
            await ExecuteAsync(viewModel.ReparentEntityCommand);
            await ExecuteAsync(viewModel.ToggleEntityVisibleCommand);
            await ExecuteAsync(viewModel.ToggleEntityLockedCommand);
            await ExecuteAsync(viewModel.DuplicateEntityCommand);

            var scene = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
            var updated = scene.GetRequiredEntity(child.EntityId);
            Assert.Equal("Playable Hero", updated.Name);
            Assert.Equal(parent.EntityId, updated.ParentId);
            Assert.False(updated.Visible);
            Assert.True(updated.Locked);
            Assert.Contains(scene.Entities, entity => entity.Id != child.EntityId && entity.Name == "Playable Hero Copy");

            await ExecuteAsync(viewModel.UndoCommand);
            scene = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
            Assert.DoesNotContain(scene.Entities, entity => entity.Id != child.EntityId && entity.Name == "Playable Hero Copy");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ModelingAddMenuCreatesAndOpensACanonicalEditablePrimitiveAsset()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-add-mesh-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                new RecordingPreviewSession())
            {
                ProjectPathInput = root,
                ProjectNameInput = "Add Mesh Test",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);
            viewModel.SelectedMeshPrimitive = "box";
            viewModel.MeshPrimitiveAssetIdInput = "hero-box";

            await ExecuteAsync(viewModel.CreateMeshPrimitiveCommand);

            Assert.Equal("hero-box", viewModel.SelectedMeshAssetId);
            Assert.Contains("hero-box", viewModel.MeshAssetIds);
            Assert.NotNull(viewModel.MeshViewportImage);
            Assert.Equal("hero-box", viewModel.ModelAssetIdInput);
            Assert.Equal("Hero Box", viewModel.ModelAssetDisplayNameInput);
            Assert.Equal("Hero Box", viewModel.ModelEntityNameInput);
            Assert.True(viewModel.PublishAndPlaceModelCommand.CanExecute(null));
            var mesh = await new RekallAgeMeshAssetStore().LoadAsync(root, "hero-box", CancellationToken.None);
            Assert.Equal("hero-box", mesh.AssetId);
            Assert.True(new RekallAgeMeshValidator().Validate(mesh).IsValid);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task OpeningAnEntitysAnimationMixerAddingALayerThroughTheUiAndApplyingPersistsToTheScene()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-animation-mixer-" + Guid.NewGuid().ToString("N"));
        try
        {
            var preview = new RecordingPreviewSession();
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                preview)
            {
                ProjectPathInput = root,
                ProjectNameInput = "Animation Mixer Test",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);
            await ExecuteAsync(viewModel.AddEntityCommand);
            var entity = Assert.Single(viewModel.EntityNodes);
            viewModel.ComponentTypeInput = "Rekall.AnimationMixer";
            await ExecuteAsync(viewModel.AddComponentCommand);
            await viewModel.SelectEntityAsync(entity);

            await ExecuteAsync(viewModel.OpenAnimationMixerCommand);
            Assert.True(viewModel.AnimationMixerIsOpen);
            Assert.Empty(viewModel.AnimationMixerLayers);

            await ExecuteAsync(viewModel.AddAnimationMixerLayerCommand);
            var layer = Assert.Single(viewModel.AnimationMixerLayers);
            layer.Name = "idle";
            layer.Clip = "hero-idle";
            layer.Weight = "1";
            await ExecuteAsync(viewModel.ApplyAnimationMixerLayersCommand);

            var persisted = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
            var mixer = persisted.Entities.Single(item => item.Id == entity.EntityId)
                .Components.Single(component => component.Type == "Rekall.AnimationMixer");
            var persistedLayer = Assert.Single(((JsonArray)mixer.Properties["layers"]!).OfType<JsonObject>());
            Assert.Equal("idle", persistedLayer["name"]!.GetValue<string>());
            Assert.Equal("hero-idle", persistedLayer["clip"]!.GetValue<string>());
            Assert.Equal(1, persistedLayer["weight"]!.GetValue<double>(), precision: 6);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task OpeningAMaterialGraphExposesNodesAndAppliesAColorParameterEditThroughTheRealPatchPipeline()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-material-graph-" + Guid.NewGuid().ToString("N"));
        try
        {
            var nodes = new[]
            {
                new RekallAgeMaterialGraphNode("emissive", "rekall.material.surface.emissive", 1, new JsonObject()),
                new RekallAgeMaterialGraphNode("output", "rekall.material.output", 1, new JsonObject())
            };
            var graph = RekallAgeMaterialGraphAsset.Create(
                "glow-material",
                "Glow Material",
                nodes,
                [new RekallAgeMaterialGraphLink("emissive-output", "emissive", "surface", "output", "surface")],
                new RekallAgeMaterialGraphOutput("surface", "output", "surface"));

            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                new RecordingPreviewSession())
            {
                ProjectPathInput = root,
                ProjectNameInput = "Material Graph Test",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);
            await new RekallAgeMaterialGraphAssetStore().SaveIfRevisionAsync(
                root, graph, Rekall.Age.Core.Persistence.RekallAgeDocumentRevision.Missing, CancellationToken.None);
            viewModel.SelectedMaterialGraphAssetId = "glow-material";
            await ExecuteAsync(viewModel.OpenMaterialGraphCommand);

            Assert.Equal(2, viewModel.MaterialGraphNodes.Count);
            viewModel.SelectedMaterialGraphNode = viewModel.MaterialGraphNodes.Single(node => node.NodeId == "emissive");
            var colorEditor = Assert.Single(viewModel.MaterialGraphParameterEditors, editor => editor.ParameterId == "color");
            Assert.Equal("#ffffff", colorEditor.ValueText);
            colorEditor.ValueText = "#ff8800";
            Assert.True(colorEditor.IsValid);
            Assert.True(colorEditor.IsModified);

            await ExecuteAsync(viewModel.ApplyMaterialGraphParametersCommand);

            var persisted = await new RekallAgeMaterialGraphAssetStore().LoadAsync(root, "glow-material", CancellationToken.None);
            var emissiveNode = Assert.Single(persisted.Nodes, node => node.NodeId == "emissive");
            Assert.Equal("#ff8800", emissiveNode.Parameters["color"]!.GetValue<string>());
            Assert.Equal(2, persisted.Revision);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task OpeningAMeshExposesItsNamedAttributesAndMaterialSlots()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-uv-attributes-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                new RecordingPreviewSession())
            {
                ProjectPathInput = root,
                ProjectNameInput = "UV Attributes Test",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);
            viewModel.SelectedMeshPrimitive = "box";
            viewModel.MeshPrimitiveAssetIdInput = "hero-box";
            await ExecuteAsync(viewModel.CreateMeshPrimitiveCommand);

            // A freshly authored primitive genuinely has no named attributes or material slots yet
            // - the inspector must reflect that honestly rather than showing placeholder rows.
            Assert.Empty(viewModel.MeshAttributeSummaries);
            Assert.Empty(viewModel.MeshMaterialSlotSummaries);

            viewModel.MeshEditDomain = RekallAgeGeometryDomain.Face;
            viewModel.SelectedMeshElementId = viewModel.MeshElementIds[0];
            await ExecuteAsync(viewModel.SelectMeshElementCommand);
            viewModel.SelectedMeshOperationId = "generate_normals";
            await ExecuteAsync(viewModel.ApplyMeshOperationCommand);

            Assert.Contains(viewModel.MeshAttributeSummaries, line => line.Contains("normal.generated", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ModalDragAppliesTheOperationThroughTheRealPreviewApplyPipeline()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-modal-drag-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                new RecordingPreviewSession())
            {
                ProjectPathInput = root,
                ProjectNameInput = "Modal Drag Test",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);
            viewModel.SelectedMeshPrimitive = "box";
            viewModel.MeshPrimitiveAssetIdInput = "hero-box";
            await ExecuteAsync(viewModel.CreateMeshPrimitiveCommand);
            var beforeRevision = (await new RekallAgeMeshAssetStore().LoadAsync(root, "hero-box", CancellationToken.None)).Revision;

            viewModel.MeshEditDomain = RekallAgeGeometryDomain.Face;
            viewModel.SelectedMeshElementId = viewModel.MeshElementIds[0];
            await ExecuteAsync(viewModel.SelectMeshElementCommand);
            viewModel.SelectedMeshOperationId = "extrude_faces";

            Assert.True(viewModel.BeginModalMeshOperationDrag(0.5));
            await viewModel.UpdateModalMeshOperationDragAsync(0.65);
            Assert.True(viewModel.MeshSummary.Contains("PREVIEW", StringComparison.Ordinal));
            await viewModel.CompleteModalMeshOperationDragAsync(0.65);

            var afterRevision = (await new RekallAgeMeshAssetStore().LoadAsync(root, "hero-box", CancellationToken.None)).Revision;
            Assert.True(afterRevision > beforeRevision);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task TwoPortClicksOnTheNodeGraphCanvasCreateALinkThroughTheRealPatchPipeline()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-graph-canvas-" + Guid.NewGuid().ToString("N"));
        try
        {
            // "join" accepts multiple linked geometry inputs, so box1->join->output is already a
            // complete, valid, savable graph and box2 can sit unlinked (a box has no required
            // input of its own) without failing strict save/patch validation. That lets the test
            // add a *second* real link (box2 -> join) rather than needing an incomplete graph.
            var nodes = new[]
            {
                new RekallAgeModelingGraphNode("box1", "rekall.modeling.primitive.box", 1, new JsonObject { ["sizeX"] = 2.0 }),
                new RekallAgeModelingGraphNode("box2", "rekall.modeling.primitive.box", 1, new JsonObject { ["sizeX"] = 1.0 }),
                new RekallAgeModelingGraphNode("join", "rekall.modeling.join", 1, new JsonObject()),
                new RekallAgeModelingGraphNode("output", "rekall.modeling.output.mesh", 1, new JsonObject())
            };
            var links = new[]
            {
                new RekallAgeModelingGraphLink("box1-join", "box1", "geometry", "join", "geometry"),
                new RekallAgeModelingGraphLink("join-output", "join", "geometry", "output", "input")
            };
            var graph = RekallAgeModelingGraphAsset.Create(
                "link-test-graph", "Link Test Graph", nodes, links, [new("mesh", "output", "geometry")]);

            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                new RecordingPreviewSession())
            {
                ProjectPathInput = root,
                ProjectNameInput = "Graph Canvas Link Test",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);
            await new RekallAgeModelingGraphAssetStore().SaveAsync(root, graph, CancellationToken.None);
            viewModel.SelectedModelingGraphAssetId = "link-test-graph";
            await ExecuteAsync(viewModel.OpenModelingGraphCommand);

            // Use the frame Studio actually rendered. The opening view is fit-to-canvas and may
            // differ from a separately reconstructed baseline as layout behavior evolves.
            var frameField = typeof(RekallAgeStudioViewModel).GetField(
                "_modelingGraphCanvasFrame",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var frame = Assert.IsType<RekallAgeStudioModelingGraphCanvasFrame>(frameField.GetValue(viewModel));
            var box2Output = frame.PortPoints[new RekallAgeStudioModelingGraphPortKey("box2", "geometry", true)];
            var joinInput = frame.PortPoints[new RekallAgeStudioModelingGraphPortKey("join", "geometry", false)];

            await viewModel.ClickModelingGraphCanvasAsync(box2Output.X / 640, box2Output.Y / 360);
            var pendingField = typeof(RekallAgeStudioViewModel).GetField(
                "_modelingGraphPendingLinkPort",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.Equal(
                new RekallAgeStudioModelingGraphPortKey("box2", "geometry", true),
                Assert.IsType<RekallAgeStudioModelingGraphPortKey>(pendingField.GetValue(viewModel)));
            await viewModel.ClickModelingGraphCanvasAsync(joinInput.X / 640, joinInput.Y / 360);
            Assert.Null(pendingField.GetValue(viewModel));
            Assert.DoesNotContain(viewModel.ModelingGraphDiagnosticLines, line => line.StartsWith("error:", StringComparison.Ordinal));

            var persisted = await new RekallAgeModelingGraphAssetStore().LoadAsync(root, "link-test-graph", CancellationToken.None);
            Assert.Contains(persisted.Links, link =>
                link.FromNodeId == "box2" && link.FromPortId == "geometry"
                && link.ToNodeId == "join" && link.ToPortId == "geometry");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task OrbitingMeshViewportChangesRenderedImageWithoutMutatingMeshData()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-orbit-camera-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                new RecordingPreviewSession())
            {
                ProjectPathInput = root,
                ProjectNameInput = "Orbit Camera Test",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);
            viewModel.SelectedMeshPrimitive = "box";
            viewModel.MeshPrimitiveAssetIdInput = "hero-box";
            await ExecuteAsync(viewModel.CreateMeshPrimitiveCommand);
            var before = viewModel.MeshViewportImage;
            var mesh = await new RekallAgeMeshAssetStore().LoadAsync(root, "hero-box", CancellationToken.None);

            viewModel.OrbitMeshViewport(0.4, 0.1);

            Assert.NotSame(before, viewModel.MeshViewportImage);
            var meshAfterOrbit = await new RekallAgeMeshAssetStore().LoadAsync(root, "hero-box", CancellationToken.None);
            Assert.Equal(mesh.Revision, meshAfterOrbit.Revision);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task OpeningADifferentMeshAssetResetsCameraButReopeningSameMeshStartsFreshToo()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-orbit-reset-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                new RecordingPreviewSession())
            {
                ProjectPathInput = root,
                ProjectNameInput = "Orbit Reset Test",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);
            viewModel.SelectedMeshPrimitive = "box";
            viewModel.MeshPrimitiveAssetIdInput = "box-a";
            await ExecuteAsync(viewModel.CreateMeshPrimitiveCommand);
            viewModel.MeshPrimitiveAssetIdInput = "box-b";
            await ExecuteAsync(viewModel.CreateMeshPrimitiveCommand);
            // box-b is now open at the identity camera (CreateMeshPrimitiveAsync opens what it creates).
            var identityImageForBoxB = viewModel.MeshViewportImage;

            viewModel.SelectedMeshAssetId = "box-a";
            await ExecuteAsync(viewModel.OpenMeshAssetCommand);
            viewModel.OrbitMeshViewport(0.6, 0.2);
            var orbitedImageForBoxA = viewModel.MeshViewportImage;
            Assert.NotSame(identityImageForBoxB, orbitedImageForBoxA);

            viewModel.SelectedMeshAssetId = "box-b";
            await ExecuteAsync(viewModel.OpenMeshAssetCommand);

            // Re-opening box-b must start at the identity camera again, not inherit box-a's orbit.
            var reopenedBoxBBytes = ToBytes(viewModel.MeshViewportImage!);
            var identityBoxBBytes = ToBytes(identityImageForBoxB!);
            Assert.Equal(identityBoxBBytes, reopenedBoxBBytes);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }

        static byte[] ToBytes(System.Windows.Media.Imaging.BitmapSource image)
        {
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
    }

    [Fact]
    public async Task ViewModelExposesDistinctEditAndPersistentSimulateModes()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-mode-" + Guid.NewGuid().ToString("N"));
        try
        {
            var clock = new ManualMonotonicClock();
            var preview = new RecordingPreviewSession();
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                preview,
                clock);
            viewModel.ProjectPathInput = root;
            viewModel.ProjectNameInput = "Mode Test";
            viewModel.SceneNameInput = "Main";
            await ExecuteAsync(viewModel.CreateCommand);

            Assert.Equal(RekallAgeStudioMode.Edit, viewModel.Mode);
            Assert.True(viewModel.SimulateCommand.CanExecute(null));

            await ExecuteAsync(viewModel.SimulateCommand);
            clock.Advance(RekallAgeStudioPreviewCadence.PresentationInterval);
            await viewModel.AdvanceLivePreviewAsync();

            Assert.Equal(RekallAgeStudioMode.Simulate, viewModel.Mode);
            Assert.True(viewModel.IsSimulating);
            Assert.False(viewModel.PlayCommand.CanExecute(null));
            Assert.Equal(1, viewModel.PreviewFrameIndex);
            Assert.Equal(2, preview.ResetCount);
            Assert.Equal(1, preview.StepCount);
            Assert.All(preview.ResetSizes, size => Assert.Equal((800, 450), size));
            Assert.True(viewModel.ViewportAvailable);
            Assert.Contains("Vulkan", viewModel.ViewportBackendLabel, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("hardware", viewModel.ViewportBackendLabel, StringComparison.OrdinalIgnoreCase);

            await ExecuteAsync(viewModel.StopCommand);

            Assert.Equal(RekallAgeStudioMode.Edit, viewModel.Mode);
            Assert.False(viewModel.IsSimulating);
            Assert.Equal(3, preview.ResetCount);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PanningAndZoomingMeshViewportEachChangeTheRenderedCameraView()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-pan-zoom-camera-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                new RecordingPreviewSession())
            {
                ProjectPathInput = root,
                ProjectNameInput = "Pan Zoom Camera Test",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);
            viewModel.SelectedMeshPrimitive = "box";
            viewModel.MeshPrimitiveAssetIdInput = "camera-box";
            await ExecuteAsync(viewModel.CreateMeshPrimitiveCommand);
            var initial = ToBytes(viewModel.MeshViewportImage!);

            viewModel.PanMeshViewport(80, -35);
            var panned = ToBytes(viewModel.MeshViewportImage!);
            viewModel.ZoomMeshViewport(1.7);
            var zoomed = ToBytes(viewModel.MeshViewportImage!);

            Assert.False(initial.SequenceEqual(panned));
            Assert.False(panned.SequenceEqual(zoomed));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }

        static byte[] ToBytes(System.Windows.Media.Imaging.BitmapSource image)
        {
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
    }

    [Fact]
    public async Task EmptyMeshViewportStillRendersACameraAwareGrid()
    {
        await using var viewModel = new RekallAgeStudioViewModel();
        var initial = ToBytes(Assert.IsAssignableFrom<System.Windows.Media.Imaging.BitmapSource>(viewModel.MeshViewportImage));

        viewModel.OrbitMeshViewport(0.45, 0.2);
        var orbited = ToBytes(viewModel.MeshViewportImage!);
        viewModel.PanMeshViewport(70, -25);
        var panned = ToBytes(viewModel.MeshViewportImage!);
        viewModel.ZoomMeshViewport(1.5);
        var zoomed = ToBytes(viewModel.MeshViewportImage!);

        Assert.False(initial.SequenceEqual(orbited));
        Assert.False(orbited.SequenceEqual(panned));
        Assert.False(panned.SequenceEqual(zoomed));

        static byte[] ToBytes(System.Windows.Media.Imaging.BitmapSource image)
        {
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
    }

    [Fact]
    public async Task SimulationIsBlockedWhenPreviewOmittedUnverifiedProjectModules()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-module-block-" + Guid.NewGuid().ToString("N"));
        try
        {
            var preview = new RecordingPreviewSession
            {
                ProjectModuleDiagnostic = new RekallAgeStudioProjectModuleDiagnostic(
                    "REKALL_MODULE_TRUST_REPARSE_POINT",
                    "Module output traverses an untrusted reparse point.")
            };
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                preview);
            viewModel.ProjectPathInput = root;
            viewModel.ProjectNameInput = "Module Block Test";
            viewModel.SceneNameInput = "Main";
            await ExecuteAsync(viewModel.CreateCommand);

            await ExecuteAsync(viewModel.SimulateCommand);

            Assert.Equal(RekallAgeStudioMode.Edit, viewModel.Mode);
            Assert.False(viewModel.IsSimulating);
            Assert.Equal(0, preview.StepCount);
            Assert.Contains("REKALL_MODULE_TRUST_REPARSE_POINT", viewModel.StatusText, StringComparison.Ordinal);
            Assert.Contains(
                viewModel.ValidationLines,
                line => line.StartsWith("blocking: REKALL_MODULE_TRUST_REPARSE_POINT", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SimulateRecoversAResizeRaceWithinTheSameModeTransition()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-sim-resize-" + Guid.NewGuid().ToString("N"));
        try
        {
            var preview = new RecordingPreviewSession();
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                preview)
            {
                ProjectPathInput = root,
                ProjectNameInput = "Sim Resize Test",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);
            preview.ReturnResizeMismatchOnce = true;

            await ExecuteAsync(viewModel.SimulateCommand);

            Assert.Equal(RekallAgeStudioMode.Simulate, viewModel.Mode);
            Assert.True(viewModel.ViewportAvailable);
            Assert.Equal(3, preview.ResetCount);
            Assert.Equal(0, preview.PresentCurrentCount);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task UnavailableVulkanPreviewSurfacesStructuredFailureWithoutChangingEditorMode()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-vulkan-unavailable-" + Guid.NewGuid().ToString("N"));
        try
        {
            var preview = new RecordingPreviewSession { ReturnUnavailable = true };
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                preview)
            {
                ProjectPathInput = root,
                ProjectNameInput = "Unavailable Test",
                SceneNameInput = "Main"
            };

            await ExecuteAsync(viewModel.CreateCommand);

            Assert.Equal(RekallAgeStudioMode.Edit, viewModel.Mode);
            Assert.False(viewModel.ViewportAvailable);
            Assert.Contains("Vulkan is unavailable", viewModel.ViewportUnavailableReason, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(
                viewModel.ValidationLines,
                line => line.Contains("REKALL_STUDIO_VULKAN_UNAVAILABLE", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MainWindowRetryPathRecoversAnUnavailableViewModelWithoutAWorkspaceTransition()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-vulkan-recovery-" + Guid.NewGuid().ToString("N"));
        try
        {
            var preview = new RecordingPreviewSession { ReturnUnavailable = true };
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                preview)
            {
                ProjectPathInput = root,
                ProjectNameInput = "Recovery Test",
                SceneNameInput = "Main"
            };
            var recovery = new RekallAgeStudioViewportRecoveryState(TimeSpan.FromSeconds(1));
            var now = new DateTimeOffset(2026, 8, 29, 10, 0, 0, TimeSpan.Zero);
            await ExecuteAsync(viewModel.CreateCommand);
            var unavailable = recovery.Synchronize(viewModel.HasProject, viewModel.ViewportAvailable, now);

            preview.ReturnUnavailable = false;
            Assert.True(recovery.TryBeginAutomaticRetry(now));
            await viewModel.PresentViewportAtHostSizeAsync(preview.Metrics);
            var recovered = recovery.Synchronize(
                viewModel.HasProject,
                viewModel.ViewportAvailable,
                now + TimeSpan.FromSeconds(1));

            Assert.False(unavailable.PresentationSurfaceVisible);
            Assert.True(unavailable.PlaceholderVisible);
            Assert.True(viewModel.ViewportAvailable);
            Assert.DoesNotContain("Vulkan is unavailable", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
            Assert.True(recovered.PresentationSurfaceVisible);
            Assert.False(recovered.PlaceholderVisible);
            Assert.Equal(1, preview.PresentCurrentCount);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task TransientZeroHostMetricsDoNotReplaceAValidVulkanStatusWithAnUnavailableError()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-vulkan-transient-size-" + Guid.NewGuid().ToString("N"));
        try
        {
            var preview = new RecordingPreviewSession();
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                preview)
            {
                ProjectPathInput = root,
                ProjectNameInput = "Transient Size Test",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);
            Assert.True(viewModel.ViewportAvailable);

            preview.Metrics = default;
            await ExecuteAsync(viewModel.SimulateCommand);

            Assert.DoesNotContain("Vulkan is unavailable", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
            Assert.True(viewModel.ViewportAvailable);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MainWindowEditTickRefreshesOnePendingExternalDependencyChangeOnlyOnce()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-edit-dependency-tick-" + Guid.NewGuid().ToString("N"));
        try
        {
            var preview = new RecordingPreviewSession();
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                preview)
            {
                ProjectPathInput = root,
                ProjectNameInput = "Edit Dependency Tick Test",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);

            await viewModel.RefreshEditViewportDependenciesAsync(preview.Metrics);
            await viewModel.RefreshEditViewportDependenciesAsync(preview.Metrics);
            preview.QueueExternalDependencyChange();
            await viewModel.RefreshEditViewportDependenciesAsync(preview.Metrics);
            await viewModel.RefreshEditViewportDependenciesAsync(preview.Metrics);

            Assert.Equal(RekallAgeStudioMode.Edit, viewModel.Mode);
            Assert.Equal(4, preview.ExternalDependencyPollCount);
            Assert.Equal(1, preview.ExternalDependencyPresentationCount);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PausedSimulationSuppressesAutomaticTicksAndSingleStepAdvancesExactlyOneFrame()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-step-" + Guid.NewGuid().ToString("N"));
        try
        {
            var clock = new ManualMonotonicClock();
            var preview = new RecordingPreviewSession();
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                preview,
                clock)
            {
                ProjectPathInput = root,
                ProjectNameInput = "Pause Step Test",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);
            await ExecuteAsync(viewModel.SimulateCommand);

            await ExecuteAsync(viewModel.PauseSimulationCommand);
            Assert.True(viewModel.IsSimulationPaused);
            await viewModel.AdvanceLivePreviewAsync();
            Assert.Equal(0, viewModel.PreviewFrameIndex);
            Assert.Equal(0, preview.StepCount);

            await ExecuteAsync(viewModel.StepSimulationCommand);
            Assert.Equal(1, viewModel.PreviewFrameIndex);
            Assert.Equal(1, preview.StepCount);

            await ExecuteAsync(viewModel.PauseSimulationCommand);
            Assert.False(viewModel.IsSimulationPaused);
            clock.Advance(RekallAgeStudioPreviewCadence.PresentationInterval);
            await viewModel.AdvanceLivePreviewAsync();
            Assert.Equal(2, viewModel.PreviewFrameIndex);
            Assert.Equal(2, preview.StepCount);

            await ExecuteAsync(viewModel.StopCommand);
            Assert.False(viewModel.IsSimulationPaused);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SimulationCadenceCatchesUpAtMostSixFramesAndPresentsOnlyNewestState()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-cadence-catchup-" + Guid.NewGuid().ToString("N"));
        try
        {
            var clock = new ManualMonotonicClock();
            var preview = new RecordingPreviewSession();
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                preview,
                clock)
            {
                ProjectPathInput = root,
                ProjectNameInput = "Cadence Catchup Test",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);
            await ExecuteAsync(viewModel.SimulateCommand);

            clock.Advance(TimeSpan.FromMilliseconds(100));
            await viewModel.AdvanceLivePreviewAsync();

            Assert.Equal(6, viewModel.PreviewFrameIndex);
            Assert.Equal(1, preview.StepCount);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PausingAndResumingSimulationDiscardElapsedTimeWhileManualStepAdvancesOnce()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-cadence-pause-" + Guid.NewGuid().ToString("N"));
        try
        {
            var clock = new ManualMonotonicClock();
            var preview = new RecordingPreviewSession();
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                preview,
                clock)
            {
                ProjectPathInput = root,
                ProjectNameInput = "Cadence Pause Test",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);
            await ExecuteAsync(viewModel.SimulateCommand);
            await ExecuteAsync(viewModel.PauseSimulationCommand);

            clock.Advance(TimeSpan.FromSeconds(10));
            await viewModel.AdvanceLivePreviewAsync();
            await ExecuteAsync(viewModel.StepSimulationCommand);

            Assert.True(viewModel.IsSimulationPaused);
            Assert.Equal(1, viewModel.PreviewFrameIndex);

            await ExecuteAsync(viewModel.PauseSimulationCommand);
            await viewModel.AdvanceLivePreviewAsync();
            Assert.Equal(1, viewModel.PreviewFrameIndex);

            clock.Advance(RekallAgeStudioPreviewCadence.PresentationInterval);
            await viewModel.AdvanceLivePreviewAsync();
            Assert.Equal(2, viewModel.PreviewFrameIndex);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task UnavailableAndRecoveredVulkanPreviewStartsSimulationCadenceFresh()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-cadence-recovery-" + Guid.NewGuid().ToString("N"));
        try
        {
            var clock = new ManualMonotonicClock();
            var preview = new RecordingPreviewSession();
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                preview,
                clock)
            {
                ProjectPathInput = root,
                ProjectNameInput = "Cadence Recovery Test",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);
            await ExecuteAsync(viewModel.SimulateCommand);

            preview.ReturnUnavailable = true;
            clock.Advance(TimeSpan.FromSeconds(10));
            await viewModel.PresentViewportAtHostSizeAsync(preview.Metrics);
            await viewModel.AdvanceLivePreviewAsync();

            Assert.False(viewModel.ViewportAvailable);
            Assert.Equal(0, viewModel.PreviewFrameIndex);
            Assert.Equal(0, preview.StepCount);

            preview.ReturnUnavailable = false;
            await viewModel.PresentViewportAtHostSizeAsync(preview.Metrics);
            await viewModel.AdvanceLivePreviewAsync();
            Assert.Equal(0, viewModel.PreviewFrameIndex);

            clock.Advance(RekallAgeStudioPreviewCadence.PresentationInterval);
            await viewModel.AdvanceLivePreviewAsync();
            Assert.Equal(1, viewModel.PreviewFrameIndex);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task StopAndNextSimulationResetDiscardPriorCadenceState()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-cadence-stop-" + Guid.NewGuid().ToString("N"));
        try
        {
            var clock = new ManualMonotonicClock();
            var preview = new RecordingPreviewSession();
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                preview,
                clock)
            {
                ProjectPathInput = root,
                ProjectNameInput = "Cadence Stop Test",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);
            await ExecuteAsync(viewModel.SimulateCommand);
            clock.Advance(TimeSpan.FromMilliseconds(100));
            await viewModel.AdvanceLivePreviewAsync();
            await ExecuteAsync(viewModel.StopCommand);
            await ExecuteAsync(viewModel.SimulateCommand);
            await viewModel.AdvanceLivePreviewAsync();

            Assert.Equal(0, viewModel.PreviewFrameIndex);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LiveOffSuppressesAutomaticEditPreviewAndPersistentCaptureArtifacts()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-live-off-" + Guid.NewGuid().ToString("N"));
        try
        {
            var preview = new RecordingPreviewSession();
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                preview)
            {
                ProjectPathInput = root,
                ProjectNameInput = "Live Off Test",
                SceneNameInput = "Main",
                IsLiveViewportEnabled = false
            };

            await ExecuteAsync(viewModel.CreateCommand);
            await ExecuteAsync(viewModel.AddEntityCommand);

            Assert.Equal(0, preview.ResetCount);
            Assert.False(Directory.Exists(Path.Combine(root, "Artifacts", "Studio", "Viewport")));

            viewModel.IsLiveViewportEnabled = true;
            await ExecuteAsync(viewModel.AddEntityCommand);

            Assert.Equal(1, preview.ResetCount);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ModeTransitionDisablesConflictingCommandsBeforeAwaitingPreview()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-transition-" + Guid.NewGuid().ToString("N"));
        try
        {
            var preview = new RecordingPreviewSession();
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                preview)
            {
                ProjectPathInput = root,
                ProjectNameInput = "Transition Test",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);
            preview.BlockNextReset();

            var simulate = ExecuteAsync(viewModel.SimulateCommand);
            await preview.WaitForBlockedResetAsync();

            Assert.True(viewModel.IsBusy);
            Assert.False(viewModel.PlayCommand.CanExecute(null));
            Assert.False(viewModel.SimulateCommand.CanExecute(null));
            Assert.False(viewModel.StopCommand.CanExecute(null));

            preview.ReleaseBlockedReset();
            await simulate;
            Assert.Equal(RekallAgeStudioMode.Simulate, viewModel.Mode);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RepeatedDisposeAwaitsTheSameInProgressShutdown()
    {
        var preview = new RecordingPreviewSession();
        preview.BlockDispose();
        var viewModel = new RekallAgeStudioViewModel(
            new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
            new EmptyModel(),
            preview);

        var first = viewModel.DisposeAsync().AsTask();
        await preview.WaitForDisposeAsync();
        var second = viewModel.DisposeAsync().AsTask();

        Assert.Same(first, second);
        Assert.False(second.IsCompleted);

        preview.ReleaseDispose();
        await Task.WhenAll(first, second);
    }

    [Fact]
    public void StudioShellRequiresSharedDarkControlsAndVisibleModeAffordances()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var app = File.ReadAllText(Path.Combine(root, "src", "Rekall.Age.Studio", "App.xaml"));
        var window = File.ReadAllText(Path.Combine(root, "src", "Rekall.Age.Studio", "MainWindow.xaml"));

        Assert.Contains("Property=\"FontFamily\" Value=\"Segoe UI\"", app, StringComparison.Ordinal);
        Assert.Contains("TargetType=\"{x:Type Button}\"", app, StringComparison.Ordinal);
        Assert.Contains("TargetType=\"{x:Type TextBox}\"", app, StringComparison.Ordinal);
        Assert.Contains("TargetType=\"{x:Type ComboBox}\"", app, StringComparison.Ordinal);
        Assert.Contains("TargetType=\"{x:Type ListBox}\"", app, StringComparison.Ordinal);
        Assert.Contains("InactiveSelectionHighlightBrushKey", app, StringComparison.Ordinal);
        Assert.Contains("InactiveSelectionHighlightTextBrushKey", app, StringComparison.Ordinal);
        Assert.Contains("<Trigger Property=\"IsSelected\" Value=\"True\">", app, StringComparison.Ordinal);
        Assert.Contains("SimulateCommand", window, StringComparison.Ordinal);
        Assert.Contains("IsLiveViewportEnabled", window, StringComparison.Ordinal);
        Assert.Contains("ModeLabel", window, StringComparison.Ordinal);
    }
    [Fact]
    public void StudioRejectsLowCoverageAdvisoryAsTaskSpecificVisualProof()
    {
        var analysis = new RekallAgeViewportFrameAnalysis(
            true,
            true,
            100,
            100,
            5,
            0.96,
            1,
            0.2,
            0.1,
            ["REKALL_VIEWPORT_LOW_VISUAL_COVERAGE"]);

        Assert.False(RekallAgeStudioViewModel.IsStudioVisualProofAcceptable(analysis));
        Assert.True(RekallAgeStudioViewModel.IsStudioVisualProofAcceptable(
            analysis with { DominantColorRatio = 0.7, WarningCodes = [] }));
    }

    [Theory]
    [InlineData("REKALL_VIEWPORT_CAMERA_FACES_AWAY_FROM_CONTENT")]
    [InlineData("REKALL_VIEWPORT_UI_LARGE_COVERAGE")]
    public void StudioRejectsBlockingLayoutWarningsAsTaskSpecificVisualProof(string warningCode)
    {
        var informative = new RekallAgeViewportFrameAnalysis(
            true,
            true,
            100,
            100,
            12,
            0.7,
            0.3,
            0.5,
            0.2,
            []);

        Assert.False(RekallAgeStudioViewModel.IsStudioVisualProofAcceptable(
            informative,
            [warningCode]));
    }

    [Fact]
    public void AutomationRejectsANonInformativeViewportEvenWhenItIsNonblankAndPackaged()
    {
        var archive = Path.GetTempFileName();
        try
        {
            Assert.False(RekallAgeStudioAutomation.IsSuccessful(
                "AI authoring completed with evidence.",
                nonblankViewport: true,
                visuallyInformativeViewport: false,
                requireVisuallyInformativeViewport: true,
                archive));
            Assert.True(RekallAgeStudioAutomation.IsSuccessful(
                "AI authoring completed with evidence.",
                nonblankViewport: true,
                visuallyInformativeViewport: true,
                requireVisuallyInformativeViewport: true,
                archive));
        }
        finally
        {
            File.Delete(archive);
        }
    }

    [Fact]
    public void StudioAutomationLeavesTurnLimitDisabledWhenItIsNotRequested()
    {
        Assert.True(RekallAgeStudioAutomation.TryParse(
            [
                "--studio-agent-automation",
                "--project", "C:\\Game",
                "--project-name", "Game",
                "--model", "model",
                "--task", "Create a game",
                "--evidence", "C:\\Evidence\\result.json"
            ],
            out var options,
            out var error), error);

        Assert.Equal(default(int?), (int?)options!.MaxTurns);
    }

    [Fact]
    public async Task HeadlessOpenAiAutomationStopsAtTheStableCredentialGateAndWritesEvidence()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-openai-gate-" + Guid.NewGuid().ToString("N"));
        var evidence = Path.Combine(root, "evidence", "result.json");
        var catalog = new RekallAgeLanguageModelProviderCatalog(
            new RekallAgeLanguageModelProviderSettings { OpenAiApiKey = " " },
            () => new HttpClient(new ProviderLifecycleHandler(blockOllamaChat: false), disposeHandler: true));
        try
        {
            var result = await RekallAgeStudioAutomation.RunWithCatalogAsync(
                new RekallAgeStudioAutomationOptions(
                    root,
                    "Credential Gate",
                    "Main",
                    "gpt-5.6-sol",
                    "Author a game.",
                    evidence)
                {
                    Provider = "openai"
                },
                catalog,
                CancellationToken.None);

            const string expected =
                "REKALL_OPENAI_API_KEY_MISSING: OpenAI requires OPENAI_API_KEY or a session-only API key.";
            Assert.False(result.Succeeded);
            Assert.Equal(expected, result.Status);
            Assert.False(File.Exists(Path.Combine(root, "rekall.project.json")));
            Assert.True(File.Exists(evidence));
            Assert.Contains(expected, await File.ReadAllTextAsync(evidence), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task StudioStartsWithAnEmptyOrdinaryLanguageAuthoringRequest()
    {
        await using var viewModel = new RekallAgeStudioViewModel(
            new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
            new EmptyModel());

        Assert.Empty(viewModel.AgentTaskInput);
        Assert.False(viewModel.RunAgentCommand.CanExecute(null));
    }

    [Fact]
    public async Task EmptyProjectInspectorWaitsForAnEntitySelectionInsteadOfInventingComponentState()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-empty-inspector-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                new RecordingPreviewSession())
            {
                ProjectPathInput = root,
                ProjectNameInput = "Empty Inspector",
                SceneNameInput = "Main"
            };

            await ExecuteAsync(viewModel.CreateCommand);

            Assert.False(viewModel.HasInspectorSelection);
            Assert.Empty(viewModel.ComponentTypeInput);
            Assert.Empty(viewModel.PropertyNameInput);
            Assert.Empty(viewModel.PropertyValueInput);
            Assert.Equal("Select an entity to inspect components.", viewModel.InspectorEmptyStateText);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AdvancedInspectorProjectsSearchesAndSelectsAttachedComponents()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-advanced-inspector-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                new RecordingPreviewSession())
            {
                ProjectPathInput = root,
                ProjectNameInput = "Advanced Inspector",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);
            await ExecuteAsync(viewModel.AddEntityCommand);
            viewModel.ComponentTypeInput = "Rekall.Transform2D";
            await ExecuteAsync(viewModel.AddComponentCommand);
            viewModel.ComponentTypeInput = "Rekall.ShapeRenderer2D";
            await ExecuteAsync(viewModel.AddComponentCommand);

            Assert.Equal(2, viewModel.InspectorComponents.Count);
            Assert.Equal(viewModel.EntityNameInput, viewModel.InspectorSelectionName);
            Assert.Equal(viewModel.SelectedEntityId, viewModel.InspectorSelectionId);
            Assert.Equal("2 components", viewModel.InspectorComponentCountText);

            viewModel.InspectorSearchInput = "shape";

            var shape = Assert.Single(viewModel.InspectorComponents);
            Assert.Equal("Rekall.ShapeRenderer2D", shape.Type);
            Assert.Same(shape, viewModel.SelectedInspectorComponent);
            Assert.Equal(shape.Type, viewModel.ComponentTypeInput);
            Assert.False(string.IsNullOrWhiteSpace(viewModel.SelectedInspectorComponentDescription));

            viewModel.InspectorSearchInput = "not-present";
            Assert.Empty(viewModel.InspectorComponents);
            Assert.Null(viewModel.SelectedInspectorComponent);
            Assert.Contains("No attached components match", viewModel.InspectorComponentBrowserEmptyText, StringComparison.Ordinal);

            viewModel.InspectorSearchInput = string.Empty;
            var transform = viewModel.InspectorComponents.Single(component => component.Type == "Rekall.Transform2D");
            viewModel.SelectedInspectorComponent = transform;
            Assert.Equal("Rekall.Transform2D", viewModel.ComponentTypeInput);
            Assert.NotEmpty(viewModel.PropertySchemas);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ManualMeshPreviewPreservesTheSelectedOperationForApply()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-manual-preview-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                new RecordingPreviewSession())
            {
                ProjectPathInput = root,
                ProjectNameInput = "Manual Preview Test",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);
            viewModel.SelectedMeshPrimitive = "box";
            viewModel.MeshPrimitiveAssetIdInput = "preview-box";
            await ExecuteAsync(viewModel.CreateMeshPrimitiveCommand);
            viewModel.MeshEditDomain = RekallAgeGeometryDomain.Face;
            viewModel.SelectedMeshElementId = viewModel.MeshElementIds[0];
            await ExecuteAsync(viewModel.SelectMeshElementCommand);
            viewModel.SelectedMeshOperationId = "extrude_faces";
            viewModel.MeshOperationIds.CollectionChanged += (_, _) => viewModel.SelectedMeshOperationId = null;

            await ExecuteAsync(viewModel.PreviewMeshOperationCommand);

            Assert.Equal("extrude_faces", viewModel.SelectedMeshOperationId);
            Assert.Contains("PREVIEW", viewModel.MeshSummary, StringComparison.Ordinal);

            await ExecuteAsync(viewModel.ApplyMeshOperationCommand);

            Assert.Equal("extrude_faces", viewModel.SelectedMeshOperationId);
            Assert.DoesNotContain("PREVIEW", viewModel.MeshSummary, StringComparison.Ordinal);
            var persisted = await new RekallAgeMeshAssetStore().LoadAsync(root, "preview-box", default);
            Assert.Equal(10, persisted.Topology.FaceIds.Count);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AutomationArgumentsRequireExplicitBoundedInputs()
    {
        var parsed = RekallAgeStudioAutomation.TryParse(
            [
                RekallAgeStudioAutomation.AutomationSwitch,
                "--project", "game",
                "--project-name", "Game",
                "--scene", "Main",
                "--provider", "openai",
                "--model", "model",
                "--task", "Author a game",
                "--evidence", "evidence.json",
                "--max-turns", "40",
                "--require-task-specific-completion"
            ],
            out var options,
            out var error);

        Assert.True(parsed, error);
        Assert.Equal("game", options!.ProjectRoot);
        Assert.Equal("openai", options.Provider);
        Assert.False(options.TreatGauntletAsTerminalSuccess);
        Assert.Equal(40, options.MaxTurns);
        Assert.False(RekallAgeStudioAutomation.TryParse(
            [RekallAgeStudioAutomation.AutomationSwitch, "--project", "game"],
            out _, out var missing));
        Assert.Contains("--model", missing, StringComparison.Ordinal);
    }

    [Fact]
    public void AutomationArgumentsAcceptCodexAsAProjectAgentProvider()
    {
        var parsed = RekallAgeStudioAutomation.TryParse(
            [
                RekallAgeStudioAutomation.AutomationSwitch,
                "--project", "game",
                "--project-name", "Game",
                "--provider", "codex",
                "--model", "gpt-5.6-sol",
                "--task", "Author and prove a game",
                "--evidence", "evidence.json"
            ],
            out var options,
            out var error);

        Assert.True(parsed, error);
        Assert.Equal("codex", options!.Provider);
    }

    [Fact]
    public async Task HeadlessAutomationCreatesProjectAndCompletesAgentGauntlet()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-agent-" + Guid.NewGuid().ToString("N"));
        var evidence = Path.Combine(root + "-evidence", "studio-agent.json");
        try
        {
            var result = await RekallAgeStudioAutomation.RunAsync(
                new RekallAgeStudioAutomationOptions(root, "Automated Agent Game", "Main", "deterministic", "Author and prove a playable game.", evidence),
                new GauntletModel(root),
                CancellationToken.None);

            Assert.True(result.Succeeded, result.Status + Environment.NewLine + result.ViewportSummary + Environment.NewLine + string.Join(Environment.NewLine, result.AgentTranscript));
            Assert.True(result.NonblankViewport);
            Assert.True(result.ViewportRenderableCount > 0);
            Assert.NotEmpty(result.AgentToolExecutions);
            Assert.True(File.Exists(result.PackageArchivePath));
            Assert.True(File.Exists(evidence));
            Assert.Contains("provider: deterministic", result.AgentTranscript);
            Assert.Contains("model: deterministic", result.AgentTranscript);
            Assert.Contains(result.AgentTranscript, line => line.StartsWith("response: deterministic-response-", StringComparison.Ordinal));
            Assert.Contains("usage: input=200 output=20 cached=unavailable reasoning=unavailable", result.AgentTranscript);
            Assert.Contains("tools: 2", result.AgentTranscript);
            Assert.Contains(result.AgentTranscript, line => line.StartsWith("elapsed: ", StringComparison.Ordinal) && line.EndsWith(" ms", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            var evidenceRoot = Path.GetDirectoryName(evidence)!;
            if (Directory.Exists(evidenceRoot)) Directory.Delete(evidenceRoot, recursive: true);
        }
    }

    [Fact]
    public async Task HeadlessAutomationDoesNotCallAnEmptyDebugFrameNonblank()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-empty-" + Guid.NewGuid().ToString("N"));
        var evidence = Path.Combine(root + "-evidence", "studio-agent.json");
        try
        {
            var result = await RekallAgeStudioAutomation.RunAsync(
                new RekallAgeStudioAutomationOptions(root, "Empty", "Main", "deterministic", "Inspect only.", evidence)
                {
                    TreatGauntletAsTerminalSuccess = false,
                    MaxTurns = 2
                },
                new EmptyModel(),
                CancellationToken.None);

            Assert.Equal(0, result.ViewportRenderableCount);
            Assert.False(result.NonblankViewport);
            Assert.False(result.VisuallyInformativeViewport);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            var evidenceRoot = Path.GetDirectoryName(evidence)!;
            if (Directory.Exists(evidenceRoot)) Directory.Delete(evidenceRoot, recursive: true);
        }
    }

    [Fact]
    public async Task AgentPreservesCompletedToolEvidenceWhenALaterModelTurnFails()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-partial-evidence-" + Guid.NewGuid().ToString("N"));
        try
        {
            var registry = RekallAgeDefaultCommandRegistry.Create();
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(registry),
                new FailsAfterToolModel());
            viewModel.ProjectPathInput = root;
            viewModel.ProjectNameInput = "Partial Evidence";
            viewModel.SceneNameInput = "Main";
            await ExecuteAsync(viewModel.CreateCommand);
            viewModel.AgentTaskInput = "Inspect the engine, then continue.";

            await ExecuteAsync(viewModel.RunAgentCommand);

            var execution = Assert.Single(viewModel.LastAgentToolExecutions);
            Assert.Equal("rekall.context.engine_status", execution.Name);
            Assert.True(execution.Succeeded);
            Assert.Contains("REKALL_STUDIO_UNEXPECTED_FAILURE", viewModel.ValidationLines.Single(), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CancellingAuthoringReloadsMutationsAlreadyWrittenByTheAgent()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-cancel-reload-" + Guid.NewGuid().ToString("N"));
        var model = new MutateThenBlockModel(root);
        try
        {
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                model);
            viewModel.ProjectPathInput = root;
            viewModel.ProjectNameInput = "Cancel Reload";
            viewModel.SceneNameInput = "Main";
            await ExecuteAsync(viewModel.CreateCommand);
            viewModel.AgentTaskInput = "Add the authored marker, then wait.";

            var run = ExecuteAsync(viewModel.RunAgentCommand);
            await model.WaitForBlockingTurnAsync();
            Assert.DoesNotContain(viewModel.EntityNodes, entity => entity.Name == "Authored Marker");

            await ExecuteAsync(viewModel.CancelAgentCommand);
            await run;

            Assert.Contains(viewModel.EntityNodes, entity => entity.Name == "Authored Marker");
            Assert.Equal("AI authoring cancelled.", viewModel.StatusText);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CancellingWithoutAnActiveAuthoringRunKeepsTheIdleStatus()
    {
        await using var viewModel = new RekallAgeStudioViewModel();

        await ExecuteAsync(viewModel.CancelAgentCommand);

        Assert.Equal("AI authoring is idle.", viewModel.AgentActivityText);
    }

    [Fact]
    public async Task WorldAuthoringActivityTracksTheRunningAgentAndItsCancellation()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-world-authoring-" + Guid.NewGuid().ToString("N"));
        var model = new MutateThenBlockModel(root);
        try
        {
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                model);
            viewModel.ProjectPathInput = root;
            viewModel.ProjectNameInput = "World Authoring";
            viewModel.SceneNameInput = "Main";
            await ExecuteAsync(viewModel.CreateCommand);
            viewModel.AgentTaskInput = "Add the authored marker, then wait.";

            var run = ExecuteAsync(viewModel.RunAgentCommand);
            await model.WaitForBlockingTurnAsync();

            Assert.True(viewModel.IsAgentRunning);
            Assert.StartsWith("Turn 2 · turn.started", viewModel.AgentActivityText, StringComparison.Ordinal);
            Assert.Contains("Running agent turn 2", viewModel.AgentActivityText, StringComparison.Ordinal);

            await ExecuteAsync(viewModel.CancelAgentCommand);
            await run;

            Assert.Equal("AI authoring cancelled.", viewModel.AgentActivityText);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task HeadlessAutomationContinuesAnExistingStudioProject()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-existing-" + Guid.NewGuid().ToString("N"));
        var evidence = Path.Combine(root + "-evidence", "studio-agent.json");
        try
        {
            await using (var setup = new RekallAgeStudioViewModel())
            {
                setup.ProjectPathInput = root;
                setup.ProjectNameInput = "Existing Game";
                setup.SceneNameInput = "Main";
                await ExecuteAsync(setup.CreateCommand);
            }

            var result = await RekallAgeStudioAutomation.RunAsync(
                new RekallAgeStudioAutomationOptions(root, "Must Not Replace Existing Game", "Main", "deterministic", "Inspect the existing game.", evidence)
                {
                    TreatGauntletAsTerminalSuccess = true,
                    MaxTurns = 2
                },
                new EmptyModel(),
                CancellationToken.None);

            Assert.StartsWith("AI authoring completed", result.Status, StringComparison.Ordinal);
            Assert.True(File.Exists(evidence));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            var evidenceRoot = Path.GetDirectoryName(evidence)!;
            if (Directory.Exists(evidenceRoot)) Directory.Delete(evidenceRoot, recursive: true);
        }
    }

    [Fact]
    public void AutomationFindsNestedAgentAuthoredPackageOutput()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-package-" + Guid.NewGuid().ToString("N"));
        try
        {
            var nested = Path.Combine(root, "Output", "Packages");
            Directory.CreateDirectory(nested);
            var archive = Path.Combine(nested, "EchoFoundry.zip");
            File.WriteAllText(archive, "package");

            Assert.Equal(archive, RekallAgeStudioAutomation.ResolvePackageArchivePath(root));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CodeWorkflowScaffoldsBuildsAndAttachesComponentToSelectedEntity()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-code-workflow-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var viewModel = new RekallAgeStudioViewModel
            {
                ProjectPathInput = root,
                ProjectNameInput = "Studio Code Workflow",
                SceneNameInput = "Main",
                CodeModuleNameInput = "Mover",
                CodeComponentNameInput = "MoverState",
                CodeSystemNameInput = "MoverSystem"
            };

            await ExecuteAsync(viewModel.CreateCommand);
            viewModel.EntityNameInput = "Moving Entity";
            await ExecuteAsync(viewModel.AddEntityCommand);
            await ExecuteAsync(viewModel.CreateAttachCodeComponentCommand);

            Assert.True(File.Exists(Path.Combine(root, "Modules", "Mover", "MoverModule.cs")));
            Assert.Contains(viewModel.CodeOutputLines, line =>
                line.Contains("Built", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("MoverModule.cs", viewModel.SelectedCodeSource?.FileName);
            Assert.Contains("public sealed class MoverState", viewModel.CodeSourceText, StringComparison.Ordinal);
            Assert.Contains(viewModel.InspectorLines, line =>
                line.Contains("Game.Modules.Mover.MoverState", StringComparison.Ordinal));

            viewModel.CodeSourceText += Environment.NewLine + "// unsaved";
            Assert.True(viewModel.IsCodeDirty);
            Assert.False(viewModel.CreateAttachCodeComponentCommand.CanExecute(null));

            var scene = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
            var entity = Assert.Single(scene.Entities);
            var component = Assert.Single(entity.Components, candidate =>
                candidate.Type == "Game.Modules.Mover.MoverState");
            Assert.True(component.Properties["enabled"]!.GetValue<bool>());
            Assert.Equal(1, component.Properties["valuePerSecond"]!.GetValue<double>());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CodeWorkspaceFindsPlayerAndCliInPackagedDistributionLayout()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-distribution-" + Guid.NewGuid().ToString("N"));
        try
        {
            var studioRoot = Path.Combine(root, "tools", "studio");
            var cliPath = Path.Combine(root, "tools", "cli", "Rekall.Age.Cli.exe");
            var playerPath = Path.Combine(root, "players", "windows", "Rekall.Age.Player.Windows.exe");
            Directory.CreateDirectory(studioRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(cliPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(playerPath)!);
            File.WriteAllBytes(cliPath, [0]);
            File.WriteAllBytes(playerPath, [0]);

            Assert.Equal(Path.GetFullPath(cliPath), RekallAgeStudioViewModel.ResolveCliExecutable(studioRoot));
            Assert.Equal(Path.GetFullPath(playerPath), RekallAgeStudioViewModel.ResolvePlayerExecutable(studioRoot));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ViewModelCreatesAndEditsProjectThroughSchemaGuidedCanonicalCommands()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-vm-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var viewModel = new RekallAgeStudioViewModel();
            viewModel.ProjectPathInput = root;
            viewModel.ProjectNameInput = "Automated Studio Game";
            viewModel.SceneNameInput = "Main";

            await ExecuteAsync(viewModel.CreateCommand);
            await ExecuteAsync(viewModel.AddEntityCommand);

            Assert.Contains(viewModel.ComponentSchemas, schema => schema.Type == "Rekall.Transform2D");
            viewModel.ComponentTypeInput = "Rekall.Transform2D";
            await ExecuteAsync(viewModel.AddComponentCommand);
            viewModel.PropertyNameInput = "x";
            viewModel.PropertyValueInput = "12.5";
            Assert.Equal("number", viewModel.SelectedPropertySchema?.EditorKind);
            await ExecuteAsync(viewModel.SetPropertyCommand);

            var scene = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
            var entity = Assert.Single(scene.Entities);
            var transform = Assert.Single(entity.Components, component => component.Type == "Rekall.Transform2D");
            Assert.Equal(12.5, transform.Properties["x"]!.GetValue<double>());

            await ExecuteAsync(viewModel.UndoCommand);
            scene = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
            transform = Assert.Single(Assert.Single(scene.Entities).Components, component => component.Type == "Rekall.Transform2D");
            Assert.False(transform.Properties.ContainsKey("x"));

            await ExecuteAsync(viewModel.RedoCommand);
            scene = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
            transform = Assert.Single(Assert.Single(scene.Entities).Components, component => component.Type == "Rekall.Transform2D");
            Assert.Equal(12.5, transform.Properties["x"]!.GetValue<double>());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InlineInspectorRowsGroupUndefinedPropertiesAndCommitAndResetThroughCanonicalCommands()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-inline-inspector-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var viewModel = new RekallAgeStudioViewModel
            {
                ProjectPathInput = root,
                ProjectNameInput = "Inline Inspector",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);
            await ExecuteAsync(viewModel.AddEntityCommand);
            viewModel.ComponentTypeInput = "Rekall.Transform2D";
            await ExecuteAsync(viewModel.AddComponentCommand);

            viewModel.InspectorSearchInput = "does-not-match";
            Assert.Empty(viewModel.InspectorComponentEditors);
            viewModel.InspectorSearchInput = string.Empty;
            var group = Assert.Single(viewModel.InspectorComponentEditors);
            Assert.Equal("Rekall.Transform2D", group.Type);
            Assert.Equal(group.PropertyEditors.Count, viewModel.InspectorPropertyEditors.Count);
            var x = group.PropertyEditors.Single(row => row.Name == "x");
            var y = group.PropertyEditors.Single(row => row.Name == "y");
            Assert.False(x.IsDefined);
            Assert.False(viewModel.ResetInspectorPropertyCommand.CanExecute(x));

            y.TextValue = "7";
            x.TextValue = "12.5";
            Assert.True(viewModel.CommitInspectorPropertyCommand.CanExecute(x));
            await ExecuteAsync(viewModel.CommitInspectorPropertyCommand, x);

            var scene = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
            var transform = Assert.Single(Assert.Single(scene.Entities).Components, component => component.Type == "Rekall.Transform2D");
            Assert.Equal(12.5, transform.Properties["x"]!.GetValue<double>());
            var persistedX = viewModel.InspectorPropertyEditors.Single(row => row.ComponentType == "Rekall.Transform2D" && row.Name == "x");
            Assert.True(persistedX.IsDefined);
            Assert.False(persistedX.IsDirty);
            Assert.Same(y, viewModel.InspectorPropertyEditors.Single(row => row.Name == "y"));
            Assert.Equal("7", y.TextValue);
            Assert.True(y.IsDirty);
            Assert.Contains(viewModel.TransactionLines, line => line.StartsWith("Set Rekall.Transform2D.x:", StringComparison.Ordinal));

            Assert.True(viewModel.ResetInspectorPropertyCommand.CanExecute(persistedX));
            await ExecuteAsync(viewModel.ResetInspectorPropertyCommand, persistedX);

            scene = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
            transform = Assert.Single(Assert.Single(scene.Entities).Components, component => component.Type == "Rekall.Transform2D");
            Assert.False(transform.Properties.ContainsKey("x"));
            var resetX = viewModel.InspectorPropertyEditors.Single(row => row.ComponentType == "Rekall.Transform2D" && row.Name == "x");
            Assert.False(resetX.IsDefined);
            Assert.False(resetX.IsDirty);
            Assert.Contains(viewModel.TransactionLines, line => line.StartsWith("Remove Rekall.Transform2D.x:", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InlineInspectorLocalValidationLeavesTheSceneAndTransactionHistoryUntouched()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-inline-local-validation-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var viewModel = new RekallAgeStudioViewModel
            {
                ProjectPathInput = root,
                ProjectNameInput = "Inline Local Validation",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);
            await ExecuteAsync(viewModel.AddEntityCommand);
            viewModel.ComponentTypeInput = "Rekall.Transform2D";
            await ExecuteAsync(viewModel.AddComponentCommand);
            var transactionCount = viewModel.TransactionLines.Count;
            var x = viewModel.InspectorPropertyEditors.Single(row => row.Name == "x");

            x.TextValue = "not-a-number";

            Assert.True(x.IsDirty);
            Assert.False(x.IsValid);
            Assert.Contains("finite number", x.ValidationMessage, StringComparison.OrdinalIgnoreCase);
            Assert.False(viewModel.CommitInspectorPropertyCommand.CanExecute(x));
            await ExecuteAsync(viewModel.CommitInspectorPropertyCommand, x);
            Assert.Equal(transactionCount, viewModel.TransactionLines.Count);
            var scene = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
            var transform = Assert.Single(Assert.Single(scene.Entities).Components, component => component.Type == "Rekall.Transform2D");
            Assert.False(transform.Properties.ContainsKey("x"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InlineInspectorServerRejectionPreservesTheStructuredDraftAndAttachesTheError()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-inline-server-validation-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var viewModel = new RekallAgeStudioViewModel
            {
                ProjectPathInput = root,
                ProjectNameInput = "Inline Server Validation",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);
            await ExecuteAsync(viewModel.AddEntityCommand);
            viewModel.ComponentTypeInput = "Rekall.AnimationClip";
            await ExecuteAsync(viewModel.AddComponentCommand);
            var transactionCount = viewModel.TransactionLines.Count;
            var tracks = viewModel.InspectorPropertyEditors.Single(row => row.Name == "tracks");
            Assert.Equal("json", tracks.TemplateKind);

            tracks.TextValue = "{}";
            Assert.True(tracks.IsDirty);
            Assert.True(tracks.IsValid);
            await ExecuteAsync(viewModel.CommitInspectorPropertyCommand, tracks);

            Assert.Same(tracks, viewModel.InspectorPropertyEditors.Single(row => row.Name == "tracks"));
            Assert.Equal("{}", tracks.TextValue);
            Assert.True(tracks.IsDirty);
            Assert.Contains("array", tracks.ValidationMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(viewModel.ValidationLines, line => line.Contains("REKALL_COMPONENT_PROPERTY_SHAPE_INVALID", StringComparison.Ordinal));
            Assert.Equal(transactionCount, viewModel.TransactionLines.Count);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InlineInspectorStringEditPreservesEscapedPersistedContentAcrossCommits()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-inline-string-roundtrip-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var viewModel = new RekallAgeStudioViewModel
            {
                ProjectPathInput = root,
                ProjectNameInput = "Inline String Roundtrip",
                SceneNameInput = "Main"
            };
            await ExecuteAsync(viewModel.CreateCommand);
            await ExecuteAsync(viewModel.AddEntityCommand);
            viewModel.ComponentTypeInput = "Rekall.AudioBus";
            await ExecuteAsync(viewModel.AddComponentCommand);
            var name = viewModel.InspectorPropertyEditors.Single(row => row.Name == "name");
            const string initial = "primary\n\"route\"\\main";

            name.TextValue = initial;
            await ExecuteAsync(viewModel.CommitInspectorPropertyCommand, name);

            var persisted = viewModel.InspectorPropertyEditors.Single(row => row.Name == "name");
            Assert.Equal(initial, persisted.TextValue);
            const string edited = "primary\n\"route\"\\main\n\"secondary\"\\tail";
            persisted.TextValue = edited;
            await ExecuteAsync(viewModel.CommitInspectorPropertyCommand, persisted);

            var scene = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
            var audioBus = Assert.Single(Assert.Single(scene.Entities).Components, component => component.Type == "Rekall.AudioBus");
            Assert.Equal(edited, audioBus.Properties["name"]!.GetValue<string>());
            Assert.Equal(edited, viewModel.InspectorPropertyEditors.Single(row => row.Name == "name").TextValue);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static Task ExecuteAsync(System.Windows.Input.ICommand command) =>
        ((RekallAgeAsyncCommand)command).ExecuteAsync(null);

    private static Task ExecuteAsync(System.Windows.Input.ICommand command, object? parameter) =>
        ((RekallAgeAsyncCommand)command).ExecuteAsync(parameter);

    private static async Task CreateQualityProjectAsync(string root)
    {
        await new RekallAgeProjectStore().SaveAsync(
            root,
            RekallAgeProjectManifest.Create("Studio Quality", ["world", "rendering3d"]),
            default);
        var quality = RekallAgeEntityDocument.Create("Quality", ["rendering"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.RenderQualityProfile",
                new JsonObject { ["preset"] = "High" }));
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"]).AddEntity(quality),
            default);
    }

    private static RekallAgeQualityPresetCapture PartialQualityCapture(bool nonBlank = false) => new(
        "High",
        "High",
        1,
        "D:/captures/partial-high.png",
        nonBlank,
        320,
        180,
        320,
        180,
        4096,
        1,
        0,
        [],
        new RekallAgeGpuFrameTimingReport(
            true,
            null,
            1,
            [new RekallAgeGpuPassTiming("forward", 2_000_000, 2)],
            2_500_000,
            2.5,
            "vulkan-timestamp-query"),
        RekallAgeViewportFrameAnalysis.NotAnalyzed);

    private sealed class PartialQualityComparisonCommand
        : IRekallAgeCommand<CompareQualityPresetsRequest, CompareQualityPresetsResult>
    {
        public string Name => "rekall.render.compare_quality_presets";
        public RekallAgeCommandSchema Schema => new(
            Name,
            "Returns one usable comparison capture followed by a deterministic failure.",
            typeof(CompareQualityPresetsRequest).FullName!,
            typeof(CompareQualityPresetsResult).FullName!);

        public ValueTask<RekallAgeCommandResult<CompareQualityPresetsResult>> ExecuteAsync(
            CompareQualityPresetsRequest request,
            RekallAgeCommandContext context)
        {
            var value = new CompareQualityPresetsResult(
                request.SceneName,
                1,
                [PartialQualityCapture()],
                ["command execute rekall.render.performance.inspect_scene_budget"]);
            var error = new RekallAgeCommandError(
                "REKALL_TEST_PARTIAL_COMPARISON",
                "The second requested preset failed after the first capture completed.",
                "Performance");
            return ValueTask.FromResult(RekallAgeCommandResult<CompareQualityPresetsResult>.Failure(
                value,
                error.Message,
                [error]));
        }
    }

    private sealed class CancellationBlockingCaptureCommand
        : IRekallAgeCommand<CaptureRuntimeViewportRequest, CaptureRuntimeViewportResult>
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _cancellationObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Name => "rekall.render.capture_runtime_viewport";
        public RekallAgeCommandSchema Schema => new(
            Name,
            "Waits deterministically for lifecycle cancellation.",
            typeof(CaptureRuntimeViewportRequest).FullName!,
            typeof(CaptureRuntimeViewportResult).FullName!);

        public async ValueTask<RekallAgeCommandResult<CaptureRuntimeViewportResult>> ExecuteAsync(
            CaptureRuntimeViewportRequest request,
            RekallAgeCommandContext context)
        {
            _started.TrySetResult();
            using var registration = context.CancellationToken.Register(() => _cancellationObserved.TrySetResult());
            var signal = await Task.WhenAny(_cancellationObserved.Task, _release.Task);
            if (ReferenceEquals(signal, _cancellationObserved.Task))
            {
                await _release.Task;
                context.CancellationToken.ThrowIfCancellationRequested();
            }

            var error = new RekallAgeCommandError("REKALL_TEST_CAPTURE_RELEASED", "The deterministic capture was released by its test.");
            return RekallAgeCommandResult<CaptureRuntimeViewportResult>.Failure(
                default!,
                error.Message,
                [error]);
        }

        public Task WaitForStartAsync() => _started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        public Task WaitForCancellationAsync() => _cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        public void Release() => _release.TrySetResult();
    }

    private sealed class ProviderLifecycleHandler(
        bool blockOllamaChat,
        bool blockOllamaModels = false,
        IReadOnlyList<string>? ollamaModels = null,
        IReadOnlyList<string>? openAiModels = null,
        bool pauseModelResponse = false,
        Exception? modelFailure = null,
        Exception? chatFailure = null) : HttpMessageHandler
    {
        private readonly TaskCompletionSource _chatStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _modelsStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _modelsRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ConcurrentQueue<string> _events = new();

        public IReadOnlyList<string> Events => _events.ToArray();

        public Task WaitForChatAsync() => _chatStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        public Task WaitForModelsAsync() => _modelsStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        public void ReleaseModels() => _modelsRelease.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/api/tags", StringComparison.Ordinal))
            {
                _modelsStarted.TrySetResult();
                if (blockOllamaModels)
                {
                    var cancellation = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    var completed = await Task.WhenAny(cancellation, _disposed.Task);
                    if (ReferenceEquals(completed, cancellation))
                    {
                        _events.Enqueue("models-cancelled");
                        await cancellation;
                    }

                    throw new OperationCanceledException("The provider lease was disposed before model refresh cancellation was observed.");
                }
                if (pauseModelResponse) await _modelsRelease.Task.WaitAsync(cancellationToken);
                if (modelFailure is not null) throw modelFailure;
                var models = ollamaModels ?? ["qwen3.8:27b", "gemma3:latest"];
                return JsonResponse(new JsonObject
                {
                    ["models"] = new JsonArray(models.Select((model, index) => new JsonObject
                    {
                        ["name"] = model,
                        ["size"] = index + 1
                    }).ToArray())
                }.ToJsonString());
            }

            if (request.RequestUri.AbsolutePath.EndsWith("/api/show", StringComparison.Ordinal))
            {
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
                var model = body["model"]!.GetValue<string>();
                var supportsTools = !model.StartsWith("dolphin-", StringComparison.OrdinalIgnoreCase);
                return JsonResponse(new JsonObject
                {
                    ["capabilities"] = new JsonArray(supportsTools ? ["completion", "tools"] : ["completion"])
                }.ToJsonString());
            }

            if (request.RequestUri.AbsolutePath.EndsWith("/api/chat", StringComparison.Ordinal))
            {
                _chatStarted.TrySetResult();
                if (chatFailure is not null) throw chatFailure;
                if (blockOllamaChat)
                {
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        _events.Enqueue("chat-cancelled");
                        throw;
                    }
                }

                return JsonResponse("""
                    {"model":"qwen3.8:27b","message":{"role":"assistant","content":"complete"},"done":true,"done_reason":"stop","total_duration":1,"prompt_eval_count":1,"eval_count":1}
                    """);
            }

            if (request.RequestUri.AbsolutePath.EndsWith("/models", StringComparison.Ordinal))
            {
                _modelsStarted.TrySetResult();
                if (pauseModelResponse) await _modelsRelease.Task.WaitAsync(cancellationToken);
                if (modelFailure is not null) throw modelFailure;
                var models = openAiModels ?? ["gpt-5.6-sol-preview", "gpt-5.6-sol"];
                return JsonResponse(new JsonObject
                {
                    ["data"] = new JsonArray(models.Select(model => new JsonObject { ["id"] = model }).ToArray())
                }.ToJsonString());
            }

            throw new InvalidOperationException($"Unexpected provider request: {request.Method} {request.RequestUri.AbsolutePath}");
        }

        protected override void Dispose(bool disposing)
        {
            _events.Enqueue("lease-disposed");
            _disposed.TrySetResult();
            base.Dispose(disposing);
        }

        private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class GauntletModel(string projectRoot) : IRekallAgeLanguageModelClient
    {
        private int _calls;
        public string ProviderId => "deterministic";

        public ValueTask<IReadOnlyList<RekallAgeLanguageModelInfo>> ListModelsAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<RekallAgeLanguageModelInfo>>([]);

        public ValueTask<RekallAgeLanguageModelResponse> ChatAsync(
            RekallAgeLanguageModelRequest request,
            CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _calls) == 1
                ? new RekallAgeLanguageModelToolCall("rekall.context.engine_status", new JsonObject())
                : new RekallAgeLanguageModelToolCall(
                    "rekall.workflow.agent_authoring_gauntlet",
                    new JsonObject
                    {
                        ["projectRoot"] = projectRoot,
                        ["projectName"] = "Automated Agent Game",
                        ["sceneName"] = "Main"
                    });
            return ValueTask.FromResult(new RekallAgeLanguageModelResponse(
                ProviderId,
                request.Model,
                string.Empty,
                "Run the complete generic proof.",
                [call],
                "tool_calls",
                new RekallAgeLanguageModelUsage(100, 10, 1))
            {
                ResponseId = $"deterministic-response-{_calls}"
            });
        }
    }

    private sealed class BlockingFaultingModel(string failurePayload) : IRekallAgeLanguageModelClient
    {
        private readonly TaskCompletionSource _chatStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseChat = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string ProviderId => "deterministic";

        public ValueTask<IReadOnlyList<RekallAgeLanguageModelInfo>> ListModelsAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<RekallAgeLanguageModelInfo>>(
                [new RekallAgeLanguageModelInfo("qwen3.5:35b", 1)]);

        public async ValueTask<RekallAgeLanguageModelResponse> ChatAsync(
            RekallAgeLanguageModelRequest request,
            CancellationToken cancellationToken)
        {
            _chatStarted.TrySetResult();
            await _releaseChat.Task;
            throw new InvalidDataException(failurePayload);
        }

        public Task WaitForChatAsync() => _chatStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public void ReleaseChat() => _releaseChat.TrySetResult();
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    private sealed class RecordingPreviewSession : IRekallAgeStudioPreviewSession
    {
        private int _frame;
        private int _width = 800;
        private int _height = 450;
        private TaskCompletionSource? _blockedReset;
        private TaskCompletionSource? _resetEntered;
        private TaskCompletionSource? _disposeBlocked;
        private TaskCompletionSource? _disposeEntered;
        public int ResetCount { get; private set; }
        public int StepCount { get; private set; }
        public int PresentCurrentCount { get; private set; }
        public int ExternalDependencyPollCount { get; private set; }
        public int ExternalDependencyPresentationCount { get; private set; }
        public RekallAgeStudioViewportMetrics Metrics { get; set; } = new(800, 450, 800, 450, true);
        public List<(int Width, int Height)> ResetSizes { get; } = [];
        public List<RekallAgeStudioViewportPickRegion> Regions { get; } = [];
        public bool IsDisposed { get; private set; }
        public bool IsDisposalComplete { get; private set; }
        public bool ReturnUnavailable { get; set; }
        public bool ReturnResizeMismatchOnce { get; set; }
        public RekallAgeStudioViewportRenderStyle RenderStyle { get; private set; } = RekallAgeStudioViewportRenderStyle.Textured;
        public RekallAgeStudioProjectModuleDiagnostic? ProjectModuleDiagnostic { get; set; }
        private bool _externalDependencyChangePending;

        public void SetRenderStyle(RekallAgeStudioViewportRenderStyle style) => RenderStyle = style;

        public ValueTask<RekallAgeStudioPreviewFrame> ResetAsync(
            string projectRoot,
            string sceneName,
            int width,
            int height,
            CancellationToken cancellationToken)
        {
            ResetCount++;
            _frame = 0;
            _width = width;
            _height = height;
            ResetSizes.Add((width, height));
            _resetEntered?.TrySetResult();
            if (ReturnResizeMismatchOnce)
            {
                ReturnResizeMismatchOnce = false;
                var runtimeFrame = CreateRuntimeFrame(_frame, width - 4, height);
                return ValueTask.FromResult(new RekallAgeStudioPreviewFrame(
                    RekallAgeVulkanPresentationFrame.Unavailable(
                        runtimeFrame,
                        $"The Studio Vulkan surface resized from {width - 4}x{height} to {width}x{height} before presentation."),
                    new RekallAgeStudioViewportInteractionSnapshot(width - 4, height, Regions),
                    ProjectModuleDiagnostic));
            }
            return _blockedReset is null
                ? ValueTask.FromResult(CreateFrame(_frame))
                : AwaitBlockedResetAsync(_blockedReset, cancellationToken);
        }

        public ValueTask<RekallAgeStudioPreviewFrame> StepAsync(int frameCount, CancellationToken cancellationToken)
        {
            StepCount++;
            _frame += frameCount;
            return ValueTask.FromResult(CreateFrame(_frame));
        }

        public ValueTask<RekallAgeStudioPreviewFrame> PresentCurrentAsync(
            int width,
            int height,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PresentCurrentCount++;
            _width = width;
            _height = height;
            return ValueTask.FromResult(CreateFrame(_frame));
        }

        public ValueTask<RekallAgeStudioPreviewFrame?> RefreshExternalDependenciesAsync(
            int width,
            int height,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExternalDependencyPollCount++;
            if (!_externalDependencyChangePending)
            {
                return ValueTask.FromResult<RekallAgeStudioPreviewFrame?>(null);
            }

            _externalDependencyChangePending = false;
            ExternalDependencyPresentationCount++;
            _width = width;
            _height = height;
            return ValueTask.FromResult<RekallAgeStudioPreviewFrame?>(CreateFrame(_frame));
        }

        public async ValueTask DisposeAsync()
        {
            IsDisposed = true;
            _disposeEntered?.TrySetResult();
            if (_disposeBlocked is not null) await _disposeBlocked.Task;
            IsDisposalComplete = true;
        }

        public ValueTask ClearAsync(CancellationToken cancellationToken)
        {
            _frame = 0;
            return ValueTask.CompletedTask;
        }

        public void BlockNextReset()
        {
            _blockedReset = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _resetEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public Task WaitForBlockedResetAsync() => _resetEntered?.Task.WaitAsync(TimeSpan.FromSeconds(5))
            ?? Task.CompletedTask;

        public void ReleaseBlockedReset() => _blockedReset?.TrySetResult();

        public void BlockDispose()
        {
            _disposeBlocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _disposeEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public Task WaitForDisposeAsync() => _disposeEntered?.Task.WaitAsync(TimeSpan.FromSeconds(5))
            ?? Task.CompletedTask;

        public void ReleaseDispose() => _disposeBlocked?.TrySetResult();

        public void QueueExternalDependencyChange() => _externalDependencyChangePending = true;

        private async ValueTask<RekallAgeStudioPreviewFrame> AwaitBlockedResetAsync(
            TaskCompletionSource blockedReset,
            CancellationToken cancellationToken)
        {
            await blockedReset.Task.WaitAsync(cancellationToken);
            _blockedReset = null;
            return CreateFrame(_frame);
        }

        private RekallAgeStudioPreviewFrame CreateFrame(int frame)
        {
            var runtimeFrame = CreateRuntimeFrame(frame, _width, _height);
            var presentation = ReturnUnavailable
                ? RekallAgeVulkanPresentationFrame.Unavailable(
                    runtimeFrame,
                    "Vulkan is unavailable: simulated initialization failure.",
                    ["REKALL_STUDIO_VULKAN_UNAVAILABLE"])
                : RekallAgeVulkanPresentationFrame.Presented(runtimeFrame, "test-gpu");
            return new RekallAgeStudioPreviewFrame(
                presentation,
                new RekallAgeStudioViewportInteractionSnapshot(_width, _height, Regions),
                ProjectModuleDiagnostic);
        }

        private static RekallAgeRuntimeViewportFrame CreateRuntimeFrame(int frame, int width, int height) =>
            new(
                "Main",
                frame,
                frame / 60d,
                width,
                height,
                null,
                [],
                [],
                0,
                new RekallAgeRuntimeViewportOverlay(false, 0),
                []);
    }

    private sealed class ManualMonotonicClock : IRekallAgeStudioMonotonicClock
    {
        private TimeSpan _timestamp;

        public TimeSpan GetTimestamp() => _timestamp;

        public void Advance(TimeSpan elapsed) => _timestamp += elapsed;
    }

    private sealed class UnauthenticatedCodexRunner : IRekallAgeCodexProjectAgentRunner
    {
        private bool _authenticated;
        public int DisposeCount { get; private set; }
        public Uri? LaunchedUri { get; set; }
        public string ProviderId => "codex";
        public RekallAgeCodexApprovalCallback? ApprovalCallback { get; set; }
        public RekallAgeLanguageModelProviderDescriptor CurrentProviderDescriptor { get; private set; } =
            RekallAgeLanguageModelProviderCatalog.DescribeCodexProvider(
                new RekallAgeCodexAccount(null, true, false));

        public ValueTask<IReadOnlyList<RekallAgeLanguageModelInfo>> ListModelsAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_authenticated)
            {
                return ValueTask.FromException<IReadOnlyList<RekallAgeLanguageModelInfo>>(
                    new RekallAgeLanguageModelProviderException(
                        RekallAgeCodexErrorCodes.AuthenticationRequired, "codex",
                        "Codex authentication is required. Sign in through Codex and retry."));
            }
            return ValueTask.FromResult<IReadOnlyList<RekallAgeLanguageModelInfo>>(
                [new RekallAgeLanguageModelInfo(RekallAgeCodexProjectAgentRunner.RequiredModel, 0)]);
        }

        public async ValueTask<RekallAgeCodexAccount> SignInWithChatGptAsync(
            RekallAgeCodexAuthenticationLauncher launchAuthentication,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await launchAuthentication(new Uri("https://chatgpt.com/sign-in"));
            _authenticated = true;
            var account = new RekallAgeCodexAccount("chatgpt", true, true);
            CurrentProviderDescriptor = RekallAgeLanguageModelProviderCatalog.DescribeCodexProvider(account);
            return account;
        }

        public ValueTask<RekallAgeProjectAgentSessionResult> RunAsync(
            RekallAgeProjectAgentSessionRequest request,
            IProgress<RekallAgeLanguageModelAgentProgress>? progress,
            CancellationToken cancellationToken) =>
            ValueTask.FromException<RekallAgeProjectAgentSessionResult>(new NotSupportedException());

        public ValueTask<RekallAgeLanguageModelResponse> ChatAsync(
            RekallAgeLanguageModelRequest request,
            CancellationToken cancellationToken) =>
            ValueTask.FromException<RekallAgeLanguageModelResponse>(new NotSupportedException());

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class EmptyModel : IRekallAgeLanguageModelClient
    {
        public string ProviderId => "deterministic";

        public ValueTask<IReadOnlyList<RekallAgeLanguageModelInfo>> ListModelsAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<RekallAgeLanguageModelInfo>>([]);

        public ValueTask<RekallAgeLanguageModelResponse> ChatAsync(
            RekallAgeLanguageModelRequest request,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new RekallAgeLanguageModelResponse(
                ProviderId,
                request.Model,
                "No content authored.",
                string.Empty,
                [],
                "stop",
                new RekallAgeLanguageModelUsage(1, 1, 1)));
    }

    private sealed class FailsAfterToolModel : IRekallAgeLanguageModelClient
    {
        private int _calls;
        public string ProviderId => "deterministic";

        public ValueTask<IReadOnlyList<RekallAgeLanguageModelInfo>> ListModelsAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<RekallAgeLanguageModelInfo>>([]);

        public ValueTask<RekallAgeLanguageModelResponse> ChatAsync(
            RekallAgeLanguageModelRequest request,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) > 1)
            {
                throw new InvalidDataException("simulated later model failure");
            }

            return ValueTask.FromResult(new RekallAgeLanguageModelResponse(
                ProviderId,
                request.Model,
                string.Empty,
                string.Empty,
                [new RekallAgeLanguageModelToolCall("rekall.context.engine_status", new JsonObject())],
                "tool_calls",
                new RekallAgeLanguageModelUsage(1, 1, 1)));
        }
    }

    private sealed class MutateThenBlockModel(string projectRoot) : IRekallAgeLanguageModelClient
    {
        private readonly TaskCompletionSource _blockingTurnStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public string ProviderId => "deterministic";

        public ValueTask<IReadOnlyList<RekallAgeLanguageModelInfo>> ListModelsAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<RekallAgeLanguageModelInfo>>([]);

        public async ValueTask<RekallAgeLanguageModelResponse> ChatAsync(
            RekallAgeLanguageModelRequest request,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                return new RekallAgeLanguageModelResponse(
                    ProviderId,
                    request.Model,
                    string.Empty,
                    string.Empty,
                    [new RekallAgeLanguageModelToolCall(
                        "rekall.scene.apply_blueprint",
                        new JsonObject
                        {
                            ["projectRoot"] = projectRoot,
                            ["sceneName"] = "Main",
                            ["clearExisting"] = false,
                            ["entities"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["name"] = "Authored Marker",
                                    ["components"] = new JsonArray
                                    {
                                        new JsonObject
                                        {
                                            ["type"] = "Rekall.Transform3D",
                                            ["properties"] = new JsonObject { ["X"] = 1 }
                                        }
                                    }
                                }
                            }
                        })],
                    "tool_calls",
                    new RekallAgeLanguageModelUsage(1, 1, 1));
            }

            _blockingTurnStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The cancelled blocking turn unexpectedly resumed.");
        }

        public Task WaitForBlockingTurnAsync() => _blockingTurnStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
