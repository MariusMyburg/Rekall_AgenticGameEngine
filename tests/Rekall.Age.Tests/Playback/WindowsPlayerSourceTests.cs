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
    public void WindowsPlayerConsumesAuthoredHemisphericalAmbientLighting()
    {
        var program = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Rekall.Age.Player.Windows", "Program.cs"));

        Assert.Contains("AmbientSkyColor", program, StringComparison.Ordinal);
        Assert.Contains("AmbientGroundColor", program, StringComparison.Ordinal);
        Assert.Contains("mix(Frame.EnvironmentAmbientGroundColor.rgb, Frame.EnvironmentAmbientSkyColor.rgb", program, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsPlayerNormalMappingFallsBackForDegenerateUvDerivatives()
    {
        var program = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Rekall.Age.Player.Windows", "Program.cs"));

        Assert.Contains("float determinant = st1.s * st2.t - st1.t * st2.s;", program, StringComparison.Ordinal);
        Assert.Contains("if (abs(determinant) <= 0.0000001)", program, StringComparison.Ordinal);
        Assert.Contains("return normal;", program, StringComparison.Ordinal);
        Assert.Contains("tangentRaw - normal * dot(normal, tangentRaw)", program, StringComparison.Ordinal);
    }

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
            layout(set = 0, binding = 0) uniform FrameUniformBuffer
            {
                mat4 ViewProjection;
                vec4 LightDirection;
                vec4 LightColor;
                vec4 LightPosition;
                vec4 CameraPosition;
            } Frame;
            layout(set = 1, binding = 0) uniform DrawUniformBuffer
            {
                mat4 Model;
                vec4 MaterialFactors;
                vec4 EmissiveFactors;
                vec4 AtmosphereFactors0;
                vec4 AtmosphereFactors1;
                vec4 AtmosphereColor0;
                vec4 AtmosphereColor1;
                vec4 AtmosphereColor2;
                vec4 CloudFactors;
                vec4 CloudColor;
                vec4 CloudShadowFactors;
                vec4 SurfaceWaterFactors;
            } Draw;
            layout(location = 0) out vec2 fragUv;
            void main()
            {
                gl_Position = Frame.ViewProjection * Draw.Model * vec4(inPosition, 1.0);
                fragUv = inPosition.xy * 0.5 + 0.5;
            }
            """);
        var fragmentPath = Path.Combine(shaderRoot, "magenta.frag");
        await File.WriteAllTextAsync(fragmentPath, """
            #version 450
            layout(location = 0) in vec2 fragUv;
            layout(set = 2, binding = 0) uniform texture2D BaseColorTexture;
            layout(set = 2, binding = 1) uniform sampler BaseColorSampler;
            layout(location = 0) out vec4 outColor;
            void main()
            {
                float sampledAlpha = texture(sampler2D(BaseColorTexture, BaseColorSampler), fragUv).a;
                outColor = vec4(1.0, 0.0, 1.0, sampledAlpha);
            }
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
