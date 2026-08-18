using System.Numerics;
using System.Text.Json.Nodes;
using Rekall.Age.Assets;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Runtime;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Rendering;

public sealed class VulkanSceneMorphTargetTests
{
    [Fact]
    public async Task RenderFrameProjectsOnlyValidatedMorphState()
    {
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "animation", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Actor", ["actor"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject()))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.MeshRenderer",
                    new JsonObject { ["mesh"] = "morph-asset" }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.MorphWeights",
                    new JsonObject { ["weights"] = new JsonArray(0.5, -0.25) })));
        var initial = new RekallAgeRuntimeWorldBuilder().Build(scene);
        var runtime = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(initial, 1, CancellationToken.None);

        var renderable = Assert.Single(new RekallAgeRuntimeRenderFrameBuilder()
            .Build(runtime.World, 64, 64, false).Renderables);

        Assert.True(renderable.Morph!.AuthoredOverride);
        Assert.Equal([0.5, -0.25], renderable.Morph.Weights);
    }

    [Fact]
    public void BuilderAppliesExactSignedWeightsAndNormalizesResult()
    {
        var mesh = Build(new RekallAgeRuntimeViewportMorph([0.5, -0.25], true));

        var vertex = Assert.Single(mesh.Vertices);
        Assert.Equal(2, vertex.X, precision: 5);
        Assert.Equal(2.5, vertex.Y, precision: 5);
        Assert.Equal(2.75, vertex.Z, precision: 5);
        Assert.Equal(0.5547, vertex.NormalX, precision: 4);
        Assert.Equal(0.8320503, vertex.NormalY, precision: 6);
        Assert.Equal("authored", mesh.MorphWeightSource);
    }

    [Fact]
    public void BuilderUsesImportedDefaultsAndAllZeroWeightsPreserveBase()
    {
        var defaults = Build(null);
        Assert.Equal(1.5, Assert.Single(defaults.Vertices).X, precision: 5);
        Assert.Equal("default", defaults.MorphWeightSource);

        var zero = Build(new RekallAgeRuntimeViewportMorph([0, 0], true));
        var vertex = Assert.Single(zero.Vertices);
        Assert.Equal(1, vertex.X);
        Assert.Equal(2, vertex.Y);
        Assert.Equal(3, vertex.Z);
    }

    [Fact]
    public void BuilderFallsBackAtomicallyToDefaultsOnOverrideCountMismatch()
    {
        var mesh = Build(new RekallAgeRuntimeViewportMorph([1], true));

        Assert.Equal(1.5, Assert.Single(mesh.Vertices).X, precision: 5);
        Assert.Equal("default", mesh.MorphWeightSource);
    }

    [Fact]
    public async Task AssetResolverReportsCountMismatchAndBuilderUsesImportedDefaults()
    {
        var root = TestPaths.CreateTempDirectory();
        var path = Path.Combine(root, "morph.glb");
        await File.WriteAllBytesAsync(path, GlbTestMeshFactory.CreateMorphTriangleGlb());
        await new RekallAgeAssetCatalogStore().SaveAsync(
            root,
            new RekallAgeAssetCatalogDocument(
            [
                new("morph-asset", "morph", "Morph", "model", path, path, "hash")
            ]),
            CancellationToken.None);
        var frame = Frame(Renderable(new RekallAgeRuntimeViewportMorph([1], true)));

        var assets = await new RekallAgeRuntimeViewportAssetResolver().ResolveAsync(
            root,
            frame,
            CancellationToken.None);

        var issue = Assert.Single(assets.Issues, item => item.Code == "REKALL_RENDER_MORPH_WEIGHT_COUNT_MISMATCH");
        Assert.Contains("supplies 1", issue.Message, StringComparison.Ordinal);
        Assert.Contains("requires 2", issue.Message, StringComparison.Ordinal);
        var mesh = Assert.Single(new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame, assets));
        Assert.Equal("default", mesh.MorphWeightSource);
    }

    [Fact]
    public void BuilderFailsClosedWhenWeightedOutputCannotFitFiniteGpuFloats()
    {
        var source = SourceMesh() with
        {
            Vertices = [Vertex(float.MaxValue, 0, 0, 0, 1, 0)],
            MorphTargets = [new RekallAgeVulkanSceneMorphTarget("overflow", [new Vector3(float.MaxValue, 0, 0)], [Vector3.Zero])],
            DefaultMorphWeights = [1]
        };

        var mesh = Assert.Single(new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(
            Frame(Renderable(null)),
            Assets(source)));

        Assert.Equal(float.MaxValue, Assert.Single(mesh.Vertices).X);
        Assert.Equal("none", mesh.MorphWeightSource);
    }

    [Fact]
    public void BuilderFallsBackToBaseNormalWhenMorphSumIsNearZero()
    {
        var source = SourceMesh() with
        {
            Vertices = [Vertex(0, 0, 0, 0, 1, 0)],
            MorphTargets = [new RekallAgeVulkanSceneMorphTarget("cancel", [Vector3.Zero], [new Vector3(0, -1, 0)])],
            DefaultMorphWeights = [1]
        };

        var mesh = Assert.Single(new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(
            Frame(Renderable(null)),
            Assets(source)));

        Assert.Equal(1, Assert.Single(mesh.Vertices).NormalY);
    }

    [Fact]
    public void BuilderAppliesMorphBeforeSkeletalSkinning()
    {
        var source = SourceMesh() with
        {
            Vertices = [Vertex(1, 0, 0, 0, 1, 0)],
            MorphTargets = [new RekallAgeVulkanSceneMorphTarget("move", [new Vector3(1, 0, 0)], [Vector3.Zero])],
            DefaultMorphWeights = [0],
            SkinIndex = 0,
            SkinBindings = [new RekallAgeVulkanSceneSkinBinding(0, 0, 0, 0, 1, 0, 0, 0)]
        };
        var skinMatrix = new double[]
        {
            2,0,0,0,
            0,2,0,0,
            0,0,2,0,
            0,0,0,1
        };
        var renderable = Renderable(new RekallAgeRuntimeViewportMorph([1], true)) with
        {
            Skin = new RekallAgeRuntimeViewportSkin(0, [skinMatrix])
        };

        var mesh = Assert.Single(new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(
            Frame(renderable),
            Assets(source)));

        Assert.Equal(4, Assert.Single(mesh.Vertices).X, precision: 5);
    }

    private static RekallAgeVulkanSceneMesh Build(RekallAgeRuntimeViewportMorph? morph) =>
        Assert.Single(new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(
            Frame(Renderable(morph)),
            Assets(SourceMesh())));

    private static RekallAgeVulkanSceneMesh SourceMesh() => new(
        "morph-asset",
        "source",
        "glb",
        [Vertex(1, 2, 3, 0, 1, 0)],
        [0, 0, 0])
    {
        MorphTargets =
        [
            new RekallAgeVulkanSceneMorphTarget("wide", [new Vector3(2, 0, 0)], [new Vector3(1, 0, 0)]),
            new RekallAgeVulkanSceneMorphTarget("raised", [new Vector3(0, -2, 1)], [new Vector3(0, 1, 0)])
        ],
        DefaultMorphWeights = [0.25f, 0]
    };

    private static RekallAgeVulkanSceneVertex Vertex(float x, float y, float z, float nx, float ny, float nz) =>
        new(x, y, z, nx, ny, nz, 1, 1, 1, 1, 0, 0);

    private static RekallAgeRuntimeViewportRenderable Renderable(RekallAgeRuntimeViewportMorph? morph) =>
        new("actor", "Actor", "mesh", "morph-asset", 0, 0, 0, 1, Morph: morph);

    private static RekallAgeRuntimeViewportFrame Frame(RekallAgeRuntimeViewportRenderable renderable) =>
        new("Main", 1, 1.0 / 60, 64, 64, null, [], [renderable], 0, new(false, 0), []);

    private static RekallAgeRuntimeViewportAssetSet Assets(RekallAgeVulkanSceneMesh mesh) =>
        new(
            new Dictionary<string, RekallAgeRgbaImage>(),
            new Dictionary<string, IReadOnlyList<RekallAgeVulkanSceneMesh>> { ["morph-asset"] = [mesh] },
            []);
}
