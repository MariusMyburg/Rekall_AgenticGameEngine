using System.Text.Json;
using System.Text.Json.Nodes;
using System.IO.Compression;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modules;
using Rekall.Age.Workflows.Commands;
using Rekall.Age.Modules.Security;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Workflows;

public sealed class PlayablePackageIntegrityTests
{
    [Fact]
    public async Task PackageCopyStopsWhenFinalModuleTrustPreflightFails()
    {
        var root = TestPaths.CreateTempDirectory();
        var initialOutput = Path.Combine(TestPaths.CreateTempDirectory(), "InitialTrustPackage");
        var output = Path.Combine(TestPaths.CreateTempDirectory(), "RejectedTrustPackage");
        var context = new RekallAgeCommandContext(
            "package-trust-preflight-test",
            RekallAgeTransaction.Begin("package trust preflight"),
            CancellationToken.None);
        var authored = await new RunAgentAuthoringGauntletCommand().ExecuteAsync(
            new RunAgentAuthoringGauntletRequest(root, "Package Trust Game", "Main", initialOutput),
            context);
        Assert.True(authored.Ok, authored.Summary);
        var moduleDirectory = Assert.Single(Directory.EnumerateDirectories(Path.Combine(root, "Modules")));
        var moduleName = Path.GetFileName(moduleDirectory);
        var assemblyPath = Path.Combine(moduleDirectory, "bin", "rekall", "net10.0", $"{moduleName}.dll");
        var inspector = new RekallAgeProjectModuleTrustInspector(
            readAttributes: path => Path.GetFullPath(path).Equals(Path.GetFullPath(assemblyPath), StringComparison.OrdinalIgnoreCase)
                ? FileAttributes.Normal | FileAttributes.ReparsePoint
                : File.GetAttributes(path));

        var packaged = await new PackagePlayableGameCommand(inspector).ExecuteAsync(
            new PackagePlayableGameRequest(root, "Main", output),
            context);

        Assert.False(packaged.Ok);
        Assert.Contains(packaged.Errors, error => error.Code == "REKALL_MODULE_TRUST_REPARSE_POINT");
        Assert.False(Directory.Exists(Path.Combine(output, "Game")));
    }

    [Fact]
    public async Task GraphicsPackageIncludesDeterministicProofPlayerForCaptureAndAudit()
    {
        var root = TestPaths.CreateTempDirectory();
        var output = Path.Combine(TestPaths.CreateTempDirectory(), "GraphicsPackage");
        var context = new RekallAgeCommandContext(
            "graphics-package-proof-test",
            RekallAgeTransaction.Begin("graphics package proof"),
            CancellationToken.None);
        var authored = await new RunAgentAuthoringGauntletCommand().ExecuteAsync(
            new RunAgentAuthoringGauntletRequest(
                root,
                "Graphics Proof Game",
                "Main",
                Path.Combine(TestPaths.CreateTempDirectory(), "InitialPackage")),
            context);
        Assert.True(authored.Ok, authored.Summary);

        var packaged = await new PackagePlayableGameCommand().ExecuteAsync(
            new PackagePlayableGameRequest(root, "Main", output, Graphics: true),
            context);

        Assert.True(packaged.Ok, packaged.Summary);
        var inspection = await new InspectPlayablePackageCommand().ExecuteAsync(
            new InspectPlayablePackageRequest(output),
            context);
        Assert.True(inspection.Ok, inspection.Summary);
        Assert.EndsWith("Rekall.Age.Player.Windows.exe", inspection.Value.Manifest.LaunchPath, StringComparison.Ordinal);
        Assert.EndsWith("Rekall.Age.Player.exe", inspection.Value.Manifest.ProofLaunchPath, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(output, inspection.Value.Manifest.ProofLaunchPath!.Replace('/', Path.DirectorySeparatorChar))));

        var capture = await new CapturePlayablePackageFrameCommand().ExecuteAsync(
            new CapturePlayablePackageFrameRequest(
                output,
                Path.Combine(root, "Builds", "GraphicsProof"),
                FrameIndex: 1),
            context);
        Assert.True(capture.Ok, capture.Summary);
        Assert.True(capture.Value.NonBlank);
        Assert.True(capture.Value.FrameAnalysis.Analyzed);
        Assert.True(capture.Value.FrameAnalysis.VisuallyInformative);
        Assert.Equal("package_play_frame_001.png", Path.GetFileName(capture.Value.OutputPath));
        Assert.Equal("runtime-viewport", capture.Value.Kind);
        Assert.Contains("sprite", capture.Value.DrawCommandKinds);

        var audit = await new AuditPlayablePackageCommand().ExecuteAsync(
            new AuditPlayablePackageRequest(
                output,
                Path.Combine(root, "Builds", "GraphicsAudit")),
            context);
        Assert.True(audit.Ok, audit.Summary);
        Assert.True(audit.Value.Ready);
        Assert.Contains(
            audit.Value.Checks,
            check => check.Name == "informative-frame" && check.Passed);

        var relocated = await new RelocatePlayablePackageCommand().ExecuteAsync(
            new RelocatePlayablePackageRequest(
                output,
                Path.Combine(TestPaths.CreateTempDirectory(), "RelocatedGraphicsPackage")),
            context);
        Assert.True(relocated.Ok, relocated.Summary);
        var relocatedAudit = await new AuditPlayablePackageCommand().ExecuteAsync(
            new AuditPlayablePackageRequest(
                relocated.Value.PackagePath,
                Path.Combine(root, "Builds", "RelocatedGraphicsAudit")),
            context);
        Assert.True(relocatedAudit.Ok, relocatedAudit.Summary);
        Assert.True(relocatedAudit.Value.Ready);
        Assert.True(relocatedAudit.Value.Capture.FrameAnalysis.VisuallyInformative);

        var insufficientDestination = Path.Combine(TestPaths.CreateTempDirectory(), "InsufficientSpacePackage");
        var insufficientSpace = await new RelocatePlayablePackageCommand(_ => 0).ExecuteAsync(
            new RelocatePlayablePackageRequest(output, insufficientDestination),
            context);
        Assert.False(insufficientSpace.Ok);
        var capacityError = Assert.Single(
            insufficientSpace.Errors,
            error => error.Code == "REKALL_PACKAGE_RELOCATION_SPACE_INSUFFICIENT");
        Assert.Contains("do not retry", capacityError.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(insufficientDestination));
        Assert.Empty(Directory.EnumerateDirectories(
            Path.GetDirectoryName(insufficientDestination)!,
            ".rekall-relocate-*",
            SearchOption.TopDirectoryOnly));

        var unsafeAudit = await new AuditPlayablePackageCommand().ExecuteAsync(
            new AuditPlayablePackageRequest(output, output),
            context);
        Assert.False(unsafeAudit.Ok);
        var unsafeOutput = Assert.Single(
            unsafeAudit.Errors,
            error => error.Code == "REKALL_PACKAGE_PROOF_OUTPUT_UNSAFE");
        var retryAudit = Assert.Single(unsafeOutput.SuggestedCommands!);
        Assert.Equal("rekall.workflow.audit_playable_package", retryAudit.Tool);
        Assert.False(File.Exists(Path.Combine(output, "package_play_frame_001.png")));

        var inspectionAfterRejectedAudit = await new InspectPlayablePackageCommand().ExecuteAsync(
            new InspectPlayablePackageRequest(output),
            context);
        Assert.True(inspectionAfterRejectedAudit.Ok, inspectionAfterRejectedAudit.Summary);
    }

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
        Directory.CreateDirectory(Path.Combine(root, ".rekall", "cache"));
        await File.WriteAllTextAsync(Path.Combine(root, ".rekall", "cache", "sdk-cache.dll"), "cache");
        await File.WriteAllTextAsync(Path.Combine(root, "DevOnly.cs"), "// authored source");
        await File.WriteAllTextAsync(Path.Combine(root, "DevOnly.csproj"), "<Project />");
        await File.WriteAllTextAsync(Path.Combine(root, "local.env"), "SECRET=do-not-ship");
        var importedAudio = Path.Combine(root, "Assets", "audio", "asset-tone.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(importedAudio)!);
        await File.WriteAllBytesAsync(importedAudio, CreatePcm16Wave());
        await File.WriteAllTextAsync(
            Path.Combine(root, "Assets", "assets.age.catalog.json"),
            $$"""
              {
                "assets": [
                  {
                    "id": "asset-tone",
                    "name": "tone",
                    "displayName": "Tone",
                    "kind": "audio",
                    "sourcePath": "{{Path.Combine(root, "authoring", "tone.wav").Replace("\\", "\\\\")}}",
                    "importedPath": "{{importedAudio.Replace("\\", "\\\\")}}",
                    "contentHash": "test"
                  }
                ]
              }
              """);
        var sceneStore = new RekallAgeSceneStore();
        var scene = await sceneStore.LoadAsync(root, "Main", CancellationToken.None);
        scene = scene
            .AddEntity(RekallAgeEntityDocument.Create("Audio Listener", ["audio"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject()))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.AudioListener", new JsonObject())))
            .AddEntity(RekallAgeEntityDocument.Create("Tone", ["audio"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject()))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.AudioEmitter",
                    new JsonObject { ["Clip"] = "asset-tone", ["PlayOnStart"] = true, ["Loop"] = true })))
            .AddEntity(RekallAgeEntityDocument.Create("HUD", ["ui"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.UiCanvas",
                    new JsonObject { ["referenceWidth"] = 320, ["referenceHeight"] = 180 })))
            .AddEntity(RekallAgeEntityDocument.Create("Status", ["ui"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Button",
                    new JsonObject { ["x"] = 10, ["y"] = 10, ["width"] = 100, ["height"] = 30, ["text"] = "Ready" })))
            .AddEntity(RekallAgeEntityDocument.Create("Animated", ["actor"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject { ["x"] = 0 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.AnimationClip",
                    new JsonObject
                    {
                        ["version"] = 1,
                        ["durationSeconds"] = 1,
                        ["tracks"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["component"] = "Rekall.Transform3D",
                                ["property"] = "x",
                                ["keys"] = new JsonArray
                                {
                                    new JsonObject { ["time"] = 0, ["value"] = 0 },
                                    new JsonObject { ["time"] = 1, ["value"] = 10 }
                                }
                            }
                        }
                    }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.AnimationPlayer",
                    new JsonObject { ["playing"] = true, ["loopMode"] = "clamp" })));
        await sceneStore.SaveAsync(root, scene, CancellationToken.None);

        var result = await new PackagePlayableGameCommand().ExecuteAsync(
            new PackagePlayableGameRequest(root, "Main", output),
            context);

        Assert.True(result.Ok, result.Summary);
        var boundedInspection = await new InspectPlayablePackageCommand(
            maximumEntries: 1,
            maximumEntrySizeBytes: long.MaxValue,
            maximumPackageSizeBytes: long.MaxValue).ExecuteAsync(
                new InspectPlayablePackageRequest(output),
                context);
        Assert.False(boundedInspection.Ok);
        Assert.Contains(
            boundedInspection.Errors,
            error => error.Code == "REKALL_PACKAGE_DIRECTORY_LIMIT_EXCEEDED");
        var reparseInspection = await new InspectPlayablePackageCommand(
            maximumEntries: int.MaxValue,
            maximumEntrySizeBytes: long.MaxValue,
            maximumPackageSizeBytes: long.MaxValue,
            readAttributes: path => Path.GetFileName(path).Equals("Game", StringComparison.Ordinal)
                ? FileAttributes.Directory | FileAttributes.ReparsePoint
                : File.GetAttributes(path)).ExecuteAsync(
                    new InspectPlayablePackageRequest(output),
                    context);
        Assert.False(reparseInspection.Ok);
        Assert.Contains(
            reparseInspection.Errors,
            error => error.Code == "REKALL_PACKAGE_PATH_REPARSE_POINT");

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
        var packagedCatalogText = await File.ReadAllTextAsync(
            Path.Combine(output, "Game", "Assets", "assets.age.catalog.json"));
        Assert.DoesNotContain(root, packagedCatalogText, StringComparison.OrdinalIgnoreCase);
        using var catalog = JsonDocument.Parse(packagedCatalogText);
        var packagedAsset = Assert.Single(catalog.RootElement.GetProperty("assets").EnumerateArray());
        Assert.Equal(string.Empty, packagedAsset.GetProperty("sourcePath").GetString());
        Assert.Equal("Assets/audio/asset-tone.wav", packagedAsset.GetProperty("importedPath").GetString());

        var packagedRun = await new RunPlayablePackageCommand().ExecuteAsync(
            new RunPlayablePackageRequest(output, Frames: 30),
            context);
        Assert.True(packagedRun.Ok, packagedRun.Summary);
        var runtimeState = Assert.IsType<Rekall.Age.Playback.RekallAgePlaybackRuntimeState>(
            packagedRun.Value.RenderFrames[^1].RuntimeState);
        Assert.Equal(1, runtimeState.UiElementCount);
        Assert.Equal(1, runtimeState.AnimationPlayerCount);
        Assert.Equal(1, runtimeState.AudioVoiceCount);
        Assert.DoesNotContain(runtimeState.Observations, observation => observation.Subsystem == "audio");
        Assert.Equal(5, runtimeState.Entities.Single(entity => entity.Name == "Animated").X, precision: 3);

        var relocatedDirectory = Path.Combine(TestPaths.CreateTempDirectory(), "RelocatedPackage");
        var relocation = await new RelocatePlayablePackageCommand().ExecuteAsync(
            new RelocatePlayablePackageRequest(output, relocatedDirectory),
            context);
        Assert.True(relocation.Ok, relocation.Summary);
        Assert.True(relocation.Value.Ready);
        Assert.Equal(Path.GetFullPath(relocatedDirectory), relocation.Value.PackagePath);
        Assert.True(File.Exists(relocation.Value.ManifestPath));
        var relocatedDirectoryRun = await new RunPlayablePackageCommand().ExecuteAsync(
            new RunPlayablePackageRequest(relocation.Value.PackagePath, Frames: 30),
            context);
        Assert.True(relocatedDirectoryRun.Ok, relocatedDirectoryRun.Summary);

        var relocationRoot = TestPaths.CreateTempDirectory();
        var relocatedArchive = Path.Combine(relocationRoot, "audio-game-relocated.zip");
        ZipFile.CreateFromDirectory(output, relocatedArchive, CompressionLevel.Fastest, includeBaseDirectory: false);
        var relocatedRun = await new RunPlayablePackageCommand().ExecuteAsync(
            new RunPlayablePackageRequest(relocatedArchive, Frames: 30),
            context);
        Assert.True(relocatedRun.Ok, relocatedRun.Summary);
        var relocatedRuntimeState = Assert.IsType<Rekall.Age.Playback.RekallAgePlaybackRuntimeState>(
            relocatedRun.Value.RenderFrames[^1].RuntimeState);
        Assert.Equal(1, relocatedRuntimeState.AudioVoiceCount);
        Assert.DoesNotContain(relocatedRuntimeState.Observations, observation => observation.Subsystem == "audio");

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
    public async Task InspectExecutableReturnsStructuredPackagePathDiagnostic()
    {
        var executable = Path.Combine(TestPaths.CreateTempDirectory(), "Player.exe");
        await File.WriteAllBytesAsync(executable, [0x4d, 0x5a, 0x00, 0x00]);
        var context = new RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("inspect invalid package path"),
            CancellationToken.None);

        var result = await new InspectPlayablePackageCommand().ExecuteAsync(
            new InspectPlayablePackageRequest(executable),
            context);

        Assert.False(result.Ok);
        var error = Assert.Single(result.Errors, item => item.Code == "REKALL_PACKAGE_PATH_KIND_INVALID");
        Assert.Contains("OutputDirectory", error.Message, StringComparison.Ordinal);
        Assert.Contains("ArchivePath", error.Message, StringComparison.Ordinal);
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

        var changedAfterInspectionZip = Path.Combine(relocationRoot, "changed-after-inspection.zip");
        ZipFile.CreateFromDirectory(
            packageDirectory,
            changedAfterInspectionZip,
            CompressionLevel.Fastest,
            includeBaseDirectory: false);
        var changedDestination = Path.Combine(relocationRoot, "changed-destination");
        var changedSource = await new RelocatePlayablePackageCommand(_ =>
        {
            using var archive = ZipFile.Open(changedAfterInspectionZip, ZipArchiveMode.Update);
            using var writer = new StreamWriter(archive.CreateEntry("../escaped-during-relocation.txt").Open());
            writer.Write("must never be extracted");
            return long.MaxValue;
        }).ExecuteAsync(
            new RelocatePlayablePackageRequest(changedAfterInspectionZip, changedDestination),
            context);

        Assert.False(changedSource.Ok);
        Assert.Contains(
            changedSource.Errors,
            error => error.Code == "REKALL_PACKAGE_RELOCATION_SOURCE_CHANGED");
        Assert.False(Directory.Exists(changedDestination));
        Assert.False(File.Exists(Path.Combine(relocationRoot, "escaped-during-relocation.txt")));
        Assert.Empty(Directory.EnumerateDirectories(
            relocationRoot,
            ".rekall-relocate-*",
            SearchOption.TopDirectoryOnly));

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

    private static byte[] CreatePcm16Wave()
    {
        const int sampleRate = 48_000;
        const short channels = 1;
        var samples = Enumerable.Range(0, sampleRate)
            .Select(index => (short)(Math.Sin(2 * Math.PI * 440 * index / sampleRate) * 8_000))
            .ToArray();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        var dataLength = samples.Length * sizeof(short);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * sizeof(short));
        writer.Write((short)(channels * sizeof(short)));
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataLength);
        foreach (var sample in samples)
        {
            writer.Write(sample);
        }

        writer.Flush();
        return stream.ToArray();
    }
}
