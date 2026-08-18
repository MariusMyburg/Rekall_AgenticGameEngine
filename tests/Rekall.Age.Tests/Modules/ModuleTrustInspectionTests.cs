using System.Text.Json.Nodes;
using Rekall.Age.Build.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modules.Commands;
using Rekall.Age.Modules.Security;

namespace Rekall.Age.Tests.Modules;

public sealed class ModuleTrustInspectionTests
{
    [Fact]
    public async Task CanonicalBuildWritesInspectableFullTrustReceipt()
    {
        var (root, moduleDirectory) = await ScaffoldAndBuildAsync("ReceiptModule");

        var inspection = new RekallAgeProjectModuleTrustInspector().Inspect(root);

        Assert.True(inspection.Ready, string.Join(Environment.NewLine, inspection.Issues.Select(issue => issue.Message)));
        var module = Assert.Single(inspection.Modules);
        Assert.Equal("in-process-full-trust", module.TrustPosture);
        Assert.Equal("ReceiptModule", module.ModuleName);
        Assert.Matches("^[0-9a-f]{64}$", module.SourceFingerprint);
        Assert.NotEmpty(module.OutputFiles);
        Assert.DoesNotContain(module.OutputFiles, file => file.Path.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase));
        Assert.All(module.OutputFiles, file =>
        {
            Assert.False(Path.IsPathRooted(file.Path));
            Assert.DoesNotContain("..", file.Path, StringComparison.Ordinal);
            Assert.Matches("^[0-9a-f]{64}$", file.Sha256);
        });
        Assert.True(File.Exists(Path.Combine(moduleDirectory, "bin", "rekall", "net10.0", "rekall.module.build.json")));
    }

    [Fact]
    public async Task SourceMutationMakesAuthoringReceiptStale()
    {
        var (root, moduleDirectory) = await ScaffoldAndBuildAsync("StaleSourceModule");
        var source = Assert.Single(Directory.EnumerateFiles(moduleDirectory, "*.cs", SearchOption.TopDirectoryOnly));
        await File.AppendAllTextAsync(source, Environment.NewLine + "// changed after build");

        var inspection = new RekallAgeProjectModuleTrustInspector().Inspect(root);

        Assert.False(inspection.Ready);
        Assert.Contains(inspection.Issues, issue => issue.Code == "REKALL_MODULE_SOURCE_STALE");
    }

    [Fact]
    public async Task OutputMutationFailsHashBeforeAnyAssemblyLoad()
    {
        var (root, moduleDirectory) = await ScaffoldAndBuildAsync("TamperedOutputModule");
        var assemblyPath = Path.Combine(moduleDirectory, "bin", "rekall", "net10.0", "TamperedOutputModule.dll");
        await using (var stream = new FileStream(assemblyPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            stream.Position = stream.Length - 1;
            var original = stream.ReadByte();
            stream.Position = stream.Length - 1;
            stream.WriteByte((byte)(original ^ 0xff));
        }

        var inspection = new RekallAgeProjectModuleTrustInspector().Inspect(root);

        Assert.False(inspection.Ready);
        Assert.Contains(inspection.Issues, issue => issue.Code == "REKALL_MODULE_OUTPUT_HASH_MISMATCH");
    }

    [Fact]
    public async Task PackagedOutputWithoutAuthoringSourceRemainsVerifiable()
    {
        var (root, moduleDirectory) = await ScaffoldAndBuildAsync("PackagedModule");
        foreach (var source in Directory.EnumerateFiles(moduleDirectory, "*.cs", SearchOption.TopDirectoryOnly))
        {
            File.Delete(source);
        }
        File.Delete(Path.Combine(moduleDirectory, "PackagedModule.csproj"));
        Directory.Delete(Path.Combine(moduleDirectory, "obj"), recursive: true);

        var inspection = new RekallAgeProjectModuleTrustInspector().Inspect(root);

        Assert.True(inspection.Ready, string.Join(Environment.NewLine, inspection.Issues.Select(issue => issue.Message)));
        Assert.Single(inspection.Modules);
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("traversal")]
    [InlineData("duplicate")]
    [InlineData("extra")]
    public async Task MalformedOrNonExactReceiptsFailClosed(string mutation)
    {
        var (root, moduleDirectory) = await ScaffoldAndBuildAsync($"Invalid{mutation}Module");
        var output = Path.Combine(moduleDirectory, "bin", "rekall", "net10.0");
        var receiptPath = Path.Combine(output, "rekall.module.build.json");
        if (mutation == "malformed")
        {
            await File.WriteAllTextAsync(receiptPath, "{");
        }
        else if (mutation == "extra")
        {
            await File.WriteAllTextAsync(Path.Combine(output, "unexpected.bin"), "unexpected");
        }
        else
        {
            var receipt = JsonNode.Parse(await File.ReadAllTextAsync(receiptPath))!.AsObject();
            var files = receipt["outputFiles"]!.AsArray();
            if (mutation == "traversal")
            {
                files[0]!["path"] = "../escape.dll";
            }
            else
            {
                files.Add(files[0]!.DeepClone());
            }
            await File.WriteAllTextAsync(receiptPath, receipt.ToJsonString());
        }

        var inspection = new RekallAgeProjectModuleTrustInspector().Inspect(root);

        Assert.False(inspection.Ready);
        Assert.NotEmpty(inspection.Issues);
    }

    [Fact]
    public async Task ReceiptHashAndAssemblyIdentityMutationsAreRejectedSpecifically()
    {
        var (hashRoot, hashModule) = await ScaffoldAndBuildAsync("ForgedHashModule");
        var hashReceiptPath = ReceiptPath(hashModule);
        var hashReceipt = JsonNode.Parse(await File.ReadAllTextAsync(hashReceiptPath))!.AsObject();
        hashReceipt["outputFiles"]!.AsArray()[0]!["sha256"] = new string('0', 64);
        await File.WriteAllTextAsync(hashReceiptPath, hashReceipt.ToJsonString());

        var (identityRoot, identityModule) = await ScaffoldAndBuildAsync("ForgedIdentityModule");
        var identityReceiptPath = ReceiptPath(identityModule);
        var identityReceipt = JsonNode.Parse(await File.ReadAllTextAsync(identityReceiptPath))!.AsObject();
        var mainAssembly = identityReceipt["outputFiles"]!.AsArray()
            .Select(item => item!.AsObject())
            .Single(item => item["path"]!.GetValue<string>() == "ForgedIdentityModule.dll");
        mainAssembly["assemblyIdentity"] = "ForgedIdentityModule, Version=999.0.0.0";
        await File.WriteAllTextAsync(identityReceiptPath, identityReceipt.ToJsonString());

        var hashInspection = new RekallAgeProjectModuleTrustInspector().Inspect(hashRoot);
        var identityInspection = new RekallAgeProjectModuleTrustInspector().Inspect(identityRoot);

        Assert.Contains(hashInspection.Issues, issue => issue.Code == "REKALL_MODULE_OUTPUT_HASH_MISMATCH");
        Assert.Contains(identityInspection.Issues, issue => issue.Code == "REKALL_MODULE_ASSEMBLY_IDENTITY_MISMATCH");
    }

    [Fact]
    public async Task InjectedInspectionBoundsAndReparsePointFailClosed()
    {
        var (root, moduleDirectory) = await ScaffoldAndBuildAsync("BoundedTrustModule");
        var assemblyPath = Path.Combine(moduleDirectory, "bin", "rekall", "net10.0", "BoundedTrustModule.dll");
        var bounded = new RekallAgeProjectModuleTrustInspector(
            new RekallAgeModuleTrustLimits(MaximumOutputFilesPerModule: 1)).Inspect(root);
        var reparse = new RekallAgeProjectModuleTrustInspector(
            readAttributes: path => Path.GetFullPath(path).Equals(Path.GetFullPath(assemblyPath), StringComparison.OrdinalIgnoreCase)
                ? FileAttributes.Normal | FileAttributes.ReparsePoint
                : File.GetAttributes(path)).Inspect(root);

        Assert.Contains(bounded.Issues, issue => issue.Code == "REKALL_MODULE_OUTPUT_BOUNDS_EXCEEDED");
        Assert.Contains(reparse.Issues, issue => issue.Code == "REKALL_MODULE_TRUST_REPARSE_POINT");
    }

    [Fact]
    public async Task ReceiptServiceRefusesSourceChangedAfterFingerprintCapture()
    {
        var (root, moduleDirectory) = await ScaffoldAndBuildAsync("ConcurrentSourceModule");
        var candidate = Assert.Single(new RekallAgeModuleBuildPolicy().Inspect(root).Candidates);
        var service = new RekallAgeModuleBuildReceiptService();
        var fingerprint = service.CaptureSourceFingerprint(candidate);
        var source = Assert.Single(candidate.SourcePaths);
        await File.AppendAllTextAsync(source, Environment.NewLine + "// concurrent change");

        var exception = await Assert.ThrowsAsync<RekallAgeModuleReceiptException>(async () =>
            await service.WriteAsync(root, candidate, fingerprint, CancellationToken.None));

        Assert.Equal("REKALL_MODULE_SOURCE_CHANGED_DURING_BUILD", exception.Code);
        Assert.Equal(moduleDirectory, exception.Target);
    }

    private static async Task<(string Root, string ModuleDirectory)> ScaffoldAndBuildAsync(string moduleName)
    {
        var root = TestPaths.CreateTempDirectory();
        var scaffold = await new ScaffoldPlayableModuleCommand().ExecuteAsync(
            new ScaffoldPlayableModuleRequest(root, $"test.{moduleName.ToLowerInvariant()}", moduleName, moduleName),
            CreateContext("trust scaffold"));
        Assert.True(scaffold.Ok, scaffold.Summary);
        var build = await new BuildModulesCommand().ExecuteAsync(
            new BuildModulesRequest(root),
            CreateContext("trust build"));
        Assert.True(build.Ok, build.Summary);
        var result = Assert.Single(build.Value.Modules);
        Assert.Equal("in-process-full-trust", result.TrustPosture);
        Assert.True(File.Exists(result.ReceiptPath), result.ReceiptPath);
        return (root, Path.GetDirectoryName(scaffold.Value.ProjectPath)!);
    }

    private static RekallAgeCommandContext CreateContext(string name) => new(
        "test",
        RekallAgeTransaction.Begin(name),
        CancellationToken.None);

    private static string ReceiptPath(string moduleDirectory) => Path.Combine(
        moduleDirectory,
        "bin",
        "rekall",
        "net10.0",
        RekallAgeModuleBuildReceiptService.ReceiptFileName);
}
