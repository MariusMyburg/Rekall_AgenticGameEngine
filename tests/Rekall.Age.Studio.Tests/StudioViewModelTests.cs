using System.IO;
using System.Text.Json.Nodes;
using Rekall.Age.Agent.LanguageModels;
using Rekall.Age.Editor;
using Rekall.Age.Rendering;
using Rekall.Age.Studio;
using Rekall.Age.Workflows;
using Rekall.Age.World;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Rekall.Age.Studio.Tests;

public sealed class StudioViewModelTests
{
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
            preview.Regions.Add(new(entity.EntityId, RekallAgeStudioViewportRegionKind.World, 40, 40, 20, 20, 2, 0));

            Assert.True(await viewModel.SelectViewportEntityAsync(200, 100, 100, 50));
            Assert.Equal(entity.EntityId, viewModel.SelectedEntityId);
            Assert.False(await viewModel.SelectViewportEntityAsync(200, 100, 20, 50));
            Assert.Equal(entity.EntityId, viewModel.SelectedEntityId);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ViewModelExposesDistinctEditAndPersistentSimulateModes()
    {
        var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-mode-" + Guid.NewGuid().ToString("N"));
        try
        {
            var preview = new RecordingPreviewSession();
            await using var viewModel = new RekallAgeStudioViewModel(
                new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
                new EmptyModel(),
                preview);
            viewModel.ProjectPathInput = root;
            viewModel.ProjectNameInput = "Mode Test";
            viewModel.SceneNameInput = "Main";
            await ExecuteAsync(viewModel.CreateCommand);

            Assert.Equal(RekallAgeStudioMode.Edit, viewModel.Mode);
            Assert.True(viewModel.SimulateCommand.CanExecute(null));

            await ExecuteAsync(viewModel.SimulateCommand);
            await viewModel.AdvanceLivePreviewAsync();

            Assert.Equal(RekallAgeStudioMode.Simulate, viewModel.Mode);
            Assert.True(viewModel.IsSimulating);
            Assert.False(viewModel.PlayCommand.CanExecute(null));
            Assert.Equal(6, viewModel.PreviewFrameIndex);
            Assert.Equal(2, preview.ResetCount);
            Assert.Equal(1, preview.StepCount);

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
    public async Task StudioStartsWithAnEmptyOrdinaryLanguageAuthoringRequest()
    {
        await using var viewModel = new RekallAgeStudioViewModel(
            new RekallAgeWorkbenchSession(RekallAgeDefaultCommandRegistry.Create()),
            new EmptyModel());

        Assert.Empty(viewModel.AgentTaskInput);
        Assert.False(viewModel.RunAgentCommand.CanExecute(null));
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

            Assert.True(result.Succeeded, result.Status + Environment.NewLine + result.ViewportSummary + Environment.NewLine + string.Join(Environment.NewLine, result.AgentTranscript));
            Assert.True(result.NonblankViewport);
            Assert.True(result.ViewportRenderableCount > 0);
            Assert.NotEmpty(result.AgentToolExecutions);
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

    private sealed class RecordingPreviewSession : IRekallAgeStudioPreviewSession
    {
        private int _frame;
        private TaskCompletionSource? _blockedReset;
        private TaskCompletionSource? _resetEntered;
        private TaskCompletionSource? _disposeBlocked;
        private TaskCompletionSource? _disposeEntered;
        public int ResetCount { get; private set; }
        public int StepCount { get; private set; }
        public List<RekallAgeStudioViewportPickRegion> Regions { get; } = [];

        public ValueTask<RekallAgeStudioPreviewFrame> ResetAsync(
            string projectRoot,
            string sceneName,
            int width,
            int height,
            CancellationToken cancellationToken)
        {
            ResetCount++;
            _frame = 0;
            _resetEntered?.TrySetResult();
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

        public ValueTask DisposeAsync()
        {
            _disposeEntered?.TrySetResult();
            return _disposeBlocked is null ? ValueTask.CompletedTask : new ValueTask(_disposeBlocked.Task);
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
            var image = BitmapSource.Create(
                100, 100, 96, 96, PixelFormats.Bgra32, null, new byte[40_000], 400);
            image.Freeze();
            return new RekallAgeStudioPreviewFrame(
                image, frame, 0, 0, "software-live",
                new RekallAgeStudioViewportInteractionSnapshot(100, 100, Regions));
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
}
