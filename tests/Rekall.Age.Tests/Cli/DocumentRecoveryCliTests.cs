using System.Diagnostics;
using Rekall.Age.Core.Persistence;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Cli;

public sealed class DocumentRecoveryCliTests
{
    [Fact]
    public async Task CliInspectsAndExplicitlyRestoresDamagedScene()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeSceneStore();
        await store.SaveAsync(root, RekallAgeSceneDocument.Create("Main", ["world"]), CancellationToken.None);
        var first = await store.LoadVersionedAsync(root, "Main", CancellationToken.None);
        await store.SaveIfRevisionAsync(root, first.Value with { Id = "replacement" }, first.Revision, CancellationToken.None);
        var corrupt = System.Text.Encoding.UTF8.GetBytes("{ damaged");
        await File.WriteAllBytesAsync(store.GetScenePath(root, "Main"), corrupt);
        var corruptRevision = RekallAgeDocumentRevision.Compute(corrupt);

        var inspect = await RunAsync("recovery", "inspect", "scene", root, "Main");
        Assert.Equal(0, inspect.ExitCode);
        Assert.Contains("REKALL_DOCUMENT_JSON_MALFORMED", inspect.Output);
        Assert.Contains($"recoverable: True", inspect.Output);

        var restore = await RunAsync("recovery", "restore", "scene", root, "Main", corruptRevision);
        Assert.Equal(0, restore.ExitCode);
        Assert.Contains($"Restored revision: {first.Revision}", restore.Output);
        Assert.Equal(first.Value.Id, (await store.LoadAsync(root, "Main", CancellationToken.None)).Id);
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(params string[] args)
    {
        var repository = FindRepositoryRoot();
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = repository
        };
        startInfo.ArgumentList.Add(Path.Combine(repository, "src", "Rekall.Age.Cli", "bin", "Debug", "net10.0", "Rekall.Age.Cli.dll"));
        foreach (var argument in args)
        {
            startInfo.ArgumentList.Add(argument);
        }
        using var process = Process.Start(startInfo)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, output + error);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Rekall.AGE.sln")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
