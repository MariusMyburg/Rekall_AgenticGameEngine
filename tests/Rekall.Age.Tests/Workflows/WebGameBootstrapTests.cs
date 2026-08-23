using System.Text;
using System.Text.Json.Nodes;
using System.Net;
using System.Net.Http.Headers;
using Rekall.Age.Core.Persistence;
using Rekall.Age.Modules;
using Rekall.Age.Project;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;
using Rekall.Age.Workflows.Web;
using Rekall.Age.Player.Web;

namespace Rekall.Age.Tests.Workflows;

public sealed class WebGameBootstrapTests
{
    [Fact]
    public void SerializesStructuredBootstrapEvidenceThroughTrimSafeJsonMetadata()
    {
        var evidence = new RekallAgeWebBootstrapEvidence(
            true,
            "runtime-world-ready",
            new string('a', 64),
            "Bootstrap Game",
            "Scenes/Main.age.scene.json",
            ["game.bootstrap-rules"],
            1,
            ["game.bootstrap-rules.system"],
            [new("hero", "Hero", ["Game.BootstrapState"])],
            []);

        var json = WebGameBootstrapEvidenceJson.Serialize(evidence);

        Assert.Contains("\"state\":\"runtime-world-ready\"", json, StringComparison.Ordinal);
        Assert.Contains("\"componentTypes\":[\"Game.BootstrapState\"]", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HttpFetcherReadsOnlyCanonicalRelativeContentWithinTheRequestedBound()
    {
        var handler = new StaticResponseHandler(
            HttpStatusCode.OK,
            Encoding.UTF8.GetBytes("scene"));
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://game.invalid/build/")
        };
        var fetcher = new RekallAgeHttpWebContentFetcher(client);

        var bytes = await fetcher.FetchAsync("Scenes/Main#Alt.age.scene.json", 5, CancellationToken.None);

        Assert.Equal("scene", Encoding.UTF8.GetString(bytes.Span));
        Assert.Equal(
            "https://game.invalid/build/Scenes/Main%23Alt.age.scene.json",
            handler.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task HttpFetcherRejectsDeclaredResponseBeyondBoundBeforeReadingIt()
    {
        using var client = new HttpClient(new StaticResponseHandler(
            HttpStatusCode.OK,
            new byte[12]))
        {
            BaseAddress = new Uri("https://game.invalid/build/")
        };
        var fetcher = new RekallAgeHttpWebContentFetcher(client);

        var error = await Assert.ThrowsAsync<RekallAgeGameContentException>(() =>
            fetcher.FetchAsync("game.manifest.json", 8, CancellationToken.None).AsTask());

        Assert.Equal("REKALL_WEB_FETCH_TOO_LARGE", error.Code);
    }

    [Fact]
    public async Task BootsHashValidatedStagedSceneThroughStaticAuthoredModuleRegistration()
    {
        var package = await CreateStagedPackageAsync();

        using var result = await new RekallAgeWebGameBootstrapper().BootAsync(
            package.FetchAsync,
            [CreateRegistration()],
            CancellationToken.None);

        Assert.True(result.Evidence.Succeeded, string.Join(Environment.NewLine, result.Evidence.Diagnostics.Select(item => item.Message)));
        Assert.NotNull(result.Session);
        Assert.Equal(package.Manifest.BuildIdentity, result.Evidence.BuildIdentity);
        Assert.Equal("Bootstrap Game", result.Evidence.ProjectName);
        Assert.Equal("Scenes/Main.age.scene.json", result.Evidence.EntryScenePath);
        Assert.Equal(["game.bootstrap-rules"], result.Evidence.ModuleIds);
        Assert.Contains("game.bootstrap-rules.system", result.Evidence.SystemIds);
        Assert.Equal(1, result.Evidence.RuntimeFrameIndex);
        var fact = Assert.Single(result.Evidence.Entities);
        Assert.Equal("hero", fact.Id);
        Assert.Contains("Game.BootstrapState", fact.ComponentTypes);
        Assert.Equal(1, result.Session.World.FindEntity("Hero")!.ComponentNumber("Game.BootstrapState", "ticks"));
    }

    [Fact]
    public async Task ReportsStableDiagnosticWhenDeclaredSceneBytesAreTampered()
    {
        var package = CreatePackage();
        var tampered = package.Content["Scenes/Main.age.scene.json"].ToArray();
        tampered[10] ^= 1;
        package.Content["Scenes/Main.age.scene.json"] = tampered;

        using var result = await new RekallAgeWebGameBootstrapper().BootAsync(
            package.FetchAsync,
            [CreateRegistration()],
            CancellationToken.None);

        Assert.False(result.Evidence.Succeeded);
        Assert.Null(result.Session);
        var diagnostic = Assert.Single(result.Evidence.Diagnostics);
        Assert.Equal("REKALL_WEB_CONTENT_HASH_MISMATCH", diagnostic.Code);
        Assert.Contains("Scenes/Main.age.scene.json", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReportsStableContentDiagnosticForHashValidSceneWithInvalidShape()
    {
        var package = CreatePackage();
        var invalidScene = Encoding.UTF8.GetBytes(
            "{\"id\":\"scene_main\",\"name\":\"Main\",\"schemaVersion\":1,\"capabilities\":[],\"entities\":\"invalid\"}");
        package.Content["Scenes/Main.age.scene.json"] = invalidScene;
        var entries = package.Manifest.Content
            .Select(entry => entry.Path == "Scenes/Main.age.scene.json"
                ? entry with
                {
                    SizeBytes = invalidScene.LongLength,
                    Sha256 = RekallAgeDocumentRevision.Compute(invalidScene)
                }
                : entry)
            .ToArray();
        package.Manifest = RekallAgeWebGameManifestCodec.Create(
            package.Manifest.Project,
            package.Manifest.EntryScenePath,
            package.Manifest.Viewport,
            package.Manifest.Modules,
            package.Manifest.RequiredRenderingCapabilities,
            entries);

        using var result = await new RekallAgeWebGameBootstrapper().BootAsync(
            package.FetchAsync,
            [CreateRegistration()],
            CancellationToken.None);

        Assert.False(result.Evidence.Succeeded);
        Assert.Equal("REKALL_WEB_CONTENT_INVALID", Assert.Single(result.Evidence.Diagnostics).Code);
    }

    [Fact]
    public async Task RejectsProjectIdentityThatDoesNotMatchDeclaredProjectContent()
    {
        var package = CreatePackage();
        package.Manifest = package.Manifest with
        {
            Project = package.Manifest.Project with { ProjectManifestSha256 = new string('0', 64) }
        };
        package.Manifest = package.Manifest with
        {
            BuildIdentity = RekallAgeWebGameManifestCodec.ComputeBuildIdentity(package.Manifest)
        };

        using var result = await new RekallAgeWebGameBootstrapper().BootAsync(
            package.FetchAsync,
            [CreateRegistration()],
            CancellationToken.None);

        Assert.False(result.Evidence.Succeeded);
        Assert.Equal("REKALL_WEB_PROJECT_IDENTITY_MISMATCH", Assert.Single(result.Evidence.Diagnostics).Code);
    }

    [Fact]
    public async Task RejectsCompiledModuleIdentityThatDoesNotMatchManifestFingerprint()
    {
        var package = CreatePackage();
        package.Manifest = package.Manifest with
        {
            Modules = [package.Manifest.Modules[0] with { SourceFingerprint = new string('b', 64) }]
        };
        package.Manifest = package.Manifest with
        {
            BuildIdentity = RekallAgeWebGameManifestCodec.ComputeBuildIdentity(package.Manifest)
        };

        using var result = await new RekallAgeWebGameBootstrapper().BootAsync(
            package.FetchAsync,
            [CreateRegistration()],
            CancellationToken.None);

        Assert.False(result.Evidence.Succeeded);
        Assert.Equal("REKALL_WEB_MODULE_REGISTRATION_MISMATCH", Assert.Single(result.Evidence.Diagnostics).Code);
    }

    [Fact]
    public async Task RejectsStaticRegistrationSetBeyondBootstrapBound()
    {
        var package = CreatePackage();
        var modules = Enumerable.Range(0, RekallAgeWebGameBootstrapper.MaximumModuleRegistrations + 1)
            .Select(index => new RekallAgeWebModuleIdentity(
                $"game.module-{index:D4}",
                $"Module{index:D4}",
                $"Module{index:D4}, Version=1.0.0.0",
                new string('a', 64)))
            .ToArray();
        package.Manifest = RekallAgeWebGameManifestCodec.Create(
            package.Manifest.Project,
            package.Manifest.EntryScenePath,
            package.Manifest.Viewport,
            modules,
            package.Manifest.RequiredRenderingCapabilities,
            package.Manifest.Content);
        var registrations = modules
            .Select(module => CreateRegistration() with { ModuleId = module.Id })
            .ToArray();

        using var result = await new RekallAgeWebGameBootstrapper().BootAsync(
            package.FetchAsync,
            registrations,
            CancellationToken.None);

        Assert.False(result.Evidence.Succeeded);
        Assert.Equal("REKALL_WEB_MODULE_REGISTRATION_LIMIT", Assert.Single(result.Evidence.Diagnostics).Code);
    }

    private static RekallAgeRuntimeModuleRegistration CreateRegistration() => new(
        typeof(BootstrapRulesModule),
        static () => new BootstrapRulesModule(),
        [new(typeof(BootstrapRulesSystem), static () => new BootstrapRulesSystem())])
    {
        ModuleId = "game.bootstrap-rules",
        ModuleName = "BootstrapRules",
        AssemblyIdentity = "BootstrapRules, Version=1.0.0.0",
        SourceFingerprint = new string('a', 64)
    };

    private static PackageFixture CreatePackage()
    {
        var project = Encoding.UTF8.GetBytes(
            "{\"name\":\"Bootstrap Game\",\"schemaVersion\":1,\"capabilities\":[\"world\"]}");
        var scene = Encoding.UTF8.GetBytes(
            "{\"id\":\"scene_main\",\"name\":\"Main\",\"schemaVersion\":1,\"capabilities\":[\"world\"],\"entities\":[{\"id\":\"hero\",\"name\":\"Hero\",\"tags\":[],\"components\":[{\"type\":\"Game.BootstrapState\",\"properties\":{\"ticks\":0}}],\"parentId\":null,\"prefabSourceId\":null,\"visible\":true,\"locked\":false}]}");
        var content = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["rekall.project.json"] = project,
            ["Scenes/Main.age.scene.json"] = scene
        };
        var entries = content
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => new RekallAgeWebContentEntry(
                item.Key,
                "application/json",
                item.Value.LongLength,
                RekallAgeDocumentRevision.Compute(item.Value)))
            .ToArray();
        var manifest = RekallAgeWebGameManifestCodec.Create(
            new("Bootstrap Game", entries.Single(item => item.Path == "rekall.project.json").Sha256),
            "Scenes/Main.age.scene.json",
            new(1280, 720, "fit"),
            [new("game.bootstrap-rules", "BootstrapRules", "BootstrapRules, Version=1.0.0.0", new string('a', 64))],
            [],
            entries);
        return new PackageFixture(manifest, content);
    }

    private static async Task<StagedPackageFixture> CreateStagedPackageAsync()
    {
        var root = TestPaths.CreateTempDirectory();
        var output = TestPaths.CreateTempDirectory();
        Directory.Delete(output);
        Directory.CreateDirectory(Path.Combine(root, "Scenes"));
        await File.WriteAllTextAsync(
            Path.Combine(root, "rekall.project.json"),
            "{\"name\":\"Bootstrap Game\",\"schemaVersion\":1,\"capabilities\":[\"world\"]}");
        await File.WriteAllTextAsync(
            Path.Combine(root, "Scenes", "Main.age.scene.json"),
            "{\"id\":\"scene_main\",\"name\":\"Main\",\"schemaVersion\":1,\"capabilities\":[\"world\"],\"entities\":[{\"id\":\"hero\",\"name\":\"Hero\",\"tags\":[],\"components\":[{\"type\":\"Game.BootstrapState\",\"properties\":{\"ticks\":0}}],\"parentId\":null,\"prefabSourceId\":null,\"visible\":true,\"locked\":false}]}");
        var identity = new RekallAgeWebModuleIdentity(
            "game.bootstrap-rules",
            "BootstrapRules",
            "BootstrapRules, Version=1.0.0.0",
            new string('a', 64));
        var modulePlan = new RekallAgeWebModuleRegistryPlan(
            root,
            [new(identity, "Modules/BootstrapRules/BootstrapRules.csproj", typeof(BootstrapRulesModule).FullName!, [typeof(BootstrapRulesSystem).FullName!])],
            [],
            [],
            string.Empty,
            string.Empty);
        var staged = await new RekallAgeWebGameExporter().StageAsync(
            new RekallAgeWebGameStageRequest(root, "Main", output, ModulePlan: modulePlan),
            CancellationToken.None);
        return new StagedPackageFixture(staged);
    }

    private sealed class PackageFixture(
        RekallAgeWebGameManifest manifest,
        Dictionary<string, byte[]> content)
    {
        public RekallAgeWebGameManifest Manifest { get; set; } = manifest;

        public Dictionary<string, byte[]> Content { get; } = content;

        public ValueTask<ReadOnlyMemory<byte>> FetchAsync(
            string logicalPath,
            long maximumBytes,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = logicalPath == "game.manifest.json"
                ? RekallAgeWebGameManifestCodec.EncodeForFile(Manifest)
                : Content[logicalPath];
            if (bytes.LongLength > maximumBytes)
            {
                throw new InvalidDataException($"Fixture response exceeds {maximumBytes} bytes.");
            }
            return ValueTask.FromResult<ReadOnlyMemory<byte>>(bytes);
        }
    }

    private sealed class StagedPackageFixture(RekallAgeWebGameStageResult staged)
    {
        private readonly RekallAgeFileGameContent _content = new(staged.OutputDirectory);

        public RekallAgeWebGameManifest Manifest => staged.Manifest;

        public async ValueTask<ReadOnlyMemory<byte>> FetchAsync(
            string logicalPath,
            long maximumBytes,
            CancellationToken cancellationToken) =>
            (await _content.ReadAsync(logicalPath, maximumBytes, cancellationToken)).Bytes;
    }

    [RekallAgeModule("game.bootstrap-rules", "Bootstrap rules")]
    public sealed class BootstrapRulesModule : RekallAgeModule
    {
        public override void Configure(RekallAgeModuleBuilder builder) =>
            builder.RegisterRuntimeSystem<BootstrapRulesSystem>();
    }

    public sealed class BootstrapRulesSystem : IRekallAgeRuntimeModuleSystem
    {
        public string Id => "game.bootstrap-rules.system";

        public int Priority => 10;

        public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
            RekallAgeRuntimeWorld world,
            RekallAgeRuntimeModuleFrameContext context)
        {
            var hero = world.FindEntity("Hero")!;
            return ValueTask.FromResult(world.UpdateEntity(
                hero.Id,
                current => current.WithComponentNumber("Game.BootstrapState", "ticks", 1)));
        }
    }

    private sealed class StaticResponseHandler(HttpStatusCode status, byte[] bytes) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentLength = bytes.LongLength;
            return Task.FromResult(new HttpResponseMessage(status) { Content = content });
        }
    }
}
