using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Rekall.Age.Build.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Product;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modules.Commands;
using Rekall.Age.Workflows.Web;

namespace Rekall.Age.Tests.Workflows;

public sealed class WebGameExporterTests
{
    [Fact]
    public async Task GeneratesDeterministicStaticModuleBuildInputsFromAuthoredProject()
    {
        var root = TestPaths.CreateTempDirectory();
        var generated = TestPaths.CreateTempDirectory();
        Directory.Delete(generated);
        await WriteProjectAsync(root, "Assets/Imported/hero.png");
        await BuildRuntimeModuleAsync(root);

        var generator = new RekallAgeWebModuleRegistryGenerator();
        var result = generator.Generate(root, generated);

        var module = Assert.Single(result.Modules);
        Assert.Equal("game.web-rules", module.Identity.Id);
        Assert.Equal("WebRules", module.Identity.ModuleName);
        Assert.Matches("^WebRules, Version=", module.Identity.AssemblyIdentity);
        Assert.Matches("^[0-9a-f]{64}$", module.Identity.SourceFingerprint);
        Assert.Equal(
            Path.Combine(root, "Modules", "WebRules", "WebRules.csproj"),
            Assert.Single(result.ModuleProjectPaths));
        Assert.Equal(
            "Game.Modules.WebRules.WebRulesModule",
            module.ModuleTypeName);
        Assert.Equal(
            ["Game.Modules.WebRules.WebRulesSystem"],
            module.RuntimeSystemTypeNames);

        var source = await File.ReadAllTextAsync(result.RegistrySourcePath);
        Assert.Contains("typeof(global::Game.Modules.WebRules.WebRulesModule)", source, StringComparison.Ordinal);
        Assert.Contains("static () => new global::Game.Modules.WebRules.WebRulesModule()", source, StringComparison.Ordinal);
        Assert.Contains("ModuleId = \"game.web-rules\"", source, StringComparison.Ordinal);
        Assert.Contains("ModuleName = \"WebRules\"", source, StringComparison.Ordinal);
        Assert.Contains("AssemblyIdentity = \"WebRules, Version=", source, StringComparison.Ordinal);
        Assert.Contains("SourceFingerprint = \"", source, StringComparison.Ordinal);
        Assert.Contains("typeof(global::Game.Modules.WebRules.WebRulesSystem)", source, StringComparison.Ordinal);
        Assert.Contains("static () => new global::Game.Modules.WebRules.WebRulesSystem()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Assembly.Load", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetTypes", source, StringComparison.Ordinal);

        var inputs = await File.ReadAllTextAsync(result.MsBuildInputsPath);
        Assert.Contains("WebRules.csproj", inputs, StringComparison.Ordinal);
        Assert.Contains("RekallAgePublishedModules.g.cs", inputs, StringComparison.Ordinal);
        Assert.Contains("GetFileHash", inputs, StringComparison.Ordinal);
        Assert.Contains("BeforeTargets=\"PrepareForBuild\"", inputs, StringComparison.Ordinal);
        Assert.Contains("AfterTargets=\"Publish\"", inputs, StringComparison.Ordinal);
        Assert.Equal(2, result.ModuleSources.Count);
        var repeated = generator.Discover(root);
        Assert.Equal(source, repeated.RegistrySource);
        Assert.Equal(inputs, repeated.MsBuildInputs);
    }

    [Fact]
    public async Task StagesAuthoredStaticModuleIdentityInCanonicalManifest()
    {
        var root = TestPaths.CreateTempDirectory();
        var output = TestPaths.CreateTempDirectory();
        Directory.Delete(output);
        await WriteProjectAsync(root, "Assets/Imported/hero.png");
        await BuildRuntimeModuleAsync(root);
        var modulePlan = new RekallAgeWebModuleRegistryGenerator().Discover(root);

        var result = await new RekallAgeWebGameExporter().StageAsync(
            new RekallAgeWebGameStageRequest(root, "Main", output, ModulePlan: modulePlan),
            CancellationToken.None);

        var module = Assert.Single(result.Manifest.Modules);
        Assert.Equal("game.web-rules", module.Id);
        Assert.Equal("WebRules", module.ModuleName);
        Assert.Matches("^WebRules, Version=", module.AssemblyIdentity);
        Assert.Matches("^[0-9a-f]{64}$", module.SourceFingerprint);
        var decoded = RekallAgeWebGameManifestCodec.DecodeAndValidate(
            await File.ReadAllBytesAsync(result.ManifestPath));
        Assert.Equal(module, Assert.Single(decoded.Modules));
    }

    [Fact]
    public async Task TrimmedWebAssemblyPublishIncludesTheStaticallyRegisteredAuthoredModule()
    {
        var root = TestPaths.CreateTempDirectory();
        var generated = TestPaths.CreateTempDirectory();
        var publish = TestPaths.CreateTempDirectory();
        Directory.Delete(generated);
        Directory.Delete(publish);
        await WriteProjectAsync(root, "Assets/Imported/hero.png");
        await BuildRuntimeModuleAsync(root);
        var registry = new RekallAgeWebModuleRegistryGenerator().Generate(root, generated);
        var stagedDirectory = TestPaths.CreateTempDirectory();
        Directory.Delete(stagedDirectory);
        var staged = await new RekallAgeWebGameExporter().StageAsync(
            new RekallAgeWebGameStageRequest(
                root,
                "Main",
                stagedDirectory,
                ModulePlan: new RekallAgeWebModuleRegistryGenerator().Discover(root)),
            CancellationToken.None);
        var repository = FindRepositoryRoot();
        var webProject = Path.Combine(
            repository,
            "src",
            "Rekall.Age.Player.Web",
            "Rekall.Age.Player.Web.csproj");
        var restore = await RunDotNetAsync("restore", webProject, "--locked-mode");
        Assert.True(restore.ExitCode == 0, restore.Output);
        foreach (var moduleProject in registry.ModuleProjectPaths)
        {
            var moduleRestore = await RunDotNetAsync("restore", moduleProject);
            Assert.True(moduleRestore.ExitCode == 0, moduleRestore.Output);
        }

        var result = await RunDotNetAsync(
            "publish",
            webProject,
            "-c",
            "Release",
            "--no-restore",
            "-p:PublishTrimmed=true",
            "-p:ILLinkTreatWarningsAsErrors=false",
            "-p:SuppressTrimAnalysisWarnings=true",
            $"-p:RekallAgeWebPublishInputsPath={registry.MsBuildInputsPath}",
            $"-p:RekallAgeWebGameContentPath={staged.OutputDirectory}",
            "-o",
            publish);

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains(
            Directory.EnumerateFiles(publish, "*", SearchOption.AllDirectories),
            path => Path.GetFileName(path).Contains("WebRules", StringComparison.Ordinal));
        var publishedFiles = Directory.EnumerateFiles(publish, "*", SearchOption.AllDirectories).ToArray();
        var publishedManifest = Assert.Single(
            publishedFiles,
            path => Path.GetFileName(path).Equals("game.manifest.json", StringComparison.Ordinal));
        var publishedContentRoot = Path.GetDirectoryName(publishedManifest)!;
        foreach (var stagedPath in Directory.EnumerateFiles(staged.OutputDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(staged.OutputDirectory, stagedPath);
            var publishedPath = Path.Combine(publishedContentRoot, relativePath);
            Assert.True(File.Exists(publishedPath), $"Published web package is missing staged content '{relativePath}'.");
            Assert.Equal(await File.ReadAllBytesAsync(stagedPath), await File.ReadAllBytesAsync(publishedPath));
        }
        Assert.Contains(publishedFiles, path =>
        {
            var bytes = File.ReadAllBytes(path);
            return ContainsBytes(bytes, Encoding.UTF8.GetBytes("WebRulesModule"))
                && ContainsBytes(bytes, Encoding.UTF8.GetBytes("WebRulesSystem"));
        });
    }

    [Fact]
    public async Task RejectsGeneratedModuleBuildInputsInsideTheAuthoredProject()
    {
        var root = TestPaths.CreateTempDirectory();
        await WriteProjectAsync(root, "Assets/Imported/hero.png");
        await BuildRuntimeModuleAsync(root);
        var generated = Path.Combine(root, "Build", "WebInputs");

        var error = Assert.Throws<InvalidOperationException>(() =>
            new RekallAgeWebModuleRegistryGenerator().Generate(root, generated));

        Assert.Contains("overlap", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(generated));
    }

    [Fact]
    public async Task RejectsAuthoredModuleWithoutAStableIdentity()
    {
        var root = TestPaths.CreateTempDirectory();
        await WriteProjectAsync(root, "Assets/Imported/hero.png");
        await BuildRuntimeModuleAsync(root, " ");

        var error = Assert.Throws<InvalidDataException>(() =>
            new RekallAgeWebModuleRegistryGenerator().Discover(root));

        Assert.Contains("identity", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejectsMultipleModuleClassesInOneAuthoredProjectBeforeManifestCreation()
    {
        var root = TestPaths.CreateTempDirectory();
        await WriteProjectAsync(root, "Assets/Imported/hero.png");
        await BuildRuntimeModuleAsync(root);
        await File.WriteAllTextAsync(
            Path.Combine(root, "Modules", "WebRules", "SecondModule.cs"),
            """
            using Rekall.Age.Modules;
            namespace Game.Modules.WebRules;
            [RekallAgeModule("game.second", "Second")]
            public sealed class SecondModule : RekallAgeModule
            {
                public override void Configure(RekallAgeModuleBuilder builder) { }
            }
            """);
        await RebuildModulesAsync(root);

        var error = Assert.Throws<InvalidDataException>(() =>
            new RekallAgeWebModuleRegistryGenerator().Discover(root));

        Assert.Contains("exactly one concrete", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejectsOpenGenericModuleTypesBeforeGeneratingCSharp()
    {
        var root = TestPaths.CreateTempDirectory();
        await WriteProjectAsync(root, "Assets/Imported/hero.png");
        await BuildRuntimeModuleAsync(root);
        var sourcePath = Directory.EnumerateFiles(
            Path.Combine(root, "Modules", "WebRules"), "*.cs", SearchOption.TopDirectoryOnly).Single();
        var source = await File.ReadAllTextAsync(sourcePath);
        source = source.Replace("public sealed class WebRulesModule", "public sealed class WebRulesModule<T>", StringComparison.Ordinal);
        await File.WriteAllTextAsync(sourcePath, source);
        await RebuildModulesAsync(root);

        var error = Assert.Throws<InvalidDataException>(() =>
            new RekallAgeWebModuleRegistryGenerator().Discover(root));

        Assert.Contains("static browser publication", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EscapesKeywordIdentifierSegmentsInGeneratedCSharpTypeReferences()
    {
        var root = TestPaths.CreateTempDirectory();
        await WriteProjectAsync(root, "Assets/Imported/hero.png");
        await BuildRuntimeModuleAsync(root);
        var sourcePath = Directory.EnumerateFiles(
            Path.Combine(root, "Modules", "WebRules"), "*.cs", SearchOption.TopDirectoryOnly).Single();
        var source = await File.ReadAllTextAsync(sourcePath);
        source = source.Replace("namespace Game.Modules.WebRules", "namespace Game.@event.WebRules", StringComparison.Ordinal);
        await File.WriteAllTextAsync(sourcePath, source);
        await RebuildModulesAsync(root);

        var plan = new RekallAgeWebModuleRegistryGenerator().Discover(root);

        Assert.Contains("global::Game.@event.WebRules.WebRulesModule", plan.RegistrySource, StringComparison.Ordinal);
        Assert.Contains("global::Game.@event.WebRules.WebRulesSystem", plan.RegistrySource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GeneratedBuildInputsRejectSourceChangedAfterDiscovery()
    {
        var root = TestPaths.CreateTempDirectory();
        var generated = TestPaths.CreateTempDirectory();
        Directory.Delete(generated);
        await WriteProjectAsync(root, "Assets/Imported/hero.png");
        await BuildRuntimeModuleAsync(root);
        var registry = new RekallAgeWebModuleRegistryGenerator().Generate(root, generated);
        var moduleProject = Assert.Single(registry.ModuleProjectPaths);
        var moduleRestore = await RunDotNetAsync("restore", moduleProject);
        Assert.True(moduleRestore.ExitCode == 0, moduleRestore.Output);
        var sourcePath = Directory.EnumerateFiles(
            Path.GetDirectoryName(moduleProject)!, "*.cs", SearchOption.TopDirectoryOnly).Single();
        await File.AppendAllTextAsync(sourcePath, Environment.NewLine + "// changed after discovery");
        var webProject = Path.Combine(FindRepositoryRoot(), "src", "Rekall.Age.Player.Web", "Rekall.Age.Player.Web.csproj");

        var result = await RunDotNetAsync(
            "build", webProject, "-c", "Release", "--no-restore",
            $"-p:RekallAgeWebPublishInputsPath={registry.MsBuildInputsPath}");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("changed after web publication discovery", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StagesOnlyTheDeterministicDeclarativeEntrySceneClosure()
    {
        var first = await CreateAndStageProjectAsync("first");
        var second = await CreateAndStageProjectAsync("second");

        Assert.Equal(first.Manifest.BuildIdentity, second.Manifest.BuildIdentity);
        Assert.Equal(
            await File.ReadAllBytesAsync(first.ManifestPath),
            await File.ReadAllBytesAsync(second.ManifestPath));
        Assert.Equal(
            [
                "Assets/Imported/hero.png",
                "Assets/assets.age.catalog.json",
                "Scenes/Main.age.scene.json",
                "rekall.project.json"
            ],
            first.Manifest.Content.Select(entry => entry.Path));
        Assert.True(File.Exists(Path.Combine(first.OutputDirectory, "Assets", "Imported", "hero.png")));
        Assert.False(File.Exists(Path.Combine(first.OutputDirectory, "Assets", "Imported", "unused.png")));
        Assert.False(File.Exists(Path.Combine(first.OutputDirectory, "index.html")));
        Assert.Empty(first.Manifest.Modules);
        Assert.Equal(["rendering2d", "ui"], first.Manifest.RequiredRenderingCapabilities);
        var stagedCatalog = JsonDocument.Parse(await File.ReadAllBytesAsync(
            Path.Combine(first.OutputDirectory, "Assets", "assets.age.catalog.json")));
        var stagedAsset = Assert.Single(stagedCatalog.RootElement.GetProperty("assets").EnumerateArray());
        Assert.Equal("asset.hero", stagedAsset.GetProperty("id").GetString());
        Assert.Equal("Assets/Imported/hero.png", stagedAsset.GetProperty("importedPath").GetString());
    }

    [Fact]
    public async Task StagesTheModelAssetDefinitionDocumentAlongsideItsCompiledMeshOutput()
    {
        var root = TestPaths.CreateTempDirectory();
        var output = TestPaths.CreateTempDirectory();
        Directory.Delete(output);
        Directory.CreateDirectory(Path.Combine(root, "Scenes"));
        Directory.CreateDirectory(Path.Combine(root, "Assets", "Models", "Compiled", "ball-model"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "rekall.project.json"),
            """
            {"name":"Model Asset Web Game","schemaVersion":1,"capabilities":["world","rendering3d","ui"]}
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "Scenes", "Main.age.scene.json"),
            """
            {"id":"scene_main","name":"Main","schemaVersion":1,"capabilities":["world","rendering3d","ui"],"entities":[{"id":"ball","name":"Ball","tags":[],"components":[{"type":"Rekall.ModelAssetReference","properties":{"assetId":"ball-model"}}],"parentId":null,"prefabSourceId":null,"visible":true,"locked":false}]}
            """);
        var compiledMeshHash = new string('c', 64);
        var catalog = new
        {
            assets = new object[]
            {
                new
                {
                    id = "ball-model",
                    name = "ball-model",
                    displayName = "Ball",
                    kind = "model",
                    sourcePath = "Modeling/Meshes/ball-mesh.age.mesh.json",
                    importedPath = $"Assets/Models/Compiled/ball-model/{compiledMeshHash}.age.compiled-mesh.json",
                    contentHash = compiledMeshHash,
                    modelAssetMetadata = new
                    {
                        modelDocumentPath = "Assets/Models/ball-model.age.model.json",
                        sourceKind = "Mesh",
                        sourceAssetId = "ball-mesh",
                        compiledOutputPath = $"Assets/Models/Compiled/ball-model/{compiledMeshHash}.age.compiled-mesh.json",
                        compiledContentHash = compiledMeshHash
                    }
                }
            }
        };
        await File.WriteAllTextAsync(
            Path.Combine(root, "Assets", "assets.age.catalog.json"),
            JsonSerializer.Serialize(catalog));
        await File.WriteAllTextAsync(
            Path.Combine(root, "Assets", "Models", "ball-model.age.model.json"),
            """{"schemaVersion":1,"assetId":"ball-model","displayName":"Ball","revision":1}""");
        await File.WriteAllBytesAsync(
            Path.Combine(root, "Assets", "Models", "Compiled", "ball-model", $"{compiledMeshHash}.age.compiled-mesh.json"),
            Encoding.UTF8.GetBytes("compiled-mesh-bytes"));

        var result = await new RekallAgeWebGameExporter().StageAsync(
            new RekallAgeWebGameStageRequest(root, "Main", output),
            CancellationToken.None);

        Assert.Contains(
            "Assets/Models/ball-model.age.model.json",
            result.Manifest.Content.Select(entry => entry.Path));
        Assert.True(File.Exists(Path.Combine(output, "Assets", "Models", "ball-model.age.model.json")));
        Assert.True(File.Exists(Path.Combine(
            output, "Assets", "Models", "Compiled", "ball-model", $"{compiledMeshHash}.age.compiled-mesh.json")));
    }

    [Fact]
    public async Task RejectsAnAssetCatalogPathThatEscapesTheProject()
    {
        var root = TestPaths.CreateTempDirectory();
        var output = TestPaths.CreateTempDirectory();
        Directory.Delete(output);
        await WriteProjectAsync(root, "../outside.png");

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new RekallAgeWebGameExporter().StageAsync(
                new RekallAgeWebGameStageRequest(root, "Main", output),
                CancellationToken.None).AsTask());

        Assert.Contains("outside the project root", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(output));
    }

    [Fact]
    public async Task RejectsAnExistingOrProjectOverlappingOutputWithoutDeletingIt()
    {
        var root = TestPaths.CreateTempDirectory();
        await WriteProjectAsync(root, "Assets/Imported/hero.png");
        var existing = TestPaths.CreateTempDirectory();
        var marker = Path.Combine(existing, "keep.txt");
        await File.WriteAllTextAsync(marker, "keep");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RekallAgeWebGameExporter().StageAsync(
                new RekallAgeWebGameStageRequest(root, "Main", existing),
                CancellationToken.None).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RekallAgeWebGameExporter().StageAsync(
                new RekallAgeWebGameStageRequest(root, "Main", Path.Combine(root, "Builds", "Web")),
                CancellationToken.None).AsTask());

        Assert.Equal("keep", await File.ReadAllTextAsync(marker));
    }

    [Fact]
    public async Task RejectsADeclarativeClosureBeyondTheEntryLimitBeforeReadingAssets()
    {
        var root = TestPaths.CreateTempDirectory();
        var output = TestPaths.CreateTempDirectory();
        Directory.Delete(output);
        await WriteProjectAsync(root, "Assets/Imported/hero.png");
        var assetCount = RekallAgeWebGameExporter.MaximumContentEntries;
        var ids = Enumerable.Range(0, assetCount).Select(index => $"asset.{index:D5}").ToArray();
        var scene = new
        {
            id = "scene_main",
            name = "Main",
            schemaVersion = 1,
            capabilities = new[] { "world" },
            entities = new[]
            {
                new
                {
                    id = "refs", name = "References", tags = Array.Empty<string>(),
                    components = new[] { new { type = "Game.References", properties = new { assetIds = ids } } },
                    parentId = (string?)null, prefabSourceId = (string?)null, visible = true, locked = false
                }
            }
        };
        var assets = ids.Select(id => new
        {
            id,
            name = id,
            displayName = id,
            kind = "sprite",
            sourcePath = string.Empty,
            importedPath = $"Assets/Imported/{id}.png",
            contentHash = new string('a', 64)
        }).ToArray();
        await File.WriteAllTextAsync(
            Path.Combine(root, "Scenes", "Main.age.scene.json"),
            JsonSerializer.Serialize(scene));
        await File.WriteAllTextAsync(
            Path.Combine(root, "Assets", "assets.age.catalog.json"),
            JsonSerializer.Serialize(new { assets }));

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new RekallAgeWebGameExporter().StageAsync(
                new RekallAgeWebGameStageRequest(root, "Main", output),
                CancellationToken.None).AsTask());

        Assert.Contains("content-entry limit", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(output));
    }

    private static async Task<RekallAgeWebGameStageResult> CreateAndStageProjectAsync(string suffix)
    {
        var root = TestPaths.CreateTempDirectory();
        var output = TestPaths.CreateTempDirectory();
        Directory.Delete(output);
        var importedPath = suffix.Equals("second", StringComparison.Ordinal)
            ? Path.Combine(root, "Assets", "Imported", "hero.png")
            : "Assets/Imported/hero.png";
        await WriteProjectAsync(root, importedPath);
        return await new RekallAgeWebGameExporter().StageAsync(
            new RekallAgeWebGameStageRequest(root, "Main", output),
            CancellationToken.None);
    }

    private static async Task WriteProjectAsync(string root, string importedPath)
    {
        Directory.CreateDirectory(Path.Combine(root, "Scenes"));
        Directory.CreateDirectory(Path.Combine(root, "Assets", "Imported"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "rekall.project.json"),
            """
            {"name":"Closure Game","schemaVersion":1,"capabilities":["world","rendering2d","ui"]}
            """);
        await File.WriteAllTextAsync(
            Path.Combine(root, "Scenes", "Main.age.scene.json"),
            """
            {"id":"scene_main","name":"Main","schemaVersion":1,"capabilities":["world","rendering2d","ui"],"entities":[{"id":"hero","name":"Hero","tags":[],"components":[{"type":"Rekall.SpriteRenderer","properties":{"assetId":"asset.hero"}}],"parentId":null,"prefabSourceId":null,"visible":true,"locked":false}]}
            """);
        var catalog = new
        {
            assets = new object[]
            {
                new { id = "asset.hero", name = "hero", displayName = "Hero", kind = "sprite", sourcePath = "hero.png", importedPath, contentHash = new string('a', 64) },
                new { id = "asset.unused", name = "unused", displayName = "Unused", kind = "sprite", sourcePath = "unused.png", importedPath = "Assets/Imported/unused.png", contentHash = new string('b', 64) }
            }
        };
        await File.WriteAllTextAsync(
            Path.Combine(root, "Assets", "assets.age.catalog.json"),
            JsonSerializer.Serialize(catalog));
        await File.WriteAllBytesAsync(Path.Combine(root, "Assets", "Imported", "hero.png"), Encoding.UTF8.GetBytes("hero-image"));
        await File.WriteAllBytesAsync(Path.Combine(root, "Assets", "Imported", "unused.png"), Encoding.UTF8.GetBytes("unused-image"));
    }

    private static async Task BuildRuntimeModuleAsync(
        string root,
        string moduleId = "game.web-rules")
    {
        var context = new RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("web module fixture"),
            CancellationToken.None);
        var scaffold = await new ScaffoldRuntimeSystemModuleCommand().ExecuteAsync(
            new ScaffoldRuntimeSystemModuleRequest(
                root,
                moduleId,
                "Web Rules",
                "WebRules",
                "WebState",
                "WebRulesSystem"),
            context);
        var build = await new BuildModulesCommand().ExecuteAsync(
            new BuildModulesRequest(root),
            context);

        Assert.True(scaffold.Ok, scaffold.Summary);
        Assert.True(build.Ok, string.Join(Environment.NewLine, build.Value.Modules.Select(module => module.Output)));
    }

    private static async Task RebuildModulesAsync(string root)
    {
        var context = new RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("rebuild web module fixture"),
            CancellationToken.None);
        var build = await new BuildModulesCommand().ExecuteAsync(new BuildModulesRequest(root), context);
        Assert.True(build.Ok, string.Join(Environment.NewLine, build.Value.Modules.Select(module => module.Output)));
    }

    private static async Task<(int ExitCode, string Output)> RunDotNetAsync(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, await standardOutput + await standardError);
    }

    private static bool ContainsBytes(ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> value) =>
        bytes.IndexOf(value) >= 0;

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Rekall.AGE.sln")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not find Rekall.AGE.sln from the test output directory.");
    }
}
