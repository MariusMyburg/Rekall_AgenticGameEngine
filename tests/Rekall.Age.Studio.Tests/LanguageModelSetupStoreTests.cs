using System.IO;
using System.Text.Json;
using Rekall.Age.Studio;

namespace Rekall.Age.Studio.Tests;

public sealed class LanguageModelSetupStoreTests
{
    [Fact]
    public async Task LoadAsyncReturnsIncompleteWhenNoSetupFileExists()
    {
        await using var directory = new TemporaryDirectory();

        var loaded = await new RekallAgeStudioLanguageModelSetupStore(directory.File("setup.json"))
            .LoadAsync(CancellationToken.None);

        Assert.Equal(RekallAgeStudioLanguageModelSetup.Incomplete, loaded);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"version\":2,\"isComplete\":true}")]
    [InlineData("{\"version\":1,\"isComplete\":true,\"providerId\":\"unknown\",\"modelId\":\"model\",\"reasoningEffort\":\"high\",\"readinessVersion\":1}")]
    public async Task LoadAsyncReturnsIncompleteForCorruptFutureOrUnsupportedState(string document)
    {
        await using var directory = new TemporaryDirectory();
        var path = directory.File("setup.json");
        await File.WriteAllTextAsync(path, document);

        var loaded = await new RekallAgeStudioLanguageModelSetupStore(path).LoadAsync(CancellationToken.None);

        Assert.Equal(RekallAgeStudioLanguageModelSetup.Incomplete, loaded);
    }

    [Fact]
    public async Task SaveAsyncRoundTripsACompletedValidatedSetupWithoutSecrets()
    {
        await using var directory = new TemporaryDirectory();
        var path = directory.File("setup.json");
        var setup = CompletedSetup();
        var store = new RekallAgeStudioLanguageModelSetupStore(path);

        await store.SaveAsync(setup, CancellationToken.None);
        var serialized = await File.ReadAllTextAsync(path);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(setup, loaded);
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
        Assert.DoesNotContain("key", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("authorization", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("test-secret-8b5d24d9", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveAsyncPreservesAnIncompleteDismissalForTheNextLaunch()
    {
        await using var directory = new TemporaryDirectory();
        var setup = RekallAgeStudioLanguageModelSetup.Incomplete with
        {
            ProviderId = "gguf",
            ModelId = "local-architect",
            ReasoningEffort = "medium"
        };
        var store = new RekallAgeStudioLanguageModelSetupStore(directory.File("setup.json"));

        await store.SaveAsync(setup, CancellationToken.None);

        Assert.Equal(setup, await store.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task LoadAsyncRejectsInvalidModelReasoningAndEndpointValues()
    {
        await using var directory = new TemporaryDirectory();
        var path = directory.File("setup.json");
        var invalid = CompletedSetup() with
        {
            ModelId = " ",
            ReasoningEffort = "unbounded",
            OllamaUrl = "file:///private-model",
            OpenAiUrl = "relative/path"
        };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(invalid));

        var loaded = await new RekallAgeStudioLanguageModelSetupStore(path).LoadAsync(CancellationToken.None);

        Assert.Equal(RekallAgeStudioLanguageModelSetup.Incomplete, loaded);
    }

    [Fact]
    public async Task DefaultStoreUsesOnlyAnAbsoluteSetupRootOverride()
    {
        await using var directory = new TemporaryDirectory();
        var previous = Environment.GetEnvironmentVariable("REKALL_AGE_STUDIO_SETUP_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("REKALL_AGE_STUDIO_SETUP_ROOT", "relative-root");
            Assert.DoesNotContain("relative-root", new RekallAgeStudioLanguageModelSetupStore().Path, StringComparison.OrdinalIgnoreCase);

            Environment.SetEnvironmentVariable("REKALL_AGE_STUDIO_SETUP_ROOT", directory.Path);
            var store = new RekallAgeStudioLanguageModelSetupStore();
            await store.SaveAsync(RekallAgeStudioLanguageModelSetup.Incomplete, CancellationToken.None);

            Assert.Equal(directory.File("language-model-setup-v1.json"), store.Path);
            Assert.True(File.Exists(directory.File("language-model-setup-v1.json")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("REKALL_AGE_STUDIO_SETUP_ROOT", previous);
        }
    }

    private static RekallAgeStudioLanguageModelSetup CompletedSetup() => new(
        Version: RekallAgeStudioLanguageModelSetup.CurrentVersion,
        IsComplete: true,
        ProviderId: "ollama",
        ModelId: "qwen3.8:27b",
        ReasoningEffort: "high",
        OllamaUrl: "http://127.0.0.1:11434",
        OpenAiUrl: null,
        KimiUrl: null,
        LastSuccessfulCheckUtc: new DateTimeOffset(2026, 8, 30, 8, 0, 0, TimeSpan.Zero),
        ReadinessVersion: RekallAgeStudioLanguageModelSetup.CurrentReadinessVersion);

    private sealed class TemporaryDirectory : IAsyncDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rekall-age-setup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string File(string name) => System.IO.Path.Combine(Path, name);

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
            return ValueTask.CompletedTask;
        }
    }
}
