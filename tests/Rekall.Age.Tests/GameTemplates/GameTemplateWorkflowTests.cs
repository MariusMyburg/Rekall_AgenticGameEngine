using Rekall.Age.Agent;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.GameTemplates;
using Rekall.Age.GameTemplates.Commands;
using Rekall.Age.Playback;
using Rekall.Age.Playback.Commands;
using Rekall.Age.Project;
using Rekall.Age.Rendering;
using Rekall.Age.Runtime;
using Rekall.Age.Validation;
using Rekall.Age.World;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rekall.Age.Tests.GameTemplates;

public sealed class GameTemplateWorkflowTests
{
    private static readonly string[] RequiredTemplateIds =
    [
        "pong",
        "breakout",
        "asteroids",
        "top-down-shooter",
        "platformer-2d",
        "tower-defense",
        "visual-novel",
        "first-person-exploration",
        "collectathon-3d",
        "puzzle"
    ];

    public static TheoryData<string> RequiredTemplates => new()
    {
        RequiredTemplateIds[0],
        RequiredTemplateIds[1],
        RequiredTemplateIds[2],
        RequiredTemplateIds[3],
        RequiredTemplateIds[4],
        RequiredTemplateIds[5],
        RequiredTemplateIds[6],
        RequiredTemplateIds[7],
        RequiredTemplateIds[8],
        RequiredTemplateIds[9]
    };

    [Theory]
    [MemberData(nameof(RequiredTemplates))]
    public async Task TemplateCreatesInspectableRunnableProject(string templateId)
    {
        var root = TestPaths.CreateTempDirectory();
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new CreateGameFromTemplateCommand());
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin($"create {templateId}"), CancellationToken.None);

        var result = await registry.ExecuteAsync<CreateGameFromTemplateRequest, CreateGameFromTemplateResult>(
            "rekall.workflow.create_game_from_template",
            new CreateGameFromTemplateRequest(root, $"Game {templateId}", templateId),
            context);

        Assert.True(result.Ok);
        Assert.Equal(templateId, result.Value.Template.Id);
        Assert.True(File.Exists(Path.Combine(root, "rekall.project.json")));
        Assert.True(File.Exists(Path.Combine(root, "Scenes", "Main.age.scene.json")));
        Assert.Equal(templateId, result.Value.Manifest.SourceTemplateId);
        var manifestJson = await File.ReadAllTextAsync(Path.Combine(root, "rekall.project.json"));
        Assert.Contains($$""""sourceTemplateId": "{{templateId}}"""", manifestJson, StringComparison.Ordinal);

        var sceneStore = new RekallAgeSceneStore();
        var validator = new RekallAgeProjectValidator(sceneStore);
        var summary = await new RekallAgeContextBuilder(new RekallAgeProjectStore(), sceneStore, validator)
            .BuildProjectSummaryAsync(root, CancellationToken.None);
        Assert.Equal("ok", summary.Health.Status);

        var runtime = await new RekallAgeHeadlessRuntime(sceneStore, validator)
            .RunAsync(root, "Main", TimeSpan.FromMilliseconds(20), CancellationToken.None);
        Assert.True(runtime.Ok);

        var screenshot = await new RekallAgeSoftwarePreview(sceneStore)
            .CaptureAsync(root, "Main", Path.Combine(root, "Artifacts", "Screenshots"), CancellationToken.None);
        Assert.True(screenshot.NonBlank);
    }

    [Fact]
    public void TemplateCatalogDescribesAgentRelevantCapabilities()
    {
        var catalog = RekallAgeGameTemplateCatalog.CreateDefault();

        Assert.Contains(catalog.Templates, template => template.Id == "first-person-exploration" && template.Capabilities.Contains("rendering3d"));
        Assert.Contains(catalog.Templates, template => template.Id == "visual-novel" && template.Capabilities.Contains("ui"));
    }

    [Fact]
    public void TemplateCatalogDoesNotUsePlaceholderAssetIds()
    {
        var catalog = RekallAgeGameTemplateCatalog.CreateDefault();
        var serialized = System.Text.Json.JsonSerializer.Serialize(catalog.Templates);

        Assert.DoesNotContain("placeholder", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TemplateScenesDoNotInjectCoreGameplayComponents()
    {
        var catalog = RekallAgeGameTemplateCatalog.CreateDefault();
        var allowedRekallTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "Rekall.Transform",
            "Rekall.Transform2D",
            "Rekall.Transform3D",
            "Rekall.InputActionMap",
            "Rekall.Camera2D",
            "Rekall.Camera3D",
            "Rekall.SpriteRenderer",
            "Rekall.MeshRenderer",
            "Rekall.MeshSet",
            "Rekall.GeometryPrimitive",
            "Rekall.GeometryMesh",
            "Rekall.GeometryExtrusion",
            "Rekall.DirectionalLight",
            "Rekall.PointLight",
            "Rekall.SpotLight",
            "Rekall.Rigidbody3D",
            "Rekall.BoxCollider3D",
            "Rekall.SphereCollider3D",
            "Rekall.CapsuleCollider3D",
            "Rekall.MeshCollider",
            "Rekall.AudioEmitter"
        };

        foreach (var template in catalog.Templates)
        {
            Assert.DoesNotContain(
                template.Entities.SelectMany(entity => entity.Components),
                component => component.Type.StartsWith("Rekall.", StringComparison.Ordinal)
                    && !allowedRekallTypes.Contains(component.Type));
            Assert.DoesNotContain(
                template.Entities.SelectMany(entity => entity.Components),
                component => component.Type.EndsWith(".GameRules", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void FirstPersonTemplatePublishesSemanticInputActionsForAgents()
    {
        var catalog = RekallAgeGameTemplateCatalog.CreateDefault();

        var template = catalog.GetRequired("first-person-exploration");
        var inputMap = Assert.Single(
            template.Entities.SelectMany(entity => entity.Components),
            component => component.Type == "Rekall.InputActionMap");
        var actions = Assert.IsType<JsonArray>(inputMap.Properties["actions"]);
        var actionObjects = actions.OfType<JsonObject>().ToArray();

        var moveX = Assert.Single(actionObjects, action => ReadString(action, "name") == "move.x");
        Assert.Equal("D", ReadString(moveX, "positiveKey"));
        Assert.Equal("A", ReadString(moveX, "negativeKey"));

        var moveZ = Assert.Single(actionObjects, action => ReadString(action, "name") == "move.z");
        Assert.Equal("W", ReadString(moveZ, "positiveKey"));
        Assert.Equal("S", ReadString(moveZ, "negativeKey"));

        var lookX = Assert.Single(actionObjects, action => ReadString(action, "name") == "look.x");
        Assert.Equal("x", ReadString(lookX, "mouseAxis"));
        Assert.Equal(-0.13, ReadNumber(lookX, "mouseScale"), precision: 6);

        var lookY = Assert.Single(actionObjects, action => ReadString(action, "name") == "look.y");
        Assert.Equal("y", ReadString(lookY, "mouseAxis"));
        Assert.Equal(0.09, ReadNumber(lookY, "mouseScale"), precision: 6);

        Assert.Contains(actionObjects, action => ReadString(action, "name") == "fire" && ReadString(action, "button") == "Left");
        Assert.Contains(actionObjects, action => ReadString(action, "name") == "fire" && ReadString(action, "key") == "Space");
        Assert.Contains(actionObjects, action => ReadString(action, "name") == "move.fast" && ReadString(action, "key") == "LeftShift");
    }

    [Fact]
    public void TemplateCatalogExposesAgentRenderableDrawContracts()
    {
        var catalog = RekallAgeGameTemplateCatalog.CreateDefault();

        var breakout = catalog.GetRequired("breakout");
        Assert.Contains(breakout.DrawCommands, command => command.Id == "brick-field" && command.Kind == "rect");
        Assert.Contains(breakout.DrawCommands, command => command.Id == "ball" && command.Kind == "circle");

        var visualNovel = catalog.GetRequired("visual-novel");
        Assert.Contains(visualNovel.DrawCommands, command => command.Id == "dialogue-box" && command.Kind == "rect");
        Assert.Contains(visualNovel.DrawCommands, command => command.Id == "choice-cursor" && command.Kind == "text");

        var collectathon = catalog.GetRequired("collectathon-3d");
        Assert.Contains(collectathon.DrawCommands, command => command.Id == "camera-orbit" && command.Kind == "rect");
        Assert.Contains(collectathon.DrawCommands, command => command.Id == "goal-gate" && command.Kind == "text");
    }

    [Fact]
    public async Task InspectTemplateReturnsAgentWorkflowGuidance()
    {
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("inspect template"), CancellationToken.None);

        var result = await new InspectGameTemplateCommand().ExecuteAsync(
            new InspectGameTemplateRequest("pong"),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.Equal("pong", result.Value.Template.Id);
        Assert.Contains(result.Value.Template.DrawCommands, command => command.Id == "ball" && command.Kind == "circle");
        Assert.Contains(result.Value.SuggestedCommands, command => command.Tool == "rekall.workflow.create_playable_package_from_template");
        var packageCommand = result.Value.SuggestedCommands.Single(command => command.Tool == "rekall.workflow.create_playable_package_from_template");
        Assert.Equal("pong", packageCommand.Arguments["templateId"]);
        Assert.Equal("Main", packageCommand.Arguments["sceneName"]);
    }

    [Fact]
    public async Task VerifyPlayableGamePassesForBuiltPlayableTemplate()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("verify playable"), CancellationToken.None);
        var create = await new CreatePlayableGameFromTemplateCommand().ExecuteAsync(
            new CreatePlayableGameFromTemplateRequest(root, "Verified Pong", "pong"),
            context);
        Assert.True(create.Ok, create.Summary);

        var result = await new VerifyPlayableGameCommand().ExecuteAsync(
            new VerifyPlayableGameRequest(
                root,
                "Main",
                2,
                [
                    new RekallAgePlaybackInput(1, PrimaryAction: true),
                    new RekallAgePlaybackInput(-1)
                ],
                [
                    new RekallAgeFrameAssertion(0, "Score 10"),
                    new RekallAgeFrameAssertion(1, "Left paddle lane 0")
                ]),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.True(result.Value.Ready);
        Assert.All(result.Value.Checks, check => Assert.True(check.Passed, check.Summary));
        Assert.True(result.Value.BuildSucceeded);
        Assert.True(result.Value.PlaytestPassed);
        Assert.Contains(result.Value.DrawAssertions, assertion =>
            assertion.Id == "ball" &&
            assertion.Kind == "circle" &&
            assertion.Passed);
    }

    [Fact]
    public async Task VerifyPlayableGamePassesStructuredDrawAssertionsThroughReadinessCheck()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("verify playable draws"), CancellationToken.None);
        var create = await new CreatePlayableGameFromTemplateCommand().ExecuteAsync(
            new CreatePlayableGameFromTemplateRequest(root, "Verified Draw Pong", "pong"),
            context);
        Assert.True(create.Ok, create.Summary);

        var result = await new VerifyPlayableGameCommand().ExecuteAsync(
            new VerifyPlayableGameRequest(
                root,
                "Main",
                1,
                [
                    new RekallAgePlaybackInput(1, PrimaryAction: true)
                ],
                [
                    new RekallAgeFrameAssertion(0, "Score 10")
                ],
                [
                    new RekallAgeDrawCommandAssertion(0, Id: "ball", Kind: "circle"),
                    new RekallAgeDrawCommandAssertion(0, Kind: "text", TextContains: "Score 10")
                ]),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.True(result.Value.Ready);
        Assert.All(result.Value.DrawAssertions, assertion => Assert.True(assertion.Passed));
        Assert.Contains(result.Value.RenderFrames[0].DrawCommands, command => command.Id == "ball" && command.Kind == "circle");
    }

    [Fact]
    public async Task VerifyPlayableGameFailsWithStructuredChecksWhenPlayableModuleIsMissing()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("verify missing playable"), CancellationToken.None);
        var create = await new CreateGameFromTemplateCommand().ExecuteAsync(
            new CreateGameFromTemplateRequest(root, "Unbuilt Pong", "pong"),
            context);
        Assert.True(create.Ok, create.Summary);

        var result = await new VerifyPlayableGameCommand().ExecuteAsync(
            new VerifyPlayableGameRequest(root, "Main", 1),
            context);

        Assert.False(result.Ok);
        Assert.False(result.Value.Ready);
        Assert.Contains(result.Value.Checks, check => check.Name == "module-build" && !check.Passed);
        Assert.Contains(result.Errors, error => error.Code == "REKALL_PLAYABLE_GAME_NOT_READY");
    }

    [Fact]
    public async Task PackagePlayableGameVerifiesAndPublishesPlayer()
    {
        var root = TestPaths.CreateTempDirectory();
        var output = Path.Combine(root, "Builds", "PackagedPlayer");
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("package playable"), CancellationToken.None);
        var create = await new CreatePlayableGameFromTemplateCommand().ExecuteAsync(
            new CreatePlayableGameFromTemplateRequest(root, "Packaged Pong", "pong"),
            context);
        Assert.True(create.Ok, create.Summary);

        var package = await new PackagePlayableGameCommand().ExecuteAsync(
            new PackagePlayableGameRequest(
                root,
                "Main",
                output,
                Frames: 1,
                Assertions: [new RekallAgeFrameAssertion(0, "PONG")]),
            context);

        Assert.True(package.Ok, package.Summary);
        Assert.True(package.Value.Ready);
        Assert.True(File.Exists(package.Value.LaunchPath), package.Value.LaunchPath);
        Assert.Equal(output, package.Value.OutputDirectory);
        Assert.True(File.Exists(package.Value.ManifestPath), package.Value.ManifestPath);
        Assert.True(File.Exists(package.Value.ArchivePath), package.Value.ArchivePath);
        var bundledGameRoot = Path.Combine(output, "Game");
        Assert.True(File.Exists(Path.Combine(bundledGameRoot, "rekall.project.json")));
        Assert.True(File.Exists(Path.Combine(bundledGameRoot, "Scenes", "Main.age.scene.json")));
        Assert.True(File.Exists(Path.Combine(bundledGameRoot, "Modules", "PongPlayable", "bin", "Debug", "net10.0", "PongPlayable.dll")));
        Assert.Contains(bundledGameRoot, package.Value.Arguments);
        Assert.DoesNotContain(root, package.Value.Arguments);
        Assert.Contains("Main", package.Value.Arguments);

        var packagedPlay = await new PlaySceneCommand().ExecuteAsync(
            new PlaySceneRequest(bundledGameRoot, "Main", 1),
            context);
        Assert.True(packagedPlay.Ok, packagedPlay.Summary);
        Assert.Contains("PONG", packagedPlay.Value.Frames[0], StringComparison.Ordinal);

        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(package.Value.ManifestPath));
        Assert.Equal("rekall.age.playable.package", manifest.RootElement.GetProperty("kind").GetString());
        Assert.Equal("Main", manifest.RootElement.GetProperty("sceneName").GetString());
        Assert.Equal("pong", manifest.RootElement.GetProperty("sourceTemplateId").GetString());
        Assert.Equal(package.Value.LaunchPath, manifest.RootElement.GetProperty("launchPath").GetString());
        Assert.Equal(bundledGameRoot, manifest.RootElement.GetProperty("gameRoot").GetString());
        Assert.True(manifest.RootElement.GetProperty("checks").GetArrayLength() > 0);
        var drawCommands = manifest.RootElement.GetProperty("drawCommands");
        Assert.Contains(drawCommands.EnumerateArray(), command =>
            command.GetProperty("id").GetString() == "ball" &&
            command.GetProperty("kind").GetString() == "circle");
        var drawAssertions = manifest.RootElement.GetProperty("drawAssertions");
        Assert.Contains(drawAssertions.EnumerateArray(), assertion =>
            assertion.GetProperty("id").GetString() == "ball" &&
            assertion.GetProperty("kind").GetString() == "circle" &&
            assertion.GetProperty("passed").GetBoolean());

        using var archive = ZipFile.OpenRead(package.Value.ArchivePath);
        Assert.Contains(archive.Entries, entry => entry.FullName == "rekall.package.json");
        Assert.Contains(archive.Entries, entry => entry.FullName == "Game/rekall.project.json");
        Assert.Contains(archive.Entries, entry => entry.FullName.EndsWith("PongPlayable.dll", StringComparison.Ordinal));

        var inspectManifest = await new InspectPlayablePackageCommand().ExecuteAsync(
            new InspectPlayablePackageRequest(package.Value.ManifestPath),
            context);
        var inspectArchive = await new InspectPlayablePackageCommand().ExecuteAsync(
            new InspectPlayablePackageRequest(package.Value.ArchivePath),
            context);

        Assert.True(inspectManifest.Ok, inspectManifest.Summary);
        Assert.True(inspectArchive.Ok, inspectArchive.Summary);
        Assert.True(inspectArchive.Value.Ready);
        Assert.Equal("pong", inspectArchive.Value.Manifest.SourceTemplateId);
        Assert.Contains(inspectArchive.Value.Manifest.DrawCommands, command => command.Id == "ball" && command.Kind == "circle");
        Assert.Contains(inspectArchive.Value.Manifest.DrawAssertions, assertion => assertion.Id == "ball" && assertion.Passed);
        Assert.Contains(inspectArchive.Value.Files, file => file.Path == "rekall.package.json" && file.IsKeyArtifact);
        Assert.Contains(inspectArchive.Value.Files, file => file.Path == "Game/rekall.project.json" && file.IsKeyArtifact);
        Assert.Contains(inspectArchive.Value.Files, file => file.Path.EndsWith("PongPlayable.dll", StringComparison.Ordinal) && file.IsKeyArtifact);
        Assert.True(inspectArchive.Value.FileCount > 0);
        Assert.Contains("rekall.package.json", inspectArchive.Value.KeyArtifacts);

        var runArchive = await new RunPlayablePackageCommand().ExecuteAsync(
            new RunPlayablePackageRequest(package.Value.ArchivePath, Frames: 1),
            context);

        Assert.True(runArchive.Ok, runArchive.Summary);
        Assert.True(runArchive.Value.Ready);
        Assert.Equal(0, runArchive.Value.ExitCode);
        Assert.Contains("FRAME 1", runArchive.Value.Output, StringComparison.Ordinal);
        Assert.Contains("PONG", Assert.Single(runArchive.Value.Frames), StringComparison.Ordinal);
        var renderFrame = Assert.Single(runArchive.Value.RenderFrames);
        Assert.Equal("pong", renderFrame.Kind);
        Assert.Contains(renderFrame.DrawCommands, command => command.Id == "ball" && command.Kind == "circle");

        var captureArchive = await new CapturePlayablePackageFrameCommand().ExecuteAsync(
            new CapturePlayablePackageFrameRequest(package.Value.ArchivePath, Path.Combine(root, "PackageFrames"), FrameIndex: 1),
            context);

        Assert.True(captureArchive.Ok, captureArchive.Summary);
        Assert.True(captureArchive.Value.Captured);
        Assert.True(captureArchive.Value.NonBlank);
        Assert.True(File.Exists(captureArchive.Value.OutputPath), captureArchive.Value.OutputPath);
        Assert.Equal("pong", captureArchive.Value.Kind);
        Assert.Contains(captureArchive.Value.DrawCommandKinds, kind => kind == "circle");

        var auditArchive = await new AuditPlayablePackageCommand().ExecuteAsync(
            new AuditPlayablePackageRequest(package.Value.ArchivePath, Path.Combine(root, "PackageAudit"), Frames: 1),
            context);

        Assert.True(auditArchive.Ok, auditArchive.Summary);
        Assert.True(auditArchive.Value.Ready);
        Assert.Empty(auditArchive.Value.MissingKeyArtifacts);
        Assert.True(auditArchive.Value.Inspection.Ready);
        Assert.True(auditArchive.Value.Run.Ready);
        Assert.True(auditArchive.Value.Capture.Captured);
        Assert.True(auditArchive.Value.Capture.NonBlank);
        Assert.True(File.Exists(auditArchive.Value.Capture.OutputPath), auditArchive.Value.Capture.OutputPath);
    }

    [Fact]
    public async Task PackagePlayableGameGraphicsLaunchesWindowsPlayerInPlayableMode()
    {
        var root = TestPaths.CreateTempDirectory();
        var output = Path.Combine(root, "Builds", "GraphicsPackage");
        Directory.CreateDirectory(output);
        var stalePackageFile = Path.Combine(output, "stale-package-file.txt");
        await File.WriteAllTextAsync(stalePackageFile, "old package content");
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("graphics package playable"), CancellationToken.None);
        var create = await new CreatePlayableGameFromTemplateCommand().ExecuteAsync(
            new CreatePlayableGameFromTemplateRequest(root, "Graphics Pong", "pong"),
            context);
        Assert.True(create.Ok, create.Summary);

        var package = await new PackagePlayableGameCommand().ExecuteAsync(
            new PackagePlayableGameRequest(
                root,
                "Main",
                output,
                Frames: 1,
                Assertions: [new RekallAgeFrameAssertion(0, "PONG")],
                Graphics: true),
            context);

        Assert.True(package.Ok, package.Summary);
        Assert.True(package.Value.Ready);
        Assert.EndsWith("Rekall.Age.Player.Windows.exe", package.Value.LaunchPath, StringComparison.Ordinal);
        Assert.Contains("--graphics", package.Value.Arguments);
        Assert.Contains("--backend", package.Value.Arguments);
        Assert.Contains("--playable", package.Value.Arguments);
        Assert.False(File.Exists(stalePackageFile), "Packaging should clean the output directory before publishing.");
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(package.Value.ManifestPath));
        var arguments = manifest.RootElement.GetProperty("arguments").EnumerateArray().Select(item => item.GetString()).ToArray();
        Assert.Contains("--playable", arguments);
    }

    [Fact]
    public async Task PackagePlayableGameAcceptsRelativeOutputDirectory()
    {
        var root = TestPaths.CreateTempDirectory();
        var relativeOutput = Path.Combine(".age-test-output", Guid.NewGuid().ToString("N"), "RelativePackage");
        var fullOutput = Path.GetFullPath(relativeOutput);
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("relative package playable"), CancellationToken.None);
        try
        {
            var create = await new CreatePlayableGameFromTemplateCommand().ExecuteAsync(
                new CreatePlayableGameFromTemplateRequest(root, "Relative Packaged Pong", "pong"),
                context);
            Assert.True(create.Ok, create.Summary);

            var package = await new PackagePlayableGameCommand().ExecuteAsync(
                new PackagePlayableGameRequest(
                    root,
                    "Main",
                    relativeOutput,
                    Frames: 1,
                    Assertions: [new RekallAgeFrameAssertion(0, "PONG")]),
                context);

            Assert.True(package.Ok, package.Summary);
            Assert.Equal(fullOutput, Path.GetFullPath(package.Value.OutputDirectory));
            Assert.True(File.Exists(Path.Combine(fullOutput, "Rekall.Age.Player.exe")));
            Assert.True(File.Exists(package.Value.ManifestPath), package.Value.ManifestPath);
        }
        finally
        {
            if (Directory.Exists(fullOutput))
            {
                Directory.Delete(fullOutput, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CreatePlayablePackageFromTemplateBuildsRunsAndCapturesProof()
    {
        var root = TestPaths.CreateTempDirectory();
        var output = Path.Combine(root, "Builds", "OneShotPackage");
        var frameOutput = Path.Combine(root, "Artifacts", "OneShotFrames");
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("one-shot playable package"), CancellationToken.None);

        var result = await new CreatePlayablePackageFromTemplateCommand().ExecuteAsync(
            new CreatePlayablePackageFromTemplateRequest(
                root,
                "One Shot Pong",
                "pong",
                OutputDirectory: output,
                CaptureOutputDirectory: frameOutput,
                Frames: 1),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.True(result.Value.Ready);
        Assert.Equal("pong", result.Value.TemplateId);
        Assert.True(File.Exists(result.Value.Package.ArchivePath), result.Value.Package.ArchivePath);
        Assert.True(result.Value.Inspection.Ready);
        Assert.True(result.Value.Run.Ready);
        Assert.Contains("PONG", Assert.Single(result.Value.Run.Frames), StringComparison.Ordinal);
        Assert.True(result.Value.Capture.Captured);
        Assert.True(result.Value.Capture.NonBlank);
        Assert.True(File.Exists(result.Value.Capture.OutputPath), result.Value.Capture.OutputPath);
        Assert.Contains(result.Value.Run.RenderFrames[0].DrawCommands, command => command.Id == "ball" && command.Kind == "circle");
    }

    [Fact]
    public async Task AgentAuthoringGauntletCreatesVerifiesPackagesAndAuditsTemplateGame()
    {
        var root = TestPaths.CreateTempDirectory();
        var output = Path.Combine(root, "Builds", "GauntletPackage");
        var auditOutput = Path.Combine(root, "Artifacts", "GauntletAudit");
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("agent authoring gauntlet"), CancellationToken.None);

        var result = await new RunAgentAuthoringGauntletCommand().ExecuteAsync(
            new RunAgentAuthoringGauntletRequest(
                root,
                "Gauntlet Pong",
                "pong",
                OutputDirectory: output,
                AuditOutputDirectory: auditOutput,
                Frames: 1),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.True(result.Value.Ready);
        Assert.Equal("pong", result.Value.TemplateId);
        Assert.True(File.Exists(result.Value.PackageArchivePath), result.Value.PackageArchivePath);
        Assert.True(File.Exists(result.Value.ProofFramePath), result.Value.ProofFramePath);
        Assert.Contains(result.Value.Checks, check => check.Name == "create-playable" && check.Passed);
        Assert.Contains(result.Value.Checks, check => check.Name == "verify-playable" && check.Passed);
        Assert.Contains(result.Value.Checks, check => check.Name == "package-playable" && check.Passed);
        Assert.Contains(result.Value.Checks, check => check.Name == "audit-package" && check.Passed);
        Assert.Contains("rekall.workflow.inspect_playable_package", result.Value.RecommendedNextActions);
        Assert.Contains("rekall.workflow.run_playable_package", result.Value.RecommendedNextActions);
    }

    [Fact]
    public async Task VerifyMvpTemplatesReportsEveryRequiredTemplateReady()
    {
        var root = TestPaths.CreateTempDirectory();
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("verify mvp templates"), CancellationToken.None);

        var result = await new VerifyMvpTemplatesCommand().ExecuteAsync(
            new VerifyMvpTemplatesRequest(root, Frames: 1, Cleanup: false),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.True(result.Value.Ready);
        Assert.Equal(RequiredTemplateIds.Order(StringComparer.Ordinal), result.Value.Templates.Select(item => item.TemplateId).Order(StringComparer.Ordinal));
        Assert.All(result.Value.Templates, template =>
        {
            Assert.True(template.Ready, template.Summary);
            Assert.True(File.Exists(template.ModuleAssemblyPath), template.ModuleAssemblyPath);
            Assert.Equal(1, template.FrameCount);
            Assert.True(template.DrawCommandCount > 0);
        });
    }

    private static string? ReadString(JsonObject obj, string key)
    {
        return obj.TryGetPropertyValue(key, out var node)
            && node is JsonValue value
            && value.TryGetValue<string>(out var text)
            ? text
            : null;
    }

    private static double ReadNumber(JsonObject obj, string key)
    {
        return obj.TryGetPropertyValue(key, out var node)
            && node is JsonValue value
            && value.TryGetValue<double>(out var number)
            ? number
            : double.NaN;
    }
}
