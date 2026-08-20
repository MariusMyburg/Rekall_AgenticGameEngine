using System.Text.Json;
using System.Diagnostics;
using Rekall.Age.Build.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.ModuleHost;
using Rekall.Age.Modules;
using Rekall.Age.Modules.Commands;
using Rekall.Age.Modules.Hosting;
using Rekall.Age.Modules.Security;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Tests.Modules;

public sealed class ModuleHostServerTests
{
    private static readonly Lazy<Task<string>> RuntimePlan = new(CreateRuntimeFixtureLoadPlanAsync);
    private static readonly Lazy<Task<string>> PlayablePlan = new(CreatePlayableFixtureLoadPlanAsync);

    [Fact]
    public async Task InitializeDiscoversVerifiedSystemsAndSchemasThenShutsDown()
    {
        var planPath = await RuntimePlan.Value;
        await using var input = new MemoryStream();
        var writer = new RekallAgeModuleHostFrameCodec();
        await writer.WriteAsync(
            input,
            RekallAgeModuleHostEnvelope.Request(
                1,
                RekallAgeModuleHostOperations.Initialize,
                new RekallAgeModuleHostInitializeRequest(planPath)),
            CancellationToken.None);
        await writer.WriteAsync(
            input,
            RekallAgeModuleHostEnvelope.Request(2, RekallAgeModuleHostOperations.Shutdown, new { }),
            CancellationToken.None);
        input.Position = 0;
        await using var output = new MemoryStream();
        await using var error = new MemoryStream();

        await new RekallAgeModuleHostServer().RunAsync(input, output, error, CancellationToken.None);

        output.Position = 0;
        var reader = new RekallAgeModuleHostFrameCodec();
        var initializedEnvelope = await reader.ReadAsync(output, CancellationToken.None);
        var shutdownEnvelope = await reader.ReadAsync(output, CancellationToken.None);
        var initialized = initializedEnvelope.DeserializePayload<RekallAgeModuleHostInitializeResponse>();
        Assert.True(initializedEnvelope.Ok is true);
        Assert.Equal(RekallAgeModuleHostProtocol.Version, initialized.ProtocolVersion);
        Assert.Equal(RekallAgeModuleTrustPostures.WindowsAppContainerRestricted, initialized.TrustPosture);
        var system = Assert.Single(initialized.Systems);
        Assert.Equal("FixtureSystem", system.Id);
        Assert.Equal(7, system.Priority);
        Assert.Equal("agent.host-fixture", system.ModuleId);
        Assert.Contains(initialized.ComponentSchemas, component =>
            component.TypeName == "Game.Modules.HostFixture.FixtureComponent");
        Assert.Null(initialized.PlayableKind);
        Assert.True(shutdownEnvelope.Ok is true);
        Assert.Equal(0, error.Length);
    }

    [Fact]
    public async Task RuntimeUpdateExecutesExactlyTheNamedPersistentSystem()
    {
        var requests = new[]
        {
            RekallAgeModuleHostEnvelope.Request(1, RekallAgeModuleHostOperations.Initialize,
                new RekallAgeModuleHostInitializeRequest(await RuntimePlan.Value)),
            RekallAgeModuleHostEnvelope.Request(2, RekallAgeModuleHostOperations.RuntimeUpdate,
                new RekallAgeModuleHostRuntimeUpdateRequest(
                    "FixtureSystem",
                    EmptyWorld(),
                    4,
                    TimeSpan.FromSeconds(0.25),
                    TimeSpan.FromSeconds(1),
                    RekallAgeRuntimeInputState.Empty)),
            RekallAgeModuleHostEnvelope.Request(3, RekallAgeModuleHostOperations.Shutdown, new { })
        };

        var responses = await RunAsync(requests);

        var updated = responses[1].DeserializePayload<RekallAgeModuleHostRuntimeUpdateResponse>();
        Assert.True(responses[1].Ok is true);
        Assert.Equal(4, updated.World.FrameIndex);
    }

    [Fact]
    public async Task PlayableStatePersistsAcrossCreateTickAndRender()
    {
        var requests = new[]
        {
            RekallAgeModuleHostEnvelope.Request(1, RekallAgeModuleHostOperations.Initialize,
                new RekallAgeModuleHostInitializeRequest(await PlayablePlan.Value)),
            RekallAgeModuleHostEnvelope.Request(2, RekallAgeModuleHostOperations.PlayableCreate,
                new RekallAgeModuleHostPlayableCreateRequest(new RekallAgePlayableModuleContext("Main", ["Actor"]))),
            RekallAgeModuleHostEnvelope.Request(3, RekallAgeModuleHostOperations.PlayableTick,
                new RekallAgeModuleHostPlayableTickRequest(new RekallAgePlayableModuleInput(DeltaSeconds: 1.0 / 60.0))),
            RekallAgeModuleHostEnvelope.Request(4, RekallAgeModuleHostOperations.PlayableTick,
                new RekallAgeModuleHostPlayableTickRequest(new RekallAgePlayableModuleInput(DeltaSeconds: 1.0 / 60.0))),
            RekallAgeModuleHostEnvelope.Request(5, RekallAgeModuleHostOperations.PlayableRender, new { }),
            RekallAgeModuleHostEnvelope.Request(6, RekallAgeModuleHostOperations.Shutdown, new { })
        };

        var responses = await RunAsync(requests);

        var initialized = responses[0].DeserializePayload<RekallAgeModuleHostInitializeResponse>();
        var rendered = responses[4].DeserializePayload<RekallAgeModuleHostPlayableRenderResponse>();
        Assert.Equal("agent-authored", initialized.PlayableKind);
        Assert.Equal("agent-authored", responses[1].DeserializePayload<RekallAgeModuleHostPlayableCreateResponse>().Kind);
        Assert.Contains("Scene Main", rendered.Frame.Text);
        Assert.Contains("Frame 2", rendered.Frame.Text);
    }

    [Fact]
    public async Task CallsBeforeInitializationAndDuplicateInitializationFailClosed()
    {
        var beforeInitialize = await RunResultAsync(
        [
            RekallAgeModuleHostEnvelope.Request(1, RekallAgeModuleHostOperations.Shutdown, new { })
        ]);
        var duplicateInitialize = await RunResultAsync(
        [
            RekallAgeModuleHostEnvelope.Request(1, RekallAgeModuleHostOperations.Initialize,
                new RekallAgeModuleHostInitializeRequest(await RuntimePlan.Value)),
            RekallAgeModuleHostEnvelope.Request(2, RekallAgeModuleHostOperations.Initialize,
                new RekallAgeModuleHostInitializeRequest(await RuntimePlan.Value))
        ]);

        Assert.Equal(1, beforeInitialize.ExitCode);
        Assert.Equal("REKALL_MODULE_HOST_PROTOCOL_INVALID", Assert.Single(beforeInitialize.Responses).Error!.Code);
        Assert.Equal(1, duplicateInitialize.ExitCode);
        Assert.True(duplicateInitialize.Responses[0].Ok is true);
        Assert.Equal("REKALL_MODULE_HOST_PROTOCOL_INVALID", duplicateInitialize.Responses[1].Error!.Code);
    }

    [Fact]
    public async Task UnknownSystemAndModuleExceptionReturnBoundedFailureWithoutStack()
    {
        var unknown = await RunResultAsync(
        [
            RekallAgeModuleHostEnvelope.Request(1, RekallAgeModuleHostOperations.Initialize,
                new RekallAgeModuleHostInitializeRequest(await RuntimePlan.Value)),
            RekallAgeModuleHostEnvelope.Request(2, RekallAgeModuleHostOperations.RuntimeUpdate,
                new RekallAgeModuleHostRuntimeUpdateRequest(
                    "MissingSystem", EmptyWorld(), 1, TimeSpan.Zero, TimeSpan.Zero, RekallAgeRuntimeInputState.Empty))
        ]);
        var throwing = await RunResultAsync(
        [
            RekallAgeModuleHostEnvelope.Request(1, RekallAgeModuleHostOperations.Initialize,
                new RekallAgeModuleHostInitializeRequest(await RuntimePlan.Value)),
            RekallAgeModuleHostEnvelope.Request(2, RekallAgeModuleHostOperations.RuntimeUpdate,
                new RekallAgeModuleHostRuntimeUpdateRequest(
                    "FixtureSystem", EmptyWorld(), 99, TimeSpan.Zero, TimeSpan.Zero, RekallAgeRuntimeInputState.Empty))
        ]);

        Assert.Equal("REKALL_MODULE_HOST_OUTPUT_INVALID", unknown.Responses[1].Error!.Code);
        var failure = throwing.Responses[1].Error!;
        Assert.Equal("REKALL_MODULE_HOST_MODULE_REJECTED", failure.Code);
        Assert.Equal("InvalidOperationException", failure.Type);
        Assert.InRange(failure.Message.Length, 1, 1024);
        Assert.DoesNotContain(" at ", failure.Message, StringComparison.Ordinal);
        Assert.Equal(1, throwing.ExitCode);
    }

    [Fact]
    public async Task InitializeProjectsCodedLoadPlanFailureWithoutThrowingOrLoadingCode()
    {
        var root = TestPaths.CreateTempDirectory();
        var planPath = Path.Combine(root, "rekall.module.host-plan.json");
        var incompatible = new RekallAgeModuleHostLoadPlan(
            1,
            RekallAgeModuleHostProtocol.Version,
            RekallAgeModuleTrustPostures.InProcessFullTrust,
            []);
        await File.WriteAllTextAsync(
            planPath,
            JsonSerializer.Serialize(incompatible, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        var result = await RunResultAsync(
        [
            RekallAgeModuleHostEnvelope.Request(
                1,
                RekallAgeModuleHostOperations.Initialize,
                new RekallAgeModuleHostInitializeRequest(planPath))
        ]);

        Assert.Equal(1, result.ExitCode);
        var response = Assert.Single(result.Responses);
        Assert.Equal("REKALL_MODULE_HOST_MODULE_REJECTED", response.Error!.Code);
        Assert.Equal("RekallAgeModuleHostException", response.Error.Type);
        Assert.InRange(response.Error.Message.Length, 1, 1024);
        Assert.Equal(string.Empty, result.StandardError);
    }

    [Fact]
    public async Task SequenceViolationTerminatesWithBoundedStderrAndNoStack()
    {
        var result = await RunResultAsync(
        [
            RekallAgeModuleHostEnvelope.Request(1, RekallAgeModuleHostOperations.Initialize,
                new RekallAgeModuleHostInitializeRequest(await RuntimePlan.Value)),
            RekallAgeModuleHostEnvelope.Request(1, RekallAgeModuleHostOperations.Shutdown, new { })
        ]);

        Assert.Equal(1, result.ExitCode);
        Assert.Single(result.Responses);
        Assert.Contains("REKALL_MODULE_HOST_PROTOCOL_INVALID", result.StandardError, StringComparison.Ordinal);
        Assert.InRange(System.Text.Encoding.UTF8.GetByteCount(result.StandardError), 1, RekallAgeModuleHostProtocol.MaximumStandardErrorBytes);
        Assert.DoesNotContain(" at ", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MalformedPlayableOutputBecomesCodedFailureInsteadOfEscapingSerializer()
    {
        var requests = new List<RekallAgeModuleHostEnvelope>
        {
            RekallAgeModuleHostEnvelope.Request(1, RekallAgeModuleHostOperations.Initialize,
                new RekallAgeModuleHostInitializeRequest(await PlayablePlan.Value)),
            RekallAgeModuleHostEnvelope.Request(2, RekallAgeModuleHostOperations.PlayableCreate,
                new RekallAgeModuleHostPlayableCreateRequest(new RekallAgePlayableModuleContext("Main", [])))
        };
        for (var sequence = 3; sequence <= 5; sequence++)
        {
            requests.Add(RekallAgeModuleHostEnvelope.Request(
                sequence,
                RekallAgeModuleHostOperations.PlayableTick,
                new RekallAgeModuleHostPlayableTickRequest(new RekallAgePlayableModuleInput())));
        }
        requests.Add(RekallAgeModuleHostEnvelope.Request(6, RekallAgeModuleHostOperations.PlayableRender, new { }));

        var result = await RunResultAsync(requests);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(6, result.Responses.Count);
        Assert.Equal("REKALL_MODULE_HOST_OUTPUT_INVALID", result.Responses[^1].Error!.Code);
        Assert.InRange(result.Responses[^1].Error!.Message.Length, 1, 1024);
    }

    [Fact]
    public async Task ExecutableWorkerCompletesFiniteProtocolSessionWithProtocolOnlyStdout()
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "Rekall.Age.ModuleHost.exe");
        Assert.True(File.Exists(executable), executable);
        using var process = Process.Start(new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        })!;
        var codec = new RekallAgeModuleHostFrameCodec();
        await codec.WriteAsync(
            process.StandardInput.BaseStream,
            RekallAgeModuleHostEnvelope.Request(1, RekallAgeModuleHostOperations.Initialize,
                new RekallAgeModuleHostInitializeRequest(await RuntimePlan.Value)),
            CancellationToken.None);
        await codec.WriteAsync(
            process.StandardInput.BaseStream,
            RekallAgeModuleHostEnvelope.Request(2, RekallAgeModuleHostOperations.Shutdown, new { }),
            CancellationToken.None);
        process.StandardInput.Close();
        await using var stdout = new MemoryStream();
        await process.StandardOutput.BaseStream.CopyToAsync(stdout);
        var stderr = await process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await process.WaitForExitAsync(timeout.Token);

        Assert.Equal(0, process.ExitCode);
        Assert.Equal(string.Empty, stderr);
        stdout.Position = 0;
        var reader = new RekallAgeModuleHostFrameCodec();
        Assert.True((await reader.ReadAsync(stdout, CancellationToken.None)).Ok is true);
        Assert.True((await reader.ReadAsync(stdout, CancellationToken.None)).Ok is true);
        Assert.Equal(stdout.Length, stdout.Position);
    }

    [Fact]
    public async Task WorkerLoadPlanRejectsTraversalAndArtifactMutationBeforeAssemblyLoad()
    {
        var traversalRoot = TestPaths.CreateTempDirectory();
        var traversalPath = Path.Combine(traversalRoot, "rekall.module.host-plan.json");
        var traversal = new RekallAgeModuleHostLoadPlan(
            1,
            RekallAgeModuleHostProtocol.Version,
            RekallAgeModuleTrustPostures.WindowsAppContainerRestricted,
            [new RekallAgeModuleHostLoadModule("Escape", "..", "Escape.dll", [])]);
        await File.WriteAllTextAsync(
            traversalPath,
            JsonSerializer.Serialize(traversal, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var traversalError = Assert.Throws<RekallAgeModuleHostException>(() =>
            RekallAgeModuleHostVerifiedAssemblyLoader.Load(traversalPath));

        var tamperedPlanPath = await CreateRuntimeFixtureLoadPlanAsync();
        var tamperedPlan = JsonSerializer.Deserialize<RekallAgeModuleHostLoadPlan>(
            await File.ReadAllTextAsync(tamperedPlanPath),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var tamperedModule = Assert.Single(tamperedPlan.Modules);
        var assemblyPath = Path.Combine(Path.GetDirectoryName(tamperedPlanPath)!, tamperedModule.MainAssembly);
        await using (var stream = new FileStream(assemblyPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            stream.Position = stream.Length - 1;
            var value = stream.ReadByte();
            stream.Position = stream.Length - 1;
            stream.WriteByte((byte)(value ^ 0xff));
        }
        var mutationError = Assert.Throws<RekallAgeModuleHostException>(() =>
            RekallAgeModuleHostVerifiedAssemblyLoader.Load(tamperedPlanPath));

        Assert.Equal("REKALL_MODULE_HOST_MODULE_REJECTED", traversalError.Code);
        Assert.Equal("REKALL_MODULE_HOST_MODULE_REJECTED", mutationError.Code);
    }

    private static async Task<string> CreateRuntimeFixtureLoadPlanAsync()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext(
            "test",
            RekallAgeTransaction.Begin("module host fixture"),
            CancellationToken.None);
        var scaffold = await new ScaffoldRuntimeSystemModuleCommand().ExecuteAsync(
            new ScaffoldRuntimeSystemModuleRequest(
                root,
                "agent.host-fixture",
                "Host Fixture",
                "HostFixture",
                "FixtureComponent",
                "FixtureSystem"),
            context);
        Assert.True(scaffold.Ok, scaffold.Summary);
        var source = await File.ReadAllTextAsync(scaffold.Value.SourcePath);
        source = source.Replace("public int Priority => 0;", "public int Priority => 7;", StringComparison.Ordinal);
        source = source.Replace(
            "return ValueTask.FromResult(updatedWorld);",
            "if (context.FrameIndex == 99) throw new InvalidOperationException(new string('x', 5000));\n        return ValueTask.FromResult(world with { FrameIndex = world.FrameIndex + context.FrameIndex });",
            StringComparison.Ordinal);
        await File.WriteAllTextAsync(scaffold.Value.SourcePath, source);
        var build = await new BuildModulesCommand().ExecuteAsync(new BuildModulesRequest(root), context);
        Assert.True(build.Ok, build.Summary);
        return await WriteLoadPlanAsync(root);
    }

    private static async Task<string> CreatePlayableFixtureLoadPlanAsync()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext(
            "test",
            RekallAgeTransaction.Begin("playable host fixture"),
            CancellationToken.None);
        var scaffold = await new ScaffoldPlayableModuleCommand().ExecuteAsync(
            new ScaffoldPlayableModuleRequest(root, "agent.playable-host", "Playable Host", "PlayableHost"),
            context);
        Assert.True(scaffold.Ok, scaffold.Summary);
        var source = await File.ReadAllTextAsync(scaffold.Value.SourcePath);
        source = source.Replace(
            "return new RekallAgePlayableModuleFrame($\"AGENT PLAYABLE MODULE\\nScene {state.Text[\"scene\"]}\\nFrame {frame}\");",
            "return frame == 3 ? new RekallAgePlayableModuleFrame(\"invalid\", [new RekallAgePlayableDrawCommand(\"rect\", \"bad\", double.NaN, 0, 1, 1, \"#fff\")]) : new RekallAgePlayableModuleFrame($\"AGENT PLAYABLE MODULE\\nScene {state.Text[\"scene\"]}\\nFrame {frame}\");",
            StringComparison.Ordinal);
        await File.WriteAllTextAsync(scaffold.Value.SourcePath, source);
        var build = await new BuildModulesCommand().ExecuteAsync(new BuildModulesRequest(root), context);
        Assert.True(build.Ok, build.Summary);
        return await WriteLoadPlanAsync(root);
    }

    private static async Task<string> WriteLoadPlanAsync(string root)
    {
        var inspection = new RekallAgeProjectModuleTrustInspector().Inspect(root);
        Assert.True(inspection.Ready, string.Join(Environment.NewLine, inspection.Issues.Select(issue => issue.Message)));
        var module = Assert.Single(inspection.Modules);
        var outputRoot = Path.Combine(module.ModuleDirectory, "bin", "rekall", "net10.0");
        var plan = new RekallAgeModuleHostLoadPlan(
            SchemaVersion: 1,
            ProtocolVersion: RekallAgeModuleHostProtocol.Version,
            TrustPosture: RekallAgeModuleTrustPostures.WindowsAppContainerRestricted,
            Modules:
            [
                new RekallAgeModuleHostLoadModule(
                    module.ModuleName,
                    ".",
                    $"{module.ModuleName}.dll",
                    module.OutputFiles)
            ]);
        var planPath = Path.Combine(outputRoot, "rekall.module.host-plan.json");
        await File.WriteAllTextAsync(
            planPath,
            JsonSerializer.Serialize(plan, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return planPath;
    }

    private static async Task<IReadOnlyList<RekallAgeModuleHostEnvelope>> RunAsync(
        IReadOnlyList<RekallAgeModuleHostEnvelope> requests)
    {
        var result = await RunResultAsync(requests);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(requests.Count, result.Responses.Count);
        Assert.Equal(string.Empty, result.StandardError);
        return result.Responses;
    }

    private static async Task<ServerResult> RunResultAsync(
        IReadOnlyList<RekallAgeModuleHostEnvelope> requests)
    {
        await using var input = new MemoryStream();
        var writer = new RekallAgeModuleHostFrameCodec();
        foreach (var request in requests)
        {
            await writer.WriteAsync(input, request, CancellationToken.None);
        }

        input.Position = 0;
        await using var output = new MemoryStream();
        await using var error = new MemoryStream();
        var exitCode = await new RekallAgeModuleHostServer().RunAsync(input, output, error, CancellationToken.None);
        output.Position = 0;
        var reader = new RekallAgeModuleHostFrameCodec();
        var responses = new List<RekallAgeModuleHostEnvelope>();
        while (output.Position < output.Length)
        {
            responses.Add(await reader.ReadAsync(output, CancellationToken.None));
        }

        return new ServerResult(exitCode, responses, System.Text.Encoding.UTF8.GetString(error.ToArray()));
    }

    private static RekallAgeRuntimeWorld EmptyWorld() => new(
        "scene",
        "Main",
        0,
        TimeSpan.Zero,
        [],
        RekallAgeRuntimeSubsystemViews.Empty,
        []);

    private sealed record ServerResult(
        int ExitCode,
        IReadOnlyList<RekallAgeModuleHostEnvelope> Responses,
        string StandardError);
}
