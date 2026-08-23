using System.Diagnostics;
using Rekall.Age.Assets;
using Rekall.Age.Core.Persistence;

namespace Rekall.Age.Tests.Assets;

public sealed class ModelAssetPersistenceTests
{
    [Fact]
    public async Task ModelAssetRoundTripsAsVersionedProjectAsset()
    {
        var root = TestPaths.CreateTempDirectory();
        var document = CreateDocument();
        var store = new RekallAgeModelAssetStore();

        var revision = await store.SaveIfRevisionAsync(
            root, document, RekallAgeDocumentRevision.Missing, CancellationToken.None);
        var loaded = await store.LoadVersionedAsync(root, document.AssetId, CancellationToken.None);

        Assert.Equal(revision, loaded.Revision);
        Assert.Equal(document, loaded.Value);
        Assert.EndsWith(
            Path.Combine("Assets", "Models", "hero-model.age.model.json"),
            store.GetModelPath(root, document.AssetId));
    }

    [Fact]
    public async Task NewModelAssetMustStartAtLogicalRevisionOne()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeModelAssetStore();
        var invalid = CreateDocument() with { Revision = 2 };

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.SaveIfRevisionAsync(root, invalid, RekallAgeDocumentRevision.Missing, CancellationToken.None).AsTask());

        Assert.Contains("REKALL_MODEL_LOGICAL_REVISION_INVALID", error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(store.GetModelPath(root, invalid.AssetId)));
    }

    [Fact]
    public async Task SaveAsyncRejectsNewModelAssetThatDoesNotStartAtLogicalRevisionOne()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeModelAssetStore();
        var invalid = CreateDocument() with { Revision = 2 };

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.SaveAsync(root, invalid, CancellationToken.None).AsTask());

        Assert.Contains("REKALL_MODEL_LOGICAL_REVISION_INVALID", error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(store.GetModelPath(root, invalid.AssetId)));
    }

    [Fact]
    public async Task SaveAsyncRejectsExistingModelAssetWithoutExpectedFileRevision()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeModelAssetStore();
        var first = CreateDocument();
        await store.SaveIfRevisionAsync(root, first, RekallAgeDocumentRevision.Missing, CancellationToken.None);

        var error = await Assert.ThrowsAsync<RekallAgeDocumentRevisionException>(() =>
            store.SaveAsync(
                root,
                first with { Revision = 2, DisplayName = "Unconditionally overwritten" },
                CancellationToken.None).AsTask());

        Assert.Equal("REKALL_DOCUMENT_REVISION_CONFLICT", error.Code);
        Assert.Equal(first, await store.LoadAsync(root, first.AssetId, CancellationToken.None));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("nested/model")]
    [InlineData(".")]
    [InlineData("")]
    public void ModelAssetIdsCannotEscapeModelDirectory(string assetId)
    {
        var store = new RekallAgeModelAssetStore();

        Assert.Throws<ArgumentException>(() => store.GetModelPath("C:\\safe-project", assetId));
    }

    [Fact]
    public async Task ModelAssetRequiresSourceAssetId()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeModelAssetStore();
        var invalid = CreateDocument() with { Source = new(RekallAgeModelSourceKind.Mesh, "") };

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.SaveIfRevisionAsync(root, invalid, RekallAgeDocumentRevision.Missing, CancellationToken.None).AsTask());

        Assert.Contains("REKALL_MODEL_SOURCE_ASSET_ID_REQUIRED", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task ModelAssetRequiresLowercaseSha256CompiledOutputHash(string outputHash)
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeModelAssetStore();
        var invalid = CreateDocument() with
        {
            LastSuccessfulBuild = CreateDocument().LastSuccessfulBuild! with { CompiledContentHash = outputHash }
        };

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.SaveIfRevisionAsync(root, invalid, RekallAgeDocumentRevision.Missing, CancellationToken.None).AsTask());

        Assert.Contains("REKALL_MODEL_COMPILED_CONTENT_HASH_INVALID", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveIfRevisionRejectsStaleFileRevision()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeModelAssetStore();
        var first = CreateDocument();
        await store.SaveIfRevisionAsync(root, first, RekallAgeDocumentRevision.Missing, CancellationToken.None);

        var error = await Assert.ThrowsAsync<RekallAgeDocumentRevisionException>(() =>
            store.SaveIfRevisionAsync(
                root,
                first with { Revision = 2, DisplayName = "Changed" },
                RekallAgeDocumentRevision.Missing,
                CancellationToken.None).AsTask());

        Assert.Equal("REKALL_DOCUMENT_REVISION_CONFLICT", error.Code);
    }

    [Fact]
    public async Task ModelAssetSaveLoadListAndRecoveryPathsRejectLinkedModelsRoot()
    {
        var root = TestPaths.CreateTempDirectory();
        var outside = TestPaths.CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(root, "Assets"));
        var linkedModels = Path.Combine(root, "Assets", "Models");
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(linkedModels, outside);
        }
        else
        {
            var startInfo = new ProcessStartInfo("cmd.exe")
            {
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add("mklink");
            startInfo.ArgumentList.Add("/J");
            startInfo.ArgumentList.Add(linkedModels);
            startInfo.ArgumentList.Add(outside);
            using var process = Process.Start(startInfo)!;
            process.WaitForExit();
            Assert.True(
                process.ExitCode == 0,
                $"Filesystem junction capability unavailable (mklink exit {process.ExitCode}): {process.StandardError.ReadToEnd()}");
        }

        var store = new RekallAgeModelAssetStore();
        var pathError = Assert.Throws<InvalidDataException>(() => store.GetModelPath(root, "hero-model"));
        Assert.Contains("REKALL_PATH_REPARSE_REJECTED", pathError.Message, StringComparison.Ordinal);
        var listError = Assert.Throws<InvalidDataException>(() => store.ListAssetIds(root));
        Assert.Contains("REKALL_PATH_REPARSE_REJECTED", listError.Message, StringComparison.Ordinal);
        var recoveryError = Assert.Throws<InvalidDataException>(() => store.GetRecoveryPath(root, "hero-model"));
        Assert.Contains("REKALL_PATH_REPARSE_REJECTED", recoveryError.Message, StringComparison.Ordinal);
        var loadError = await Assert.ThrowsAsync<InvalidDataException>(
            () => store.LoadAsync(root, "hero-model", default).AsTask());
        Assert.Contains("REKALL_PATH_REPARSE_REJECTED", loadError.Message, StringComparison.Ordinal);
        var saveError = await Assert.ThrowsAsync<InvalidDataException>(
            () => store.SaveAsync(root, CreateDocument(), default).AsTask());
        Assert.Contains("REKALL_PATH_REPARSE_REJECTED", saveError.Message, StringComparison.Ordinal);
    }

    private static RekallAgeModelAssetDocument CreateDocument() =>
        RekallAgeModelAssetDocument.Create(
            "hero-model",
            "Hero Model",
            new(RekallAgeModelSourceKind.Mesh, "hero-mesh"),
            RekallAgeModelBuildManifest.Success(
                sourceFileRevision: "source-revision",
                sourceLogicalRevision: 1,
                compiledMeshPath: "Assets/Models/Compiled/hero-model.age.compiled-mesh.json",
                compiledContentHash: new string('a', 64),
                compilerVersion: RekallAgeModelBuildManifest.CurrentCompilerVersion));
}
