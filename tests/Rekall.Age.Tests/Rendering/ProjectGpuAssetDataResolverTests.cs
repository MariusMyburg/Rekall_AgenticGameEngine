using System.Security.Cryptography;
using Rekall.Age.Assets;
using Rekall.Age.Rendering;

namespace Rekall.Age.Tests.Rendering;

public sealed class ProjectGpuAssetDataResolverTests
{
    [Fact]
    public async Task ResolvesRelocatedCatalogAssetsInsideTheCurrentProjectAndVerifiesTheirHash()
    {
        var root = TestPaths.CreateTempDirectory();
        var data = new byte[] { 9, 8, 7, 6 };
        var imported = Path.Combine(root, "Assets", "gpu-data", "vertices.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(imported)!);
        await File.WriteAllBytesAsync(imported, data);
        var hash = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        await new RekallAgeAssetCatalogStore().SaveAsync(
            root,
            new([new("asset:vertices", "vertices", "Vertices", "gpu-data",
                "C:\\source\\vertices.bin", "D:\\old-project\\Assets\\gpu-data\\vertices.bin", hash)]),
            CancellationToken.None);

        var result = new RekallAgeProjectGpuAssetDataResolver(root).Resolve("asset:vertices");

        Assert.True(result.Resolved, string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.Message)));
        Assert.Equal(data, result.Data);
    }

    [Fact]
    public async Task RejectsTamperedAssetData()
    {
        var root = TestPaths.CreateTempDirectory();
        var imported = Path.Combine(root, "Assets", "gpu-data", "pixels.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(imported)!);
        await File.WriteAllBytesAsync(imported, [1, 2, 3, 4]);
        await new RekallAgeAssetCatalogStore().SaveAsync(
            root,
            new([new("asset:pixels", "pixels", "Pixels", "gpu-data",
                "source.bin", imported, new string('0', 64))]),
            CancellationToken.None);

        var result = new RekallAgeProjectGpuAssetDataResolver(root).Resolve("asset:pixels");

        Assert.False(result.Resolved);
        Assert.Contains(result.Diagnostics, item => item.Code == "REKALL_GPU_ASSET_HASH_MISMATCH");
    }

    [Fact]
    public async Task NeverReadsCatalogPathsOutsideTheProjectAssetsRoot()
    {
        var root = TestPaths.CreateTempDirectory();
        var outside = Path.Combine(TestPaths.CreateTempDirectory(), "outside.bin");
        await File.WriteAllBytesAsync(outside, [1, 2, 3, 4]);
        var hash = Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3, 4 })).ToLowerInvariant();
        await new RekallAgeAssetCatalogStore().SaveAsync(
            root,
            new([new("asset:outside", "outside", "Outside", "..", outside, outside, hash)]),
            CancellationToken.None);

        var result = new RekallAgeProjectGpuAssetDataResolver(root).Resolve("asset:outside");

        Assert.False(result.Resolved);
        Assert.Contains(result.Diagnostics, item => item.Code == "REKALL_GPU_ASSET_PATH_OUTSIDE_PROJECT");
    }

    [Fact]
    public async Task MalformedCatalogPathsReturnStableDiagnosticsInsteadOfThrowing()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeAssetCatalogStore().SaveAsync(
            root,
            new([new("asset:malformed", "malformed", "Malformed", "gpu-data",
                "source.bin", "bad\0path.bin", new string('0', 64))]),
            CancellationToken.None);

        var result = new RekallAgeProjectGpuAssetDataResolver(root).Resolve("asset:malformed");

        Assert.False(result.Resolved);
        Assert.Contains(result.Diagnostics, item => item.Code == "REKALL_GPU_ASSET_RESOLUTION_FAILED");
    }

    [Fact]
    public async Task CatalogRevisionTracksContentEvenWhenLengthAndTimestampArePreserved()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeAssetCatalogStore();
        await store.SaveAsync(root, new([new("asset:a", "a", "A", "gpu-data", "a", "a", new string('0', 64))]), CancellationToken.None);
        var path = store.GetCatalogPath(root);
        var timestamp = File.GetLastWriteTimeUtc(path);
        var resolver = new RekallAgeProjectGpuAssetDataResolver(root);
        var before = resolver.CatalogRevision;
        var json = await File.ReadAllTextAsync(path);
        await File.WriteAllTextAsync(path, json.Replace("asset:a", "asset:b", StringComparison.Ordinal));
        File.SetLastWriteTimeUtc(path, timestamp);

        var after = resolver.CatalogRevision;

        Assert.NotEqual(before, after);
    }

    [Fact]
    public async Task RejectsAnAssetsDirectoryLinkBeforeFollowingItsCatalog()
    {
        var root = TestPaths.CreateTempDirectory();
        var outside = TestPaths.CreateTempDirectory();
        var outsideAsset = Path.Combine(outside, "gpu-data", "vertices.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(outsideAsset)!);
        var data = new byte[] { 1, 2, 3, 4 };
        await File.WriteAllBytesAsync(outsideAsset, data);
        await new RekallAgeAssetCatalogStore().SaveAsync(
            Path.GetDirectoryName(outside)!,
            new([]),
            CancellationToken.None);
        var outsideCatalog = Path.Combine(outside, "assets.age.catalog.json");
        await File.WriteAllTextAsync(outsideCatalog,
            $$"""{"schemaVersion":1,"assets":[{"id":"asset:vertices","sourceId":"vertices","displayName":"Vertices","kind":"gpu-data","sourcePath":"source.bin","importedPath":"{{outsideAsset.Replace("\\", "\\\\")}}","contentHash":"{{Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant()}}"}]}""");
        if (OperatingSystem.IsWindows())
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                "cmd.exe", $"/d /c mklink /J \"{Path.Combine(root, "Assets")}\" \"{outside}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
            process!.WaitForExit();
            Assert.Equal(0, process.ExitCode);
        }
        else
        {
            Directory.CreateSymbolicLink(Path.Combine(root, "Assets"), outside);
        }

        var result = new RekallAgeProjectGpuAssetDataResolver(root).Resolve("asset:vertices");

        Assert.False(result.Resolved);
        Assert.Contains(result.Diagnostics, item => item.Code == "REKALL_GPU_ASSET_PATH_OUTSIDE_PROJECT");
    }

    [Fact]
    public async Task RejectsHardLinkedPayloadsThatAliasFilesOutsideTheProject()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = TestPaths.CreateTempDirectory();
        var outside = Path.Combine(TestPaths.CreateTempDirectory(), "outside.bin");
        var linked = Path.Combine(root, "Assets", "gpu-data", "vertices.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(linked)!);
        var data = new byte[] { 4, 3, 2, 1 };
        await File.WriteAllBytesAsync(outside, data);
        using (var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            "cmd.exe", $"/d /c mklink /H \"{linked}\" \"{outside}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        }))
        {
            process!.WaitForExit();
            Assert.Equal(0, process.ExitCode);
        }
        await new RekallAgeAssetCatalogStore().SaveAsync(
            root,
            new([new("asset:vertices", "vertices", "Vertices", "gpu-data", "source.bin", linked,
                Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant())]),
            CancellationToken.None);

        var result = new RekallAgeProjectGpuAssetDataResolver(root).Resolve("asset:vertices");

        Assert.False(result.Resolved);
        Assert.Contains(result.Diagnostics, item => item.Code == "REKALL_GPU_ASSET_PATH_OUTSIDE_PROJECT");
    }
}
