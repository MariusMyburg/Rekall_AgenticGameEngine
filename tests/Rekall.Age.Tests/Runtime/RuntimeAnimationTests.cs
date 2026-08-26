using System.Text.Json.Nodes;
using Rekall.Age.Assets;
using Rekall.Age.Modules;
using Rekall.Age.Rendering;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;
using Rekall.Age.Tests.Rendering;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Runtime;

public sealed class RuntimeAnimationTests
{
    [Fact]
    public async Task AnimationPlayerSphericallySamplesNamedNativeRigRotationTrack()
    {
        var actor = RekallAgeEntityDocument.Create("Actor", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.RigPose",
                new JsonObject
                {
                    ["assetId"] = "test.rig",
                    ["jointDeltas"] = new JsonArray()
                }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AnimationClip",
                new JsonObject
                {
                    ["version"] = 1,
                    ["durationSeconds"] = 1,
                    ["tracks"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["component"] = "Rekall.RigPose",
                            ["jointId"] = "shin_l",
                            ["property"] = "rotation",
                            ["interpolation"] = "linear",
                            ["keys"] = new JsonArray
                            {
                                new JsonObject { ["time"] = 0, ["value"] = new JsonArray(0, 0, 0, 1) },
                                new JsonObject
                                {
                                    ["time"] = 1,
                                    ["value"] = new JsonArray(
                                        Math.Sin(Math.PI / 4), 0, 0, Math.Cos(Math.PI / 4))
                                }
                            }
                        }
                    }
                }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AnimationPlayer",
                new JsonObject { ["playing"] = true, ["loopMode"] = "clamp" }));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(
                new RekallAgeRuntimeWorldBuilder().Build(
                    RekallAgeSceneDocument.Create("Main", ["world", "animation"]).AddEntity(actor)),
                30,
                CancellationToken.None);

        var pose = Assert.Single(Assert.Single(result.World.Entities).Components,
            component => component.Type == "Rekall.RigPose");
        var delta = Assert.IsType<JsonObject>(Assert.Single(Assert.IsType<JsonArray>(pose.Properties["jointDeltas"])));
        Assert.Equal("shin_l", delta["jointId"]!.GetValue<string>());
        var matrix = Assert.IsType<JsonArray>(delta["matrix"])
            .Select(value => value!.GetValue<double>())
            .ToArray();
        Assert.Equal(Math.Sqrt(0.5), matrix[5], precision: 3);
        Assert.Equal(Math.Sqrt(0.5), Math.Abs(matrix[6]), precision: 3);
        Assert.DoesNotContain(result.World.Observations, observation => observation.Severity == "error");
    }

    [Fact]
    public async Task AnimationMixerBlendsNativeRigRotationsPerStableJointId()
    {
        var root = TestPaths.CreateTempDirectory();
        var assetDirectory = Path.Combine(root, "Assets", "animation");
        Directory.CreateDirectory(assetDirectory);
        var identity = new JsonArray(0, 0, 0, 1);
        var quarterX = new JsonArray(Math.Sin(Math.PI / 4), 0, 0, Math.Cos(Math.PI / 4));
        var quarterY = new JsonArray(0, Math.Sin(Math.PI / 4), 0, Math.Cos(Math.PI / 4));
        await File.WriteAllTextAsync(
            Path.Combine(assetDirectory, "idle.age.animation.json"),
            RigClip(RigRotationTrack("shin_l", identity), RigRotationTrack("foot_l", identity)).ToJsonString());
        await File.WriteAllTextAsync(
            Path.Combine(assetDirectory, "walk.age.animation.json"),
            RigClip(RigRotationTrack("shin_l", quarterX), RigRotationTrack("foot_l", quarterY)).ToJsonString());
        await new RekallAgeAssetCatalogStore().SaveAsync(
            root,
            new RekallAgeAssetCatalogDocument(
            [
                new("asset-idle", "idle", "Idle", "animation", string.Empty,
                    "Assets/animation/idle.age.animation.json", "test"),
                new("asset-walk", "walk", "Walk", "animation", string.Empty,
                    "Assets/animation/walk.age.animation.json", "test")
            ]),
            CancellationToken.None);
        var actor = RekallAgeEntityDocument.Create("Actor", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.RigPose",
                new JsonObject { ["assetId"] = "test.rig", ["jointDeltas"] = new JsonArray() }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AnimationMixer",
                new JsonObject
                {
                    ["playing"] = true,
                    ["layers"] = new JsonArray
                    {
                        new JsonObject { ["name"] = "idle", ["clip"] = "asset-idle", ["weight"] = 0.5 },
                        new JsonObject { ["name"] = "walk", ["clip"] = "asset-walk", ["weight"] = 0.5 }
                    }
                }));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault(root).RunAsync(
            new RekallAgeRuntimeWorldBuilder().Build(
                RekallAgeSceneDocument.Create("Main", ["world", "animation"]).AddEntity(actor)),
            1,
            CancellationToken.None);

        var pose = Assert.Single(Assert.Single(result.World.Entities).Components,
            component => component.Type == "Rekall.RigPose");
        var deltas = Assert.IsType<JsonArray>(pose.Properties["jointDeltas"]);
        Assert.Equal(2, deltas.Count);
        var shin = Assert.Single(deltas.OfType<JsonObject>(), delta => delta["jointId"]!.GetValue<string>() == "shin_l");
        var foot = Assert.Single(deltas.OfType<JsonObject>(), delta => delta["jointId"]!.GetValue<string>() == "foot_l");
        Assert.Equal(Math.Sqrt(0.5), shin["matrix"]![5]!.GetValue<double>(), precision: 3);
        Assert.Equal(Math.Sqrt(0.5), foot["matrix"]![0]!.GetValue<double>(), precision: 3);
        Assert.DoesNotContain(result.World.Observations, observation => observation.Severity == "error");
    }

    [Fact]
    public async Task PreAnimationGameplayCanDriveNativeRigMixerBeforeSameFrameSampling()
    {
        var root = TestPaths.CreateTempDirectory();
        var assetDirectory = Path.Combine(root, "Assets", "animation");
        Directory.CreateDirectory(assetDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(assetDirectory, "idle.age.animation.json"),
            RigClip(RigRotationTrack("shin_l", new JsonArray(0, 0, 0, 1))).ToJsonString());
        await File.WriteAllTextAsync(
            Path.Combine(assetDirectory, "walk.age.animation.json"),
            RigClip(RigRotationTrack("shin_l", new JsonArray(
                Math.Sin(Math.PI / 4), 0, 0, Math.Cos(Math.PI / 4)))).ToJsonString());
        await new RekallAgeAssetCatalogStore().SaveAsync(
            root,
            new RekallAgeAssetCatalogDocument(
            [
                new("asset-idle", "idle", "Idle", "animation", string.Empty,
                    "Assets/animation/idle.age.animation.json", "test"),
                new("asset-walk", "walk", "Walk", "animation", string.Empty,
                    "Assets/animation/walk.age.animation.json", "test")
            ]),
            CancellationToken.None);
        var actor = RekallAgeEntityDocument.Create("Actor", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.RigPose",
                new JsonObject { ["assetId"] = "test.rig", ["jointDeltas"] = new JsonArray() }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AnimationMixer",
                new JsonObject
                {
                    ["playing"] = true,
                    ["layers"] = new JsonArray
                    {
                        new JsonObject { ["name"] = "idle", ["clip"] = "asset-idle", ["weight"] = 1 },
                        new JsonObject { ["name"] = "walk", ["clip"] = "asset-walk", ["weight"] = 0 }
                    }
                }));
        var world = new RekallAgeRuntimeWorldBuilder().Build(
            RekallAgeSceneDocument.Create("Main", ["world", "animation"]).AddEntity(actor));
        using var loop = new RekallAgeRuntimeExecutionLoop(
            [new PreAnimationRigMixerDriver(), new RekallAgeTransformAnimationSystem(root)],
            TimeSpan.FromSeconds(1.0 / 60.0));

        var result = await loop.RunAsync(world, 1, CancellationToken.None);

        var pose = Assert.Single(Assert.Single(result.World.Entities).Components,
            component => component.Type == "Rekall.RigPose");
        var delta = Assert.IsType<JsonObject>(Assert.Single(Assert.IsType<JsonArray>(pose.Properties["jointDeltas"])));
        Assert.Equal(0, delta["matrix"]![5]!.GetValue<double>(), precision: 3);
        Assert.Equal(1, Math.Abs(delta["matrix"]![6]!.GetValue<double>()), precision: 3);
    }

    [Fact]
    public async Task AnimationPlayerLoadsReusableClipFromProjectAssetCatalog()
    {
        var root = TestPaths.CreateTempDirectory();
        var assetDirectory = Path.Combine(root, "Assets", "animation");
        Directory.CreateDirectory(assetDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(assetDirectory, "move.age.animation.json"),
            new JsonObject
            {
                ["version"] = 1,
                ["durationSeconds"] = 1,
                ["tracks"] = new JsonArray { ScalarTrack("Rekall.Transform3D", "x", 0, 12, "linear") }
            }.ToJsonString());
        await new RekallAgeAssetCatalogStore().SaveAsync(
            root,
            new RekallAgeAssetCatalogDocument(
            [
                new RekallAgeAssetDocument(
                    "asset-move",
                    "move",
                    "Move",
                    "animation",
                    string.Empty,
                    "Assets/animation/move.age.animation.json",
                    "test")
            ]),
            CancellationToken.None);
        var actor = RekallAgeEntityDocument.Create("Actor", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject { ["x"] = 0 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AnimationPlayer",
                new JsonObject { ["clip"] = "asset-move", ["playing"] = true, ["loopMode"] = "clamp" }));
        var world = new RekallAgeRuntimeWorldBuilder().Build(
            RekallAgeSceneDocument.Create("Main", ["world", "animation"]).AddEntity(actor));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault(root)
            .RunAsync(world, 30, CancellationToken.None);

        Assert.Equal(6, Assert.Single(result.World.Entities).Transform.Position3D.X, precision: 3);
        var player = Assert.Single(result.World.Subsystems.Animation.Players);
        Assert.Equal("asset-move", player.ClipAssetId);
        Assert.False(player.InlineClip);
        Assert.DoesNotContain(result.World.Observations, observation => observation.Severity == "error");
    }

    [Fact]
    public async Task AnimationMixerCrossFadesGenericPropertyTracksDeterministically()
    {
        var root = TestPaths.CreateTempDirectory();
        var assetDirectory = Path.Combine(root, "Assets", "animation");
        Directory.CreateDirectory(assetDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(assetDirectory, "source.age.animation.json"),
            new JsonObject
            {
                ["version"] = 1,
                ["durationSeconds"] = 1,
                ["tracks"] = new JsonArray { ScalarTrack("Rekall.Transform3D", "x", 0, 0, "linear") }
            }.ToJsonString());
        await File.WriteAllTextAsync(
            Path.Combine(assetDirectory, "target.age.animation.json"),
            new JsonObject
            {
                ["version"] = 1,
                ["durationSeconds"] = 1,
                ["tracks"] = new JsonArray { ScalarTrack("Rekall.Transform3D", "x", 10, 10, "linear") }
            }.ToJsonString());
        await new RekallAgeAssetCatalogStore().SaveAsync(
            root,
            new RekallAgeAssetCatalogDocument(
            [
                new RekallAgeAssetDocument("asset-source", "source", "Source", "animation", string.Empty, "Assets/animation/source.age.animation.json", "test"),
                new RekallAgeAssetDocument("asset-target", "target", "Target", "animation", string.Empty, "Assets/animation/target.age.animation.json", "test")
            ]),
            CancellationToken.None);
        var actor = RekallAgeEntityDocument.Create("Actor", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject { ["x"] = 0 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AnimationMixer",
                new JsonObject
                {
                    ["playing"] = true,
                    ["layers"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["name"] = "source", ["clip"] = "asset-source", ["weight"] = 1,
                            ["targetWeight"] = 0, ["fadeSeconds"] = 1, ["loopMode"] = "clamp"
                        },
                        new JsonObject
                        {
                            ["name"] = "target", ["clip"] = "asset-target", ["weight"] = 0,
                            ["targetWeight"] = 1, ["fadeSeconds"] = 1, ["loopMode"] = "clamp"
                        }
                    }
                }));
        var loop = RekallAgeRuntimeExecutionLoop.CreateDefault(root);

        var halfway = await loop.RunAsync(
            new RekallAgeRuntimeWorldBuilder().Build(
                RekallAgeSceneDocument.Create("Main", ["world", "animation"]).AddEntity(actor)),
            30,
            CancellationToken.None);

        var halfwayActor = Assert.Single(halfway.World.Entities);
        Assert.Equal(5, halfwayActor.Transform.Position3D.X, precision: 3);
        var halfwayState = Assert.Single(halfwayActor.Components, component => component.Type == "Rekall.AnimationState");
        var halfwayLayers = Assert.IsType<JsonArray>(halfwayState.Properties["layers"]);
        Assert.Equal(0.5, halfwayLayers[0]!["weight"]!.GetValue<double>(), precision: 3);
        Assert.Equal(0.5, halfwayLayers[1]!["weight"]!.GetValue<double>(), precision: 3);

        var completed = await loop.RunAsync(halfway.World, 30, CancellationToken.None);

        var continuous = await RekallAgeRuntimeExecutionLoop.CreateDefault(root).RunAsync(
            new RekallAgeRuntimeWorldBuilder().Build(
                RekallAgeSceneDocument.Create("Main", ["world", "animation"]).AddEntity(actor)),
            60,
            CancellationToken.None);

        Assert.Equal(10, Assert.Single(completed.World.Entities).Transform.Position3D.X, precision: 3);
        Assert.Equal(
            Assert.Single(continuous.World.Entities).Transform.Position3D.X,
            Assert.Single(completed.World.Entities).Transform.Position3D.X,
            precision: 6);
        var player = Assert.Single(completed.World.Subsystems.Animation.Players);
        Assert.Equal("AnimationMixer", player.Kind);
        Assert.Equal(2, player.LayerCount);
        Assert.Equal(1, player.ActiveLayerCount);
        Assert.Collection(
            player.Layers,
            source =>
            {
                Assert.Equal("source", source.Name);
                Assert.Equal(0, source.Weight, precision: 3);
            },
            target =>
            {
                Assert.Equal("target", target.Name);
                Assert.Equal(1, target.Weight, precision: 3);
            });
        Assert.DoesNotContain(completed.World.Observations, observation => observation.Severity == "error");
    }

    [Fact]
    public async Task AnimationMixerBlendsTracksOnTargetedChildHierarchy()
    {
        var root = TestPaths.CreateTempDirectory();
        var assetDirectory = Path.Combine(root, "Assets", "animation");
        Directory.CreateDirectory(assetDirectory);
        var actor = RekallAgeEntityDocument.Create("Actor", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AnimationMixer",
                new JsonObject
                {
                    ["playing"] = true,
                    ["layers"] = new JsonArray
                    {
                        new JsonObject { ["name"] = "guard", ["clip"] = "clip-guard", ["weight"] = 0.25 },
                        new JsonObject { ["name"] = "strike", ["clip"] = "clip-strike", ["weight"] = 0.75 }
                    }
                }));
        var hand = RekallAgeEntityDocument.Create("Hand", ["joint"]) with { ParentId = actor.Id };
        hand = hand.AddComponent(RekallAgeComponentDocument.Create(
            "Rekall.Transform3D", new JsonObject { ["roll"] = 0 }));
        foreach (var (name, value) in new[] { ("guard", -20), ("strike", 40) })
        {
            var target = name == "guard"
                ? new KeyValuePair<string, JsonNode?>("targetPath", "Hand")
                : new KeyValuePair<string, JsonNode?>("targetEntityId", hand.Id);
            await File.WriteAllTextAsync(
                Path.Combine(assetDirectory, $"{name}.age.animation.json"),
                new JsonObject
                {
                    ["version"] = 1,
                    ["durationSeconds"] = 1,
                    ["tracks"] = new JsonArray
                    {
                        new JsonObject
                        {
                            [target.Key] = target.Value,
                            ["component"] = "Rekall.Transform3D",
                            ["property"] = "roll",
                            ["interpolation"] = "linear",
                            ["keys"] = new JsonArray
                            {
                                new JsonObject { ["time"] = 0, ["value"] = value },
                                new JsonObject { ["time"] = 1, ["value"] = value }
                            }
                        }
                    }
                }.ToJsonString());
        }
        await new RekallAgeAssetCatalogStore().SaveAsync(
            root,
            new RekallAgeAssetCatalogDocument(
            [
                new RekallAgeAssetDocument("clip-guard", "guard", "Guard", "animation", string.Empty,
                    "Assets/animation/guard.age.animation.json", "test"),
                new RekallAgeAssetDocument("clip-strike", "strike", "Strike", "animation", string.Empty,
                    "Assets/animation/strike.age.animation.json", "test")
            ]),
            CancellationToken.None);
        var world = new RekallAgeRuntimeWorldBuilder().Build(
            RekallAgeSceneDocument.Create("Main", ["world", "animation"])
                .AddEntity(actor)
                .AddEntity(hand));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault(root)
            .RunAsync(world, 1, CancellationToken.None);

        var runtimeHand = result.World.Entities.Single(entity => entity.Id == hand.Id);
        Assert.Equal(25, runtimeHand.Transform.Rotation3D.Z, precision: 3);
        Assert.DoesNotContain(result.World.Observations, observation => observation.Severity == "error");
    }

    [Fact]
    public async Task AnimationMixerRejectsUnboundedLayerWork()
    {
        var layers = new JsonArray();
        for (var index = 0; index < 33; index++)
        {
            layers.Add(new JsonObject { ["name"] = $"layer-{index}", ["clip"] = $"asset-{index}" });
        }
        var actor = RekallAgeEntityDocument.Create("Actor", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject()))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AnimationMixer",
                new JsonObject { ["layers"] = layers }));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault().RunAsync(
            new RekallAgeRuntimeWorldBuilder().Build(
                RekallAgeSceneDocument.Create("Main", ["world", "animation"]).AddEntity(actor)),
            1,
            CancellationToken.None);

        var observation = Assert.Single(result.World.Observations, item =>
            item.Code == "runtime.animation.mixer_layer_limit_exceeded");
        Assert.Contains("32", observation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnimationMixerBlendsVectorsColorsAndDiscreteValuesByWeight()
    {
        var root = TestPaths.CreateTempDirectory();
        var assetDirectory = Path.Combine(root, "Assets", "animation");
        Directory.CreateDirectory(assetDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(assetDirectory, "visual-source.age.animation.json"),
            new JsonObject
            {
                ["version"] = 1,
                ["durationSeconds"] = 1,
                ["tracks"] = new JsonArray
                {
                    ValueTrack("Rekall.SpriteRenderer", "offset", new JsonArray(0, 0), new JsonArray(0, 0)),
                    ValueTrack("Rekall.SpriteRenderer", "tint", "#000000", "#000000"),
                    ValueTrack("Rekall.SpriteRenderer", "sprite", "idle", "idle")
                }
            }.ToJsonString());
        await File.WriteAllTextAsync(
            Path.Combine(assetDirectory, "visual-target.age.animation.json"),
            new JsonObject
            {
                ["version"] = 1,
                ["durationSeconds"] = 1,
                ["tracks"] = new JsonArray
                {
                    ValueTrack("Rekall.SpriteRenderer", "offset", new JsonArray(8, 4), new JsonArray(8, 4)),
                    ValueTrack("Rekall.SpriteRenderer", "tint", "#ffffff", "#ffffff"),
                    ValueTrack("Rekall.SpriteRenderer", "sprite", "run", "run")
                }
            }.ToJsonString());
        await new RekallAgeAssetCatalogStore().SaveAsync(
            root,
            new RekallAgeAssetCatalogDocument(
            [
                new RekallAgeAssetDocument("asset-visual-source", "visual-source", "Visual Source", "animation", string.Empty, "Assets/animation/visual-source.age.animation.json", "test"),
                new RekallAgeAssetDocument("asset-visual-target", "visual-target", "Visual Target", "animation", string.Empty, "Assets/animation/visual-target.age.animation.json", "test")
            ]),
            CancellationToken.None);
        var actor = RekallAgeEntityDocument.Create("Actor", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.SpriteRenderer",
                new JsonObject { ["offset"] = new JsonArray(0, 0), ["tint"] = "#000000", ["sprite"] = "idle" }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AnimationMixer",
                new JsonObject
                {
                    ["layers"] = new JsonArray
                    {
                        new JsonObject { ["name"] = "source", ["clip"] = "asset-visual-source", ["weight"] = 0.25 },
                        new JsonObject { ["name"] = "target", ["clip"] = "asset-visual-target", ["weight"] = 0.75 }
                    }
                }));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault(root).RunAsync(
            new RekallAgeRuntimeWorldBuilder().Build(
                RekallAgeSceneDocument.Create("Main", ["world", "animation"]).AddEntity(actor)),
            1,
            CancellationToken.None);

        var sprite = Assert.Single(Assert.Single(result.World.Entities).Components, component =>
            component.Type == "Rekall.SpriteRenderer");
        Assert.Equal(6, sprite.Properties["offset"]![0]!.GetValue<double>(), precision: 3);
        Assert.Equal(3, sprite.Properties["offset"]![1]!.GetValue<double>(), precision: 3);
        Assert.Equal("#bfbfbf", sprite.Properties["tint"]!.GetValue<string>());
        Assert.Equal("run", sprite.Properties["sprite"]!.GetValue<string>());
    }

    [Fact]
    public async Task SkeletalAnimatorSamplesGlbJointPoseAtFixedTime()
    {
        var root = TestPaths.CreateTempDirectory();
        var source = Path.Combine(root, "animated-source.glb");
        await File.WriteAllBytesAsync(source, GlbTestMeshFactory.CreateSingleJointAnimatedGlb());
        var asset = await RekallAgeAssetImporter.ImportAsync(
            root,
            source,
            "model",
            "Animated Rig",
            CancellationToken.None);
        await new RekallAgeAssetCatalogStore().SaveAsync(
            root,
            new RekallAgeAssetCatalogDocument([asset]),
            CancellationToken.None);
        var actor = RekallAgeEntityDocument.Create("Rigged Actor", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Transform3D",
                new JsonObject { ["z"] = 3 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.MeshRenderer",
                new JsonObject { ["mesh"] = asset.Id }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.SkeletalAnimator",
                new JsonObject
                {
                    ["Model"] = asset.Id,
                    ["Animation"] = "Lift",
                    ["SkinIndex"] = 0,
                    ["Playing"] = true,
                    ["LoopMode"] = "clamp"
                }));
        var camera = RekallAgeEntityDocument.Create("Camera", ["camera"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Camera3D",
                new JsonObject { ["active"] = true }));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault(root).RunAsync(
            new RekallAgeRuntimeWorldBuilder().Build(
                RekallAgeSceneDocument.Create("Main", ["world", "animation"])
                    .AddEntity(actor)
                    .AddEntity(camera)),
            30,
            CancellationToken.None);

        var runtimeActor = Assert.Single(result.World.Entities, entity => entity.Name == "Rigged Actor");
        var pose = Assert.Single(runtimeActor.Components, component => component.Type == "Rekall.SkeletonPose");
        Assert.Equal(1, pose.Properties["jointCount"]!.GetValue<int>());
        var joint = Assert.IsType<JsonObject>(Assert.IsType<JsonArray>(pose.Properties["joints"])[0]);
        Assert.Equal("Joint", joint["name"]!.GetValue<string>());
        Assert.Equal(1, joint["translation"]![1]!.GetValue<double>(), precision: 3);
        var player = Assert.Single(result.World.Subsystems.Animation.Players);
        Assert.Equal("SkeletalAnimator", player.Kind);
        Assert.Equal("Lift", player.AnimationName);
        Assert.Equal("Rig", player.SkinName);
        Assert.Equal(1, player.JointCount);
        Assert.Equal(0.5, player.TimeSeconds, precision: 3);
        Assert.DoesNotContain(result.World.Observations, observation => observation.Severity == "error");
        var frame = new RekallAgeRuntimeRenderFrameBuilder().Build(result.World, 160, 90, false);
        var assets = await new RekallAgeRuntimeViewportAssetResolver().ResolveAsync(root, frame, CancellationToken.None);
        var renderedMesh = Assert.Single(new RekallAgeVulkanSceneMeshBuilder().BuildMeshes(frame, assets));
        Assert.Equal(1, renderedMesh.Vertices.Min(vertex => vertex.Y), precision: 3);
        Assert.Equal(2, renderedMesh.Vertices.Max(vertex => vertex.Y), precision: 3);
    }

    [Fact]
    public async Task SkeletalAnimatorSamplesGlbCubicSplineTangentsAtFixedTime()
    {
        var root = TestPaths.CreateTempDirectory();
        var source = Path.Combine(root, "cubic-animated-source.glb");
        await File.WriteAllBytesAsync(source, GlbTestMeshFactory.CreateSingleJointCubicAnimatedGlb());
        var asset = await RekallAgeAssetImporter.ImportAsync(
            root, source, "model", "Cubic Rig", CancellationToken.None);
        await new RekallAgeAssetCatalogStore().SaveAsync(
            root, new RekallAgeAssetCatalogDocument([asset]), CancellationToken.None);
        var actor = RekallAgeEntityDocument.Create("Rigged Actor", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.SkeletalAnimator",
                new JsonObject
                {
                    ["Model"] = asset.Id,
                    ["Animation"] = "Cubic Lift",
                    ["SkinIndex"] = 0,
                    ["Playing"] = true,
                    ["LoopMode"] = "clamp"
                }));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault(root).RunAsync(
            new RekallAgeRuntimeWorldBuilder().Build(
                RekallAgeSceneDocument.Create("Main", ["world", "animation"]).AddEntity(actor)),
            30,
            CancellationToken.None);

        var pose = Assert.Single(Assert.Single(result.World.Entities).Components,
            component => component.Type == "Rekall.SkeletonPose");
        var joint = Assert.IsType<JsonObject>(Assert.IsType<JsonArray>(pose.Properties["joints"])[0]);
        Assert.Equal(1.5, joint["translation"]![1]!.GetValue<double>(), precision: 3);
        Assert.DoesNotContain(result.World.Observations, observation => observation.Severity == "error");
    }

    [Fact]
    public async Task SkeletalAnimatorAppliesCubicScaleAndNormalizesCubicRotation()
    {
        var scaleResult = await RunCubicSkeletalAssetAsync(
            GlbTestMeshFactory.CreateSingleJointCubicAnimatedGlb(targetPath: "scale"),
            "Cubic Lift");
        var scalePose = Assert.Single(Assert.Single(scaleResult.World.Entities).Components,
            component => component.Type == "Rekall.SkeletonPose");
        var scaleJoint = Assert.IsType<JsonObject>(Assert.IsType<JsonArray>(scalePose.Properties["joints"])[0]);
        Assert.Equal(1.5, scaleJoint["scale"]![1]!.GetValue<double>(), precision: 3);

        var rotationResult = await RunCubicSkeletalAssetAsync(
            GlbTestMeshFactory.CreateSingleJointCubicRotationGlb(),
            "Cubic Turn");
        var rotationPose = Assert.Single(Assert.Single(rotationResult.World.Entities).Components,
            component => component.Type == "Rekall.SkeletonPose");
        var rotationJoint = Assert.IsType<JsonObject>(Assert.IsType<JsonArray>(rotationPose.Properties["joints"])[0]);
        var rotation = Assert.IsType<JsonArray>(rotationJoint["rotation"]);
        Assert.Equal(0.894427, rotation[2]!.GetValue<double>(), precision: 4);
        Assert.Equal(0.447214, rotation[3]!.GetValue<double>(), precision: 4);
        var lengthSquared = rotation.Select(component => component!.GetValue<double>())
            .Sum(component => component * component);
        Assert.Equal(1, lengthSquared, precision: 5);
    }

    [Fact]
    public async Task SkeletalAnimatorRejectsNearZeroCubicQuaternionWithoutPublishingPose()
    {
        var result = await RunCubicSkeletalAssetAsync(
            GlbTestMeshFactory.CreateSingleJointCubicRotationGlb(zeroQuaternion: true),
            "Cubic Turn");

        Assert.DoesNotContain(Assert.Single(result.World.Entities).Components,
            component => component.Type == "Rekall.SkeletonPose");
        var observation = Assert.Single(result.World.Observations,
            item => item.Code == "runtime.animation.skeletal_sample_invalid");
        Assert.Contains("near-zero quaternion", observation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SkeletalAnimatorReportsMissingModelAsStructuredObservation()
    {
        var actor = RekallAgeEntityDocument.Create("Rigged Actor", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.SkeletalAnimator",
                new JsonObject { ["Model"] = "asset-missing" }));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault().RunAsync(
            new RekallAgeRuntimeWorldBuilder().Build(
                RekallAgeSceneDocument.Create("Main", ["world", "animation"]).AddEntity(actor)),
            1,
            CancellationToken.None);

        Assert.Contains(result.World.Observations, observation =>
            observation.Code == "runtime.animation.skeletal_model_missing"
            && observation.EntityName == "Rigged Actor");
    }

    [Fact]
    public async Task AnimationAssetCannotEscapeProjectRoot()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeAssetCatalogStore().SaveAsync(
            root,
            new RekallAgeAssetCatalogDocument(
            [
                new RekallAgeAssetDocument(
                    "asset-escape",
                    "escape",
                    "Escape",
                    "animation",
                    string.Empty,
                    "../outside.age.animation.json",
                    "test")
            ]),
            CancellationToken.None);
        var actor = RekallAgeEntityDocument.Create("Actor", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject { ["x"] = 0 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AnimationPlayer",
                new JsonObject { ["clip"] = "asset-escape", ["playing"] = true }));
        var world = new RekallAgeRuntimeWorldBuilder().Build(
            RekallAgeSceneDocument.Create("Main", ["world", "animation"]).AddEntity(actor));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault(root)
            .RunAsync(world, 1, CancellationToken.None);

        var observation = Assert.Single(result.World.Observations, item =>
            item.Code == "runtime.animation.clip_asset_invalid");
        Assert.Contains("escapes the project root", observation.Message);
    }

    [Fact]
    public async Task AnimationAssetSizeLimitIsEnforcedBeforeJsonParsing()
    {
        var root = TestPaths.CreateTempDirectory();
        var assetDirectory = Path.Combine(root, "Assets", "animation");
        Directory.CreateDirectory(assetDirectory);
        var path = Path.Combine(assetDirectory, "oversized.age.animation.json");
        await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.SetLength((4 * 1024 * 1024) + 1);
        }
        await new RekallAgeAssetCatalogStore().SaveAsync(
            root,
            new RekallAgeAssetCatalogDocument(
            [
                new RekallAgeAssetDocument(
                    "asset-oversized",
                    "oversized",
                    "Oversized",
                    "animation",
                    string.Empty,
                    "Assets/animation/oversized.age.animation.json",
                    "test")
            ]),
            CancellationToken.None);
        var actor = RekallAgeEntityDocument.Create("Actor", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject { ["x"] = 0 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AnimationPlayer",
                new JsonObject { ["clip"] = "asset-oversized", ["playing"] = true }));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault(root).RunAsync(
            new RekallAgeRuntimeWorldBuilder().Build(
                RekallAgeSceneDocument.Create("Main", ["world", "animation"]).AddEntity(actor)),
            1,
            CancellationToken.None);

        var observation = Assert.Single(result.World.Observations, item =>
            item.Code == "runtime.animation.clip_asset_invalid");
        Assert.Contains("4194304-byte limit", observation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnsupportedAnimationClipVersionProducesStructuredObservation()
    {
        var actor = RekallAgeEntityDocument.Create("Actor", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AnimationClip",
                new JsonObject { ["version"] = 99, ["durationSeconds"] = 1, ["tracks"] = new JsonArray() }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AnimationPlayer",
                new JsonObject { ["playing"] = true }));
        var world = new RekallAgeRuntimeWorldBuilder().Build(
            RekallAgeSceneDocument.Create("Main", ["world", "animation"]).AddEntity(actor));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(world, 1, CancellationToken.None);

        var observation = Assert.Single(result.World.Observations, item =>
            item.Code == "runtime.animation.unsupported_clip_version");
        Assert.Equal("animation", observation.Subsystem);
        Assert.Equal("Actor", observation.TargetName);
    }

    [Fact]
    public async Task MissingAnimationTrackTargetProducesStructuredObservation()
    {
        var actor = RekallAgeEntityDocument.Create("Actor", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AnimationClip",
                new JsonObject
                {
                    ["version"] = 1,
                    ["durationSeconds"] = 2,
                    ["tracks"] = new JsonArray { ScalarTrack("Rekall.Transform3D", "x", 0, 1, "linear") }
                }))
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.AnimationPlayer", new JsonObject()));
        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault().RunAsync(
            new RekallAgeRuntimeWorldBuilder().Build(
                RekallAgeSceneDocument.Create("Main", ["world", "animation"]).AddEntity(actor)),
            1,
            CancellationToken.None);

        Assert.Contains(result.World.Observations, observation =>
            observation.Code == "runtime.animation.track_component_missing"
            && observation.Message.Contains("Rekall.Transform3D"));
    }

    [Fact]
    public async Task AnimationClipSamplesGenericScalarAndSpriteTracksDeterministically()
    {
        var actor = RekallAgeEntityDocument.Create("Actor", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Transform3D",
                new JsonObject { ["x"] = 0, ["y"] = 0, ["z"] = 0 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.SpriteRenderer",
                new JsonObject
                {
                    ["sprite"] = "idle",
                    ["opacity"] = 1,
                    ["offset"] = new JsonArray(0, 10),
                    ["tint"] = "#000000"
                }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AnimationClip",
                new JsonObject
                {
                    ["version"] = 1,
                    ["durationSeconds"] = 1,
                    ["tracks"] = new JsonArray
                    {
                        ScalarTrack("Rekall.Transform3D", "x", 0, 10, "linear"),
                        ScalarTrack("Rekall.SpriteRenderer", "opacity", 1, 0, "linear"),
                        ValueTrack(
                            "Rekall.SpriteRenderer",
                            "offset",
                            new JsonArray(0, 10),
                            new JsonArray(10, 20)),
                        ValueTrack("Rekall.SpriteRenderer", "tint", "#000000", "#ffffff"),
                        new JsonObject
                        {
                            ["component"] = "Rekall.SpriteRenderer",
                            ["property"] = "sprite",
                            ["interpolation"] = "step",
                            ["keys"] = new JsonArray
                            {
                                new JsonObject { ["time"] = 0, ["value"] = "idle" },
                                new JsonObject { ["time"] = 0.5, ["value"] = "run" }
                            }
                        }
                    }
                }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AnimationPlayer",
                new JsonObject { ["playing"] = true, ["loopMode"] = "clamp" }));
        var world = new RekallAgeRuntimeWorldBuilder().Build(
            RekallAgeSceneDocument.Create("Main", ["world", "animation"]).AddEntity(actor));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(world, 30, CancellationToken.None);

        var runtimeActor = Assert.Single(result.World.Entities);
        Assert.Equal(5, runtimeActor.Transform.Position3D.X, precision: 4);
        var sprite = Assert.Single(runtimeActor.Components, component => component.Type == "Rekall.SpriteRenderer");
        Assert.Equal(0.5, sprite.Properties["opacity"]!.GetValue<double>(), precision: 4);
        Assert.Equal("run", sprite.Properties["sprite"]!.GetValue<string>());
        Assert.Equal(5, sprite.Properties["offset"]![0]!.GetValue<double>(), precision: 4);
        Assert.Equal(15, sprite.Properties["offset"]![1]!.GetValue<double>(), precision: 4);
        Assert.Equal("#808080", sprite.Properties["tint"]!.GetValue<string>());
        var state = Assert.Single(runtimeActor.Components, component => component.Type == "Rekall.AnimationState");
        Assert.Equal(0.5, state.Properties["timeSeconds"]!.GetValue<double>(), precision: 4);
        var player = Assert.Single(result.World.Subsystems.Animation.Players);
        Assert.True(player.InlineClip);
        Assert.True(player.Playing);
        Assert.Equal(0.5, player.TimeSeconds, precision: 4);
        Assert.DoesNotContain(result.World.Observations, observation =>
            observation.Code == "REKALL_ANIMATION_MISSING_CLIP");
    }

    [Fact]
    public async Task AnimationClipTargetsChildHierarchyAndExplicitEntityDeterministically()
    {
        var actor = RekallAgeEntityDocument.Create("Actor", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Transform3D",
                new JsonObject { ["x"] = 0, ["roll"] = 0 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AnimationClip",
                new JsonObject
                {
                    ["version"] = 1,
                    ["durationSeconds"] = 1,
                    ["tracks"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["targetPath"] = "Shoulder/Hand",
                            ["component"] = "Rekall.Transform3D",
                            ["property"] = "roll",
                            ["interpolation"] = "linear",
                            ["keys"] = new JsonArray
                            {
                                new JsonObject { ["time"] = 0, ["value"] = -20 },
                                new JsonObject { ["time"] = 1, ["value"] = 40 }
                            }
                        }
                    }
                }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AnimationPlayer",
                new JsonObject { ["playing"] = true, ["loopMode"] = "clamp" }));
        var shoulder = RekallAgeEntityDocument.Create("Shoulder", ["joint"]) with { ParentId = actor.Id };
        shoulder = shoulder.AddComponent(RekallAgeComponentDocument.Create(
            "Rekall.Transform3D", new JsonObject { ["roll"] = 0 }));
        var hand = RekallAgeEntityDocument.Create("Hand", ["joint"]) with { ParentId = shoulder.Id };
        hand = hand.AddComponent(RekallAgeComponentDocument.Create(
            "Rekall.Transform3D", new JsonObject { ["roll"] = 0 }));
        var lantern = RekallAgeEntityDocument.Create("Lantern", ["prop"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Transform3D", new JsonObject { ["x"] = 0 }));
        var explicitTrack = new JsonObject
        {
            ["targetEntityId"] = lantern.Id,
            ["component"] = "Rekall.Transform3D",
            ["property"] = "x",
            ["interpolation"] = "linear",
            ["keys"] = new JsonArray
            {
                new JsonObject { ["time"] = 0, ["value"] = 0 },
                new JsonObject { ["time"] = 1, ["value"] = 8 }
            }
        };
        ((JsonArray)actor.Components.Single(component => component.Type == "Rekall.AnimationClip")
            .Properties["tracks"]!).Add(explicitTrack);
        var world = new RekallAgeRuntimeWorldBuilder().Build(
            RekallAgeSceneDocument.Create("Main", ["world", "animation"])
                .AddEntity(actor)
                .AddEntity(shoulder)
                .AddEntity(hand)
                .AddEntity(lantern));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(world, 30, CancellationToken.None);

        var runtimeHand = result.World.Entities.Single(entity => entity.Id == hand.Id);
        var runtimeLantern = result.World.Entities.Single(entity => entity.Id == lantern.Id);
        Assert.Equal(10, runtimeHand.Transform.Rotation3D.Z, precision: 3);
        Assert.Equal(4, runtimeLantern.Transform.Position3D.X, precision: 3);
        Assert.DoesNotContain(result.World.Observations, observation => observation.Severity == "error");
    }

    [Fact]
    public async Task CubicAnimationTrackUsesDurationScaledHermiteTangents()
    {
        var actor = RekallAgeEntityDocument.Create("Actor", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Transform3D",
                new JsonObject { ["x"] = 0 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AnimationClip",
                new JsonObject
                {
                    ["version"] = 1,
                    ["durationSeconds"] = 1,
                    ["tracks"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["component"] = "Rekall.Transform3D",
                            ["property"] = "x",
                            ["interpolation"] = "cubic",
                            ["keys"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["time"] = 0,
                                    ["value"] = 0,
                                    ["inTangent"] = 0,
                                    ["outTangent"] = 6
                                },
                                new JsonObject
                                {
                                    ["time"] = 2,
                                    ["value"] = 6,
                                    ["inTangent"] = 0,
                                    ["outTangent"] = 0
                                }
                            }
                        }
                    }
                }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AnimationPlayer",
                new JsonObject { ["playing"] = true, ["loopMode"] = "clamp" }));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault().RunAsync(
            new RekallAgeRuntimeWorldBuilder().Build(
                RekallAgeSceneDocument.Create("Main", ["world", "animation"]).AddEntity(actor)),
            60,
            CancellationToken.None);

        Assert.Equal(4.5, Assert.Single(result.World.Entities).Transform.Position3D.X, precision: 4);
        Assert.DoesNotContain(result.World.Observations, observation => observation.Severity == "error");
    }

    [Fact]
    public async Task CubicAnimationTrackSamplesFlatVectorsAndColorsComponentWise()
    {
        var actor = RekallAgeEntityDocument.Create("Actor", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.SpriteRenderer",
                new JsonObject { ["offset"] = new JsonArray(0, 10), ["tint"] = "#102030" }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AnimationClip",
                new JsonObject
                {
                    ["version"] = 1,
                    ["durationSeconds"] = 1,
                    ["tracks"] = new JsonArray
                    {
                        CubicTrack(
                            "Rekall.SpriteRenderer",
                            "offset",
                            new JsonArray(0, 10),
                            new JsonArray(10, 20),
                            new JsonArray(20, 40),
                            new JsonArray(0, 0)),
                        CubicTrack(
                            "Rekall.SpriteRenderer",
                            "tint",
                            "#102030",
                            "#506070",
                            new JsonArray(256, 0, -256),
                            new JsonArray(0, 0, 0))
                    }
                }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AnimationPlayer",
                new JsonObject { ["playing"] = true, ["loopMode"] = "clamp" }));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault().RunAsync(
            new RekallAgeRuntimeWorldBuilder().Build(
                RekallAgeSceneDocument.Create("Main", ["world", "animation"]).AddEntity(actor)),
            30,
            CancellationToken.None);

        var sprite = Assert.Single(Assert.Single(result.World.Entities).Components,
            component => component.Type == "Rekall.SpriteRenderer");
        Assert.Equal("7.5", sprite.Properties["offset"]![0]!.ToJsonString());
        Assert.Equal("20", sprite.Properties["offset"]![1]!.ToJsonString());
        Assert.Equal("#504030", sprite.Properties["tint"]!.GetValue<string>());
        Assert.DoesNotContain(result.World.Observations, observation => observation.Severity == "error");
    }

    [Fact]
    public async Task UnknownAnimationInterpolationFailsClosedWithoutTargetMutation()
    {
        var actor = RekallAgeEntityDocument.Create("Actor", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Transform3D",
                new JsonObject { ["x"] = 7 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AnimationClip",
                new JsonObject
                {
                    ["version"] = 1,
                    ["durationSeconds"] = 1,
                    ["tracks"] = new JsonArray
                    {
                        ScalarTrack("Rekall.Transform3D", "x", 0, 10, "bezier")
                    }
                }))
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.AnimationPlayer", new JsonObject()));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault().RunAsync(
            new RekallAgeRuntimeWorldBuilder().Build(
                RekallAgeSceneDocument.Create("Main", ["world", "animation"]).AddEntity(actor)),
            30,
            CancellationToken.None);

        Assert.Equal(7, Assert.Single(result.World.Entities).Transform.Position3D.X);
        var observation = Assert.Single(result.World.Observations,
            item => item.Code == "runtime.animation.interpolation_invalid");
        Assert.Contains("Rekall.Transform3D.x", observation.Message, StringComparison.Ordinal);
        Assert.Contains("bezier", observation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MalformedCubicAnimationTrackEmitsBoundedObservationWithoutTargetMutation()
    {
        var invalidTrack = CubicTrack("Rekall.Transform3D", "x", 0, 10, 20, 0);
        ((JsonObject)((JsonArray)invalidTrack["keys"]!)[0]!).Remove("outTangent");
        var actor = RekallAgeEntityDocument.Create("Actor", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Transform3D",
                new JsonObject { ["x"] = 7 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AnimationClip",
                new JsonObject
                {
                    ["version"] = 1,
                    ["durationSeconds"] = 1,
                    ["tracks"] = new JsonArray { invalidTrack }
                }))
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.AnimationPlayer", new JsonObject()));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault().RunAsync(
            new RekallAgeRuntimeWorldBuilder().Build(
                RekallAgeSceneDocument.Create("Main", ["world", "animation"]).AddEntity(actor)),
            30,
            CancellationToken.None);

        Assert.Equal(7, Assert.Single(result.World.Entities).Transform.Position3D.X);
        var observation = Assert.Single(result.World.Observations,
            item => item.Code == "runtime.animation.cubic_key_invalid");
        Assert.Contains("Rekall.Transform3D.x", observation.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("System.", observation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnimationClipLoopsAndEmitsBoundMarkerFacts()
    {
        var actor = RekallAgeEntityDocument.Create("Actor", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Transform3D",
                new JsonObject { ["x"] = 0 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AnimationClip",
                new JsonObject
                {
                    ["version"] = 1,
                    ["durationSeconds"] = 0.25,
                    ["tracks"] = new JsonArray { ScalarTrack("Rekall.Transform3D", "x", 0, 1, "linear") },
                    ["events"] = new JsonArray
                    {
                        new JsonObject { ["time"] = 0.125, ["name"] = "midpoint" }
                    }
                }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AnimationPlayer",
                new JsonObject { ["playing"] = true, ["loopMode"] = "loop", ["speed"] = 1 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.EventBindings",
                new JsonObject
                {
                    ["events"] = new JsonArray
                    {
                        new JsonObject { ["event"] = "animation.event", ["handler"] = "on-marker" }
                    }
                }));
        var world = new RekallAgeRuntimeWorldBuilder().Build(
            RekallAgeSceneDocument.Create("Main", ["world", "animation"]).AddEntity(actor));
        var loop = RekallAgeRuntimeExecutionLoop.CreateDefault();

        var result = await loop.RunAsync(world, 8, CancellationToken.None);

        var marker = Assert.Single(result.World.Subsystems.Events.Events, runtimeEvent =>
            runtimeEvent.Type == "animation.event" && runtimeEvent.Handler == "on-marker");
        Assert.Equal("midpoint", marker.Payload["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task AnimationPlayerSupportsClampAndPingPongTimeModes()
    {
        static RekallAgeEntityDocument Animated(string name, string mode) =>
            RekallAgeEntityDocument.Create(name, ["actor"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject { ["x"] = 0 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.AnimationClip",
                    new JsonObject
                    {
                        ["version"] = 1,
                        ["durationSeconds"] = 1,
                        ["tracks"] = new JsonArray { ScalarTrack("Rekall.Transform3D", "x", 0, 10, "linear") }
                    }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.AnimationPlayer",
                    new JsonObject { ["playing"] = true, ["loopMode"] = mode }));
        var world = new RekallAgeRuntimeWorldBuilder().Build(
            RekallAgeSceneDocument.Create("Main", ["world", "animation"])
                .AddEntity(Animated("Clamp", "clamp"))
                .AddEntity(Animated("PingPong", "pingpong")));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
            .RunAsync(world, 75, CancellationToken.None);

        var clamp = Assert.Single(result.World.Entities, entity => entity.Name == "Clamp");
        var pingPong = Assert.Single(result.World.Entities, entity => entity.Name == "PingPong");
        Assert.Equal(10, clamp.Transform.Position3D.X, precision: 4);
        Assert.False(result.World.Subsystems.Animation.Players.Single(player => player.EntityName == "Clamp").Playing);
        Assert.Equal(7.5, pingPong.Transform.Position3D.X, precision: 3);
    }

    [Fact]
    public async Task AnimationRuntimeRejectsUnboundedInlineTrackAndKeyWorkWithStructuredObservations()
    {
        var excessiveTracks = new JsonArray();
        for (var index = 0; index < 1_025; index++)
        {
            excessiveTracks.Add(ScalarTrack("Rekall.Transform3D", "x", 0, index, "linear"));
        }

        var excessiveKeys = new JsonArray();
        for (var index = 0; index < 4_097; index++)
        {
            excessiveKeys.Add(new JsonObject { ["time"] = index / 60.0, ["value"] = index });
        }

        static RekallAgeEntityDocument Animated(string name, JsonArray tracks) =>
            RekallAgeEntityDocument.Create(name, ["actor"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject { ["x"] = 0 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.AnimationClip",
                    new JsonObject { ["version"] = 1, ["durationSeconds"] = 120, ["tracks"] = tracks }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.AnimationPlayer", new JsonObject()));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault().RunAsync(
            new RekallAgeRuntimeWorldBuilder().Build(
                RekallAgeSceneDocument.Create("Main", ["world", "animation"])
                    .AddEntity(Animated("Too Many Tracks", excessiveTracks))
                    .AddEntity(Animated(
                        "Too Many Keys",
                        new JsonArray
                        {
                            new JsonObject
                            {
                                ["component"] = "Rekall.Transform3D",
                                ["property"] = "x",
                                ["keys"] = excessiveKeys
                            }
                        }))),
            1,
            CancellationToken.None);

        Assert.Contains(result.World.Observations, observation =>
            observation.TargetName == "Too Many Tracks"
            && observation.Code == "runtime.animation.track_limit_exceeded");
        Assert.Contains(result.World.Observations, observation =>
            observation.TargetName == "Too Many Keys"
            && observation.Code == "runtime.animation.key_limit_exceeded");
        Assert.All(result.World.Entities, entity => Assert.Equal(0, entity.Transform.Position3D.X));
    }

    [Fact]
    public async Task AnimationRuntimeReportsMalformedTracksInsteadOfFailingSilently()
    {
        var actor = RekallAgeEntityDocument.Create("Malformed", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject { ["x"] = 0 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AnimationClip",
                new JsonObject
                {
                    ["version"] = 1,
                    ["durationSeconds"] = 1,
                    ["tracks"] = new JsonArray
                    {
                        new JsonObject { ["component"] = "Rekall.Transform3D", ["keys"] = new JsonArray() }
                    }
                }))
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.AnimationPlayer", new JsonObject()));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault().RunAsync(
            new RekallAgeRuntimeWorldBuilder().Build(
                RekallAgeSceneDocument.Create("Main", ["world", "animation"]).AddEntity(actor)),
            1,
            CancellationToken.None);

        Assert.Contains(result.World.Observations, observation =>
            observation.Code == "runtime.animation.track_invalid"
            && observation.TargetName == "Malformed");
    }

    [Fact]
    public async Task AnimationRuntimeBoundsMarkerWorkAndReportsInvalidTrackEntries()
    {
        var markers = new JsonArray();
        for (var index = 0; index < 4_097; index++)
        {
            markers.Add(new JsonObject { ["time"] = index / 4_096.0, ["name"] = $"marker-{index}" });
        }

        var actor = RekallAgeEntityDocument.Create("Bounded", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject { ["x"] = 0 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AnimationClip",
                new JsonObject
                {
                    ["version"] = 1,
                    ["durationSeconds"] = 1,
                    ["tracks"] = new JsonArray
                    {
                        "not-an-object",
                        new JsonObject
                        {
                            ["component"] = "Rekall.Transform3D",
                            ["property"] = "x",
                            ["keys"] = new JsonArray { new JsonObject { ["time"] = 0 } }
                        }
                    },
                    ["events"] = markers
                }))
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.AnimationPlayer", new JsonObject()))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.EventBindings",
                new JsonObject
                {
                    ["events"] = new JsonArray
                    {
                        new JsonObject { ["event"] = "animation.event", ["handler"] = "on-animation" }
                    }
                }));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault().RunAsync(
            new RekallAgeRuntimeWorldBuilder().Build(
                RekallAgeSceneDocument.Create("Main", ["world", "animation"]).AddEntity(actor)),
            1,
            CancellationToken.None);

        Assert.Contains(result.World.Observations, observation =>
            observation.Code == "runtime.animation.track_invalid");
        Assert.Contains(result.World.Observations, observation =>
            observation.Code == "runtime.animation.track_no_valid_keys");
        Assert.Contains(result.World.Observations, observation =>
            observation.Code == "runtime.animation.marker_limit_exceeded");
        Assert.DoesNotContain(result.World.Subsystems.Events.Events, runtimeEvent =>
            runtimeEvent.Type == "animation.event");
    }

    [Fact]
    public async Task AnimationRuntimeIsDeterministicAcrossLongRunResumeBoundaries()
    {
        var actor = RekallAgeEntityDocument.Create("Long Running", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject { ["x"] = 0 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AnimationClip",
                new JsonObject
                {
                    ["version"] = 1,
                    ["durationSeconds"] = 1.25,
                    ["tracks"] = new JsonArray { ScalarTrack("Rekall.Transform3D", "x", -10, 10, "smoothstep") }
                }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AnimationPlayer",
                new JsonObject { ["loopMode"] = "pingpong", ["speed"] = 0.75 }));
        var initial = new RekallAgeRuntimeWorldBuilder().Build(
            RekallAgeSceneDocument.Create("Main", ["world", "animation"]).AddEntity(actor));
        var loop = RekallAgeRuntimeExecutionLoop.CreateDefault();

        var continuous = await loop.RunAsync(initial, 7_200, CancellationToken.None);
        var firstHalf = await loop.RunAsync(initial, 3_600, CancellationToken.None);
        var resumed = await loop.RunAsync(firstHalf.World, 3_600, CancellationToken.None);

        var continuousEntity = Assert.Single(continuous.World.Entities);
        var resumedEntity = Assert.Single(resumed.World.Entities);
        Assert.Equal(continuousEntity.Transform.Position3D.X, resumedEntity.Transform.Position3D.X, precision: 10);
        var continuousState = Assert.Single(continuousEntity.Components, component => component.Type == "Rekall.AnimationState");
        var resumedState = Assert.Single(resumedEntity.Components, component => component.Type == "Rekall.AnimationState");
        Assert.Equal(
            continuousState.Properties["rawTimeSeconds"]!.GetValue<double>(),
            resumedState.Properties["rawTimeSeconds"]!.GetValue<double>(),
            precision: 10);
        Assert.Equal(
            continuousState.Properties["completedCycles"]!.GetValue<int>(),
            resumedState.Properties["completedCycles"]!.GetValue<int>());
    }

    [Fact]
    public async Task AnimationObservationsRemainBoundedToTheCurrentFrameDuringLongRuns()
    {
        var actor = RekallAgeEntityDocument.Create("Invalid", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AnimationClip",
                new JsonObject { ["version"] = 99 }))
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.AnimationPlayer", new JsonObject()));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault().RunAsync(
            new RekallAgeRuntimeWorldBuilder().Build(
                RekallAgeSceneDocument.Create("Main", ["world", "animation"]).AddEntity(actor)),
            7_200,
            CancellationToken.None);

        var observation = Assert.Single(result.World.Observations);
        Assert.Equal("runtime.animation.unsupported_clip_version", observation.Code);
        Assert.Equal(7_200, observation.Frame);
    }

    [Fact]
    public async Task AnimationRuntimeSurvivesDeterministicMalformedTrackCorpus()
    {
        var tracks = new JsonArray();
        for (var index = 0; index < 512; index++)
        {
            tracks.Add((index % 6) switch
            {
                0 => JsonValue.Create($"invalid-{index}"),
                1 => new JsonObject(),
                2 => new JsonObject
                {
                    ["component"] = "Rekall.Transform3D", ["property"] = "x", ["keys"] = new JsonArray()
                },
                3 => new JsonObject
                {
                    ["component"] = "Rekall.Transform3D", ["property"] = "x",
                    ["keys"] = new JsonArray { new JsonObject { ["time"] = "not-a-time" } }
                },
                4 => ScalarTrack("Rekall.MissingComponent", "value", 0, index, "linear"),
                _ => ScalarTrack("Rekall.Transform3D", "x", 0, index, "smoothstep")
            });
        }
        var actor = RekallAgeEntityDocument.Create("Corpus", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject { ["x"] = 0 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.AnimationClip",
                new JsonObject { ["version"] = 1, ["durationSeconds"] = 1, ["tracks"] = tracks }))
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.AnimationPlayer", new JsonObject()));

        var result = await RekallAgeRuntimeExecutionLoop.CreateDefault().RunAsync(
            new RekallAgeRuntimeWorldBuilder().Build(
                RekallAgeSceneDocument.Create("Main", ["world", "animation"]).AddEntity(actor)),
            120,
            CancellationToken.None);

        Assert.Equal(120, result.World.FrameIndex);
        Assert.InRange(result.World.Observations.Count, 1, 512);
        Assert.True(double.IsFinite(Assert.Single(result.World.Entities).Transform.Position3D.X));
    }

    private static JsonObject ScalarTrack(
        string component,
        string property,
        double from,
        double to,
        string interpolation)
    {
        return new JsonObject
        {
            ["component"] = component,
            ["property"] = property,
            ["interpolation"] = interpolation,
            ["keys"] = new JsonArray
            {
                new JsonObject { ["time"] = 0, ["value"] = from },
                new JsonObject { ["time"] = 1, ["value"] = to }
            }
        };
    }

    private static JsonObject RigClip(params JsonObject[] tracks) => new()
    {
        ["version"] = 1,
        ["durationSeconds"] = 1,
        ["tracks"] = new JsonArray(tracks.Select(track => (JsonNode)track).ToArray())
    };

    private static JsonObject RigRotationTrack(string jointId, JsonArray rotation) => new()
    {
        ["component"] = "Rekall.RigPose",
        ["jointId"] = jointId,
        ["property"] = "rotation",
        ["interpolation"] = "linear",
        ["keys"] = new JsonArray
        {
            new JsonObject { ["time"] = 0, ["value"] = rotation.DeepClone() },
            new JsonObject { ["time"] = 1, ["value"] = rotation.DeepClone() }
        }
    };

    private static JsonObject ValueTrack(
        string component,
        string property,
        JsonNode from,
        JsonNode to)
    {
        return new JsonObject
        {
            ["component"] = component,
            ["property"] = property,
            ["interpolation"] = "linear",
            ["keys"] = new JsonArray
            {
                new JsonObject { ["time"] = 0, ["value"] = from },
                new JsonObject { ["time"] = 1, ["value"] = to }
            }
        };
    }

    private static JsonObject CubicTrack(
        string component,
        string property,
        JsonNode from,
        JsonNode to,
        JsonNode outTangent,
        JsonNode inTangent)
    {
        return new JsonObject
        {
            ["component"] = component,
            ["property"] = property,
            ["interpolation"] = "cubic",
            ["keys"] = new JsonArray
            {
                new JsonObject
                {
                    ["time"] = 0,
                    ["value"] = from,
                    ["inTangent"] = outTangent.DeepClone(),
                    ["outTangent"] = outTangent
                },
                new JsonObject
                {
                    ["time"] = 1,
                    ["value"] = to,
                    ["inTangent"] = inTangent,
                    ["outTangent"] = inTangent.DeepClone()
                }
            }
        };
    }

    private static async Task<RekallAgeRuntimeRunResult> RunCubicSkeletalAssetAsync(
        byte[] glb,
        string animation)
    {
        var root = TestPaths.CreateTempDirectory();
        var source = Path.Combine(root, "cubic-runtime.glb");
        await File.WriteAllBytesAsync(source, glb);
        var asset = await RekallAgeAssetImporter.ImportAsync(
            root, source, "model", "Cubic Runtime Rig", CancellationToken.None);
        await new RekallAgeAssetCatalogStore().SaveAsync(
            root, new RekallAgeAssetCatalogDocument([asset]), CancellationToken.None);
        var actor = RekallAgeEntityDocument.Create("Rigged Actor", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.SkeletalAnimator",
                new JsonObject
                {
                    ["Model"] = asset.Id,
                    ["Animation"] = animation,
                    ["SkinIndex"] = 0,
                    ["Playing"] = true,
                    ["LoopMode"] = "clamp"
                }));
        return await RekallAgeRuntimeExecutionLoop.CreateDefault(root).RunAsync(
            new RekallAgeRuntimeWorldBuilder().Build(
                RekallAgeSceneDocument.Create("Main", ["world", "animation"]).AddEntity(actor)),
            30,
            CancellationToken.None);
    }

    private sealed class PreAnimationRigMixerDriver : IRekallAgeRuntimeWorldSystem
    {
        public string Id => "test.gameplay";
        public int Priority => -5;

        public ValueTask<RekallAgeRuntimeWorld> UpdateAsync(
            RekallAgeRuntimeWorld world,
            RekallAgeRuntimeWorldFrameContext context)
        {
            var actor = Assert.Single(world.Entities);
            return ValueTask.FromResult(world with
            {
                Entities =
                [
                    actor.UpsertComponent("Rekall.AnimationMixer", new JsonObject
                    {
                        ["playing"] = true,
                        ["layers"] = new JsonArray
                        {
                            new JsonObject { ["name"] = "idle", ["clip"] = "asset-idle", ["weight"] = 0 },
                            new JsonObject { ["name"] = "walk", ["clip"] = "asset-walk", ["weight"] = 1 }
                        }
                    })
                ]
            });
        }
    }
}
