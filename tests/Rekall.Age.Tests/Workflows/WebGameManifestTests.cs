using System.Text.Json;
using Rekall.Age.Core.Product;
using Rekall.Age.Workflows.Web;

namespace Rekall.Age.Tests.Workflows;

public sealed class WebGameManifestTests
{
    private const string HashA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string HashB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public void PermutedLogicalInputsProduceTheSameCanonicalManifestAndBuildIdentity()
    {
        var first = CreateManifest(
            modules:
            [
                new("rules", "Rules", "Rules, Version=1.0.0.0", HashA),
                new("camera", "Camera", "Camera, Version=1.0.0.0", HashB)
            ],
            capabilities: ["texture2d", "geometry3d"],
            content:
            [
                new("Scenes/Main.age.scene.json", "application/json", 20, HashA),
                new("rekall.project.json", "application/json", 10, HashB)
            ]);
        var second = CreateManifest(
            modules: first.Modules.Reverse().ToArray(),
            capabilities: first.RequiredRenderingCapabilities.Reverse().ToArray(),
            content: first.Content.Reverse().ToArray());

        Assert.Equal(first.BuildIdentity, second.BuildIdentity);
        Assert.Equal(
            RekallAgeWebGameManifestCodec.EncodeCanonical(first),
            RekallAgeWebGameManifestCodec.EncodeCanonical(second));
    }

    [Fact]
    public void SemanticContentChangesTheBuildIdentity()
    {
        var first = CreateManifest();
        var changed = CreateManifest(content:
        [
            new("Scenes/Main.age.scene.json", "application/json", 20, HashB),
            new("rekall.project.json", "application/json", 10, HashB)
        ]);

        Assert.NotEqual(first.BuildIdentity, changed.BuildIdentity);
    }

    [Fact]
    public void CanonicalManifestRoundTripsAndBindsCurrentEngineIdentity()
    {
        var expected = CreateManifest();

        var decoded = RekallAgeWebGameManifestCodec.DecodeAndValidate(
            RekallAgeWebGameManifestCodec.EncodeForFile(expected));

        Assert.Equal(RekallAgeProductInfo.Current.Version, decoded.Engine.ProductVersion);
        Assert.Equal(RekallAgeProductInfo.Current.ProjectSchemaVersion, decoded.Engine.ProjectSchemaVersion);
        Assert.Equal(RekallAgeProductInfo.Current.ModuleSdkCompatibilityVersion, decoded.Engine.ModuleSdkCompatibilityVersion);
        Assert.Equal(expected.BuildIdentity, decoded.BuildIdentity);
    }

    [Theory]
    [InlineData("../Scenes/Main.age.scene.json")]
    [InlineData("Scenes\\Main.age.scene.json")]
    [InlineData("/Scenes/Main.age.scene.json")]
    public void ManifestCreationRejectsNonCanonicalLogicalPaths(string path)
    {
        Assert.Throws<InvalidDataException>(() => CreateManifest(content:
        [
            new(path, "application/json", 20, HashA),
            new("rekall.project.json", "application/json", 10, HashB)
        ]));
    }

    [Fact]
    public void DecodeRejectsTamperedIdentityAndNonCanonicalOrdering()
    {
        var manifest = CreateManifest();
        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var tamperedIdentity = JsonSerializer.SerializeToUtf8Bytes(
            manifest with { BuildIdentity = HashB },
            jsonOptions);
        var reversedContent = JsonSerializer.SerializeToUtf8Bytes(
            manifest with { Content = manifest.Content.Reverse().ToArray() },
            jsonOptions);

        Assert.Throws<InvalidDataException>(() =>
            RekallAgeWebGameManifestCodec.DecodeAndValidate(tamperedIdentity));
        Assert.Throws<InvalidDataException>(() =>
            RekallAgeWebGameManifestCodec.DecodeAndValidate(reversedContent));
    }

    [Fact]
    public void ManifestCreationRejectsUppercaseOrDuplicatePortableContentIdentity()
    {
        Assert.Throws<InvalidDataException>(() => CreateManifest(content:
        [
            new("Scenes/Main.age.scene.json", "application/json", 20, HashA.ToUpperInvariant()),
            new("rekall.project.json", "application/json", 10, HashB)
        ]));
        Assert.Throws<InvalidDataException>(() => CreateManifest(content:
        [
            new("Scenes/Main.age.scene.json", "application/json", 20, HashA),
            new("scenes/main.age.scene.json", "application/json", 20, HashA),
            new("rekall.project.json", "application/json", 10, HashB)
        ]));
    }

    private static RekallAgeWebGameManifest CreateManifest(
        IReadOnlyList<RekallAgeWebModuleIdentity>? modules = null,
        IReadOnlyList<string>? capabilities = null,
        IReadOnlyList<RekallAgeWebContentEntry>? content = null) =>
        RekallAgeWebGameManifestCodec.Create(
            new RekallAgeWebProjectIdentity("Clockwork Canopy", HashB),
            "Scenes/Main.age.scene.json",
            new RekallAgeWebViewportPolicy(1280, 720, "fit"),
            modules ?? [new("rules", "Rules", "Rules, Version=1.0.0.0", HashA)],
            capabilities ?? ["geometry3d", "texture2d"],
            content ??
            [
                new("Scenes/Main.age.scene.json", "application/json", 20, HashA),
                new("rekall.project.json", "application/json", 10, HashB)
            ]);
}
