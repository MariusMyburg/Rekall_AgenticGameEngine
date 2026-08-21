using System.Diagnostics;
using Rekall.Age.Build.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modules.Commands;

namespace Rekall.Age.Tests.Build;

public sealed class BuildModulesCommandTests
{
    [Fact]
    public async Task WedgedCompilerTimesOutAndIsTerminatedWithoutReceipt()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("timeout module"),
            CancellationToken.None);
        await new ScaffoldModuleCommand().ExecuteAsync(
            new ScaffoldModuleRequest(root, "timeout.module", "Timeout Module", "TimeoutModule", "TimeoutComponent"),
            context);
        int? compilerProcessId = null;
        Process? StartWedgedCompiler(ProcessStartInfo _)
        {
            var helper = new ProcessStartInfo("pwsh")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            helper.ArgumentList.Add("-NoProfile");
            helper.ArgumentList.Add("-Command");
            helper.ArgumentList.Add("Start-Sleep -Seconds 30");
            var process = Process.Start(helper);
            compilerProcessId = process?.Id;
            return process;
        }
        var command = new BuildModulesCommand(
            TimeSpan.FromMilliseconds(200),
            StartWedgedCompiler);

        var result = await command.ExecuteAsync(new BuildModulesRequest(root), context);

        Assert.False(result.Ok);
        var error = Assert.Single(result.Errors);
        Assert.Equal("REKALL_MODULE_BUILD_TIMEOUT", error.Code);
        var module = Assert.Single(result.Value.Modules);
        Assert.True(module.TimedOut);
        Assert.Equal(-1, module.ExitCode);
        Assert.Empty(module.ReceiptPath);
        Assert.NotNull(compilerProcessId);
        Assert.False(IsProcessAlive(compilerProcessId!.Value));

        static bool IsProcessAlive(int processId)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                return !process.HasExited;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }

    [Fact]
    public async Task ExternalCancellationTerminatesWedgedCompilerAndRemainsCancellation()
    {
        var root = TestPaths.CreateTempDirectory();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var context = new RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("cancel module"),
            cancellation.Token);
        await new ScaffoldModuleCommand().ExecuteAsync(
            new ScaffoldModuleRequest(root, "cancel.module", "Cancel Module", "CancelModule", "CancelComponent"),
            new RekallAgeCommandContext(
                "agent",
                RekallAgeTransaction.Begin("scaffold cancel module"),
                CancellationToken.None));
        int? compilerProcessId = null;
        Process? StartWedgedCompiler(ProcessStartInfo _)
        {
            var helper = new ProcessStartInfo("pwsh")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            helper.ArgumentList.Add("-NoProfile");
            helper.ArgumentList.Add("-Command");
            helper.ArgumentList.Add("Start-Sleep -Seconds 30");
            var process = Process.Start(helper);
            compilerProcessId = process?.Id;
            return process;
        }
        var command = new BuildModulesCommand(TimeSpan.FromSeconds(30), StartWedgedCompiler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await command.ExecuteAsync(new BuildModulesRequest(root), context));

        Assert.NotNull(compilerProcessId);
        Assert.False(IsProcessAlive(compilerProcessId!.Value));

        static bool IsProcessAlive(int processId)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                return !process.HasExited;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }

    [Fact]
    public async Task MissingModuleProjectSuggestsExecutablePlayableScaffold()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("missing modules"),
            CancellationToken.None);

        var result = await new BuildModulesCommand().ExecuteAsync(new BuildModulesRequest(root), context);

        Assert.False(result.Ok);
        var error = Assert.Single(result.Errors, item => item.Code == "REKALL_MODULE_PROJECTS_MISSING");
        var suggestion = Assert.Single(error.SuggestedCommands!);
        Assert.Equal("rekall.module.scaffold_playable", suggestion.Tool);
        Assert.Equal(root, suggestion.Arguments["projectRoot"]);
        Assert.False(string.IsNullOrWhiteSpace((string)suggestion.Arguments["moduleId"]!));
        Assert.False(string.IsNullOrWhiteSpace((string)suggestion.Arguments["displayName"]!));
        Assert.False(string.IsNullOrWhiteSpace((string)suggestion.Arguments["moduleName"]!));
    }

    [Fact]
    public async Task PortableModulesBuildConcurrentlyWithoutSharingEngineOutputs()
    {
        var roots = Enumerable.Range(0, 6).Select(_ => TestPaths.CreateTempDirectory()).ToArray();
        var tasks = roots.Select(async (root, index) =>
        {
            var context = new RekallAgeCommandContext(
                "agent",
                RekallAgeTransaction.Begin("parallel module"),
                CancellationToken.None);
            await new ScaffoldPlayableModuleCommand().ExecuteAsync(
                new ScaffoldPlayableModuleRequest(
                    root,
                    $"parallel.{index}",
                    $"Parallel {index}",
                    $"Parallel{index}"),
                context);
            return await new BuildModulesCommand().ExecuteAsync(new BuildModulesRequest(root), context);
        });

        var results = await Task.WhenAll(tasks);

        Assert.All(results, result => Assert.True(
            result.Ok,
            string.Join(Environment.NewLine, result.Value.Modules.Select(module => module.Output))));
        Assert.All(results.SelectMany(result => result.Value.Modules), module =>
        {
            Assert.Equal(0, module.ExitCode);
            Assert.Equal("1", module.SdkVersion);
            Assert.True(File.Exists(module.AssemblyPath));
        });
    }

    [Fact]
    public async Task BuildModulesCompilesScaffoldedModuleProject()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("build modules"), CancellationToken.None);
        await new ScaffoldModuleCommand().ExecuteAsync(
            new ScaffoldModuleRequest(root, "crystal.mining", "Crystal Mining", "CrystalMining", "MiningController"),
            context);
        var command = new BuildModulesCommand();

        var result = await command.ExecuteAsync(new BuildModulesRequest(root), context);

        Assert.True(result.Ok, result.Summary);
        var module = Assert.Single(result.Value.Modules);
        Assert.Equal("CrystalMining", module.ModuleName);
        Assert.Equal("windows-appcontainer-restricted", module.TrustPosture);
        Assert.True(File.Exists(module.AssemblyPath));
        Assert.EndsWith("CrystalMining.dll", module.AssemblyPath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildModulesCompilesWhenProjectRootIsRelative()
    {
        var parent = TestPaths.CreateTempDirectory();
        var projectRoot = Path.Combine(parent, "relative-game");
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("build modules relative"), CancellationToken.None);
        await new ScaffoldModuleCommand().ExecuteAsync(
            new ScaffoldModuleRequest(projectRoot, "relative.flight", "Relative Flight", "RelativeFlight", "FlightController"),
            context);
        var previous = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = parent;

            var result = await new BuildModulesCommand().ExecuteAsync(
                new BuildModulesRequest("relative-game"),
                context);

            Assert.True(result.Ok, result.Summary);
            var module = Assert.Single(result.Value.Modules);
            Assert.True(Path.IsPathFullyQualified(module.ProjectPath));
            Assert.True(File.Exists(module.AssemblyPath));
        }
        finally
        {
            Environment.CurrentDirectory = previous;
        }
    }

    [Fact]
    public async Task RuntimeSdkCompilerFailureReturnsExactBoundedRepairContract()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("runtime sdk compiler recovery"),
            CancellationToken.None);
        var scaffold = await new ScaffoldRuntimeSystemModuleCommand().ExecuteAsync(
            new ScaffoldRuntimeSystemModuleRequest(
                root,
                "game.motion",
                "Game Motion",
                "GameMotion",
                "OrbitMotion",
                "OrbitMotionSystem"),
            context);
        var source = await File.ReadAllTextAsync(scaffold.Value.SourcePath);
        source = source.Replace(
            "var position = entity.Transform.Position3D;",
            "var position = RekallAgeRuntimeModuleSdk.GetTransform3D(entity);",
            StringComparison.Ordinal);
        await new WriteModuleSourceCommand().ExecuteAsync(
            new WriteModuleSourceRequest(root, "GameMotion", "GameMotionModule.cs", source),
            context);

        var result = await new BuildModulesCommand().ExecuteAsync(
            new BuildModulesRequest(root),
            context);

        Assert.False(result.Ok);
        var error = Assert.Single(result.Errors, item => item.Code == "REKALL_MODULE_BUILD_FAILED");
        Assert.Contains("entity.Transform.Position3D", error.Message, StringComparison.Ordinal);
        Assert.Contains("entity.ComponentNumber", error.Message, StringComparison.Ordinal);
        Assert.Contains("entity.WithPosition3D(new RekallAgeRuntimeVector3", error.Message, StringComparison.Ordinal);
        Assert.Contains("world.UpdateEntity", error.Message, StringComparison.Ordinal);
        Assert.Contains("GetTransform3D", error.Message, StringComparison.Ordinal);
        var inspection = Assert.Single(
            error.SuggestedCommands!,
            command => command.Tool == "rekall.module.inspect_runtime_sdk");
        Assert.False(string.IsNullOrWhiteSpace((string)inspection.Arguments["query"]!));
        Assert.Contains("entity transform", (string)inspection.Arguments["query"]!, StringComparison.OrdinalIgnoreCase);
    }
}
