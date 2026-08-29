using Rekall.Age.Agent.LanguageModels;
using System.Diagnostics;

namespace Rekall.Age.Tests.Agent;

public sealed class OllamaGgufImporterTests
{
    [Fact]
    public async Task ImportsExistingGgufThroughGeneratedModelfileAndStructuredOllamaArguments()
    {
        var root = Path.Combine(Path.GetTempPath(), "RekallAgeGgufTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var gguf = Path.Combine(root, "My Local Model.gguf");
        await File.WriteAllBytesAsync(gguf, [0x47, 0x47, 0x55, 0x46]);
        var runner = new RecordingRunner();
        try
        {
            var importer = new RekallAgeOllamaGgufImporter(runner);

            var result = await importer.ImportAsync(gguf, CancellationToken.None);

            Assert.Matches("^rekall-my-local-model-[0-9a-f]{12}$", result.ModelName);
            Assert.Equal("ollama", runner.Executable);
            Assert.Equal(["create", result.ModelName, "-f", runner.ModelfilePath], runner.Arguments);
            Assert.Contains("FROM \"", runner.ModelfileContents, StringComparison.Ordinal);
            Assert.Contains(Path.GetFullPath(gguf).Replace('\\', '/'), runner.ModelfileContents, StringComparison.Ordinal);
            Assert.False(File.Exists(runner.ModelfilePath));
            Assert.DoesNotContain(gguf, result.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("model.bin")]
    [InlineData("model.txt")]
    public async Task RejectsNonGgufFilesBeforeStartingOllama(string fileName)
    {
        var path = Path.Combine(Path.GetTempPath(), fileName);
        await File.WriteAllTextAsync(path, "not a model");
        var runner = new RecordingRunner();
        try
        {
            var importer = new RekallAgeOllamaGgufImporter(runner);

            var error = await Assert.ThrowsAsync<RekallAgeLanguageModelProviderException>(() =>
                importer.ImportAsync(path, CancellationToken.None).AsTask());

            Assert.Equal("REKALL_GGUF_FILE_INVALID", error.Code);
            Assert.Equal("gguf", error.ProviderId);
            Assert.Equal(0, runner.RunCount);
            Assert.DoesNotContain(path, error.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task OllamaFailureIsStableAndDoesNotExposeLocalPathOrProcessOutput()
    {
        var path = Path.Combine(Path.GetTempPath(), "secret-location-" + Guid.NewGuid().ToString("N") + ".gguf");
        await File.WriteAllTextAsync(path, "GGUF");
        var runner = new RecordingRunner(new RekallAgeOllamaProcessResult(23, "private stdout", "private stderr"));
        try
        {
            var importer = new RekallAgeOllamaGgufImporter(runner);

            var error = await Assert.ThrowsAsync<RekallAgeLanguageModelProviderException>(() =>
                importer.ImportAsync(path, CancellationToken.None).AsTask());

            Assert.Equal("REKALL_GGUF_IMPORT_FAILED", error.Code);
            Assert.Equal("Exit code 23.", error.ProviderDetail);
            Assert.DoesNotContain(path, error.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("private", error.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RejectsRenamedNonGgufContentBeforeStartingOllama()
    {
        var path = Path.Combine(Path.GetTempPath(), "renamed-" + Guid.NewGuid().ToString("N") + ".gguf");
        await File.WriteAllTextAsync(path, "this is not a GGUF file");
        var runner = new RecordingRunner();
        try
        {
            var importer = new RekallAgeOllamaGgufImporter(runner);

            var error = await Assert.ThrowsAsync<RekallAgeLanguageModelProviderException>(() =>
                importer.ImportAsync(path, CancellationToken.None).AsTask());

            Assert.Equal("REKALL_GGUF_FILE_INVALID", error.Code);
            Assert.Equal(0, runner.RunCount);
            Assert.DoesNotContain(path, error.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ProcessRunnerCancellationTerminatesTheChildWithoutDeadlockingPipes()
    {
        var runner = new RekallAgeOllamaProcessRunner();
        var (executable, arguments) = OperatingSystem.IsWindows()
            ? ("cmd.exe", (IReadOnlyList<string>)["/d", "/c", "ping -n 30 127.0.0.1 > nul"])
            : ("/bin/sh", (IReadOnlyList<string>)["-c", "sleep 30"]);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            runner.RunAsync(executable, arguments, cancellation.Token).AsTask());

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Cancellation took {stopwatch.Elapsed}.");
    }

    private sealed class RecordingRunner(
        RekallAgeOllamaProcessResult? result = null) : IRekallAgeOllamaProcessRunner
    {
        public int RunCount { get; private set; }
        public string? Executable { get; private set; }
        public IReadOnlyList<string> Arguments { get; private set; } = [];
        public string ModelfilePath { get; private set; } = string.Empty;
        public string ModelfileContents { get; private set; } = string.Empty;

        public async ValueTask<RekallAgeOllamaProcessResult> RunAsync(
            string executable,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            RunCount++;
            Executable = executable;
            Arguments = arguments.ToArray();
            ModelfilePath = arguments[^1];
            ModelfileContents = await File.ReadAllTextAsync(ModelfilePath, cancellationToken);
            return result ?? new RekallAgeOllamaProcessResult(0, string.Empty, string.Empty);
        }
    }
}
