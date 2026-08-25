using System.Security.Cryptography;
using System.Text.Json;
using Rekall.Age.Build.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Product;
using Rekall.Age.Core.Transactions;
using Rekall.Age.ModuleHost;
using Rekall.Age.Modules;
using Rekall.Age.Modules.Commands;
using Rekall.Age.Modules.Hosting;
using Rekall.Age.Modules.Hosting.Windows;
using System.Runtime.Versioning;

namespace Rekall.Age.Tests.Modules;

[SupportedOSPlatform("windows")]
public sealed class ModuleHostWindowsIsolationTests
{
    [Fact]
    public async Task RestrictedClientInitializesAndInvokesPlayableThroughTypedProtocol()
    {
        Assert.True(OperatingSystem.IsWindows());
        var projectRoot = await CreateModuleAsync();
        var hostRoot = await CreateRealHostPayloadAsync();
        await using var client = await RekallAgeRestrictedModuleHostClient.StartAsync(
            projectRoot,
            hostRoot,
            TestPaths.CreateTempDirectory(),
            CancellationToken.None);

        Assert.Equal(RekallAgeModuleHostProtocol.Version, client.Initialization.ProtocolVersion);
        Assert.Equal("windows-appcontainer-restricted", client.Initialization.TrustPosture);
        Assert.Equal("agent-authored", client.Initialization.PlayableKind);
        var created = await client.CreatePlayableAsync(
            new RekallAgePlayableModuleContext("ClientScene", []),
            CancellationToken.None);
        var rendered = await client.RenderPlayableAsync(CancellationToken.None);

        Assert.Equal("agent-authored", created.Kind);
        Assert.Contains("Scene ClientScene", rendered.Frame.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RestrictedClientAllowsBoundedSchedulerJitterWithinRequestDeadline()
    {
        Assert.True(OperatingSystem.IsWindows());
        var projectRoot = await CreateModuleAsync(source => source.Replace(
            "var frame = (int)state.Numbers[\"frame\"];",
            "System.Threading.Thread.Sleep(1200); var frame = (int)state.Numbers[\"frame\"];",
            StringComparison.Ordinal));
        var hostRoot = await CreateRealHostPayloadAsync();
        await using var client = await RekallAgeRestrictedModuleHostClient.StartAsync(
            projectRoot,
            hostRoot,
            TestPaths.CreateTempDirectory(),
            CancellationToken.None);
        await client.CreatePlayableAsync(
            new RekallAgePlayableModuleContext("Jitter", []),
            CancellationToken.None);

        var rendered = await client.RenderPlayableAsync(CancellationToken.None);

        Assert.Contains("Scene Jitter", rendered.Frame.Text, StringComparison.Ordinal);
        Assert.True(client.IsRunning);
    }

    [Fact]
    public async Task RestrictedClientTerminatesHungModuleAtRequestDeadline()
    {
        Assert.True(OperatingSystem.IsWindows());
        var projectRoot = await CreateModuleAsync(source => source.Replace(
            "var frame = (int)state.Numbers[\"frame\"];",
            "System.Threading.Thread.Sleep(5000); var frame = (int)state.Numbers[\"frame\"];",
            StringComparison.Ordinal));
        var hostRoot = await CreateRealHostPayloadAsync();
        await using var client = await RekallAgeRestrictedModuleHostClient.StartAsync(
            projectRoot,
            hostRoot,
            TestPaths.CreateTempDirectory(),
            CancellationToken.None);
        await client.CreatePlayableAsync(
            new RekallAgePlayableModuleContext("Hung", []),
            CancellationToken.None);

        var error = await Assert.ThrowsAsync<RekallAgeModuleHostException>(async () =>
            await client.RenderPlayableAsync(CancellationToken.None));

        Assert.Equal("REKALL_MODULE_HOST_REQUEST_TIMEOUT", error.Code);
        Assert.False(client.IsRunning);
    }

    [Fact]
    public async Task RestrictedClientReportsAbruptWorkerTerminationWithoutModuleDiagnostics()
    {
        Assert.True(OperatingSystem.IsWindows());
        var projectRoot = await CreateModuleAsync(source => source.Replace(
            "var frame = (int)state.Numbers[\"frame\"];",
            "System.Diagnostics.Process.GetCurrentProcess().Kill(); var frame = (int)state.Numbers[\"frame\"];",
            StringComparison.Ordinal));
        var hostRoot = await CreateRealHostPayloadAsync();
        await using var client = await RekallAgeRestrictedModuleHostClient.StartAsync(
            projectRoot,
            hostRoot,
            TestPaths.CreateTempDirectory(),
            CancellationToken.None);
        await client.CreatePlayableAsync(
            new RekallAgePlayableModuleContext("Crash", []),
            CancellationToken.None);

        var error = await Assert.ThrowsAsync<RekallAgeModuleHostException>(async () =>
            await client.RenderPlayableAsync(CancellationToken.None));

        Assert.Equal("REKALL_MODULE_HOST_CRASHED", error.Code);
        Assert.DoesNotContain("System.Diagnostics", error.Message, StringComparison.Ordinal);
        Assert.False(client.IsRunning);
    }

    [Fact]
    public async Task AppContainerWorkerDoesNotInheritBrokerSecrets()
    {
        Assert.True(OperatingSystem.IsWindows());
        const string variable = "REKALL_MODULE_HOST_TEST_SECRET";
        const string secret = "must-not-cross-the-boundary";
        var previous = Environment.GetEnvironmentVariable(variable);
        Environment.SetEnvironmentVariable(variable, secret);
        try
        {
            var projectRoot = await CreateModuleAsync(source => source.Replace(
                "return new RekallAgePlayableModuleFrame($\"AGENT PLAYABLE MODULE\\nScene {state.Text[\"scene\"]}\\nFrame {frame}\");",
                $"return new RekallAgePlayableModuleFrame(System.Environment.GetEnvironmentVariable(\"{variable}\") ?? \"absent\");",
                StringComparison.Ordinal));
            var responses = await RunRestrictedWorkerAsync(
                projectRoot,
                [
                    RekallAgeModuleHostEnvelope.Request(
                        1,
                        RekallAgeModuleHostOperations.Initialize,
                        new RekallAgeModuleHostInitializeRequest("$LOAD_PLAN")),
                    RekallAgeModuleHostEnvelope.Request(
                        2,
                        RekallAgeModuleHostOperations.PlayableCreate,
                        new RekallAgeModuleHostPlayableCreateRequest(new RekallAgePlayableModuleContext("Main", []))),
                    RekallAgeModuleHostEnvelope.Request(3, RekallAgeModuleHostOperations.PlayableRender, new { }),
                    RekallAgeModuleHostEnvelope.Request(4, RekallAgeModuleHostOperations.Shutdown, new { })
                ]);

            Assert.All(responses, response => Assert.True(response.Ok, response.Error?.Message));
            Assert.Equal(
                "absent",
                responses[2].DeserializePayload<RekallAgeModuleHostPlayableRenderResponse>().Frame.Text);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }

    [Fact]
    public async Task AppContainerWorkerCannotReadUnstagedFilesStartChildrenOrUseNetwork()
    {
        Assert.True(OperatingSystem.IsWindows());
        var privateRoot = TestPaths.CreateTempDirectory();
        var privateFile = Path.Combine(privateRoot, "broker-secret.txt");
        await File.WriteAllTextAsync(privateFile, "outside-stage-secret");
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        var escapedPrivateFile = privateFile.Replace("\\", "\\\\", StringComparison.Ordinal);
        var projectRoot = await CreateModuleAsync(source => source.Replace(
            "return new RekallAgePlayableModuleFrame($\"AGENT PLAYABLE MODULE\\nScene {state.Text[\"scene\"]}\\nFrame {frame}\");",
            $$"""
            var fileDenied = false;
                    try { _ = System.IO.File.ReadAllText("{{escapedPrivateFile}}"); } catch { fileDenied = true; }
                    var writeDenied = false;
                    try { System.IO.File.WriteAllText("{{escapedPrivateFile}}", "tampered"); } catch { writeDenied = true; }
                    var processDenied = false;
                    try
                    {
                        using var child = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = System.Environment.GetEnvironmentVariable("ComSpec")!,
                            Arguments = "/c exit 0",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        });
                    }
                    catch { processDenied = true; }
                    var networkDenied = false;
                    try
                    {
                        using var client = new System.Net.Sockets.TcpClient();
                        client.Connect("127.0.0.1", {{port}});
                    }
                    catch { networkDenied = true; }
                    return new RekallAgePlayableModuleFrame($"file={(fileDenied ? "denied" : "ALLOWED")};write={(writeDenied ? "denied" : "ALLOWED")};process={(processDenied ? "denied" : "ALLOWED")};network={(networkDenied ? "denied" : "ALLOWED")}");
            """,
            StringComparison.Ordinal));
        var responses = await RunRestrictedWorkerAsync(
            projectRoot,
            [
                RekallAgeModuleHostEnvelope.Request(
                    1,
                    RekallAgeModuleHostOperations.Initialize,
                    new RekallAgeModuleHostInitializeRequest("$LOAD_PLAN")),
                RekallAgeModuleHostEnvelope.Request(
                    2,
                    RekallAgeModuleHostOperations.PlayableCreate,
                    new RekallAgeModuleHostPlayableCreateRequest(new RekallAgePlayableModuleContext("Main", []))),
                RekallAgeModuleHostEnvelope.Request(3, RekallAgeModuleHostOperations.PlayableRender, new { }),
                RekallAgeModuleHostEnvelope.Request(4, RekallAgeModuleHostOperations.Shutdown, new { })
            ]);

        Assert.All(responses, response => Assert.True(response.Ok, response.Error?.Message));
        Assert.Equal(
            "file=denied;write=denied;process=denied;network=denied",
            responses[2].DeserializePayload<RekallAgeModuleHostPlayableRenderResponse>().Frame.Text);
        Assert.Equal("outside-stage-secret", await File.ReadAllTextAsync(privateFile));
    }

    [Fact]
    public async Task AppContainerWorkerDrainsAndBoundsExcessiveStandardError()
    {
        Assert.True(OperatingSystem.IsWindows());
        var projectRoot = await CreateModuleAsync(source => source.Replace(
            "var frame = (int)state.Numbers[\"frame\"];",
            "System.Console.Error.Write(new string('x', 256 * 1024)); var frame = (int)state.Numbers[\"frame\"];",
            StringComparison.Ordinal));
        var result = await RunRestrictedWorkerWithDiagnosticsAsync(
            projectRoot,
            [
                RekallAgeModuleHostEnvelope.Request(
                    1,
                    RekallAgeModuleHostOperations.Initialize,
                    new RekallAgeModuleHostInitializeRequest("$LOAD_PLAN")),
                RekallAgeModuleHostEnvelope.Request(
                    2,
                    RekallAgeModuleHostOperations.PlayableCreate,
                    new RekallAgeModuleHostPlayableCreateRequest(new RekallAgePlayableModuleContext("Stderr", []))),
                RekallAgeModuleHostEnvelope.Request(3, RekallAgeModuleHostOperations.PlayableRender, new { }),
                RekallAgeModuleHostEnvelope.Request(4, RekallAgeModuleHostOperations.Shutdown, new { })
            ]);

        Assert.All(result.Responses, response => Assert.True(response.Ok, response.Error?.Message));
        Assert.Equal(RekallAgeModuleHostProtocol.MaximumStandardErrorBytes, System.Text.Encoding.UTF8.GetByteCount(result.StandardError));
        Assert.All(result.StandardError, character => Assert.Equal('x', character));
    }

    [Fact]
    public async Task StagedWorkerPayloadCompletesFiniteProtocolBeforeContainment()
    {
        Assert.True(OperatingSystem.IsWindows());
        var projectRoot = await CreateModuleAsync();
        var hostRoot = await CreateRealHostPayloadAsync();
        await using var staged = await new RekallAgeModuleHostStager(TestPaths.CreateTempDirectory()).StageAsync(
            projectRoot,
            hostRoot,
            CancellationToken.None);
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(staged.HostExecutablePath)
        {
            WorkingDirectory = staged.Root,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        })!;
        var codec = new RekallAgeModuleHostFrameCodec();
        await codec.WriteAsync(
            process.StandardInput.BaseStream,
            RekallAgeModuleHostEnvelope.Request(
                1,
                RekallAgeModuleHostOperations.Initialize,
                new RekallAgeModuleHostInitializeRequest(staged.LoadPlanPath)),
            CancellationToken.None);
        await codec.WriteAsync(
            process.StandardInput.BaseStream,
            RekallAgeModuleHostEnvelope.Request(2, RekallAgeModuleHostOperations.Shutdown, new { }),
            CancellationToken.None);
        process.StandardInput.Close();

        var initialize = await codec.ReadAsync(process.StandardOutput.BaseStream, CancellationToken.None);
        var shutdown = await codec.ReadAsync(process.StandardOutput.BaseStream, CancellationToken.None);
        using var exitTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await process.WaitForExitAsync(exitTimeout.Token);
        var standardError = await process.StandardError.ReadToEndAsync(exitTimeout.Token);

        Assert.True(initialize.Ok, initialize.Error?.Message);
        Assert.True(shutdown.Ok, shutdown.Error?.Message);
        Assert.Equal(0, process.ExitCode);
        Assert.Equal(string.Empty, standardError);
    }

    [Fact]
    public async Task NoCapabilityAppContainerWorkerCompletesFiniteProtocolInsideKillOnCloseJob()
    {
        Assert.True(OperatingSystem.IsWindows());
        var projectRoot = await CreateModuleAsync();
        var hostRoot = await CreateRealHostPayloadAsync();
        var sessionsRoot = TestPaths.CreateTempDirectory();
        await using var staged = await new RekallAgeModuleHostStager(sessionsRoot).StageAsync(
            projectRoot,
            hostRoot,
            CancellationToken.None);
        using var profile = RekallAgeAppContainerProfile.OpenOrCreate();
        profile.GrantReadExecute(staged.Root);
        await using var process = RekallAgeAppContainerProcess.Start(
            staged,
            profile,
            RekallAgeModuleHostJobLimits.RestrictedDefault);
        Assert.NotEqual(Environment.ProcessId, process.ProcessId);
        var codec = new RekallAgeModuleHostFrameCodec();
        await codec.WriteAsync(
            process.StandardInput,
            RekallAgeModuleHostEnvelope.Request(
                1,
                RekallAgeModuleHostOperations.Initialize,
                new RekallAgeModuleHostInitializeRequest(staged.LoadPlanPath)),
            CancellationToken.None);
        await codec.WriteAsync(
            process.StandardInput,
            RekallAgeModuleHostEnvelope.Request(2, RekallAgeModuleHostOperations.Shutdown, new { }),
            CancellationToken.None);
        await process.StandardInput.FlushAsync();
        process.CloseInput();
        var reader = new RekallAgeModuleHostFrameCodec();
        Assert.True((await reader.ReadAsync(process.StandardOutput, CancellationToken.None)).Ok is true);
        Assert.True((await reader.ReadAsync(process.StandardOutput, CancellationToken.None)).Ok is true);
        using var exitTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var exitCode = await process.WaitForExitAsync(exitTimeout.Token);
        using var diagnosticsTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var standardError = await process.ReadBoundedStandardErrorAsync(diagnosticsTimeout.Token);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, standardError);
        Assert.True(process.AssignedToJob);
        Assert.Equal(1u, process.ActiveProcessLimit);
        Assert.Equal(512L * 1024 * 1024, process.ProcessMemoryLimitBytes);
        Assert.Empty(profile.Capabilities);
    }

    private static async Task<string> CreateModuleAsync(Func<string, string>? rewriteSource = null)
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("test", RekallAgeTransaction.Begin("isolated worker"), CancellationToken.None);
        var scaffold = await new ScaffoldPlayableModuleCommand().ExecuteAsync(
            new ScaffoldPlayableModuleRequest(root, "agent.isolated", "Isolated", "IsolatedFixture"),
            context);
        Assert.True(scaffold.Ok, scaffold.Summary);
        if (rewriteSource is not null)
        {
            var source = await File.ReadAllTextAsync(scaffold.Value!.SourcePath);
            await File.WriteAllTextAsync(scaffold.Value.SourcePath, rewriteSource(source));
        }

        var build = await new BuildModulesCommand().ExecuteAsync(new BuildModulesRequest(root), context);
        Assert.True(build.Ok, build.Summary);
        return root;
    }

    private static async Task<IReadOnlyList<RekallAgeModuleHostEnvelope>> RunRestrictedWorkerAsync(
        string projectRoot,
        IReadOnlyList<RekallAgeModuleHostEnvelope> requests)
    {
        var result = await RunRestrictedWorkerWithDiagnosticsAsync(projectRoot, requests);
        Assert.Equal(string.Empty, result.StandardError);
        return result.Responses;
    }

    private static async Task<RestrictedWorkerResult> RunRestrictedWorkerWithDiagnosticsAsync(
        string projectRoot,
        IReadOnlyList<RekallAgeModuleHostEnvelope> requests)
    {
        var hostRoot = await CreateRealHostPayloadAsync();
        var sessionsRoot = TestPaths.CreateTempDirectory();
        await using var staged = await new RekallAgeModuleHostStager(sessionsRoot).StageAsync(
            projectRoot,
            hostRoot,
            CancellationToken.None);
        using var profile = RekallAgeAppContainerProfile.OpenOrCreate();
        profile.GrantReadExecute(staged.Root);
        await using var process = RekallAgeAppContainerProcess.Start(
            staged,
            profile,
            RekallAgeModuleHostJobLimits.RestrictedDefault);
        var codec = new RekallAgeModuleHostFrameCodec();
        foreach (var request in requests)
        {
            var resolved = request.Operation == RekallAgeModuleHostOperations.Initialize
                ? RekallAgeModuleHostEnvelope.Request(
                    request.Sequence,
                    request.Operation,
                    new RekallAgeModuleHostInitializeRequest(staged.LoadPlanPath))
                : request;
            await codec.WriteAsync(process.StandardInput, resolved, CancellationToken.None);
        }

        await process.StandardInput.FlushAsync();
        process.CloseInput();
        var responses = new List<RekallAgeModuleHostEnvelope>();
        for (var index = 0; index < requests.Count; index++)
        {
            responses.Add(await codec.ReadAsync(process.StandardOutput, CancellationToken.None));
        }

        using var exitTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Assert.Equal(0, await process.WaitForExitAsync(exitTimeout.Token));
        using var diagnosticsTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var standardError = await process.ReadBoundedStandardErrorAsync(diagnosticsTimeout.Token);
        return new RestrictedWorkerResult(responses, standardError);
    }

    private static async Task<string> CreateRealHostPayloadAsync()
    {
        var sourceRoot = Path.GetDirectoryName(typeof(RekallAgeModuleHostServer).Assembly.Location)!;
        var root = TestPaths.CreateTempDirectory();
        var names = new[]
        {
            "Rekall.Age.ModuleHost.exe",
            "Rekall.Age.ModuleHost.dll",
            "Rekall.Age.ModuleHost.deps.json",
            "Rekall.Age.ModuleHost.runtimeconfig.json",
            "Rekall.Age.Modules.dll",
            "Rekall.Age.Core.dll",
            "Rekall.Age.Rendering.Abstractions.dll",
            "Rekall.Age.Runtime.Abstractions.dll",
            "Rekall.Age.World.dll"
        };
        var files = new List<RekallAgeModuleHostPayloadFile>();
        foreach (var name in names)
        {
            var source = Path.Combine(sourceRoot, name);
            Assert.True(File.Exists(source), source);
            var destination = Path.Combine(root, name);
            File.Copy(source, destination);
            files.Add(new RekallAgeModuleHostPayloadFile(
                name,
                new FileInfo(destination).Length,
                Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(destination))).ToLowerInvariant()));
        }

        var manifest = new RekallAgeModuleHostPayloadManifest(
            1,
            RekallAgeModuleHostProtocol.Version,
            RekallAgeProductInfo.Current.Version,
            "Rekall.Age.ModuleHost.exe",
            files);
        await File.WriteAllTextAsync(
            Path.Combine(root, RekallAgeModuleHostPayloadManifest.FileName),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return root;
    }

    private sealed record RestrictedWorkerResult(
        IReadOnlyList<RekallAgeModuleHostEnvelope> Responses,
        string StandardError);
}
