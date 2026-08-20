using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Playback;

[Collection("Windows player process")]
public sealed class WindowsPlayerSourceTests
{
    [Fact]
    public void WindowsPlayerSelectsAuthoredPipelinesPerDrawAndWatchesShaderTree()
    {
        var root = FindRepositoryRoot();
        var program = File.ReadAllText(Path.Combine(root, "src", "Rekall.Age.Player.Windows", "Program.cs"));
        var cache = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Rekall.Age.Player.Windows",
            "RekallAgeVeldridShaderPipelineCache.cs"));

        Assert.Contains("_shaderPipelineCache.Resolve(draw.ShaderPipeline, transparent)", program, StringComparison.Ordinal);
        Assert.Contains("new FileSystemWatcher(shadersRoot)", program, StringComparison.Ordinal);
        Assert.Contains("IncludeSubdirectories = true", program, StringComparison.Ordinal);
        Assert.Contains("REKALL_SHADER_HOT_RELOAD_RETAINED", cache, StringComparison.Ordinal);
        Assert.Contains("MaximumCachedPipelinePairs", cache, StringComparison.Ordinal);
        Assert.Contains("_waitForIdle()", cache, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WindowsPlayerCreatesAndDrawsAssignedProjectPipeline()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext(
            "player-shader-test",
            RekallAgeTransaction.Begin("author player shader scene"),
            CancellationToken.None);
        await CreateShaderProjectAsync(root, context);

        var run = await RunPlayerAsync(FindWindowsPlayer(), root, "Main", "--frames", "3", "--no-vsync");

        Assert.True(run.ExitCode == 0, run.Output);
        Assert.Contains("Frames: 3/3", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WindowsPlayerRetainsLastValidPipelineAfterInvalidHotReload()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext(
            "player-shader-reload-test",
            RekallAgeTransaction.Begin("author hot reload shader scene"),
            CancellationToken.None);
        var fragmentPath = await CreateShaderProjectAsync(root, context);
        var startInfo = CreatePlayerStartInfo(
            FindWindowsPlayer(),
            root,
            "Main",
            "--frames",
            "300");

        using var process = Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await Task.Delay(TimeSpan.FromSeconds(2));
        await File.WriteAllTextAsync(fragmentPath, "#version 450\nthis is not valid GLSL");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"Process '{startInfo.FileName}' exceeded the 45-second test bound.");
        }

        var output = await standardOutput + await standardError;
        Assert.True(process.ExitCode == 0, output);
        Assert.Contains("Frames: 300/300", output, StringComparison.Ordinal);
    }

    private static async Task<(int ExitCode, string Output)> RunPlayerAsync(
        string executable,
        params string[] arguments)
    {
        var startInfo = CreatePlayerStartInfo(executable, arguments);

        using var process = Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"Process '{executable}' exceeded the 45-second test bound.");
        }

        return (process.ExitCode, await standardOutput + await standardError);
    }

    private static ProcessStartInfo CreatePlayerStartInfo(string executable, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = FindRepositoryRoot()
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        return startInfo;
    }

    private static async Task<string> CreateShaderProjectAsync(
        string root,
        RekallAgeCommandContext context)
    {
        await TestProjectAuthoring.CreateProjectWithSceneAsync(root, context, "Player Shader Game");
        var shaderRoot = Path.Combine(root, "Shaders", "agent");
        Directory.CreateDirectory(shaderRoot);
        await File.WriteAllTextAsync(Path.Combine(shaderRoot, "magenta.vert"), """
            #version 450
            layout(location = 0) in vec3 inPosition;
            void main() { gl_Position = vec4(inPosition, 1.0); }
            """);
        var fragmentPath = Path.Combine(shaderRoot, "magenta.frag");
        await File.WriteAllTextAsync(fragmentPath, """
            #version 450
            layout(location = 0) out vec4 outColor;
            void main() { outColor = vec4(1.0, 0.0, 1.0, 1.0); }
            """);
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["z"] = -3 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Camera3D",
                    new JsonObject { ["active"] = true })))
            .AddEntity(RekallAgeEntityDocument.Create("Shader Cube", ["mesh"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D"))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.GeometryPrimitive",
                    new JsonObject { ["primitive"] = "cube" }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.MeshRenderer",
                    new JsonObject
                    {
                        ["vertexShader"] = "agent/magenta",
                        ["fragmentShader"] = "agent/magenta"
                    })));
        await new RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None);
        return fragmentPath;
    }

    private static string FindWindowsPlayer()
    {
        var root = FindRepositoryRoot();
        foreach (var configuration in new[] { "Release", "Debug" })
        {
            var executable = Path.Combine(
                root,
                "src",
                "Rekall.Age.Player.Windows",
                "bin",
                configuration,
                "net10.0-windows",
                "Rekall.Age.Player.Windows.exe");
            if (File.Exists(executable))
            {
                return executable;
            }
        }

        throw new InvalidOperationException("The Windows player has not been built.");
    }

    private static string FindRepositoryRoot([CallerFilePath] string sourceFilePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFilePath)!, "..", "..", ".."));
}
