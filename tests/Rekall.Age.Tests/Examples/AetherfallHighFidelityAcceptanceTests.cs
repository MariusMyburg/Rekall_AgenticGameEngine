using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rekall.Age.Rendering;
using Rekall.Age.World;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Examples;

public sealed class AetherfallHighFidelityAcceptanceTests
{
    [Theory]
    [InlineData("aetherfall.warden.graph", "aetherfall-warden-dark-mesh", "aetherfall-warden-dark-model.age.model.json", 6, 32, "aetherfall.warden-steel.material")]
    [InlineData("aetherfall.weathered-ruin.graph", "aetherfall-weathered-ruin-mesh", "aetherfall-weathered-ruin-model.age.model.json", 8, 24, "aetherfall.ruin-trim.material")]
    public async Task WardenAndWeatheredRuinPublishEditableRevolvedForms(
        string graphAssetId,
        string meshAssetId,
        string modelFileName,
        int minimumProfileControlPoints,
        int minimumSegments,
        string materialAssetId)
    {
        var projectRoot = Path.Combine(FindRepositoryRoot(), "Examples", "AetherfallCitadel");
        var graph = await new RekallAgeModelingGraphAssetStore().LoadAsync(
            projectRoot, graphAssetId, CancellationToken.None);
        var revolve = Assert.Single(graph.Nodes, node => node.TypeId == "rekall.modeling.curve.revolve");
        var sourceLink = Assert.Single(graph.Links, link =>
            link.ToNodeId == revolve.NodeId && link.ToPortId == "curve");
        var source = Assert.Single(graph.Nodes, node =>
            node.NodeId == sourceLink.FromNodeId && node.TypeId == "rekall.modeling.curve.source");
        var document = source.Parameters["document"]!.AsObject();
        var splines = document["splines"]!.AsArray();
        var controlPoints = Assert.Single(splines)!["controlPoints"]!.AsArray();

        Assert.True(controlPoints.Count >= minimumProfileControlPoints,
            $"Expected at least {minimumProfileControlPoints} authored profile changes, found {controlPoints.Count}.");
        Assert.True(revolve.Parameters["segments"]!.GetValue<int>() >= minimumSegments);
        Assert.Equal(materialAssetId, revolve.Parameters["materialAssetId"]!.GetValue<string>());

        var baked = await new RekallAgeMeshAssetStore().LoadVersionedAsync(
            projectRoot, meshAssetId, CancellationToken.None);
        Assert.Contains(baked.Value.MaterialSlots, slot => slot.MaterialAssetId == materialAssetId);
        Assert.Contains(baked.Value.Attributes, attribute =>
            attribute.Name == "uv.generated"
            && attribute.Domain == RekallAgeGeometryDomain.Corner
            && attribute.Semantic == "texcoord-0");
        var provenance = Assert.Single(baked.Value.Attributes, attribute => attribute.Name == "curve.source.span");
        Assert.Contains(provenance.Values, value => !string.IsNullOrWhiteSpace(value.GetString()));
        Assert.Contains(baked.Value.Attributes, attribute =>
            attribute.Name == "normal.authored" && attribute.Domain == RekallAgeGeometryDomain.Corner);

        var model = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(
            projectRoot, "Assets", "Models", modelFileName)))!.AsObject();
        Assert.Equal(baked.Revision, model["lastSuccessfulBuild"]!["sourceFileRevision"]!.GetValue<string>());
        var compiledPath = model["lastSuccessfulBuild"]!["compiledMeshPath"]!.GetValue<string>();
        Assert.True(File.Exists(Path.Combine(projectRoot, compiledPath)),
            $"Compiled revolve consumer '{compiledPath}' must be retained with the project.");
    }

    [Fact]
    public async Task ConduitPublishesCounterWoundScrewGeometryAndReplacesRuinProxies()
    {
        var projectRoot = Path.Combine(FindRepositoryRoot(), "Examples", "AetherfallCitadel");
        var graph = await new RekallAgeModelingGraphAssetStore().LoadAsync(
            projectRoot, "aetherfall.conduit.graph", CancellationToken.None);
        var screws = graph.Nodes
            .Where(node => node.TypeId == "rekall.modeling.curve.revolve")
            .OrderBy(node => node.NodeId, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(2, screws.Length);
        Assert.Contains(screws, node => node.Parameters["pitchPerTurn"]!.GetValue<double>() > 0);
        Assert.Contains(screws, node => node.Parameters["pitchPerTurn"]!.GetValue<double>() < 0);
        Assert.All(screws, node =>
        {
            Assert.True(node.Parameters["angleDegrees"]!.GetValue<double>() >= 1_080);
            Assert.True(node.Parameters["segments"]!.GetValue<int>() >= 72);
        });
        var raisedCore = Assert.Single(graph.Nodes, node =>
            node.NodeId == "core-x" && node.TypeId == "rekall.modeling.transform");
        Assert.True(raisedCore.Parameters["translation"]![1]!.GetValue<double>() >= 1.7);
        Assert.Contains(graph.Links, link => link.FromNodeId == "core" && link.ToNodeId == raisedCore.NodeId);
        Assert.Contains(graph.Links, link => link.FromNodeId == raisedCore.NodeId && link.ToNodeId == "core-material");

        var evaluation = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(
            graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default,
            new(812, 0, "aetherfall-screw-acceptance", "desktop"), CancellationToken.None);
        Assert.True(evaluation.Succeeded, string.Join(Environment.NewLine, evaluation.Diagnostics.Select(item => item.Message)));
        var evaluatedOffsets = Assert.Single(
            evaluation.Outputs["mesh"].Attributes,
            attribute => attribute.Name == "revolve.axial_offset");
        Assert.Contains(evaluatedOffsets.Values, value => value.GetDouble() >= 2.7 - 1e-9);
        Assert.Contains(evaluatedOffsets.Values, value => value.GetDouble() <= -2.7 + 1e-9);

        var baked = await new RekallAgeMeshAssetStore().LoadVersionedAsync(
            projectRoot, "aetherfall-conduit-mesh", CancellationToken.None);
        Assert.Contains(baked.Value.MaterialSlots, slot => slot.MaterialAssetId == "aetherfall.citadel-obsidian.material");
        Assert.Contains(baked.Value.MaterialSlots, slot => slot.MaterialAssetId == "aetherfall.warden-steel.material");
        Assert.Contains(baked.Value.Attributes, attribute => attribute.Name == "revolve.axial_offset");
        Assert.Contains(baked.Value.Attributes, attribute =>
            attribute.Name == "normal.authored" && attribute.Domain == RekallAgeGeometryDomain.Corner);

        var model = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(
            projectRoot, "Assets", "Models", "aetherfall-conduit-model.age.model.json")))!.AsObject();
        Assert.Equal(baked.Revision, model["lastSuccessfulBuild"]!["sourceFileRevision"]!.GetValue<string>());
        var compiledPath = model["lastSuccessfulBuild"]!["compiledMeshPath"]!.GetValue<string>();
        Assert.True(File.Exists(Path.Combine(projectRoot, compiledPath)));

        var scene = await new RekallAgeSceneStore().LoadAsync(projectRoot, "Main", CancellationToken.None);
        foreach (var name in new[] { "ArrivalConduit", "ResonanceConduit" })
        {
            var entity = Assert.Single(scene.Entities, item => item.Name == name);
            var reference = Assert.Single(entity.Components, component => component.Type == "Rekall.ModelAssetReference");
            Assert.Equal("aetherfall-conduit-model", reference.Properties["assetId"]!.GetValue<string>());
            var transform = Assert.Single(entity.Components, component => component.Type == "Rekall.Transform3D");
            Assert.True(transform.Properties["scaleY"]!.GetValue<double>() >= 0.8,
                $"{name} must present the authored helix at a readable world scale.");
            Assert.True(transform.Properties["y"]!.GetValue<double>() >= 1.3,
                $"{name} must place the centered conduit mesh above the ground plane.");
        }
    }

    [Theory]
    [InlineData("aetherfall.warden.graph", "aetherfall-warden-dark-mesh", "aetherfall-warden-dark-model.age.model.json", 55.0)]
    [InlineData("aetherfall.weathered-ruin.graph", "aetherfall-weathered-ruin-mesh", "aetherfall-weathered-ruin-model.age.model.json", 35.0)]
    public async Task WardenAndWeatheredRuinPublishSplitNormalPolicy(
        string graphAssetId,
        string meshAssetId,
        string modelFileName,
        double angleDegrees)
    {
        var projectRoot = Path.Combine(FindRepositoryRoot(), "Examples", "AetherfallCitadel");
        var graph = await new RekallAgeModelingGraphAssetStore().LoadAsync(
            projectRoot,
            graphAssetId,
            CancellationToken.None);
        var auto = Assert.Single(graph.Nodes, node => node.TypeId == "rekall.modeling.auto_smooth");
        var weighted = Assert.Single(graph.Nodes, node => node.TypeId == "rekall.modeling.weighted_normals");

        Assert.Equal(angleDegrees, auto.Parameters["angleDegrees"]!.GetValue<double>());
        Assert.Equal("normal.authored", weighted.Parameters["attribute"]!.GetValue<string>());
        Assert.Equal(1.0, weighted.Parameters["cornerAngleWeight"]!.GetValue<double>());
        Assert.Contains(graph.Links, link => link.FromNodeId == auto.NodeId && link.ToNodeId == weighted.NodeId);

        var source = await new RekallAgeMeshAssetStore().LoadVersionedAsync(
            projectRoot,
            meshAssetId,
            CancellationToken.None);
        var normals = Assert.Single(source.Value.Attributes, attribute => attribute.Name == "normal.authored");
        var sharp = Assert.Single(source.Value.Attributes, attribute => attribute.Name == "normal.sharp");
        Assert.Equal(RekallAgeGeometryDomain.Corner, normals.Domain);
        Assert.Equal(RekallAgeGeometryDomain.Edge, sharp.Domain);
        Assert.All(normals.Values, value =>
        {
            var x = value[0].GetDouble();
            var y = value[1].GetDouble();
            var z = value[2].GetDouble();
            Assert.True(double.IsFinite(x) && double.IsFinite(y) && double.IsFinite(z));
            Assert.InRange(Math.Sqrt(x * x + y * y + z * z), 0.999999, 1.000001);
        });

        var model = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(
            projectRoot,
            "Assets",
            "Models",
            modelFileName)))!.AsObject();
        Assert.Equal(source.Revision, model["lastSuccessfulBuild"]!["sourceFileRevision"]!.GetValue<string>());
        var compiledPath = model["lastSuccessfulBuild"]!["compiledMeshPath"]!.GetValue<string>();
        var compiled = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(projectRoot, compiledPath)))!.AsObject();
        var directions = compiled["vertices"]!.AsArray()
            .Select(vertex => vertex!["normal"]!)
            .Select(normal => $"{Math.Round(normal["x"]!.GetValue<double>(), 4)},{Math.Round(normal["y"]!.GetValue<double>(), 4)},{Math.Round(normal["z"]!.GetValue<double>(), 4)}")
            .Distinct(StringComparer.Ordinal)
            .Take(9)
            .Count();
        Assert.True(directions >= 8, $"Expected varied published shading directions, found {directions}.");
    }

    [Fact]
    public async Task WardenMantleUsesNativeProceduralWeightsAndRuntimePoseDeformation()
    {
        var projectRoot = Path.Combine(FindRepositoryRoot(), "Examples", "AetherfallCitadel");
        var scene = await new RekallAgeSceneStore().LoadAsync(projectRoot, "Main", CancellationToken.None);
        var mantle = Assert.Single(scene.Entities, entity => entity.Name == "Warden Deformable Mantle");
        Assert.Contains("deformable-character", mantle.Tags);
        Assert.Contains(mantle.Components, component => component.Type == "Rekall.ModelAssetReference");
        Assert.Contains(mantle.Components, component => component.Type == "Rekall.SkeletonPose");

        var initial = new RekallAgeRuntimeWorldBuilder().Build(scene, projectRoot);
        var loop = RekallAgeRuntimeExecutionLoop.CreateDefault(projectRoot);
        var early = await loop.RunAsync(initial, 1, CancellationToken.None);
        var later = await loop.RunAsync(initial, 30, CancellationToken.None);
        var builder = new RekallAgeRuntimeRenderFrameBuilder();
        var earlyFrame = builder.Build(early.World, 640, 360, false);
        var laterFrame = builder.Build(later.World, 640, 360, false);
        var earlyMesh = Assert.Single(
            new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(earlyFrame), mesh => mesh.EntityName == mantle.Name);
        var laterMesh = Assert.Single(
            new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(laterFrame), mesh => mesh.EntityName == mantle.Name);

        Assert.NotEmpty(Assert.Single(earlyFrame.Renderables, item => item.EntityName == mantle.Name).GeometryMesh!.SkinBindings!);
        Assert.NotEqual(earlyMesh.Vertices[^1].X, laterMesh.Vertices[^1].X);
        Assert.DoesNotContain(laterFrame.Observations, observation => observation.Target == mantle.Name);
    }

    [Fact]
    public async Task WardenUsesModelBackedParentedArticulationThatFollowsGameplayRoot()
    {
        var projectRoot = Path.Combine(FindRepositoryRoot(), "Examples", "AetherfallCitadel");
        var scene = await new RekallAgeSceneStore().LoadAsync(projectRoot, "Main", CancellationToken.None);
        var authoredWarden = Assert.Single(scene.Entities, entity => entity.Name == "AetherWarden");
        var attachments = scene.Entities
            .Where(entity => entity.ParentId == authoredWarden.Id
                && entity.Visible
                && entity.Tags.Contains("character-articulation", StringComparer.Ordinal)
                && entity.Components.Any(component => component.Type == "Rekall.ModelAssetReference")
                && entity.Components.Any(component => component.Type == "Rekall.AnimationPlayer")
                && entity.Components.Any(component => component.Type == "Rekall.AnimationClip"))
            .OrderBy(entity => entity.Name, StringComparer.Ordinal)
            .ToArray();
        Assert.True(attachments.Length >= 2,
            $"Expected at least two visible model-backed Warden attachments, found {attachments.Length}.");

        var runeblade = Assert.Single(attachments, entity => entity.Name == "Warden Runeblade");
        var modelReference = Assert.Single(runeblade.Components, component => component.Type == "Rekall.ModelAssetReference");
        Assert.Equal("aetherfall-warden-runeblade-model", modelReference.Properties["assetId"]!.GetValue<string>());
        var bladeGraph = await new RekallAgeModelingGraphAssetStore().LoadAsync(
            projectRoot, "aetherfall.warden-runeblade.graph", CancellationToken.None);
        Assert.Contains(bladeGraph.Nodes, node => node.TypeId == "rekall.modeling.bevel");
        Assert.Contains(bladeGraph.Nodes, node => node.TypeId == "rekall.modeling.primitive.capsule");

        var initialWorld = new RekallAgeRuntimeWorldBuilder().Build(scene, projectRoot);
        var animated = await RekallAgeRuntimeExecutionLoop.CreateDefault(projectRoot)
            .RunAsync(initialWorld, 30, CancellationToken.None);
        var animatedBlade = Assert.Single(animated.World.Entities, entity => entity.Name == "Warden Runeblade");
        var authoredBladeTransform = Assert.Single(runeblade.Components, component => component.Type == "Rekall.Transform3D");
        Assert.NotEqual(
            authoredBladeTransform.Properties["roll"]!.GetValue<double>(),
            animatedBlade.Transform.Rotation3D.Z);

        var beforeFrame = new RekallAgeRuntimeRenderFrameBuilder().Build(animated.World, 640, 360, false);
        var beforeBlade = Assert.Single(beforeFrame.Renderables, item => item.EntityName == "Warden Runeblade");
        var movedWorld = animated.World with
        {
            Entities = animated.World.Entities.Select(entity => entity.Name == "AetherWarden"
                ? entity with
                {
                    Transform = entity.Transform with
                    {
                        Position3D = new RekallAgeRuntimeVector3(
                            entity.Transform.Position3D.X + 2.5,
                            entity.Transform.Position3D.Y,
                            entity.Transform.Position3D.Z)
                    }
                }
                : entity).ToArray()
        };
        var afterFrame = new RekallAgeRuntimeRenderFrameBuilder().Build(movedWorld, 640, 360, false);
        var afterBlade = Assert.Single(afterFrame.Renderables, item => item.EntityName == "Warden Runeblade");

        Assert.Equal(2.5, afterBlade.X - beforeBlade.X, precision: 4);
        Assert.DoesNotContain(afterFrame.Observations, item => item.Target == "Warden Runeblade");
    }

    [Theory]
    [InlineData("aetherfall.weathered-ruin.graph")]
    [InlineData("aetherfall.broken-arch.graph")]
    [InlineData("aetherfall.hollow-sentinel.graph")]
    [InlineData("aetherfall.rubble-boulder.graph")]
    [InlineData("aetherfall.ruin-dressing-scatter.graph")]
    [InlineData("aetherfall.warden.graph")]
    public async Task ThreeDimensionalAuthoredModelsUseFaceAwareBoxProjection(string graphAssetId)
    {
        var projectRoot = Path.Combine(FindRepositoryRoot(), "Examples", "AetherfallCitadel");
        var graph = await new RekallAgeModelingGraphAssetStore().LoadAsync(
            projectRoot, graphAssetId, CancellationToken.None);

        var projection = Assert.Single(graph.Nodes, node => node.TypeId == "rekall.modeling.project_uv");
        Assert.Equal("box", projection.Parameters["projection"]!.GetValue<string>());
    }

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
        Assert.Contains(graph.Nodes, node => node.NodeId == "wall-piers" && node.TypeId == "rekall.modeling.array");
        Assert.Contains(graph.Nodes, node => node.NodeId == "wall-header");

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
        var compiledRelativePath = model["lastSuccessfulBuild"]!["compiledMeshPath"]!.GetValue<string>();
        var compiledMesh = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(projectRoot, compiledRelativePath)))!.AsObject();
        var surfaces = compiledMesh["surfaces"]!.AsArray();

        Assert.Equal(3, surfaces.Count);
        Assert.Equal(
            [
                "aetherfall.ruin-trim.material",
                "aetherfall.ruin-mass.material",
                "aetherfall.ruin-trim.material"
            ],
            surfaces.Select(surface => surface!["materialAssetId"]!.GetValue<string>()).ToArray());
        Assert.All(surfaces, surface => Assert.True(surface!["indexCount"]!.GetValue<int>() > 0));
    }

    [Fact]
    public async Task HollowSentinelPublishesCompleteAnatomyWithSeparatedModifierBranches()
    {
        var projectRoot = Path.Combine(FindRepositoryRoot(), "Examples", "AetherfallCitadel");
        var graph = await new RekallAgeModelingGraphAssetStore().LoadAsync(
            projectRoot, "aetherfall.hollow-sentinel.graph", CancellationToken.None);

        Assert.Contains(graph.Nodes, node => node.NodeId == "legs" && node.TypeId == "rekall.modeling.mirror");
        Assert.Contains(graph.Nodes, node => node.NodeId == "boots" && node.TypeId == "rekall.modeling.mirror");
        Assert.Contains(graph.Nodes, node => node.NodeId == "horns" && node.TypeId == "rekall.modeling.mirror");
        Assert.Contains(graph.Nodes, node => node.NodeId == "smooth-join");
        Assert.Contains(graph.Nodes, node => node.NodeId == "soften" && node.TypeId == "rekall.modeling.bevel");

        var evaluation = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(
            graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default,
            new(119, 0, "aetherfall-acceptance", "desktop"), CancellationToken.None);

        Assert.True(evaluation.Succeeded, string.Join(Environment.NewLine, evaluation.Diagnostics.Select(item => item.Message)));
        Assert.True(evaluation.Outputs["mesh"].Topology.FaceIds.Count >= 4_500);
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
        Assert.Equal(96, resample.Parameters["count"]!.GetValue<int>());
        Assert.Contains(graph.Links, link => link.FromNodeId == source.NodeId && link.ToNodeId == resample.NodeId);
        Assert.Contains(graph.Nodes, node => node.NodeId == "arch-outer-ring");

        var evaluation = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(
            graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default,
            new(607, 0, "aetherfall-acceptance", "desktop"), CancellationToken.None);

        Assert.True(evaluation.Succeeded, string.Join(Environment.NewLine, evaluation.Diagnostics.Select(item => item.Message)));
        var mesh = evaluation.Outputs["mesh"];
        Assert.True(mesh.Topology.PointIds.Count >= 8_000);
        Assert.Contains(mesh.Attributes, item => item.Name == "curve.source.span");
        Assert.True(new RekallAgeMeshValidator().Validate(mesh).IsValid);

        var model = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(
            projectRoot, "Assets", "Models", "aetherfall-broken-arch-model.age.model.json")))!.AsObject();
        var compiledRelativePath = model["lastSuccessfulBuild"]!["compiledMeshPath"]!.GetValue<string>();
        var compiledMesh = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(projectRoot, compiledRelativePath)))!.AsObject();
        var surfaces = compiledMesh["surfaces"]!.AsArray();

        Assert.Equal(2, surfaces.Count);
        Assert.Equal(
            ["aetherfall.ruin-mass.material", "aetherfall.ruin-trim.material"],
            surfaces.Select(surface => surface!["materialAssetId"]!.GetValue<string>()).ToArray());
    }

    [Fact]
    public void ResonanceCourtAuthorsACompleteScalableHighFidelityContract()
    {
        var entities = LoadMainScene()["entities"]!.AsArray();
        var components = entities.SelectMany(entity => entity!["components"]!.AsArray()).ToArray();

        Assert.Single(components, component => Type(component) == "Rekall.RenderQualityProfile");
        var environment = Assert.Single(components, component => Type(component) == "Rekall.Environment3D");
        Assert.Equal("#9fb3c2", String(environment?["properties"], "ambientSkyColor"));
        Assert.Equal("#795743", String(environment?["properties"], "ambientGroundColor"));
        Assert.Equal("#111a1d", String(environment?["properties"], "backgroundColor"));
        Assert.InRange(Number(environment?["properties"], "ambientEnergy"), 1.5, 2.25);
        Assert.Single(components, component => Type(component) == "Rekall.ShadowSettings");
        Assert.True(Bool(Assert.Single(entities, entity => HasComponent(entity, "Rekall.UiCanvas")), "visible"));
        Assert.True(components.Count(component => Type(component) == "Rekall.FogVolume") >= 2);
        var globalFog = Assert.Single(components, component =>
            Type(component) == "Rekall.FogVolume"
            && String(component?["properties"], "shape") == "global");
        Assert.InRange(Number(globalFog?["properties"], "density"), 0.004, 0.012);
        Assert.InRange(Number(globalFog?["properties"], "heightFalloff"), 0.01, 0.08);

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
    public void NearFieldEdgesUseReusableModeledDressingWithoutBlockingTheGameplayLane()
    {
        var entities = LoadMainScene()["entities"]!.AsArray();
        var edgeDressing = entities.Where(entity =>
            Bool(entity, "visible")
            && HasTag(entity, "frame-edge-dressing"))
            .ToArray();

        Assert.True(edgeDressing.Length >= 4,
            $"Expected at least four near-field edge dressing clusters, found {edgeDressing.Length}.");
        Assert.All(edgeDressing, entity =>
        {
            var model = Component(entity, "Rekall.ModelAssetReference");
            Assert.Equal("aetherfall-ruin-dressing-scatter-model", String(model?["properties"], "assetId"));

            var transform = Component(entity, "Rekall.Transform3D");
            Assert.True(Math.Abs(Number(transform?["properties"], "x")) >= 8,
                $"Edge dressing '{String(entity, "name")}' obstructs the central gameplay lane.");
            Assert.True(Number(transform?["properties"], "z") <= -8,
                $"Edge dressing '{String(entity, "name")}' is outside the camera-visible near field.");
        });
        Assert.Contains(edgeDressing, entity => Number(Component(entity, "Rekall.Transform3D")?["properties"], "x") < 0);
        Assert.Contains(edgeDressing, entity => Number(Component(entity, "Rekall.Transform3D")?["properties"], "x") > 0);

        var edgeFillLights = entities.Where(entity =>
            Bool(entity, "visible")
            && HasTag(entity, "frame-edge-fill"))
            .ToArray();
        Assert.Equal(2, edgeFillLights.Length);
        Assert.All(edgeFillLights, entity =>
        {
            var light = Component(entity, "Rekall.PointLight");
            Assert.InRange(Number(light?["properties"], "intensity"), 1.0, 3.5);
            Assert.True(Number(light?["properties"], "range") >= 10);
            Assert.True(Number(light?["properties"], "priority") >= 84,
                $"Edge fill '{String(entity, "name")}' is below Aetherfall's four-light forward budget cutoff.");
            Assert.False(Bool(light?["properties"], "castShadows"));
        });
        Assert.Contains(edgeFillLights, entity => Number(Component(entity, "Rekall.Transform3D")?["properties"], "x") < 0);
        Assert.Contains(edgeFillLights, entity => Number(Component(entity, "Rekall.Transform3D")?["properties"], "x") > 0);
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
        var authoredSurfaces = compiledMesh["surfaces"]!.AsArray();

        Assert.Equal(authoredVertexCount, geometry.Vertices.Count);
        Assert.Equal(authoredIndexCount, geometry.Indices.Count);
        Assert.True(authoredVertexCount >= 40_000,
            $"Expected the published Warden to retain production-scale layered armor geometry, found {authoredVertexCount} vertices.");
        var modelingGraph = JsonNode.Parse(File.ReadAllText(Path.Combine(
            projectRoot, "Modeling", "Graphs", "aetherfall.warden.graph.age.modeling-graph.json")))!.AsObject();
        var modelingNodes = modelingGraph["nodes"]!.AsArray();
        Assert.True(modelingNodes.Count >= 85,
            $"Expected the Warden graph to retain its detailed authored construction, found {modelingNodes.Count} nodes.");
        Assert.Contains(modelingNodes, node =>
            node?["typeId"]?.GetValue<string>() == "rekall.modeling.primitive.capsule");
        Assert.Contains(modelingNodes, node =>
            node?["nodeId"]?.GetValue<string>() == "armor-smooth-join");
        Assert.Contains(modelingNodes, node =>
            node?["nodeId"]?.GetValue<string>() == "cloth-bevel");
        Assert.Equal(2, authoredSurfaces.Count);
        Assert.Equal(
            ["aetherfall.warden-steel.material", "aetherfall.warden-cloth.material"],
            authoredSurfaces.Select(surface => surface!["materialAssetId"]!.GetValue<string>()).ToArray());
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
