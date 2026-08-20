using System.IO;
using System.Text.Json.Nodes;
using Rekall.Age.Agent.LanguageModels;
using Rekall.Age.Studio;
using Rekall.Age.World;

namespace Rekall.Age.Studio.Tests;

public sealed class StudioViewModelTests
{
    [Fact]
    public void AutomationArgumentsRequireExplicitBoundedInputs()
    {
        var parsed = RekallAgeStudioAutomation.TryParse(
            [
                RekallAgeStudioAutomation.AutomationSwitch,
                "--project", "game",
                "--project-name", "Game",
                "--scene", "Main",
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
        Assert.False(options.TreatGauntletAsTerminalSuccess);
        Assert.Equal(40, options.MaxTurns);
        Assert.False(RekallAgeStudioAutomation.TryParse(
            [RekallAgeStudioAutomation.AutomationSwitch, "--project", "game"],
            out _, out var missing));
        Assert.Contains("--model", missing, StringComparison.Ordinal);
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

            Assert.True(result.Succeeded, result.Status + Environment.NewLine + string.Join(Environment.NewLine, result.AgentTranscript));
            Assert.True(result.NonblankViewport);
            Assert.True(result.ViewportRenderableCount > 0);
            Assert.True(File.Exists(result.PackageArchivePath));
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
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            var evidenceRoot = Path.GetDirectoryName(evidence)!;
            if (Directory.Exists(evidenceRoot)) Directory.Delete(evidenceRoot, recursive: true);
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
                    TreatGauntletAsTerminalSuccess = false,
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

    private static Task ExecuteAsync(System.Windows.Input.ICommand command) =>
        ((RekallAgeAsyncCommand)command).ExecuteAsync(null);

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
                new RekallAgeLanguageModelUsage(100, 10, 1)));
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
}
