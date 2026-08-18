using System.IO.Compression;
using Rekall.Age.Workflows;

namespace Rekall.Age.Tests.Workflows;

public sealed class PackageArchivePreflightTests
{
    [Fact]
    public void ValidArchiveReturnsManifestFirstDeterministicPlan()
    {
        using var archive = OpenArchive(
            ("Game/z.txt", "z", null),
            ("rekall.package.json", "{}", null),
            ("Game/", string.Empty, null),
            ("Game/a.txt", "a", null));

        var plan = RekallAgePackageArchivePreflight.Inspect(archive);

        Assert.Equal("rekall.package.json", plan.Manifest.NormalizedPath);
        Assert.Equal(
            ["rekall.package.json", "Game/", "Game/a.txt", "Game/z.txt"],
            plan.Entries.Select(item => item.NormalizedPath));
        Assert.Equal(4, plan.EntryCount);
        Assert.Equal(4, plan.TotalUncompressedBytes);
    }

    [Fact]
    public void DuplicateManifestIsRejectedBeforeContentRead()
    {
        using var archive = OpenArchive(
            ("rekall.package.json", "{}", null),
            ("rekall.package.json", "not json and must not be read", null));

        var error = Assert.Throws<RekallAgePackageArchiveException>(
            () => RekallAgePackageArchivePreflight.Inspect(archive));

        Assert.Equal("REKALL_PACKAGE_ARCHIVE_MANIFEST_DUPLICATE", error.Code);
    }

    [Fact]
    public void OversizedManifestIsRejectedFromMetadata()
    {
        using var archive = OpenArchive(("rekall.package.json", new string('x', 32), null));
        var limits = new RekallAgePackageArchiveLimits(
            MaximumEntries: 10,
            MaximumEntryBytes: 100,
            MaximumTotalBytes: 100,
            MaximumManifestBytes: 8);

        var error = Assert.Throws<RekallAgePackageArchiveException>(
            () => RekallAgePackageArchivePreflight.Inspect(archive, limits));

        Assert.Equal("REKALL_PACKAGE_ARCHIVE_MANIFEST_TOO_LARGE", error.Code);
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("Game\\escape.txt")]
    [InlineData("Game//empty.txt")]
    [InlineData("Game/trailing.")]
    [InlineData("Game/trailing ")]
    [InlineData("Game/control\u0001.txt")]
    [InlineData("Game/CON")]
    [InlineData("Game/file?.txt")]
    public void UnsafeOrAmbiguousPathIsRejected(string unsafePath)
    {
        using var archive = OpenArchive(
            ("rekall.package.json", "{}", null),
            (unsafePath, "x", null));

        var error = Assert.Throws<RekallAgePackageArchiveException>(
            () => RekallAgePackageArchivePreflight.Inspect(archive));

        Assert.Equal("REKALL_PACKAGE_ARCHIVE_PATH_UNSAFE", error.Code);
        Assert.Equal(unsafePath, error.Target);
    }

    [Fact]
    public void CaseAndAncestorCollisionsAreRejected()
    {
        using var caseCollision = OpenArchive(
            ("rekall.package.json", "{}", null),
            ("Game/Data.bin", "a", null),
            ("game/data.bin", "b", null));
        var caseError = Assert.Throws<RekallAgePackageArchiveException>(
            () => RekallAgePackageArchivePreflight.Inspect(caseCollision));
        Assert.Equal("REKALL_PACKAGE_ARCHIVE_PATH_COLLISION", caseError.Code);

        using var ancestorCollision = OpenArchive(
            ("rekall.package.json", "{}", null),
            ("Game", "file", null),
            ("Game/scene.json", "{}", null));
        var ancestorError = Assert.Throws<RekallAgePackageArchiveException>(
            () => RekallAgePackageArchivePreflight.Inspect(ancestorCollision));
        Assert.Equal("REKALL_PACKAGE_ARCHIVE_PATH_ANCESTOR_CONFLICT", ancestorError.Code);
    }

    [Fact]
    public void SymlinkAndSpecialFileMetadataAreRejected()
    {
        const int unixSymlink = unchecked((int)0xA1FF0000);
        using var archive = OpenArchive(
            ("rekall.package.json", "{}", null),
            ("Game/link", "target", unixSymlink));

        var error = Assert.Throws<RekallAgePackageArchiveException>(
            () => RekallAgePackageArchivePreflight.Inspect(archive));

        Assert.Equal("REKALL_PACKAGE_ARCHIVE_ENTRY_SPECIAL", error.Code);
        Assert.Equal("Game/link", error.Target);
    }

    [Fact]
    public void EntryCountAndTotalSizeAreBounded()
    {
        using var countArchive = OpenArchive(
            ("rekall.package.json", "{}", null),
            ("Game/a", "a", null));
        var countError = Assert.Throws<RekallAgePackageArchiveException>(() =>
            RekallAgePackageArchivePreflight.Inspect(
                countArchive,
                new RekallAgePackageArchiveLimits(1, 100, 100, 100)));
        Assert.Equal("REKALL_PACKAGE_ARCHIVE_LIMIT_EXCEEDED", countError.Code);

        using var sizeArchive = OpenArchive(
            ("rekall.package.json", "{}", null),
            ("Game/a", "12345", null));
        var sizeError = Assert.Throws<RekallAgePackageArchiveException>(() =>
            RekallAgePackageArchivePreflight.Inspect(
                sizeArchive,
                new RekallAgePackageArchiveLimits(10, 100, 4, 100)));
        Assert.Equal("REKALL_PACKAGE_ARCHIVE_LIMIT_EXCEEDED", sizeError.Code);
    }

    [Fact]
    public void MissingExactRootManifestIsRejected()
    {
        using var archive = OpenArchive(("REKALL.PACKAGE.JSON", "{}", null));

        var error = Assert.Throws<RekallAgePackageArchiveException>(
            () => RekallAgePackageArchivePreflight.Inspect(archive));

        Assert.Equal("REKALL_PACKAGE_ARCHIVE_MANIFEST_MISSING", error.Code);
    }

    private static ZipArchive OpenArchive(params (string Path, string Content, int? ExternalAttributes)[] entries)
    {
        var stream = new MemoryStream();
        using (var writer = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var item in entries)
            {
                var entry = writer.CreateEntry(item.Path, CompressionLevel.Fastest);
                if (item.ExternalAttributes.HasValue)
                {
                    entry.ExternalAttributes = item.ExternalAttributes.Value;
                }

                if (!item.Path.EndsWith("/", StringComparison.Ordinal))
                {
                    using var text = new StreamWriter(entry.Open());
                    text.Write(item.Content);
                }
            }
        }

        stream.Position = 0;
        return new ZipArchive(stream, ZipArchiveMode.Read);
    }
}
