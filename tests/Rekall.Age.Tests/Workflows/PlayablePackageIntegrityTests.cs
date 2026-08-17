using System.Text.Json;
using System.IO.Compression;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modules;
using Rekall.Age.Workflows.Commands;

namespace Rekall.Age.Tests.Workflows;

public sealed class PlayablePackageIntegrityTests
{
    [Fact]
    public async Task PackageManifestIsRelativeHashedAndExcludesAuthoringFiles()
    {
        var root = TestPaths.CreateTempDirectory();
        var output = Path.Combine(TestPaths.CreateTempDirectory(), "PortableGame");
        var context = new RekallAgeCommandContext(
            "package-integrity-test",
            RekallAgeTransaction.Begin("package portable game"),
            CancellationToken.None);
        var authored = await new RunAgentAuthoringGauntletCommand().ExecuteAsync(
            new RunAgentAuthoringGauntletRequest(
                root,
                "Portable Game",
                "Main",
                Path.Combine(TestPaths.CreateTempDirectory(), "InitialPackage")),
            context);
        Assert.True(authored.Ok, authored.Summary);
        Directory.CreateDirectory(Path.Combine(root, ".rekall", "sdk", "1"));
        await File.WriteAllTextAsync(Path.Combine(root, ".rekall", "sdk", "1", "sdk-cache.dll"), "cache");
        await File.WriteAllTextAsync(Path.Combine(root, "DevOnly.cs"), "// authored source");
        await File.WriteAllTextAsync(Path.Combine(root, "DevOnly.csproj"), "<Project />");
        await File.WriteAllTextAsync(Path.Combine(root, "local.env"), "SECRET=do-not-ship");

        var result = await new PackagePlayableGameCommand().ExecuteAsync(
            new PackagePlayableGameRequest(root, "Main", output),
            context);

        Assert.True(result.Ok, result.Summary);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(result.Value.ManifestPath));
        var manifest = document.RootElement;
        Assert.Equal(2, manifest.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("0.1.0-preview.1", manifest.GetProperty("productVersion").GetString());
        Assert.Equal("Game", manifest.GetProperty("gameRoot").GetString());
        Assert.False(Path.IsPathRooted(manifest.GetProperty("launchPath").GetString()));
        Assert.All(
            manifest.GetProperty("arguments").EnumerateArray(),
            argument => Assert.DoesNotContain(root, argument.GetString(), StringComparison.OrdinalIgnoreCase));

        var files = manifest.GetProperty("files").EnumerateArray().ToArray();
        Assert.NotEmpty(files);
        Assert.All(files, file =>
        {
            var path = file.GetProperty("path").GetString();
            Assert.NotNull(path);
            Assert.False(Path.IsPathRooted(path));
            Assert.DoesNotContain('\\', path);
            Assert.Matches("^[0-9a-f]{64}$", file.GetProperty("sha256").GetString());
            Assert.True(file.GetProperty("sizeBytes").GetInt64() >= 0);
        });

        var packagedFiles = Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(output, path).Replace('\\', '/'))
            .ToArray();
        Assert.DoesNotContain(packagedFiles, path => path.Contains("/.rekall/", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(packagedFiles, path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(packagedFiles, path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(packagedFiles, path => path.EndsWith(".env", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(packagedFiles, path => path.EndsWith("/AgentGauntlet.dll", StringComparison.Ordinal));
        Assert.Single(RekallAgeProjectModuleAssemblyLoader.LoadBuiltModuleAssemblies(Path.Combine(output, "Game")));

        await File.AppendAllTextAsync(Path.Combine(output, "Game", "rekall.project.json"), " ");
        await File.WriteAllTextAsync(Path.Combine(output, "unexpected.txt"), "not declared");
        var tampered = await new InspectPlayablePackageCommand().ExecuteAsync(
            new InspectPlayablePackageRequest(output),
            context);

        Assert.False(tampered.Ok);
        Assert.Contains(tampered.Errors, error => error.Code == "REKALL_PACKAGE_HASH_MISMATCH");
        Assert.Contains(tampered.Errors, error => error.Code == "REKALL_PACKAGE_UNEXPECTED_FILE");
    }

    [Fact]
    public async Task RelocatedZipAuditsAndUnsafeArchiveIsRejectedBeforeExecution()
    {
        var root = TestPaths.CreateTempDirectory();
        var packageDirectory = Path.Combine(TestPaths.CreateTempDirectory(), "PackagedGame");
        var context = new RekallAgeCommandContext(
            "package-relocation-test",
            RekallAgeTransaction.Begin("relocate portable game"),
            CancellationToken.None);
        var authored = await new RunAgentAuthoringGauntletCommand().ExecuteAsync(
            new RunAgentAuthoringGauntletRequest(
                root,
                "Relocated Game",
                "Main",
                packageDirectory),
            context);
        Assert.True(authored.Ok, authored.Summary);

        var relocationRoot = TestPaths.CreateTempDirectory();
        var relocatedZip = Path.Combine(relocationRoot, "renamed-and-relocated.zip");
        ZipFile.CreateFromDirectory(packageDirectory, relocatedZip, CompressionLevel.Fastest, includeBaseDirectory: false);

        var audit = await new AuditPlayablePackageCommand().ExecuteAsync(
            new AuditPlayablePackageRequest(relocatedZip, Path.Combine(relocationRoot, "proof")),
            context);

        Assert.True(audit.Ok, string.Join(Environment.NewLine, audit.Errors.Select(error => $"{error.Code}: {error.Message}")));
        Assert.True(audit.Value.Capture.NonBlank);
        Assert.True(File.Exists(audit.Value.Capture.OutputPath));

        using (var archive = ZipFile.Open(relocatedZip, ZipArchiveMode.Update))
        {
            var unsafeEntry = archive.CreateEntry("../escaped.txt");
            await using var writer = new StreamWriter(unsafeEntry.Open());
            await writer.WriteAsync("must never be extracted");
        }

        var inspection = await new InspectPlayablePackageCommand().ExecuteAsync(
            new InspectPlayablePackageRequest(relocatedZip),
            context);
        var run = await new RunPlayablePackageCommand().ExecuteAsync(
            new RunPlayablePackageRequest(relocatedZip),
            context);

        Assert.False(inspection.Ok);
        Assert.Contains(inspection.Errors, error => error.Code == "REKALL_PACKAGE_PATH_UNSAFE");
        Assert.False(run.Ok);
        Assert.Equal(-1, run.Value.ExitCode);
        Assert.Empty(run.Value.RenderFrames);
        Assert.Contains(run.Errors, error => error.Code == "REKALL_PACKAGE_PATH_UNSAFE");
        Assert.False(File.Exists(Path.Combine(relocationRoot, "escaped.txt")));
    }
}
