using System.Diagnostics;
using Rekall.Age.Project;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Cli;

public sealed class RenderGlbCliTests
{
    [Fact]
    public async Task RenderGlbExportWritesSceneGlb()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeProjectStore().SaveAsync(
            root,
            RekallAgeProjectManifest.Create("GLB CLI", ["world", "rendering3d"]),
            CancellationToken.None);
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"]),
            CancellationToken.None);

        var createResult = await RunAsync(
            FindCliAssemblyPath(),
            "geometry",
            "mesh",
            "create",
            root,
            "Main",
            "Triangle",
            """[{"x":0,"y":0,"z":0},{"x":1,"y":0,"z":0},{"x":0,"y":1,"z":0}]""",
            "[0,1,2]",
            "0",
            "0",
            "0",
            "#33cc99");
        Assert.Equal(0, createResult.ExitCode);

        var outputPath = Path.Combine(root, "Artifacts", "Exports", "triangle.glb");
        var exportResult = await RunAsync(
            FindCliAssemblyPath(),
            "render",
            "glb",
            "export",
            root,
            "Main",
            outputPath);

        Assert.Equal(0, exportResult.ExitCode);
        Assert.Contains("Exported GLB scene 'Main'", exportResult.Output);
        Assert.True(File.Exists(outputPath));
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string cliAssembly, params string[] args)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";

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
