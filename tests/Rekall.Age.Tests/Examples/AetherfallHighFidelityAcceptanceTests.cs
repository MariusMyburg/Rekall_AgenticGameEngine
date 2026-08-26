using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Rendering;
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.Rendering.Commands;
using Rekall.Age.World;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Examples;

[Collection("Aetherfall Citadel acceptance")]
public sealed class AetherfallHighFidelityAcceptanceTests
{
    [Fact]
    public async Task WardenSteelBindsAuthoredHighFrequencySurfaceTextureEndToEnd()
    {
        const string materialAssetId = "aetherfall.warden-steel.material";
        const string textureAssetIdPrefix = "asset_aetherfall-warden-blackened-steel-albedo-v1_";
        const string normalAssetIdPrefix = "asset_aetherfall-warden-blackened-steel-normal-v1_";
        var projectRoot = Path.Combine(FindRepositoryRoot(), "Examples", "AetherfallCitadel");
        var scene = await new RekallAgeSceneStore().LoadAsync(projectRoot, "Main", CancellationToken.None);
        var world = new RekallAgeRuntimeWorldBuilder().Build(scene, projectRoot);
        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(world, 640, 360, false);

        var assets = await new RekallAgeRuntimeViewportAssetResolver().ResolveAsync(
            projectRoot,
            frame,
            CancellationToken.None);
        var material = Assert.Contains(materialAssetId, assets.Materials);
        var textureAssetId = Assert.IsType<string>(material.BaseColorTextureAssetId);
        Assert.StartsWith(textureAssetIdPrefix, textureAssetId, StringComparison.Ordinal);
        Assert.Contains(textureAssetId, assets.Images);
        var normalAssetId = Assert.IsType<string>(material.NormalTextureAssetId);
        Assert.StartsWith(normalAssetIdPrefix, normalAssetId, StringComparison.Ordinal);
        Assert.Contains(normalAssetId, assets.Images);
        Assert.InRange(material.NormalScale, 0.3f, 0.8f);

        var steelSurface = Assert.Single(
            new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame, assets),
            mesh => mesh.EntityName == "AetherWarden Surface 0");
        Assert.Equal(materialAssetId, steelSurface.MaterialAssetId);
        Assert.Equal(textureAssetId, steelSurface.BaseColorTexture?.Id);
        Assert.Equal(normalAssetId, steelSurface.NormalTexture?.Id);
    }

    [Fact]
    public async Task StaticArchitectureUsesTopologySafeVirtualGeometryWithMeasuredReduction()
    {
        var projectRoot = Path.Combine(FindRepositoryRoot(), "Examples", "AetherfallCitadel");
        var scene = await new RekallAgeSceneStore().LoadAsync(projectRoot, "Main", CancellationToken.None);
        var consumers = scene.Entities
            .Where(entity => entity.Components.Any(component => component.Type == "Rekall.VirtualGeometry"))
            .ToArray();

        Assert.True(consumers.Length >= 12);
        Assert.DoesNotContain(consumers, entity => entity.Name is
            "AetherWarden" or "CitadelGuardian" or "CourtLancer" or
            "CourtOrbiterEnemy" or "TrainingSentinel" or "Warden Articulated Pauldron");

        var result = await new InspectVirtualGeometrySceneCommand().ExecuteAsync(
            new InspectVirtualGeometrySceneRequest(projectRoot, "Main", Frames: 30, Width: 1280, Height: 720),
            new RekallAgeCommandContext(
                "acceptance",
                RekallAgeTransaction.Begin("inspect aetherfall virtual geometry"),
                CancellationToken.None));

        Assert.True(result.Ok, result.Summary);
        Assert.Equal(consumers.Length, result.Value.VirtualGeometryRenderableCount);
        Assert.True(result.Value.SourceTriangles > result.Value.SelectedTriangles);
        Assert.True(result.Value.ReducedTriangles >= 10_000);
        Assert.All(result.Value.Renderables, renderable =>
        {
            Assert.True(renderable.BudgetSatisfied, $"{renderable.EntityName} did not satisfy its virtual-geometry budget.");
            Assert.InRange(renderable.SelectedTriangles, 1, renderable.SourceTriangles);
            if (renderable.MaxSelectedTriangles > 0)
            {
                Assert.True(
                    renderable.SelectedTriangles <= renderable.MaxSelectedTriangles,
                    $"{renderable.EntityName} selected {renderable.SelectedTriangles} triangles above its {renderable.MaxSelectedTriangles} cap.");
            }
        });
    }

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
            Assert.Single(entity.Components, component => component.Type == "Rekall.MeshRenderer");
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

        var mantleGraph = await new RekallAgeModelingGraphAssetStore().LoadAsync(
            projectRoot, "aetherfall.warden-mantle.graph", CancellationToken.None);
        Assert.Contains(mantleGraph.Nodes, node => node.TypeId == "rekall.modeling.deform.taper");
        Assert.Contains(mantleGraph.Nodes, node => node.TypeId == "rekall.modeling.skin.linear_weights");
        Assert.Contains(mantleGraph.Nodes, node => node.TypeId == "rekall.modeling.solidify");
        var mantleMesh = await new RekallAgeMeshAssetStore().LoadAsync(
            projectRoot, "aetherfall-warden-mantle-mesh", CancellationToken.None);
        Assert.True(mantleMesh.Topology.PointIds.Count >= 800,
            $"Expected a dense authored mantle, found {mantleMesh.Topology.PointIds.Count} points.");
        Assert.Contains(mantleMesh.Attributes, attribute => attribute.Semantic == "joint-indices-0");
        Assert.Contains(mantleMesh.Attributes, attribute => attribute.Semantic == "joint-weights-0");

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
    public async Task WardenBodyUsesNativeNamedRigAndRuntimePoseDeformation()
    {
        var projectRoot = Path.Combine(FindRepositoryRoot(), "Examples", "AetherfallCitadel");
        var rig = await new RekallAgeRigAssetStore().LoadAsync(
            projectRoot, "aetherfall.warden.rig", CancellationToken.None);
        Assert.Equal(
            [
                "root", "pelvis", "chest", "head",
                "upper_arm_l", "forearm_l", "upper_arm_r", "forearm_r",
                "leg_l", "leg_r", "shin_l", "foot_l", "shin_r", "foot_r"
            ],
            rig.Joints.Select(joint => joint.JointId));
        Assert.Equal(8, rig.Joints[10].ParentIndex);
        Assert.Equal(10, rig.Joints[11].ParentIndex);
        Assert.Equal(9, rig.Joints[12].ParentIndex);
        Assert.Equal(12, rig.Joints[13].ParentIndex);

        var graph = await new RekallAgeModelingGraphAssetStore().LoadAsync(
            projectRoot, "aetherfall.warden.graph", CancellationToken.None);
        Assert.Contains(graph.Nodes, node => node.TypeId == "rekall.modeling.skin.envelope_weights");
        var mesh = await new RekallAgeMeshAssetStore().LoadAsync(
            projectRoot, "aetherfall-warden-dark-mesh", CancellationToken.None);
        var joints = Assert.Single(mesh.Attributes, attribute => attribute.Semantic == "joint-indices-0");
        var weights = Assert.Single(mesh.Attributes, attribute => attribute.Semantic == "joint-weights-0");
        var weightedJointIndices = joints.Values.SelectMany(value => value.EnumerateArray())
            .Select(value => value.GetInt32()).Distinct().Order().ToArray();
        Assert.True(weightedJointIndices.Length >= 10);
        Assert.All(new[] { 10, 11, 12, 13 }, jointIndex =>
            Assert.Contains(jointIndex, weightedJointIndices));
        Assert.All(weights.Values, value =>
            Assert.Equal(1, value.EnumerateArray().Sum(item => item.GetDouble()), 8));

        var scene = await new RekallAgeSceneStore().LoadAsync(projectRoot, "Main", CancellationToken.None);
        var warden = Assert.Single(scene.Entities, entity => entity.Name == "AetherWarden");
        var pose = Assert.Single(warden.Components, component => component.Type == "Rekall.RigPose");
        Assert.Equal("aetherfall.warden.rig", pose.Properties["assetId"]!.GetValue<string>());

        var initial = new RekallAgeRuntimeWorldBuilder().Build(scene, projectRoot);
        var loop = RekallAgeRuntimeExecutionLoop.CreateDefault(projectRoot);
        var early = await loop.RunAsync(initial, 1, CancellationToken.None);
        var later = await loop.RunAsync(initial, 30, CancellationToken.None);
        var earlyPose = Assert.Single(
            Assert.Single(early.World.Entities, entity => entity.Id == warden.Id).Components,
            component => component.Type == "Rekall.RigPose");
        var laterPose = Assert.Single(
            Assert.Single(later.World.Entities, entity => entity.Id == warden.Id).Components,
            component => component.Type == "Rekall.RigPose");
        Assert.NotEqual(earlyPose.Properties.ToJsonString(), laterPose.Properties.ToJsonString());
        Assert.True(laterPose.Properties["jointDeltas"]!.AsArray().Count >= 13);
        var builder = new RekallAgeRuntimeRenderFrameBuilder();
        var earlyFrame = builder.Build(early.World, 640, 360, false);
        var laterFrame = builder.Build(later.World, 640, 360, false);
        var earlyMeshes = new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(earlyFrame)
            .Where(item => item.EntityId == warden.Id)
            .OrderBy(item => item.EntityName, StringComparer.Ordinal)
            .ToArray();
        var laterMeshes = new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(laterFrame)
            .Where(item => item.EntityId == warden.Id)
            .OrderBy(item => item.EntityName, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(earlyMeshes);
        Assert.Equal(earlyMeshes.Select(item => item.EntityName), laterMeshes.Select(item => item.EntityName));
        Assert.NotEmpty(Assert.Single(earlyFrame.Renderables, item => item.EntityId == warden.Id).GeometryMesh!.SkinBindings!);
        Assert.Contains(earlyMeshes.Zip(laterMeshes), surfacePair =>
            surfacePair.First.Vertices.Zip(surfacePair.Second.Vertices).Any(vertexPair =>
                Math.Abs(vertexPair.First.X - vertexPair.Second.X) > 0.0001
                || Math.Abs(vertexPair.First.Y - vertexPair.Second.Y) > 0.0001
                || Math.Abs(vertexPair.First.Z - vertexPair.Second.Z) > 0.0001));
        Assert.DoesNotContain(laterFrame.Observations, observation => observation.Target == warden.Name);
    }

    [Fact]
    public async Task WardenRigPoseRespondsVisiblyToSemanticMovement()
    {
        var projectRoot = Path.Combine(FindRepositoryRoot(), "Examples", "AetherfallCitadel");
        var inputs = Enumerable.Range(0, 9)
            .Select(_ => new RekallAgeRuntimeInputFrame(
                SemanticActions:
                [
                    new("move.horizontal", 1, IsDown: true),
                    new("move.vertical", 0.35, IsDown: true)
                ]) { DeltaSeconds = 1.0 / 30.0 })
            .ToArray();

        var world = await new RekallAgeRuntimeSnapshotService().InspectSceneAsync(
            projectRoot, "Main", inputs.Length, inputs, CancellationToken.None);
        var warden = Assert.Single(world.Entities, entity => entity.Name == "AetherWarden");
        var pose = Assert.Single(warden.Components, component => component.Type == "Rekall.RigPose");
        var leg = Assert.Single(pose.Properties["jointDeltas"]!.AsArray(), delta =>
            delta!["jointId"]!.GetValue<string>() == "leg_l")!.AsObject();
        var matrix = leg["matrix"]!.AsArray().Select(value => value!.GetValue<double>()).ToArray();
        var shin = Assert.Single(pose.Properties["jointDeltas"]!.AsArray(), delta =>
            delta!["jointId"]!.GetValue<string>() == "shin_l")!.AsObject();
        var shinMatrix = shin["matrix"]!.AsArray().Select(value => value!.GetValue<double>()).ToArray();
        var foot = Assert.Single(pose.Properties["jointDeltas"]!.AsArray(), delta =>
            delta!["jointId"]!.GetValue<string>() == "foot_l")!.AsObject();
        var footMatrix = foot["matrix"]!.AsArray().Select(value => value!.GetValue<double>()).ToArray();

        Assert.True(Math.Abs(matrix[6]) > 0.15,
            $"Expected a readable movement-driven leg swing, found matrix[6]={matrix[6]:F6}.");
        Assert.True(Math.Abs(shinMatrix[6]) > 0.12,
            $"Expected a readable movement-driven knee bend, found matrix[6]={shinMatrix[6]:F6}.");
        Assert.True(Math.Abs(footMatrix[6]) > 0.05,
            $"Expected readable movement-driven foot compensation, found matrix[6]={footMatrix[6]:F6}.");
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
                && entity.Components.Any(component => component.Type == "Rekall.ModelAssetReference"))
            .OrderBy(entity => entity.Name, StringComparer.Ordinal)
            .ToArray();
        Assert.True(attachments.Length >= 2,
            $"Expected at least two visible model-backed Warden attachments, found {attachments.Length}.");
        Assert.All(attachments, attachment =>
            Assert.DoesNotContain(attachment.Components, component => component.Type == "Rekall.AnimationPlayer"));

        var hierarchyClip = Assert.Single(
            authoredWarden.Components, component => component.Type == "Rekall.AnimationClip");
        var targetPaths = hierarchyClip.Properties["tracks"]!.AsArray()
            .OfType<JsonObject>()
            .Select(track => track["targetPath"]?.GetValue<string>())
            .Where(path => path is not null)
            .ToArray();
        Assert.Contains("Warden Articulated Pauldron", targetPaths);
        Assert.Contains("Warden Runeblade", targetPaths);
        Assert.Contains("Warden Deformable Mantle", targetPaths);

        var runeblade = Assert.Single(attachments, entity => entity.Name == "Warden Runeblade");
        var bladeAttachment = Assert.Single(
            runeblade.Components, component => component.Type == "Rekall.RigAttachment");
        Assert.Equal("forearm_r", bladeAttachment.Properties["jointId"]!.GetValue<string>());
        var modelReference = Assert.Single(runeblade.Components, component => component.Type == "Rekall.ModelAssetReference");
        Assert.Equal("aetherfall-warden-runeblade-model", modelReference.Properties["assetId"]!.GetValue<string>());
        var bladeGraph = await new RekallAgeModelingGraphAssetStore().LoadAsync(
            projectRoot, "aetherfall.warden-runeblade.graph", CancellationToken.None);
        Assert.Contains(bladeGraph.Nodes, node =>
            node.NodeId == "hard-edge-selection"
            && node.TypeId == "rekall.modeling.selection.edge_angle");
        Assert.Contains(bladeGraph.Nodes, node =>
            node.TypeId == "rekall.modeling.bevel"
            && node.Parameters["selectionSet"]?.GetValue<string>() == "runeblade-hard-edges");
        Assert.Contains(bladeGraph.Nodes, node => node.TypeId == "rekall.modeling.primitive.capsule");

        var initialWorld = new RekallAgeRuntimeWorldBuilder().Build(scene, projectRoot);
        var animated = await RekallAgeRuntimeExecutionLoop.CreateDefault(projectRoot)
            .RunAsync(initialWorld, 30, CancellationToken.None);
        var animatedBlade = Assert.Single(animated.World.Entities, entity => entity.Name == "Warden Runeblade");
        var authoredBladeTransform = Assert.Single(runeblade.Components, component => component.Type == "Rekall.Transform3D");
        Assert.NotEqual(
            authoredBladeTransform.Properties["roll"]!.GetValue<double>(),
            animatedBlade.Transform.Rotation3D.Z);
        var authoredPauldron = Assert.Single(attachments, entity => entity.Name == "Warden Articulated Pauldron");
        var pauldronAttachment = Assert.Single(
            authoredPauldron.Components, component => component.Type == "Rekall.RigAttachment");
        Assert.Equal("upper_arm_l", pauldronAttachment.Properties["jointId"]!.GetValue<string>());
        var pauldronGraph = await new RekallAgeModelingGraphAssetStore().LoadAsync(
            projectRoot, "aetherfall.warden-pauldron.graph", CancellationToken.None);
        Assert.Contains(pauldronGraph.Nodes, node => node.NodeId == "lower-lamella");
        Assert.Contains(pauldronGraph.Nodes, node => node.NodeId == "rolled-rim");
        foreach (var obsoleteArmPart in new[] { "upper-arm", "elbow", "vambrace", "spike" })
            Assert.DoesNotContain(pauldronGraph.Nodes, node => node.NodeId == obsoleteArmPart);
        var pauldronMesh = await new RekallAgeMeshAssetStore().LoadAsync(
            projectRoot, "aetherfall-warden-pauldron-mesh", CancellationToken.None);
        var pauldronHeight = pauldronMesh.Topology.Positions.Max(point => point.Y)
            - pauldronMesh.Topology.Positions.Min(point => point.Y);
        Assert.True(pauldronHeight <= 1.2,
            $"The articulated pauldron should be a compact shoulder shell, not a duplicate full arm; height was {pauldronHeight:F3}.");
        var animatedPauldron = Assert.Single(
            animated.World.Entities, entity => entity.Name == authoredPauldron.Name);
        Assert.NotEqual(
            Assert.Single(authoredPauldron.Components, component => component.Type == "Rekall.Transform3D")
                .Properties["roll"]!.GetValue<double>(),
            animatedPauldron.Transform.Rotation3D.Z);
        var authoredMantle = Assert.Single(scene.Entities, entity => entity.Name == "Warden Deformable Mantle");
        var animatedMantle = Assert.Single(animated.World.Entities, entity => entity.Name == authoredMantle.Name);
        Assert.NotEqual(0, animatedMantle.Transform.Rotation3D.X);
        Assert.NotEqual(0, animatedMantle.Transform.Rotation3D.Z);

        var beforeFrame = new RekallAgeRuntimeRenderFrameBuilder().Build(animated.World, 640, 360, false);
        var beforeBlade = Assert.Single(beforeFrame.Renderables, item => item.EntityName == "Warden Runeblade");
        var beforePauldron = Assert.Single(beforeFrame.Renderables, item => item.EntityName == "Warden Articulated Pauldron");
        var jointMovedWorld = animated.World with
        {
            Entities = animated.World.Entities.Select(entity => entity.Name == "AetherWarden"
                ? entity with
                {
                    Components = entity.Components.Select(component => component.Type == "Rekall.RigPose"
                        ? component with { Properties = AttachmentPose(component.Properties) }
                        : component).ToArray()
                }
                : entity).ToArray()
        };
        var jointMovedFrame = new RekallAgeRuntimeRenderFrameBuilder().Build(jointMovedWorld, 640, 360, false);
        var jointMovedBlade = Assert.Single(jointMovedFrame.Renderables, item => item.EntityName == "Warden Runeblade");
        var jointMovedPauldron = Assert.Single(jointMovedFrame.Renderables, item => item.EntityName == "Warden Articulated Pauldron");
        Assert.True(RenderTransformDelta(beforeBlade, jointMovedBlade) > 0.01);
        Assert.True(RenderTransformDelta(beforePauldron, jointMovedPauldron) > 0.01);
        Assert.Equal(
            Assert.Single(animated.World.Entities, entity => entity.Name == "Warden Runeblade").Transform,
            Assert.Single(jointMovedWorld.Entities, entity => entity.Name == "Warden Runeblade").Transform);
        Assert.Equal(
            Assert.Single(animated.World.Entities, entity => entity.Name == "Warden Articulated Pauldron").Transform,
            Assert.Single(jointMovedWorld.Entities, entity => entity.Name == "Warden Articulated Pauldron").Transform);
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

        static JsonObject AttachmentPose(JsonObject source)
        {
            var properties = source.DeepClone().AsObject();
            var deltas = properties["jointDeltas"]!.AsArray();
            for (var index = deltas.Count - 1; index >= 0; index--)
            {
                var jointId = deltas[index]?["jointId"]?.GetValue<string>();
                if (jointId is "forearm_r" or "upper_arm_l")
                    deltas.RemoveAt(index);
            }
            deltas.Add(Pose("forearm_r", System.Numerics.Matrix4x4.CreateRotationZ(-0.65f)));
            deltas.Add(Pose("upper_arm_l", System.Numerics.Matrix4x4.CreateRotationZ(0.45f)));
            return properties;
        }

        static JsonObject Pose(string jointId, System.Numerics.Matrix4x4 matrix) => new()
        {
            ["jointId"] = jointId,
            ["matrix"] = new JsonArray(
                matrix.M11, matrix.M12, matrix.M13, matrix.M14,
                matrix.M21, matrix.M22, matrix.M23, matrix.M24,
                matrix.M31, matrix.M32, matrix.M33, matrix.M34,
                matrix.M41, matrix.M42, matrix.M43, matrix.M44)
        };

        static double RenderTransformDelta(
            RekallAgeRuntimeViewportRenderable before,
            RekallAgeRuntimeViewportRenderable after) =>
            Math.Abs(before.X - after.X)
            + Math.Abs(before.Y - after.Y)
            + Math.Abs(before.Z - after.Z)
            + Math.Abs(before.RotationX - after.RotationX)
            + Math.Abs(before.RotationY - after.RotationY)
            + Math.Abs(before.RotationZ - after.RotationZ);
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
        var modelingLinks = modelingGraph["links"]!.AsArray();
        Assert.True(modelingNodes.Count >= 128,
            $"Expected the Warden graph to retain its layered detailed authored construction, found {modelingNodes.Count} nodes.");
        Assert.Contains(modelingNodes, node =>
            node?["typeId"]?.GetValue<string>() == "rekall.modeling.primitive.capsule");
        Assert.Contains(modelingNodes, node =>
            node?["nodeId"]?.GetValue<string>() == "armor-smooth-join");
        Assert.Contains(modelingNodes, node =>
            node?["nodeId"]?.GetValue<string>() == "cloth-bevel");
        foreach (var nodeId in new[]
        {
            "thigh-guards", "bracers", "gauntlets", "eye-slit-x", "aether-material", "coat-tail", "coat-tails",
            "shoulder-shells", "abdomen-plates", "knee-cops", "helmet-crest-x", "breastplates",
            "forearm-body-l", "forearm-body-r", "thigh-underlayers", "shin-underlayers",
            "helmet-crown-x", "helmet-cheeks", "helmet-nose-x", "sternum-ridge-x", "shoulder-lamellas",
            "cuirass-flutes", "mail-field", "helmet-rivets", "greave-ridges"
        })
        {
            Assert.Contains(modelingNodes, node => node?["nodeId"]?.GetValue<string>() == nodeId);
        }
        foreach (var obsoletePlaceholder in new[]
        {
            "pauldron-spike", "blade", "cloak", "coat-l", "coat-r"
        })
        {
            Assert.DoesNotContain(modelingNodes, node =>
                node?["nodeId"]?.GetValue<string>() == obsoletePlaceholder);
        }
        Assert.Contains(modelingNodes, node =>
            node?["nodeId"]?.GetValue<string>() == "coat-tail"
            && node?["typeId"]?.GetValue<string>() == "rekall.modeling.primitive.capsule");
        Assert.Contains(modelingNodes, node =>
            node?["nodeId"]?.GetValue<string>() == "boot"
            && node?["typeId"]?.GetValue<string>() == "rekall.modeling.primitive.capsule");
        Assert.Contains(modelingNodes, node =>
            node?["nodeId"]?.GetValue<string>() == "faceplate"
            && node?["typeId"]?.GetValue<string>() == "rekall.modeling.primitive.capsule");
        Assert.Contains(modelingNodes, node =>
            node?["nodeId"]?.GetValue<string>() == "abdomen-plates"
            && node?["typeId"]?.GetValue<string>() == "rekall.modeling.array");
        Assert.Contains(modelingNodes, node =>
            node?["nodeId"]?.GetValue<string>() == "knee-cops"
            && node?["typeId"]?.GetValue<string>() == "rekall.modeling.mirror");
        Assert.Contains(modelingNodes, node =>
            node?["nodeId"]?.GetValue<string>() == "shoulder-shells"
            && node?["typeId"]?.GetValue<string>() == "rekall.modeling.mirror");
        Assert.Contains(modelingNodes, node =>
            node?["nodeId"]?.GetValue<string>() == "breastplate"
            && node?["typeId"]?.GetValue<string>() == "rekall.modeling.primitive.capsule");
        Assert.Contains(modelingNodes, node =>
            node?["nodeId"]?.GetValue<string>() == "breastplates"
            && node?["typeId"]?.GetValue<string>() == "rekall.modeling.array");
        Assert.DoesNotContain(modelingNodes, node =>
            node?["nodeId"]?.GetValue<string>() == "faceplate-inset");
        foreach (var nodeId in new[]
        {
            "leather-hard-join", "leather-post-join", "leather-material",
            "trim-hard-join", "trim-smooth-join", "trim-post-join", "trim-material"
        })
        {
            Assert.Contains(modelingNodes, node => node?["nodeId"]?.GetValue<string>() == nodeId);
        }
        foreach (var ownership in new[]
        {
            (Source: "belt-x", Target: "leather-hard-join"),
            (Source: "tassets", Target: "leather-hard-join"),
            (Source: "boots", Target: "leather-post-join"),
            (Source: "belt-buckle-x", Target: "trim-hard-join"),
            (Source: "gorget-x", Target: "trim-smooth-join"),
            (Source: "rivet-array", Target: "trim-smooth-join"),
            (Source: "cloak-clasp-x", Target: "trim-smooth-join"),
            (Source: "helmet-brow-x", Target: "trim-smooth-join")
        })
        {
            Assert.Contains(modelingLinks, link =>
                link?["fromNodeId"]?.GetValue<string>() == ownership.Source
                && link?["toNodeId"]?.GetValue<string>() == ownership.Target);
        }
        Assert.Equal(5, authoredSurfaces.Count);
        Assert.Equal(
            [
                "aetherfall.warden-steel.material",
                "aetherfall.warden-cloth.material",
                "aetherfall.warden-aether.material",
                "aetherfall.warden-leather.material",
                "aetherfall.warden-bronze.material"
            ],
            authoredSurfaces.Select(surface => surface!["materialAssetId"]!.GetValue<string>()).ToArray());

        var leatherMaterial = JsonNode.Parse(File.ReadAllText(Path.Combine(
            projectRoot, "Materials", "Graphs", "aetherfall.warden-leather.material.age.material-graph.json")))!.AsObject();
        var leatherPbr = leatherMaterial["nodes"]!.AsArray().Single(node =>
            node?["typeId"]?.GetValue<string>() == "rekall.material.surface.pbr")!.AsObject();
        Assert.InRange(leatherPbr["parameters"]!["metallic"]!.GetValue<double>(), 0, 0.08);
        Assert.InRange(leatherPbr["parameters"]!["roughness"]!.GetValue<double>(), 0.72, 1);

        var bronzeMaterial = JsonNode.Parse(File.ReadAllText(Path.Combine(
            projectRoot, "Materials", "Graphs", "aetherfall.warden-bronze.material.age.material-graph.json")))!.AsObject();
        var bronzePbr = bronzeMaterial["nodes"]!.AsArray().Single(node =>
            node?["typeId"]?.GetValue<string>() == "rekall.material.surface.pbr")!.AsObject();
        Assert.InRange(bronzePbr["parameters"]!["metallic"]!.GetValue<double>(), 0.45, 0.85);
        Assert.InRange(bronzePbr["parameters"]!["roughness"]!.GetValue<double>(), 0.48, 0.82);

        var aetherMaterialGraph = JsonNode.Parse(File.ReadAllText(Path.Combine(
            projectRoot, "Materials", "Graphs", "aetherfall.warden-aether.material.age.material-graph.json")))!.AsObject();
        var emissiveNode = aetherMaterialGraph["nodes"]!.AsArray().Single(node =>
            node?["typeId"]?.GetValue<string>() == "rekall.material.surface.emissive")!.AsObject();
        Assert.Equal("#d18a42", emissiveNode["parameters"]!["color"]!.GetValue<string>());
        Assert.Equal(3.2, emissiveNode["parameters"]!["strength"]!.GetValue<double>());
    }

    [Fact]
    public async Task GameplayCameraUsesCloserIsometricCompositionAroundTheWarden()
    {
        var projectRoot = Path.Combine(FindRepositoryRoot(), "Examples", "AetherfallCitadel");
        var world = await new RekallAgeRuntimeSnapshotService().InspectSceneAsync(
            projectRoot, "Main", 3, CancellationToken.None);
        var warden = Assert.Single(world.Entities, entity => entity.Name == "AetherWarden");
        var camera = Assert.Single(world.Entities, entity => entity.Name == "CitadelCamera");

        Assert.Equal(
            38,
            camera.Components.Single(component => component.Type == "Rekall.Camera3D")
                .Properties["fieldOfView"]!.GetValue<double>());
        Assert.InRange(camera.Transform.Position3D.Y, 14.4, 14.6);
        Assert.InRange(
            camera.Transform.Position3D.Z - warden.Transform.Position3D.Z,
            -15.1,
            -14.9);
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
