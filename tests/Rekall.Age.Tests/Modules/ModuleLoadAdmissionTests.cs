using Rekall.Age.Build.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modules;
using Rekall.Age.Modules.Commands;
using Rekall.Age.Modules.Security;
using System.Text.Json;

namespace Rekall.Age.Tests.Modules;

public sealed class ModuleLoadAdmissionTests
{
    [Fact]
    public async Task TamperedMainAssemblyIsRejectedBeforeModuleCodeLoads()
    {
        var (root, moduleDirectory) = await ScaffoldAndBuildAsync("TamperedLoadModule");
        var assemblyPath = MainAssembly(moduleDirectory, "TamperedLoadModule");
        await using (var stream = new FileStream(assemblyPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            stream.Position = stream.Length - 1;
            var value = stream.ReadByte();
            stream.Position = stream.Length - 1;
            stream.WriteByte((byte)(value ^ 0xff));
        }

        var exception = Assert.Throws<RekallAgeModuleTrustException>(() =>
            RekallAgeProjectModuleAssemblyLoader.LoadBuiltModuleAssemblies(root));

        Assert.Equal("REKALL_MODULE_OUTPUT_HASH_MISMATCH", exception.Code);
        Assert.Equal(assemblyPath, exception.Target);
    }

    [Fact]
    public async Task MissingReceiptIsRejectedInsteadOfLoadingUnverifiedAssembly()
    {
        var (root, moduleDirectory) = await ScaffoldAndBuildAsync("MissingReceiptModule");
        File.Delete(Path.Combine(
            moduleDirectory,
            "bin",
            "rekall",
            "net10.0",
            RekallAgeModuleBuildReceiptService.ReceiptFileName));

        var exception = Assert.Throws<RekallAgeModuleTrustException>(() =>
            RekallAgeProjectModuleAssemblyLoader.LoadBuiltModuleAssemblies(root));

        Assert.Equal("REKALL_MODULE_RECEIPT_MISSING", exception.Code);
    }

    [Fact]
    public async Task SourceChangedAfterBuildIsRejectedBeforeLoad()
    {
        var (root, moduleDirectory) = await ScaffoldAndBuildAsync("StaleLoadModule");
        var source = Assert.Single(Directory.EnumerateFiles(moduleDirectory, "*.cs", SearchOption.TopDirectoryOnly));
        await File.AppendAllTextAsync(source, Environment.NewLine + "// stale");

        var exception = Assert.Throws<RekallAgeModuleTrustException>(() =>
            RekallAgeProjectModuleAssemblyLoader.LoadBuiltModuleAssemblies(root));

        Assert.Equal("REKALL_MODULE_SOURCE_STALE", exception.Code);
    }

    [Fact]
    public async Task DynamicCommandPreservesExactModuleTrustCode()
    {
        var (root, moduleDirectory) = await ScaffoldAndBuildAsync("DynamicTrustModule");
        File.Delete(Path.Combine(
            moduleDirectory,
            "bin",
            "rekall",
            "net10.0",
            RekallAgeModuleBuildReceiptService.ReceiptFileName));
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new ListComponentSchemasCommand());

        var result = await registry.ExecuteJsonAsync(
            "rekall.module.component_schemas",
            JsonSerializer.Serialize(new ListComponentSchemasRequest(ProjectRoot: root)),
            CreateContext("dynamic trust"));

        Assert.False(result.Ok);
        var error = Assert.Single(result.Errors);
        Assert.Equal("REKALL_MODULE_RECEIPT_MISSING", error.Code);
        Assert.DoesNotContain("REKALL_COMMAND_EXECUTION_FAILED", result.Errors.Select(item => item.Code));
    }

    [Fact]
    public async Task PackagedModuleLoadsAfterAuthoringSourceAndSdkAreRemoved()
    {
        var (root, moduleDirectory) = await ScaffoldAndBuildAsync("PackagedLoadModule");
        foreach (var source in Directory.EnumerateFiles(moduleDirectory, "*.cs", SearchOption.TopDirectoryOnly))
        {
            File.Delete(source);
        }
        File.Delete(Path.Combine(moduleDirectory, "PackagedLoadModule.csproj"));
        Directory.Delete(Path.Combine(moduleDirectory, "obj"), recursive: true);
        Directory.Delete(Path.Combine(root, ".rekall"), recursive: true);

        var assemblies = RekallAgeProjectModuleAssemblyLoader.LoadBuiltModuleAssemblies(root);

        var assembly = Assert.Single(assemblies);
        Assert.Equal("PackagedLoadModule", assembly.GetName().Name);
    }

    private static async Task<(string Root, string ModuleDirectory)> ScaffoldAndBuildAsync(string moduleName)
    {
        var root = TestPaths.CreateTempDirectory();
        var scaffold = await new ScaffoldPlayableModuleCommand().ExecuteAsync(
            new ScaffoldPlayableModuleRequest(root, $"test.{moduleName.ToLowerInvariant()}", moduleName, moduleName),
            CreateContext("load scaffold"));
        Assert.True(scaffold.Ok, scaffold.Summary);
        var build = await new BuildModulesCommand().ExecuteAsync(
            new BuildModulesRequest(root),
            CreateContext("load build"));
        Assert.True(build.Ok, build.Summary);
        return (root, Path.GetDirectoryName(scaffold.Value.ProjectPath)!);
    }

    private static string MainAssembly(string moduleDirectory, string moduleName) => Path.Combine(
        moduleDirectory,
        "bin",
        "rekall",
        "net10.0",
        $"{moduleName}.dll");

    private static RekallAgeCommandContext CreateContext(string name) => new(
        "test",
        RekallAgeTransaction.Begin(name),
        CancellationToken.None);
}
