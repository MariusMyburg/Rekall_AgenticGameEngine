using System.Text.Json;
using Rekall.Age.Editor.Development;
using Rekall.Age.Project;

namespace Rekall.Age.Tests.Editor;

public sealed class ProjectDevelopmentWorkspaceTests
{
    [Fact]
    public async Task GenerateCreatesDeterministicVisualStudioAndVsCodePlayerLaunchesWithoutChangingSource()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeProjectStore().SaveAsync(
            root,
            RekallAgeProjectManifest.Create("Neon Orchard", ["world", "modules"]),
            CancellationToken.None);
        var firstModule = Path.Combine(root, "Modules", "OrchardRules");
        var secondModule = Path.Combine(root, "Modules", "PlayerMotion");
        Directory.CreateDirectory(firstModule);
        Directory.CreateDirectory(secondModule);
        await File.WriteAllTextAsync(Path.Combine(firstModule, "OrchardRules.csproj"), "<Project />");
        await File.WriteAllTextAsync(Path.Combine(secondModule, "PlayerMotion.csproj"), "<Project />");
        var authoredSourcePath = Path.Combine(firstModule, "OrchardRulesModule.cs");
        var authoredBytes = "public sealed class OrchardRulesModule { }"u8.ToArray();
        await File.WriteAllBytesAsync(authoredSourcePath, authoredBytes);
        var playerPath = Path.Combine(root, "Engine", "Rekall.Age.Player.Windows.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(playerPath)!);
        await File.WriteAllBytesAsync(playerPath, [0]);
        var cliPath = Path.Combine(root, "Engine", "rekall-age.exe");
        await File.WriteAllBytesAsync(cliPath, [0]);
        var generator = new RekallAgeProjectDevelopmentWorkspace();
        var request = new RekallAgeProjectDevelopmentWorkspaceRequest(root, "Main", playerPath, cliPath);

        var first = await generator.GenerateAsync(request, CancellationToken.None);
        var firstSolution = await File.ReadAllTextAsync(first.SolutionPath);
        var firstLaunch = await File.ReadAllTextAsync(first.VsCodeLaunchPath);
        var second = await generator.GenerateAsync(request, CancellationToken.None);

        Assert.Equal(first, second);
        Assert.Equal(firstSolution, await File.ReadAllTextAsync(second.SolutionPath));
        Assert.Equal(firstLaunch, await File.ReadAllTextAsync(second.VsCodeLaunchPath));
        Assert.Equal(authoredBytes, await File.ReadAllBytesAsync(authoredSourcePath));
        Assert.EndsWith("Neon Orchard.slnx", first.SolutionPath, StringComparison.Ordinal);
        Assert.Contains("Modules\\OrchardRules\\OrchardRules.csproj", firstSolution, StringComparison.Ordinal);
        Assert.Contains("Modules\\PlayerMotion\\PlayerMotion.csproj", firstSolution, StringComparison.Ordinal);
        Assert.Contains(".rekall\\ide\\Rekall.Game.Debug\\Rekall.Game.Debug.csproj", firstSolution, StringComparison.Ordinal);

        using var visualStudio = JsonDocument.Parse(await File.ReadAllTextAsync(first.VisualStudioLaunchSettingsPath));
        var profile = visualStudio.RootElement.GetProperty("profiles").GetProperty("Rekall AGE Game");
        Assert.Equal("Executable", profile.GetProperty("commandName").GetString());
        Assert.Equal(Path.GetFullPath(playerPath), profile.GetProperty("executablePath").GetString());
        Assert.Equal(Path.GetFullPath(root), profile.GetProperty("workingDirectory").GetString());
        Assert.Equal($"\"{Path.GetFullPath(root)}\" \"Main\" --graphics --backend vulkan", profile.GetProperty("commandLineArgs").GetString());

        using var vsCode = JsonDocument.Parse(firstLaunch);
        var configuration = Assert.Single(vsCode.RootElement.GetProperty("configurations").EnumerateArray());
        Assert.Equal("coreclr", configuration.GetProperty("type").GetString());
        Assert.Equal(Path.GetFullPath(playerPath), configuration.GetProperty("program").GetString());
        Assert.Equal(
            [Path.GetFullPath(root), "Main", "--graphics", "--backend", "vulkan"],
            configuration.GetProperty("args").EnumerateArray().Select(item => item.GetString()!).ToArray());

        using var tasks = JsonDocument.Parse(await File.ReadAllTextAsync(first.VsCodeTasksPath));
        var task = Assert.Single(tasks.RootElement.GetProperty("tasks").EnumerateArray());
        Assert.Equal(Path.GetFullPath(cliPath), task.GetProperty("command").GetString());
        Assert.Equal(["build", "modules", Path.GetFullPath(root)],
            task.GetProperty("args").EnumerateArray().Select(item => item.GetString()!).ToArray());
    }
}
