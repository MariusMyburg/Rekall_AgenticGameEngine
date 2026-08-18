using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Rekall.Age.Build.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modules.Commands;
using Rekall.Age.Modules.Security;

namespace Rekall.Age.Tests.Modules;

public sealed class ModuleSdkIntegrityTests
{
    [Fact]
    public async Task InstalledSdkManifestContainsBoundedFileIntegrityInventory()
    {
        var (root, sdkRoot) = await ScaffoldAsync("SdkInventory");
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(sdkRoot, "rekall.sdk.json")))!.AsObject();

        Assert.Equal(1, manifest["schemaVersion"]!.GetValue<int>());
        var files = manifest["files"]!.AsArray();
        Assert.Equal(5, files.Count);
        Assert.All(files, item =>
        {
            var file = item!.AsObject();
            Assert.False(Path.IsPathRooted(file["path"]!.GetValue<string>()));
            Assert.True(file["sizeBytes"]!.GetValue<long>() > 0);
            Assert.Matches("^[0-9a-f]{64}$", file["sha256"]!.GetValue<string>());
        });

        var build = await BuildAsync(root);
        Assert.True(build.Ok, build.Summary);
    }

    [Fact]
    public async Task ChangedPropsAndMatchingLocalInventoryStillFailAgainstHostCanonicalResource()
    {
        var (root, sdkRoot) = await ScaffoldAsync("PropsMutation");
        var propsPath = Path.Combine(sdkRoot, "Rekall.Age.Sdk.props");
        await File.AppendAllTextAsync(propsPath, "<!-- forged local props -->");
        await RewriteLocalInventoryAsync(sdkRoot, "Rekall.Age.Sdk.props");

        var build = await BuildAsync(root);

        AssertSdkRejected(build);
    }

    [Fact]
    public async Task ChangedAssemblyAndMatchingLocalInventoryStillFailAgainstHostAssembly()
    {
        var (root, sdkRoot) = await ScaffoldAsync("AssemblyMutation");
        var assemblyPath = Path.Combine(sdkRoot, "Rekall.Age.Modules.dll");
        await using (var stream = new FileStream(assemblyPath, FileMode.Append, FileAccess.Write, FileShare.None))
        {
            await stream.WriteAsync(new byte[] { 0x52, 0x4b });
        }
        await RewriteLocalInventoryAsync(sdkRoot, "Rekall.Age.Modules.dll");

        var build = await BuildAsync(root);

        AssertSdkRejected(build);
    }

    [Fact]
    public async Task IncompatibleOrUnexpectedSdkContentIsRejected()
    {
        var (root, sdkRoot) = await ScaffoldAsync("UnexpectedSdk");
        var manifestPath = Path.Combine(sdkRoot, "rekall.sdk.json");
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))!.AsObject();
        manifest["compatibilityVersion"] = 999;
        await File.WriteAllTextAsync(manifestPath, manifest.ToJsonString());
        await File.WriteAllTextAsync(Path.Combine(sdkRoot, "unexpected.targets"), "<Project />");

        var build = await BuildAsync(root);

        AssertSdkRejected(build);
    }

    [Fact]
    public async Task DuplicateInventoryAndUnexpectedDirectoryAreRejectedWithoutThrowing()
    {
        var (duplicateRoot, duplicateSdkRoot) = await ScaffoldAsync("DuplicateInventory");
        var duplicateManifestPath = Path.Combine(duplicateSdkRoot, "rekall.sdk.json");
        var duplicateManifest = JsonNode.Parse(await File.ReadAllTextAsync(duplicateManifestPath))!.AsObject();
        var files = duplicateManifest["files"]!.AsArray();
        files.Add(files[0]!.DeepClone());
        await File.WriteAllTextAsync(duplicateManifestPath, duplicateManifest.ToJsonString());

        var (directoryRoot, directorySdkRoot) = await ScaffoldAsync("UnexpectedDirectory");
        Directory.CreateDirectory(Path.Combine(directorySdkRoot, "nested"));

        var duplicate = await BuildAsync(duplicateRoot);
        var directory = await BuildAsync(directoryRoot);

        AssertSdkRejected(duplicate);
        AssertSdkRejected(directory);
    }

    [Fact]
    public async Task SimulatedSdkReparsePointAndLowLimitAreRejected()
    {
        var (root, sdkRoot) = await ScaffoldAsync("SdkBounds");
        var propsPath = Path.Combine(sdkRoot, "Rekall.Age.Sdk.props");
        var reparseVerifier = new RekallAgeModuleSdkIntegrityVerifier(
            new RekallAgeModuleSdkIntegrityLimits(),
            path => Path.GetFullPath(path).Equals(Path.GetFullPath(propsPath), StringComparison.OrdinalIgnoreCase)
                ? FileAttributes.Normal | FileAttributes.ReparsePoint
                : File.GetAttributes(path));
        var boundedVerifier = new RekallAgeModuleSdkIntegrityVerifier(
            new RekallAgeModuleSdkIntegrityLimits(MaximumFiles: 4));

        var reparse = await BuildAsync(root, reparseVerifier);
        var bounded = await BuildAsync(root, boundedVerifier);

        AssertSdkRejected(reparse);
        AssertSdkRejected(bounded);
    }

    private static void AssertSdkRejected(RekallAgeCommandResult<BuildModulesResult> result)
    {
        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Code == "REKALL_MODULE_SDK_INTEGRITY_FAILED");
    }

    private static async Task<(string Root, string SdkRoot)> ScaffoldAsync(string moduleName)
    {
        var root = TestPaths.CreateTempDirectory();
        var scaffold = await new ScaffoldPlayableModuleCommand().ExecuteAsync(
            new ScaffoldPlayableModuleRequest(root, $"test.{moduleName.ToLowerInvariant()}", moduleName, moduleName),
            CreateContext("sdk scaffold"));
        Assert.True(scaffold.Ok, scaffold.Summary);
        return (root, Path.Combine(root, ".rekall", "sdk", "1"));
    }

    private static async Task RewriteLocalInventoryAsync(string sdkRoot, string fileName)
    {
        var manifestPath = Path.Combine(sdkRoot, "rekall.sdk.json");
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))!.AsObject();
        var file = manifest["files"]!.AsArray()
            .Select(item => item!.AsObject())
            .Single(item => item["path"]!.GetValue<string>() == fileName);
        var bytes = await File.ReadAllBytesAsync(Path.Combine(sdkRoot, fileName));
        file["sizeBytes"] = bytes.LongLength;
        file["sha256"] = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        await File.WriteAllTextAsync(manifestPath, manifest.ToJsonString());
    }

    private static ValueTask<RekallAgeCommandResult<BuildModulesResult>> BuildAsync(
        string root,
        RekallAgeModuleSdkIntegrityVerifier? verifier = null)
    {
        var command = verifier is null
            ? new BuildModulesCommand()
            : new BuildModulesCommand(new RekallAgeModuleBuildPolicy(), verifier);
        return command.ExecuteAsync(new BuildModulesRequest(root), CreateContext("sdk build"));
    }

    private static RekallAgeCommandContext CreateContext(string name) => new(
        "test",
        RekallAgeTransaction.Begin(name),
        CancellationToken.None);
}
