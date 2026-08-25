using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rekall.Age.Rendering;
using Rekall.Age.World;
using Rekall.Age.Runtime;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Examples;

public sealed class AetherfallHighFidelityAcceptanceTests
{
    [Fact]
    public async Task WeatheredRuinPublishesLayeredMasonryAndDamagedCrownDetail()
    {
        var projectRoot = Path.Combine(FindRepositoryRoot(), "Examples", "AetherfallCitadel");
        var graph = await new RekallAgeModelingGraphAssetStore().LoadAsync(
            projectRoot, "aetherfall.weathered-ruin.graph", CancellationToken.None);

        Assert.Contains(graph.Nodes, node => node.NodeId == "string-course-array");
        Assert.Contains(graph.Nodes, node => node.NodeId == "relief-ribs");
        Assert.Contains(graph.Nodes, node => node.NodeId == "pilaster-caps");
        Assert.Contains(graph.Nodes, node => node.NodeId == "crown-blocks");

        var evaluation = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(
            graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default,
            new(811, 0, "aetherfall-acceptance", "desktop"), CancellationToken.None);

        Assert.True(evaluation.Succeeded, string.Join(Environment.NewLine, evaluation.Diagnostics.Select(item => item.Message)));
        Assert.True(evaluation.Outputs["mesh"].Topology.FaceIds.Count >= 2_100);

        var source = await new RekallAgeMeshAssetStore().LoadVersionedAsync(
            projectRoot, "aetherfall-weathered-ruin-mesh", CancellationToken.None);
        var model = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(
            projectRoot, "Assets", "Models", "aetherfall-weathered-ruin-model.age.model.json")))!.AsObject();
        Assert.Equal(source.Revision, model["lastSuccessfulBuild"]!["sourceFileRevision"]!.GetValue<string>());
    }

    [Fact]
    public async Task FracturedBoulderUsesPublishedSmoothSubdivisionDetail()
    {
        var projectRoot = Path.Combine(FindRepositoryRoot(), "Examples", "AetherfallCitadel");
        var graph = await new RekallAgeModelingGraphAssetStore().LoadAsync(projectRoot, "aetherfall.rubble-boulder.graph", CancellationToken.None);
        var subdivision = Assert.Single(graph.Nodes, node => node.TypeId == "rekall.modeling.subdivide_smooth");
        Assert.Equal(1, subdivision.Parameters["levels"]!.GetValue<int>());

        var evaluation = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(
            graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default,
            new(509, 0, "aetherfall-acceptance", "desktop"), CancellationToken.None);

        Assert.True(evaluation.Succeeded, string.Join(Environment.NewLine, evaluation.Diagnostics.Select(item => item.Message)));
        Assert.True(evaluation.Outputs["mesh"].Topology.FaceIds.Count >= 900);
        Assert.Contains(evaluation.Outputs["mesh"].Attributes, attribute => attribute.Semantic == "normal");
        var source = await new RekallAgeMeshAssetStore().LoadVersionedAsync(projectRoot, "aetherfall-rubble-boulder-mesh", CancellationToken.None);
        var model = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(projectRoot, "Assets", "Models", "aetherfall-rubble-boulder-model.age.model.json")))!.AsObject();
        Assert.Equal(source.Revision, model["lastSuccessfulBuild"]!["sourceFileRevision"]!.GetValue<string>());
        Assert.Equal(source.Value.Revision, model["lastSuccessfulBuild"]!["sourceLogicalRevision"]!.GetValue<long>());
    }

    [Fact]
    public async Task BrokenArchConsumesPersistedBezierCurveThroughTheGenericGraphPipeline()
    {
        var projectRoot = Path.Combine(FindRepositoryRoot(), "Examples", "AetherfallCitadel");
        var curve = await new RekallAgeCurveAssetStore().LoadAsync(projectRoot, "aetherfall.broken-arch", CancellationToken.None);
        var graph = await new RekallAgeModelingGraphAssetStore().LoadAsync(projectRoot, "aetherfall.broken-arch.graph", CancellationToken.None);

        var spline = Assert.Single(curve.Splines);
        Assert.Equal(RekallAgeCurveSplineKind.CubicBezier, spline.Kind);
        Assert.Equal(3, spline.ControlPoints.Count);
        var source = Assert.Single(graph.Nodes, node => node.TypeId == "rekall.modeling.curve.source");
        var resample = Assert.Single(graph.Nodes, node => node.TypeId == "rekall.modeling.curve.resample");
        Assert.True(JsonNode.DeepEquals(source.Parameters["document"], JsonSerializer.SerializeToNode(curve, RekallAgeModelingJson.Options)));
        Assert.Equal(48, resample.Parameters["count"]!.GetValue<int>());
        Assert.Contains(graph.Links, link => link.FromNodeId == source.NodeId && link.ToNodeId == resample.NodeId);

        var evaluation = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(
            graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default,
            new(607, 0, "aetherfall-acceptance", "desktop"), CancellationToken.None);

        Assert.True(evaluation.Succeeded, string.Join(Environment.NewLine, evaluation.Diagnostics.Select(item => item.Message)));
        var mesh = evaluation.Outputs["mesh"];
        Assert.True(mesh.Topology.PointIds.Count >= 8_000);
        Assert.Contains(mesh.Attributes, item => item.Name == "curve.source.span");
        Assert.True(new RekallAgeMeshValidator().Validate(mesh).IsValid);
    }

    [Fact]
    public void ResonanceCourtAuthorsACompleteScalableHighFidelityContract()
    {
        var entities = LoadMainScene()["entities"]!.AsArray();
        var components = entities.SelectMany(entity => entity!["components"]!.AsArray()).ToArray();

        Assert.Single(components, component => Type(component) == "Rekall.RenderQualityProfile");
        Assert.Single(components, component => Type(component) == "Rekall.Environment3D");
        Assert.Single(components, component => Type(component) == "Rekall.ShadowSettings");
        Assert.True(components.Count(component => Type(component) == "Rekall.FogVolume") >= 2);

        var emitters = components.Where(component => Type(component) == "Rekall.ParticleEmitter3D").ToArray();
        Assert.True(emitters.Length >= 6, $"Expected six purposeful authored emitters, found {emitters.Length}.");
        Assert.True(emitters.Select(component => String(component?["properties"], "role"))
            .Where(role => role is not null)
            .Distinct(StringComparer.Ordinal).Count() >= 6);
        Assert.All(emitters, component => Assert.True(Number(component?["properties"], "emissiveIntensity") > 0));

        var practicals = components.Where(component => Type(component) == "Rekall.PointLight")
            .Where(component => Number(component?["properties"], "priority") >= 70)
            .ToArray();
        Assert.True(practicals.Length >= 4, $"Expected four prioritized practical lights, found {practicals.Length}.");
        Assert.All(practicals, component => Assert.True(Number(component?["properties"], "range") >= 14));
    }

    [Fact]
    public void CourtUsesTexturedPbrMaterialsShadowCastersAndVisibleAnimation()
    {
        var entities = LoadMainScene()["entities"]!.AsArray();
        var materials = entities.SelectMany(entity => entity!["components"]!.AsArray())
            .Where(component => Type(component) == "Rekall.Material")
            .ToArray();

        Assert.Contains(materials, material =>
            !string.IsNullOrWhiteSpace(String(material?["properties"], "baseColorTexture"))
            && !string.IsNullOrWhiteSpace(String(material?["properties"], "normalTexture"))
            && !string.IsNullOrWhiteSpace(String(material?["properties"], "emissiveTexture")));
        Assert.True(entities.Count(entity => Bool(entity, "visible") && HasComponent(entity, "Rekall.MeshRenderer")
            && Bool(Component(entity, "Rekall.MeshRenderer")?["properties"], "castShadows")) >= 8);

        foreach (var actorName in new[] { "AetherWarden", "TrainingSentinel", "CourtOrbiterEnemy", "CourtLancer" })
        {
            var actor = entities.Single(entity => String(entity, "name") == actorName);
            Assert.True(Bool(actor, "visible"));
            Assert.True(HasComponent(actor, "Rekall.ModelAssetReference"));
            Assert.True(HasComponent(actor, "Rekall.AnimationPlayer"));
        }
    }

    [Fact]
    public void CourtHasVisibleArchitecturalDensityAndPurposefulMaterialVariation()
    {
        var entities = LoadMainScene()["entities"]!.AsArray();
        var visibleCourtArchitecture = entities.Where(entity =>
            Bool(entity, "visible")
            && HasTag(entity, "zone.resonance")
            && (HasTag(entity, "architecture") || HasTag(entity, "cover") || HasTag(entity, "rail")))
            .ToArray();

        Assert.True(entities.Count >= 125, $"Expected a dense authored flagship world, found {entities.Count} entities.");
        Assert.True(visibleCourtArchitecture.Length >= 32,
            $"Expected at least 32 visible court architecture pieces, found {visibleCourtArchitecture.Length}.");
        Assert.True(visibleCourtArchitecture
            .SelectMany(entity => entity!["components"]!.AsArray())
            .Where(component => Type(component) == "Rekall.Material")
            .Select(component => String(component?["properties"], "baseColor"))
            .Where(color => color is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 5);
    }

    [Fact]
    public async Task PublishedDarkWardenModelResolvesToItsAuthoredMeshInNativeRuntimeFrame()
    {
        var projectRoot = Path.Combine(FindRepositoryRoot(), "Examples", "AetherfallCitadel");
        var scene = await new RekallAgeSceneStore().LoadAsync(projectRoot, "Main", CancellationToken.None);
        var world = new RekallAgeRuntimeWorldBuilder().Build(scene, projectRoot);

        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 640, 360, false);

        var warden = Assert.Single(frame.Renderables, item => item.EntityName == "AetherWarden");
        Assert.DoesNotContain(frame.Observations, item => item.Target == "AetherWarden");
        var geometry = Assert.IsType<Rekall.Age.Rendering.Abstractions.RekallAgeRuntimeViewportGeometryMesh>(warden.GeometryMesh);
        var modelDocument = JsonNode.Parse(File.ReadAllText(Path.Combine(
            projectRoot, "Assets", "Models", "aetherfall-warden-dark-model.age.model.json")))!.AsObject();
        var compiledRelativePath = modelDocument["lastSuccessfulBuild"]!["compiledMeshPath"]!.GetValue<string>();
        var compiledMesh = JsonNode.Parse(File.ReadAllText(Path.Combine(projectRoot, compiledRelativePath)))!.AsObject();
        var authoredVertexCount = compiledMesh["vertices"]!.AsArray().Count;
        var authoredIndexCount = compiledMesh["indices"]!.AsArray().Count;

        Assert.Equal(authoredVertexCount, geometry.Vertices.Count);
        Assert.Equal(authoredIndexCount, geometry.Indices.Count);
        Assert.True(authoredVertexCount >= 60_000,
            $"Expected the published Warden to retain its layered armor and silhouette detail, found {authoredVertexCount} vertices.");
    }

    private static JsonObject LoadMainScene() => JsonNode.Parse(File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "Examples", "AetherfallCitadel", "Scenes", "Main.age.scene.json")))!.AsObject();

    private static JsonNode? Component(JsonNode? entity, string type) => entity?["components"]?.AsArray()
        .FirstOrDefault(component => Type(component) == type);

    private static bool HasComponent(JsonNode? entity, string type) => Component(entity, type) is not null;
    private static bool HasTag(JsonNode? entity, string tag) => entity?["tags"]?.AsArray()
        .Any(item => string.Equals(item?.GetValue<string>(), tag, StringComparison.Ordinal)) == true;
    private static string? Type(JsonNode? component) => String(component, "type");
    private static string? String(JsonNode? node, string name) => node?[name]?.GetValue<string>();
    private static bool Bool(JsonNode? node, string name) => node?[name]?.GetValue<bool>() == true;
    private static double Number(JsonNode? node, string name) => node?[name]?.GetValue<double>() ?? 0;

    private static string FindRepositoryRoot([CallerFilePath] string sourceFilePath = "")
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath)!);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Rekall.AGE.sln"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}
