using System.Diagnostics;
using System.Text.Json;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Cli;

public sealed class MeshCliTests
{
    [Fact]
    public async Task GenericCommandGatewayExecutesMeshInspectionAndRedactsArgumentsFromTransactionName()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeMeshAssetStore().SaveAsync(
            root,
            RekallAgeMeshAsset.Create("triangle", "Triangle", Triangle()),
            CancellationToken.None);
        var arguments = JsonSerializer.Serialize(new
        {
            projectRoot = root,
            assetId = "triangle",
            maximumSamples = 2
        });

        var result = await RunAsync(
            FindCliAssemblyPath(),
            "command",
            "execute",
            "rekall.mesh.inspect",
            arguments);

        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
        var mesh = document.RootElement.GetProperty("value").GetProperty("mesh");
        Assert.Equal(3, mesh.GetProperty("topology").GetProperty("pointCount").GetInt32());
        Assert.Equal(2, mesh.GetProperty("pointIdSample").GetArrayLength());
        Assert.Equal(
            "command execute rekall.mesh.inspect <json>",
            document.RootElement.GetProperty("transaction").GetProperty("name").GetString());
    }

    private static RekallAgeMeshTopology Triangle() => new(
        PointIds: [1, 2, 3],
        Positions: [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)],
        EdgeIds: [11, 12, 13],
        EdgePointIndices: [new(0, 1), new(1, 2), new(2, 0)],
        FaceIds: [21],
        FaceOffsets: [0, 3],
        CornerIds: [31, 32, 33],
        CornerPointIndices: [0, 1, 2],
        CornerEdgeIndices: [0, 1, 2]);

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
        return (process.ExitCode, await outputTask + await errorTask);
    }

    private static string FindCliAssemblyPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Rekall.AGE.sln")))
        {
            directory = directory.Parent;
        }

        var root = directory?.FullName ?? throw new InvalidOperationException("Could not find repository root.");
        var path = Path.Combine(root, "src", "Rekall.Age.Cli", "bin", "Debug", "net10.0", "Rekall.Age.Cli.dll");
        return File.Exists(path) ? path : throw new InvalidOperationException($"Could not find built CLI assembly at '{path}'.");
    }
}
