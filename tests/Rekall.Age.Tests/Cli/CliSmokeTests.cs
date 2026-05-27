using System.Diagnostics;
using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.World;
using Rekall.Age.World.Commands;

namespace Rekall.Age.Tests.Cli;

public sealed class CliSmokeTests
{
    [Fact]
    public async Task CliCreatesGenericProjectAndDoesNotExposeGameTemplates()
    {
        var root = TestPaths.CreateTempDirectory();
        var cliAssembly = FindCliAssemblyPath();

        var templates = await RunAsync(cliAssembly, "templates", "list");
        Assert.Equal(2, templates.ExitCode);

        var engine = await RunAsync(cliAssembly, "context", "engine");
        Assert.Equal(0, engine.ExitCode);
        Assert.Contains("Rekall AGE", engine.Output);
        Assert.Contains("Agent-first: True", engine.Output);
        Assert.DoesNotContain("template", engine.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rekall.workflow.agent_authoring_gauntlet", engine.Output);
        Assert.Contains("Authoring contracts:", engine.Output);
        Assert.Contains("IRekallAgeRuntimeModuleSystem", engine.Output);
        Assert.Contains("RekallAgeRuntimeRenderMesh", engine.Output);

        var create = await RunAsync(cliAssembly, "project", "create", root, "Crystal Mines", "world,rendering3d");
        Assert.Equal(0, create.ExitCode);
        Assert.Contains("Created Rekall AGE project", create.Output);

        var scene = await RunAsync(cliAssembly, "scene", "create", root, "Main", "world,rendering3d");
        Assert.Equal(0, scene.ExitCode);

        var cube = await RunAsync(cliAssembly, "geometry", "primitive", "create", root, "Main", "PuzzleGrid", "cube", "0", "0", "0", "#8ab4f8");
        Assert.Equal(0, cube.ExitCode);

        var sceneStore = new RekallAgeSceneStore();
        var authoredScene = await sceneStore.LoadAsync(root, "Main", CancellationToken.None);
        await sceneStore.SaveAsync(
            root,
            authoredScene.AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject { ["active"] = true }))),
            CancellationToken.None);

        var validation = await RunAsync(cliAssembly, "validation", "scene", root, "Main");
        Assert.Equal(0, validation.ExitCode);
        Assert.Contains("Status: ok", validation.Output);

        var summary = await RunAsync(cliAssembly, "context", "summary", root);
        Assert.Equal(0, summary.ExitCode);
        Assert.Contains("Crystal Mines: ok", summary.Output);

        var run = await RunAsync(cliAssembly, "run", "scene", root, "Main", "0.1");
        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Simulated Main", run.Output);
        Assert.Contains("Camera3D", run.Output);

        var sceneSummary = await RunAsync(cliAssembly, "context", "scene", root, "Main");
        Assert.Equal(0, sceneSummary.ExitCode);
        Assert.Contains("Scene Main", sceneSummary.Output);
        Assert.Contains("PuzzleGrid", sceneSummary.Output);

        var schemas = await RunAsync(cliAssembly, "module", "schemas");
        Assert.Equal(0, schemas.ExitCode);
        Assert.Contains("Loaded", schemas.Output);
        Assert.Contains("Transform", schemas.Output);

        var scaffold = await RunAsync(cliAssembly, "module", "scaffold", root, "crystal.mining", "Crystal Mining", "CrystalMining", "MiningController");
        Assert.Equal(0, scaffold.ExitCode);
        Assert.Contains("CrystalMiningModule.cs", scaffold.Output);
        Assert.True(File.Exists(Path.Combine(root, "Modules", "CrystalMining", "CrystalMiningModule.cs")));

        var build = await RunAsync(cliAssembly, "build", "modules", root);
        Assert.Equal(0, build.ExitCode);
        Assert.Contains("Built 1 module project", build.Output);
        Assert.Contains("CrystalMining.dll", build.Output);

        var capture = await RunAsync(cliAssembly, "capture", "screenshot", root, "Main");
        Assert.Equal(0, capture.ExitCode);
        Assert.Contains("Main_preview.png", capture.Output);
    }

    [Fact]
    public async Task CliDoesNotExposeTemplatePlayableCreationRoutes()
    {
        var root = TestPaths.CreateTempDirectory();
        var cliAssembly = FindCliAssemblyPath();

        var createPlayable = await RunAsync(cliAssembly, "game", "create-playable", root, "CLI Game", "demo");
        Assert.Equal(2, createPlayable.ExitCode);

        var createFromTemplate = await RunAsync(cliAssembly, "game", "create", root, "CLI Game", "demo");
        Assert.Equal(2, createFromTemplate.ExitCode);

        var oneShot = await RunAsync(cliAssembly, "game", "create-package-playable", root, "CLI Game", "demo", Path.Combine(root, "Package"));
        Assert.Equal(2, oneShot.ExitCode);

        var gauntlet = await RunAsync(cliAssembly, "game", "gauntlet", root, "CLI Game", Path.Combine(root, "Package"));
        Assert.Equal(0, gauntlet.ExitCode);
        Assert.Contains("Agent authoring gauntlet ready: True", gauntlet.Output);
        Assert.Contains("Package archive:", gauntlet.Output);
        Assert.Contains("Proof frame:", gauntlet.Output);
        Assert.DoesNotContain("template", gauntlet.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CliReportsInvalidPlaytestJsonWithoutUnhandledException()
    {
        var root = TestPaths.CreateTempDirectory();
        var cliAssembly = FindCliAssemblyPath();

        var playtest = await RunAsync(cliAssembly, "playtest", "scene", root, "Main", "1", "[{verticalAxis:1}]", "[]");

        Assert.Equal(1, playtest.ExitCode);
        Assert.Contains("JSON is invalid", playtest.Output);
        Assert.DoesNotContain("Unhandled exception", playtest.Output);
    }

    [Fact]
    public async Task CliLogsHandledCommandFailuresToSerilogFile()
    {
        var missingRoot = Path.Combine(TestPaths.CreateTempDirectory(), "missing");
        var logDirectory = Path.Combine(TestPaths.CreateTempDirectory(), "cli-logs");
        var cliAssembly = FindCliAssemblyPath();

        var result = await RunAsync(
            cliAssembly,
            new Dictionary<string, string> { ["REKALL_AGE_CLI_LOG_DIR"] = logDirectory },
            "context",
            "summary",
            missingRoot);

        Assert.Equal(1, result.ExitCode);
        var logFile = Assert.Single(Directory.GetFiles(logDirectory, "cli-*.log"));
        var log = await File.ReadAllTextAsync(logFile);
        Assert.Contains("CLI command failed.", log);
        Assert.Contains("context summary", log);
        Assert.Contains(missingRoot, log);
    }

    [Fact]
    public async Task CliPersistsSuccessfulProjectTransactions()
    {
        var root = TestPaths.CreateTempDirectory();
        var cliAssembly = FindCliAssemblyPath();

        var create = await RunAsync(cliAssembly, "project", "create", root, "Transaction CLI", "world");

        Assert.Equal(0, create.ExitCode);
        var log = await new RekallAgeTransactionLogStore().LoadAsync(root, CancellationToken.None);
        var transaction = Assert.Single(log.Transactions);
        Assert.Contains("project create", transaction.Name, StringComparison.Ordinal);
        Assert.Contains(
            transaction.ChangedResources,
            resource => resource.EndsWith("rekall.project.json", StringComparison.Ordinal));

        var history = await RunAsync(cliAssembly, "transaction", "history", root);
        Assert.Equal(0, history.ExitCode);
        Assert.Contains("project create", history.Output);
        Assert.Contains("cli", history.Output);
        Assert.Contains("rekall.project.json", history.Output);
        Assert.Contains("project-manifest file", history.Output);
    }

    [Fact]
    public async Task CliRestoresTransactionPreimage()
    {
        var root = TestPaths.CreateTempDirectory();
        await File.WriteAllTextAsync(Path.Combine(root, "rekall.project.json"), "{}", CancellationToken.None);
        var cliAssembly = FindCliAssemblyPath();
        var sceneStore = new RekallAgeSceneStore();
        var entity = RekallAgeEntityDocument.Create("Player", ["player"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Game.PlayerController",
                new JsonObject
                {
                    ["speed"] = 4,
                    ["health"] = 3
                }));
        await sceneStore.SaveAsync(root, RekallAgeSceneDocument.Create("Main", ["2d"]).AddEntity(entity), CancellationToken.None);
        var mutateContext = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("set speed"), CancellationToken.None);
        await new SetComponentPropertyCommand().ExecuteAsync(
            new SetComponentPropertyRequest(
                root,
                "Main",
                entity.Id,
                "Game.PlayerController",
                "speed",
                JsonValue.Create(7)!),
            mutateContext);
        await new RekallAgeTransactionLogStore().AppendAsync(root, mutateContext.Transaction, mutateContext.Actor, CancellationToken.None);

        var restore = await RunAsync(
            cliAssembly,
            "transaction",
            "restore-preimage",
            root,
            mutateContext.Transaction.Id,
            Path.Combine("Scenes", "Main.age.scene.json"));

        Assert.Equal(0, restore.ExitCode);
        Assert.Contains("Restored", restore.Output);
        var restored = await sceneStore.LoadAsync(root, "Main", CancellationToken.None);
        var component = Assert.Single(restored.Entities.Single().Components);
        Assert.Equal(4, component.Properties["speed"]!.GetValue<int>());
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string cliAssembly, params string[] args)
    {
        return await RunAsync(cliAssembly, null, args);
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(
        string cliAssembly,
        IReadOnlyDictionary<string, string>? environment,
        params string[] args)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        if (environment is not null)
        {
            foreach (var item in environment)
            {
                startInfo.Environment[item.Key] = item.Value;
            }
        }

        startInfo.ArgumentList.Add(cliAssembly);
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)!;
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await outputTask + await errorTask;
        return (process.ExitCode, output);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Rekall.AGE.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find Rekall.AGE.sln from the test output directory.");
    }

    private static string FindCliAssemblyPath()
    {
        var cliAssembly = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "Rekall.Age.Cli",
            "bin",
            "Debug",
            "net10.0",
            "Rekall.Age.Cli.dll");
        if (File.Exists(cliAssembly))
        {
            return cliAssembly;
        }

        throw new InvalidOperationException($"Could not find built CLI assembly at '{cliAssembly}'.");
    }
}
