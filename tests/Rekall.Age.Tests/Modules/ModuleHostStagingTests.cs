using System.Security.Cryptography;
using System.Text.Json;
using Rekall.Age.Build.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Core.Product;
using Rekall.Age.Modules.Commands;
using Rekall.Age.Modules.Hosting;

namespace Rekall.Age.Tests.Modules;

public sealed class ModuleHostStagingTests
{
    [Fact]
    public async Task StageCopiesOnlyVerifiedHostAndModuleInventoryAndCleansUp()
    {
        var projectRoot = await CreateModuleAsync("StageFixture");
        var hostRoot = await CreateHostPayloadAsync();
        var sessionsRoot = TestPaths.CreateTempDirectory();
        string stagingRoot;

        await using (var staged = await new RekallAgeModuleHostStager(sessionsRoot).StageAsync(
            projectRoot,
            hostRoot,
            CancellationToken.None))
        {
            stagingRoot = staged.Root;
            Assert.True(File.Exists(staged.HostExecutablePath));
            Assert.True(File.Exists(staged.LoadPlanPath));
            var files = Directory.EnumerateFiles(staged.Root, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(staged.Root, path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            Assert.Contains("host/Rekall.Age.ModuleHost.exe", files);
            Assert.Contains("host/Rekall.Age.Modules.dll", files);
            Assert.Contains("modules/StageFixture/StageFixture.dll", files);
            Assert.Contains("rekall.module.host-plan.json", files);
            Assert.DoesNotContain(files, path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(files, path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(files, path => path.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(files, path => path.EndsWith("rekall.module.build.json", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(files, path => path.EndsWith("unmanifested.txt", StringComparison.OrdinalIgnoreCase));
            Assert.All(files, relative => Assert.True(
                (File.GetAttributes(Path.Combine(staged.Root, relative.Replace('/', Path.DirectorySeparatorChar))) & FileAttributes.ReadOnly) != 0,
                relative));
            var loaded = RekallAgeModuleHostVerifiedAssemblyLoader.Load(staged.LoadPlanPath);
            Assert.Single(loaded);
        }

        Assert.False(Directory.Exists(stagingRoot));
        Assert.Empty(Directory.EnumerateFileSystemEntries(sessionsRoot));
    }

    [Fact]
    public async Task TamperedHostPayloadFailsBeforePublicationAndLeavesNoSessionTree()
    {
        var projectRoot = await CreateModuleAsync("TamperedHostFixture");
        var hostRoot = await CreateHostPayloadAsync();
        var sessionsRoot = TestPaths.CreateTempDirectory();
        await File.AppendAllTextAsync(Path.Combine(hostRoot, "Rekall.Age.Modules.dll"), "tampered");

        var error = await Assert.ThrowsAsync<RekallAgeModuleHostException>(async () =>
            await new RekallAgeModuleHostStager(sessionsRoot).StageAsync(
                projectRoot,
                hostRoot,
                CancellationToken.None));

        Assert.Equal("REKALL_MODULE_HOST_STAGING_REJECTED", error.Code);
        Assert.Empty(Directory.EnumerateFileSystemEntries(sessionsRoot));
    }

    [Fact]
    public async Task TamperedProjectArtifactFailsAdmissionWithoutCopyingProjectContent()
    {
        var projectRoot = await CreateModuleAsync("TamperedProjectFixture");
        var assembly = Path.Combine(projectRoot, "Modules", "TamperedProjectFixture", "bin", "rekall", "net10.0", "TamperedProjectFixture.dll");
        await File.AppendAllTextAsync(assembly, "tampered");
        var hostRoot = await CreateHostPayloadAsync();
        var sessionsRoot = TestPaths.CreateTempDirectory();

        var error = await Assert.ThrowsAsync<RekallAgeModuleHostException>(async () =>
            await new RekallAgeModuleHostStager(sessionsRoot).StageAsync(
                projectRoot,
                hostRoot,
                CancellationToken.None));

        Assert.Equal("REKALL_MODULE_OUTPUT_SIZE_MISMATCH", error.Code);
        Assert.Empty(Directory.EnumerateFileSystemEntries(sessionsRoot));
    }

    [Theory]
    [InlineData("nested//alias.dll")]
    [InlineData("payload.dll:stream")]
    [InlineData("CON")]
    [InlineData("trailing.")]
    public async Task WindowsAliasPathsInHostManifestFailBeforeStaging(string unsafePath)
    {
        var projectRoot = await CreateModuleAsync("UnsafeHostPathFixture");
        var hostRoot = await CreateHostPayloadAsync();
        var manifestPath = Path.Combine(hostRoot, RekallAgeModuleHostPayloadManifest.FileName);
        var manifest = JsonSerializer.Deserialize<RekallAgeModuleHostPayloadManifest>(
            await File.ReadAllTextAsync(manifestPath),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(
                manifest with
                {
                    Files = manifest.Files.Append(new RekallAgeModuleHostPayloadFile(unsafePath, 0, new string('0', 64))).ToArray()
                },
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var sessionsRoot = TestPaths.CreateTempDirectory();

        var error = await Assert.ThrowsAsync<RekallAgeModuleHostException>(async () =>
            await new RekallAgeModuleHostStager(sessionsRoot).StageAsync(projectRoot, hostRoot, CancellationToken.None));

        Assert.Equal("REKALL_MODULE_HOST_STAGING_REJECTED", error.Code);
        Assert.Equal(Path.GetFullPath(manifestPath), Path.GetFullPath(error.Target));
        Assert.Empty(Directory.EnumerateFileSystemEntries(sessionsRoot));
    }

    [Fact]
    public async Task HostPayloadFromDifferentProductVersionFailsClosed()
    {
        var projectRoot = await CreateModuleAsync("WrongHostVersionFixture");
        var hostRoot = await CreateHostPayloadAsync();
        var manifestPath = Path.Combine(hostRoot, RekallAgeModuleHostPayloadManifest.FileName);
        var manifest = JsonSerializer.Deserialize<RekallAgeModuleHostPayloadManifest>(
            await File.ReadAllTextAsync(manifestPath),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(manifest with { ProductVersion = "999.0.0" }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        var error = await Assert.ThrowsAsync<RekallAgeModuleHostException>(async () =>
            await new RekallAgeModuleHostStager(TestPaths.CreateTempDirectory()).StageAsync(
                projectRoot,
                hostRoot,
                CancellationToken.None));

        Assert.Equal("REKALL_MODULE_HOST_STAGING_REJECTED", error.Code);
    }

    private static async Task<string> CreateModuleAsync(string moduleName)
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("test", RekallAgeTransaction.Begin("stage fixture"), CancellationToken.None);
        var scaffold = await new ScaffoldPlayableModuleCommand().ExecuteAsync(
            new ScaffoldPlayableModuleRequest(root, $"agent.{moduleName.ToLowerInvariant()}", moduleName, moduleName),
            context);
        Assert.True(scaffold.Ok, scaffold.Summary);
        var build = await new BuildModulesCommand().ExecuteAsync(new BuildModulesRequest(root), context);
        Assert.True(build.Ok, build.Summary);
        return root;
    }

    private static async Task<string> CreateHostPayloadAsync()
    {
        var root = TestPaths.CreateTempDirectory();
        await File.WriteAllTextAsync(Path.Combine(root, "Rekall.Age.ModuleHost.exe"), "host");
        await File.WriteAllTextAsync(Path.Combine(root, "Rekall.Age.Modules.dll"), "modules");
        await File.WriteAllTextAsync(Path.Combine(root, "unmanifested.txt"), "excluded");
        var files = new[] { "Rekall.Age.ModuleHost.exe", "Rekall.Age.Modules.dll" }
            .Select(name =>
            {
                var path = Path.Combine(root, name);
                return new RekallAgeModuleHostPayloadFile(
                    name,
                    new FileInfo(path).Length,
                    Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
            })
            .ToArray();
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
}
