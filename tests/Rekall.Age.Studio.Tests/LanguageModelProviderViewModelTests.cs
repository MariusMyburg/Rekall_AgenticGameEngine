using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using Rekall.Age.Agent.LanguageModels;
using Rekall.Age.Core.Commands;
using Rekall.Age.Editor;
using Rekall.Age.Rendering.Windows;
using Rekall.Age.Workflows;

namespace Rekall.Age.Studio.Tests;

public sealed class LanguageModelProviderViewModelTests
{
    [Fact]
    public async Task KimiSessionKeyUnlocksOnlyKimiAndLoadsOfficialDefaultWithoutExposingKey()
    {
        const string key = "private-studio-kimi-key";
        var handlers = new Queue<HttpMessageHandler>(
            [new OllamaModelsHandler(["qwen3.8:27b"]), new KimiModelsHandler(["kimi-k2.7-code", "kimi-k3"])]);
        var catalog = new RekallAgeLanguageModelProviderCatalog(
            new RekallAgeLanguageModelProviderSettings { KimiApiKey = " " },
            () => new HttpClient(handlers.Dequeue(), disposeHandler: true));
        await using var viewModel = new RekallAgeStudioViewModel(
            new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
            catalog,
            new UnavailablePreview());

        viewModel.SelectedLanguageModelProvider = viewModel.LanguageModelProviders.Single(provider => provider.Id == "kimi");
        await viewModel.WaitForLanguageModelProviderTransitionAsync();
        Assert.False(viewModel.IsCodexSelected);
        Assert.True(viewModel.IsKimiSelected);
        Assert.Empty(viewModel.LanguageModels);

        await viewModel.ApplyKimiApiKeyAsync(key);

        Assert.True(viewModel.HasSessionKimiCredential);
        Assert.Equal(["kimi-k2.7-code", "kimi-k3"], viewModel.LanguageModels);
        Assert.Equal("kimi-k3", viewModel.SelectedLanguageModel);
        Assert.DoesNotContain(key, viewModel.ProviderStatus, StringComparison.Ordinal);
        Assert.DoesNotContain(key, string.Join('\n', viewModel.ValidationLines), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GgufImportRefreshesOllamaModelsAndSelectsTheImportedModel()
    {
        var installedModels = new List<string> { "qwen3.8:27b" };
        var handlers = new Queue<HttpMessageHandler>(
            [new OllamaModelsHandler(installedModels), new OllamaModelsHandler(installedModels)]);
        var catalog = new RekallAgeLanguageModelProviderCatalog(
            httpClientFactory: () => new HttpClient(handlers.Dequeue(), disposeHandler: true));
        var importer = new RecordingImporter(path =>
        {
            installedModels.Add("rekall-private-model-a1b2c3d4e5f6");
            return new RekallAgeGgufImportResult("rekall-private-model-a1b2c3d4e5f6");
        });
        await using var viewModel = new RekallAgeStudioViewModel(
            new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
            catalog,
            new UnavailablePreview(),
            importer);
        viewModel.SelectedLanguageModelProvider = viewModel.LanguageModelProviders.Single(provider => provider.Id == "gguf");
        await viewModel.WaitForLanguageModelProviderTransitionAsync();

        await viewModel.ImportGgufModelAsync("C:\\private\\model.gguf");

        Assert.True(viewModel.IsGgufSelected);
        Assert.Equal("C:\\private\\model.gguf", importer.LastPath);
        Assert.Contains("rekall-private-model-a1b2c3d4e5f6", viewModel.LanguageModels);
        Assert.Equal("rekall-private-model-a1b2c3d4e5f6", viewModel.SelectedLanguageModel);
        Assert.DoesNotContain("C:\\private", viewModel.ProviderStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GgufImportRejectsModelsThatDoNotAdvertiseRequiredToolCapability()
    {
        const string imported = "rekall-no-tools-a1b2c3d4e5f6";
        var installedModels = new List<string> { "qwen3.8:27b" };
        var handlers = new Queue<HttpMessageHandler>(
            [
                new OllamaModelsHandler(installedModels),
                new OllamaModelsHandler(installedModels, model => model != imported)
            ]);
        var catalog = new RekallAgeLanguageModelProviderCatalog(
            httpClientFactory: () => new HttpClient(handlers.Dequeue(), disposeHandler: true));
        var importer = new RecordingImporter(_ =>
        {
            installedModels.Add(imported);
            return new RekallAgeGgufImportResult(imported);
        });
        await using var viewModel = new RekallAgeStudioViewModel(
            new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
            catalog,
            new UnavailablePreview(),
            importer);
        viewModel.SelectedLanguageModelProvider = viewModel.LanguageModelProviders.Single(provider => provider.Id == "gguf");
        await viewModel.WaitForLanguageModelProviderTransitionAsync();

        await viewModel.ImportGgufModelAsync("C:\\private\\no-tools.gguf");

        Assert.Contains("REKALL_GGUF_TOOL_CAPABILITY_REQUIRED", viewModel.ProviderStatus, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\private", viewModel.ProviderStatus, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(imported, viewModel.LanguageModels);
    }

    [Fact]
    public async Task GgufImportPreservesOllamaDiscoveryFailureInsteadOfMisreportingToolCapability()
    {
        var handler = new OllamaFailAfterFirstDiscoveryHandler();
        var catalog = new RekallAgeLanguageModelProviderCatalog(
            httpClientFactory: () => new HttpClient(handler, disposeHandler: false));
        var importer = new RecordingImporter(_ => new RekallAgeGgufImportResult("rekall-imported-model"));
        await using var viewModel = new RekallAgeStudioViewModel(
            new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
            catalog,
            new UnavailablePreview(),
            importer);
        viewModel.SelectedLanguageModelProvider = viewModel.LanguageModelProviders.Single(provider => provider.Id == "gguf");
        await viewModel.WaitForLanguageModelProviderTransitionAsync();

        await viewModel.ImportGgufModelAsync("C:\\private\\model.gguf");

        Assert.Contains("REKALL_GGUF_OLLAMA_DISCOVERY_FAILED", viewModel.ProviderStatus, StringComparison.Ordinal);
        Assert.DoesNotContain("REKALL_GGUF_TOOL_CAPABILITY_REQUIRED", viewModel.ProviderStatus, StringComparison.Ordinal);
    }

    private sealed class RecordingImporter(
        Func<string, RekallAgeGgufImportResult> import) : IRekallAgeGgufImporter
    {
        public string? LastPath { get; private set; }

        public ValueTask<RekallAgeGgufImportResult> ImportAsync(
            string ggufPath,
            CancellationToken cancellationToken)
        {
            LastPath = ggufPath;
            return ValueTask.FromResult(import(ggufPath));
        }
    }

    private sealed class KimiModelsHandler(IReadOnlyList<string> models) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal("/v1/models", request.RequestUri?.AbsolutePath);
            return Task.FromResult(JsonResponse(new JsonObject
            {
                ["data"] = new JsonArray(models.Select(model => new JsonObject { ["id"] = model }).ToArray())
            }.ToJsonString()));
        }
    }

    private sealed class OllamaModelsHandler(
        IReadOnlyList<string> models,
        Func<string, bool>? supportsTools = null) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/api/tags")
            {
                return JsonResponse(new JsonObject
                {
                    ["models"] = new JsonArray(models.Select(model => new JsonObject
                    {
                        ["name"] = model,
                        ["size"] = 1
                    }).ToArray())
                }.ToJsonString());
            }
            if (request.RequestUri.AbsolutePath == "/api/show")
            {
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
                var model = body["model"]!.GetValue<string>();
                return JsonResponse(supportsTools?.Invoke(model) is false
                    ? "{\"capabilities\":[\"completion\"]}"
                    : "{\"capabilities\":[\"completion\",\"tools\"]}");
            }
            throw new InvalidOperationException(request.RequestUri.AbsolutePath);
        }
    }

    private sealed class OllamaFailAfterFirstDiscoveryHandler : HttpMessageHandler
    {
        private int _tagRequests;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/api/tags" && Interlocked.Increment(ref _tagRequests) > 1)
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("private provider output", Encoding.UTF8, "text/plain")
                };
            }
            if (request.RequestUri.AbsolutePath == "/api/tags")
            {
                return JsonResponse("{\"models\":[{\"name\":\"qwen3.8:27b\",\"size\":1}]}");
            }
            if (request.RequestUri.AbsolutePath == "/api/show")
            {
                _ = await request.Content!.ReadAsStringAsync(cancellationToken);
                return JsonResponse("{\"capabilities\":[\"completion\",\"tools\"]}");
            }
            throw new InvalidOperationException(request.RequestUri.AbsolutePath);
        }
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class UnavailablePreview : IRekallAgeStudioPreviewSession
    {
        public RekallAgeStudioViewportMetrics Metrics { get; } = new(1, 1, 1, 1, true);
        public bool IsDisposalComplete { get; private set; }
        public ValueTask<RekallAgeStudioPreviewFrame> ResetAsync(string projectRoot, string sceneName, int width, int height, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<RekallAgeStudioPreviewFrame> StepAsync(int frameCount, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<RekallAgeStudioPreviewFrame> PresentCurrentAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask ResizeAsync(int width, int height, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask<bool> PollExternalDependencyChangesAsync(CancellationToken cancellationToken) => ValueTask.FromResult(false);
        public ValueTask ClearAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync()
        {
            IsDisposalComplete = true;
            return ValueTask.CompletedTask;
        }
    }
}
