using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Compatibility;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Core.Persistence;
using Rekall.Age.Project;
using Rekall.Age.Project.Commands;
using System.Text;
using System.Text.Json;

namespace Rekall.Age.Tests.Project;

public sealed class ProjectManifestTests
{
    [Fact]
    public async Task CreateProjectWritesDeterministicManifest()
    {
        var root = TestPaths.CreateTempDirectory();
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new CreateProjectCommand());

        var transaction = RekallAgeTransaction.Begin("create project");
        var context = new RekallAgeCommandContext("test", transaction, CancellationToken.None);

        var result = await registry.ExecuteAsync<CreateProjectRequest, CreateProjectResult>(
            "rekall.project.create",
            new CreateProjectRequest(root, "Crystal Mines", ["rendering2d", "world", "rendering2d"]),
            context);

        Assert.True(result.Ok);
        Assert.Equal(["rendering2d", "world"], result.Value.Manifest.Capabilities);
        var manifestPath = Path.Combine(root, "rekall.project.json");
        Assert.True(File.Exists(manifestPath));

        var json = await File.ReadAllTextAsync(manifestPath);
        Assert.Contains("\"name\": \"Crystal Mines\"", json);
        Assert.Contains("\"rendering2d\"", json);
        Assert.Contains("\"world\"", json);
    }

    [Fact]
    public async Task AddCapabilityNormalizesAndPersistsCapability()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeProjectStore();
        await store.SaveAsync(root, RekallAgeProjectManifest.Create("Puzzle Box", ["world"]), CancellationToken.None);
        var command = new AddCapabilityCommand();
        var context = new RekallAgeCommandContext("test", RekallAgeTransaction.Begin("capability"), CancellationToken.None);

        var result = await command.ExecuteAsync(new AddCapabilityRequest(root, " Rendering3D "), context);

        Assert.True(result.Ok);
        Assert.Equal(["rendering3d", "world"], result.Value.Manifest.Capabilities);
    }

    [Fact]
    public async Task LegacyManifestLoadsAsCurrentWithoutRewritingSource()
    {
        var root = TestPaths.CreateTempDirectory();
        var path = Path.Combine(root, RekallAgeProjectStore.ManifestFileName);
        const string legacy = """
            {
              "name": "Legacy Project",
              "capabilities": ["world"]
            }
            """;
        await File.WriteAllTextAsync(path, legacy);

        var manifest = await new RekallAgeProjectStore().LoadAsync(root, CancellationToken.None);

        Assert.Equal(1, manifest.SchemaVersion);
        Assert.Equal(legacy, await File.ReadAllTextAsync(path));
    }

    [Theory]
    [InlineData("2", "REKALL_DOCUMENT_SCHEMA_FUTURE")]
    [InlineData("-1", "REKALL_DOCUMENT_SCHEMA_INVALID")]
    [InlineData("\"one\"", "REKALL_DOCUMENT_SCHEMA_INVALID")]
    public async Task UnsupportedManifestSchemaFailsClosed(string schemaToken, string expectedCode)
    {
        var root = TestPaths.CreateTempDirectory();
        var path = Path.Combine(root, RekallAgeProjectStore.ManifestFileName);
        await File.WriteAllTextAsync(
            path,
            $$"""{"name":"Blocked","schemaVersion":{{schemaToken}},"capabilities":[]}""");

        var error = await Assert.ThrowsAsync<RekallAgeDocumentCompatibilityException>(
            () => new RekallAgeProjectStore().LoadAsync(root, CancellationToken.None).AsTask());

        Assert.Equal(expectedCode, error.Code);
        Assert.Equal("project", error.DocumentKind);
        Assert.Equal(Path.GetFullPath(path), error.DocumentPath);
        Assert.Equal(1, error.CurrentVersion);
    }

    [Fact]
    public async Task MalformedManifestJsonReturnsTypedCompatibilityFailure()
    {
        var root = TestPaths.CreateTempDirectory();
        await File.WriteAllTextAsync(
            Path.Combine(root, RekallAgeProjectStore.ManifestFileName),
            "{ not-json");

        var error = await Assert.ThrowsAsync<RekallAgeDocumentCompatibilityException>(
            () => new RekallAgeProjectStore().LoadAsync(root, CancellationToken.None).AsTask());

        Assert.Equal("REKALL_DOCUMENT_JSON_MALFORMED", error.Code);
    }

    [Fact]
    public async Task ManifestUsesOneSchemaSnapshotAndConsistentBoundedJsonDepth()
    {
        var root = TestPaths.CreateTempDirectory();
        var path = Path.Combine(root, RekallAgeProjectStore.ManifestFileName);
        var nested = new string('[', 80) + "0" + new string(']', 80);
        var source = $$"""{"schemaVersion":1,"name":"Deep","capabilities":[],"extension":{{nested}}}""";
        await File.WriteAllTextAsync(path, source);

        var snapshot = await RekallAgeDocumentSchemaProbe.ReadSnapshotAsync(
            path,
            "project",
            currentVersion: 1,
            CancellationToken.None);
        await File.WriteAllTextAsync(path, "{\"schemaVersion\":2}");
        var manifest = snapshot.Deserialize<RekallAgeProjectManifest>(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            MaxDepth = RekallAgeDocumentSchemaProbe.MaximumDocumentDepth
        });

        Assert.Equal(1, snapshot.Schema.DetectedVersion);
        Assert.Equal(Encoding.UTF8.GetBytes(source), snapshot.File.Bytes);
        Assert.Equal("Deep", manifest.Name);
    }

    [Fact]
    public async Task ManifestSavePublishesBomlessJsonWithoutTemporarySiblings()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeProjectStore();

        await store.SaveAsync(root, RekallAgeProjectManifest.Create("Atomic", ["world"]), CancellationToken.None);

        var path = Path.Combine(root, RekallAgeProjectStore.ManifestFileName);
        var bytes = await File.ReadAllBytesAsync(path);
        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        Assert.Empty(Directory.GetFiles(root, ".rekall.project.json.tmp-*"));
    }

    [Fact]
    public async Task ManifestBeyondTheSharedJsonDepthFailsWithTypedCompatibilityCode()
    {
        var root = TestPaths.CreateTempDirectory();
        var path = Path.Combine(root, RekallAgeProjectStore.ManifestFileName);
        var nested = new string('[', RekallAgeDocumentSchemaProbe.MaximumDocumentDepth + 1)
            + "0"
            + new string(']', RekallAgeDocumentSchemaProbe.MaximumDocumentDepth + 1);
        await File.WriteAllTextAsync(
            path,
            $$"""{"schemaVersion":1,"name":"Too Deep","capabilities":[],"extension":{{nested}}}""");

        var error = await Assert.ThrowsAsync<RekallAgeDocumentCompatibilityException>(
            () => new RekallAgeProjectStore().LoadAsync(root, CancellationToken.None).AsTask());

        Assert.Equal("REKALL_DOCUMENT_JSON_MALFORMED", error.Code);
    }

    [Fact]
    public async Task VersionedManifestSaveRejectsAnInterveningWriter()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeProjectStore();
        await store.SaveAsync(root, RekallAgeProjectManifest.Create("Initial", ["world"]), CancellationToken.None);
        var loaded = await store.LoadVersionedAsync(root, CancellationToken.None);
        await store.SaveAsync(root, loaded.Value with { Name = "Intervening" }, CancellationToken.None);

        var error = await Assert.ThrowsAsync<RekallAgeDocumentRevisionException>(
            () => store.SaveIfRevisionAsync(
                root,
                loaded.Value with { Name = "Stale" },
                loaded.Revision,
                CancellationToken.None).AsTask());

        Assert.Equal("REKALL_DOCUMENT_REVISION_CONFLICT", error.Code);
        Assert.Equal("Intervening", (await store.LoadAsync(root, CancellationToken.None)).Name);
        Assert.Equal(
            RekallAgeDocumentRevision.Compute(await File.ReadAllBytesAsync(Path.Combine(root, RekallAgeProjectStore.ManifestFileName))),
            error.CurrentRevision);
    }

    [Fact]
    public async Task ProjectCreationCannotSilentlyOverwriteAnExistingManifest()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeProjectStore();
        await store.SaveAsync(root, RekallAgeProjectManifest.Create("Existing", ["world"]), CancellationToken.None);
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new CreateProjectCommand());

        var result = await registry.ExecuteJsonAsync(
            "rekall.project.create",
            JsonSerializer.Serialize(new { projectRoot = root, name = "Replacement", capabilities = new[] { "world" } }),
            new RekallAgeCommandContext("test", RekallAgeTransaction.Begin("create existing"), CancellationToken.None));

        Assert.False(result.Ok);
        Assert.Equal("REKALL_DOCUMENT_REVISION_CONFLICT", Assert.Single(result.Errors).Code);
        Assert.Equal("Existing", (await store.LoadAsync(root, CancellationToken.None)).Name);
    }
}
