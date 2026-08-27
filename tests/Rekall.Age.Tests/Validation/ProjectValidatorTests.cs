using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modules;
using Rekall.Age.Modules.BuiltIns;
using Rekall.Age.Validation;
using Rekall.Age.Validation.Commands;
using Rekall.Age.Workflows;
using Rekall.Age.World;
using Rekall.Age.World.Commands;

namespace Rekall.Age.Tests.Validation;

public sealed class ProjectValidatorTests
{
    [Fact]
    public void ReservedComponentTypeCatalogMatchesIndexedBuiltInSchemas()
    {
        var indexed = RekallAgeModuleIndexer.IndexAssembly(typeof(RekallAgeBuiltInModule).Assembly)
            .Components.Select(component => component.TypeName)
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(indexed.SetEquals(RekallAgeBuiltInComponentTypeCatalog.Types));
    }

    [Theory]
    [InlineData("Rekall.Collider3D")]
    [InlineData(" rekall.Collider3D ")]
    public void ReservedComponentTypeCatalogRejectsUnknownTypesAcrossCaseAndWhitespace(string componentType)
    {
        Assert.True(RekallAgeBuiltInComponentTypeCatalog.IsUnknownReserved(componentType));
        Assert.Equal("Rekall.BoxCollider3D", RekallAgeBuiltInComponentTypeCatalog.FindSafeSuggestion(componentType));
    }

    [Fact]
    public async Task ValidateSceneRejectsAssignedShaderPipelineWithIncompatibleAbi()
    {
        var root = TestPaths.CreateTempDirectory();
        var shaderRoot = Path.Combine(root, "Shaders", "agent");
        Directory.CreateDirectory(shaderRoot);
        await File.WriteAllTextAsync(Path.Combine(shaderRoot, "bad.vert"), """
            #version 450
            layout(location = 0) in vec2 inPosition;
            void main() { gl_Position = vec4(inPosition, 0.0, 1.0); }
            """);
        await File.WriteAllTextAsync(Path.Combine(shaderRoot, "bad.frag"), """
            #version 450
            layout(location = 0) out vec4 outColor;
            void main() { outColor = vec4(1.0); }
            """);
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Bad Shader Mesh", ["mesh"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.MeshRenderer",
                    new JsonObject
                    {
                        ["vertexShader"] = "agent/bad",
                        ["fragmentShader"] = "agent/bad"
                    })));
        var store = new RekallAgeSceneStore();
        await store.SaveAsync(root, scene, CancellationToken.None);

        var report = await new RekallAgeProjectValidator(
                store,
                new RekallAgeWorkflowShaderPipelineValidationService())
            .ValidateSceneAsync(root, "Main", CancellationToken.None);

        var issue = Assert.Single(report.Issues, issue => issue.Code == "REKALL_SHADER_VERTEX_ABI_MISMATCH");
        Assert.Equal("blocking", issue.Severity);
        Assert.Equal(scene.Entities.Single().Id, issue.Target);
        Assert.Contains(issue.SuggestedCommands!, command => command.Tool == "rekall.shader.inspect_pipeline");
    }

    [Fact]
    public async Task ValidateSceneMissingCameraSuggestsRegisteredSchemaDiscovery()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering2d"])
            .AddEntity(RekallAgeEntityDocument.Create("Visible", [])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.SpriteRenderer")));
        var sceneStore = new RekallAgeSceneStore();
        await sceneStore.SaveAsync(root, scene, CancellationToken.None);

        var report = await new RekallAgeProjectValidator(sceneStore)
            .ValidateSceneAsync(root, "Main", CancellationToken.None);

        var issue = Assert.Single(report.Issues, item => item.Code == "REKALL_CAMERA_MISSING");
        var suggestion = Assert.Single(issue.SuggestedCommands!);
        Assert.Equal("rekall.module.search_component_schemas", suggestion.Tool);
        Assert.Contains("camera", Assert.IsType<string>(suggestion.Arguments["query"]), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateSceneDoesNotRequireCameraForCanvasOnlyContent()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["ui"])
            .AddEntity(RekallAgeEntityDocument.Create("Canvas", ["ui"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.UiCanvas", new JsonObject())))
            .AddEntity(RekallAgeEntityDocument.Create("Start", ["ui"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Button",
                    new JsonObject { ["Text"] = "Start" })));
        var sceneStore = new RekallAgeSceneStore();
        await sceneStore.SaveAsync(root, scene, CancellationToken.None);

        var report = await new RekallAgeProjectValidator(sceneStore)
            .ValidateSceneAsync(root, "Main", CancellationToken.None);

        Assert.DoesNotContain(report.Issues, issue => issue.Code == "REKALL_CAMERA_MISSING");
    }

    [Fact]
    public async Task ValidateSceneRejectsUnknownReservedCanvasAndUiWithoutRealCanvas()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["ui"])
            .AddEntity(RekallAgeEntityDocument.Create("Invented Canvas", ["ui"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Canvas", new JsonObject())))
            .AddEntity(RekallAgeEntityDocument.Create("Start", ["ui"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Button",
                    new JsonObject { ["Text"] = "Start" })));
        var sceneStore = new RekallAgeSceneStore();
        await sceneStore.SaveAsync(root, scene, CancellationToken.None);

        var report = await new RekallAgeProjectValidator(sceneStore)
            .ValidateSceneAsync(root, "Main", CancellationToken.None);

        var unknown = Assert.Single(report.Issues, issue => issue.Code == "REKALL_COMPONENT_RESERVED_TYPE_UNKNOWN");
        Assert.Equal("blocking", unknown.Severity);
        Assert.Contains("Rekall.UiCanvas", unknown.Message, StringComparison.Ordinal);
        var noCanvas = Assert.Single(report.Issues, issue => issue.Code == "REKALL_UI_ELEMENT_NO_CANVAS");
        Assert.Equal("blocking", noCanvas.Severity);
        Assert.Contains("Rekall.UiCanvas", noCanvas.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateSceneRejectsEveryUnknownReservedTypeAndOnlyRepairsSafeSuffixAlias()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world"])
            .AddEntity(RekallAgeEntityDocument.Create("Aliased Transform", [])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Components.Transform3D",
                    new JsonObject { ["X"] = 3 })))
            .AddEntity(RekallAgeEntityDocument.Create("Invented Drive", [])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.CompletelyInventedWarpDrive",
                    new JsonObject())));
        var sceneStore = new RekallAgeSceneStore();
        await sceneStore.SaveAsync(root, scene, CancellationToken.None);

        var report = await new RekallAgeProjectValidator(sceneStore)
            .ValidateSceneAsync(root, "Main", CancellationToken.None);

        var unknown = report.Issues
            .Where(issue => issue.Code == "REKALL_COMPONENT_RESERVED_TYPE_UNKNOWN")
            .OrderBy(issue => issue.Target, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(2, unknown.Length);
        var alias = Assert.Single(unknown, issue =>
            issue.Message.Contains("Rekall.Components.Transform3D", StringComparison.Ordinal));
        Assert.Contains("Rekall.Transform3D", alias.Message, StringComparison.Ordinal);
        Assert.Equal(2, alias.SuggestedCommands?.Count);
        var invented = Assert.Single(unknown, issue =>
            issue.Message.Contains("Rekall.CompletelyInventedWarpDrive", StringComparison.Ordinal));
        Assert.DoesNotContain("Did you mean", invented.Message, StringComparison.Ordinal);
        Assert.Null(invented.SuggestedCommands);
        Assert.All(unknown, issue => Assert.Equal("blocking", issue.Severity));
    }

    [Fact]
    public async Task ValidateSceneRejectsUnknownPropertiesOnKnownBuiltInComponents()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["animation"])
            .AddEntity(RekallAgeEntityDocument.Create("Animated", ["actor"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Transform3D",
                    new JsonObject { ["PositionX"] = 3, ["X"] = 1 }))
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.AnimationPlayer",
                    new JsonObject { ["IsPlaying"] = true, ["Playing"] = true })));
        var sceneStore = new RekallAgeSceneStore();
        await sceneStore.SaveAsync(root, scene, CancellationToken.None);

        var report = await new RekallAgeProjectValidator(sceneStore)
            .ValidateSceneAsync(root, "Main", CancellationToken.None);

        Assert.Contains(report.Issues, issue =>
            issue.Code == "REKALL_COMPONENT_PROPERTY_UNKNOWN"
            && issue.Severity == "blocking"
            && issue.Message.Contains("PositionX", StringComparison.Ordinal));
        Assert.Contains(report.Issues, issue =>
            issue.Code == "REKALL_COMPONENT_PROPERTY_UNKNOWN"
            && issue.Message.Contains("IsPlaying", StringComparison.Ordinal));

        var transformIssue = Assert.Single(report.Issues, issue =>
            issue.Code == "REKALL_COMPONENT_PROPERTY_UNKNOWN"
            && issue.Message.Contains("PositionX", StringComparison.Ordinal));
        Assert.Contains(transformIssue.SuggestedCommands!, command =>
            command.Tool == "rekall.component.remove_property"
            && Equals(command.Arguments["projectRoot"], root)
            && Equals(command.Arguments["sceneName"], "Main")
            && Equals(command.Arguments["entityId"], scene.Entities[0].Id)
            && Equals(command.Arguments["componentType"], "Rekall.Transform3D")
            && Equals(command.Arguments["propertyName"], "PositionX"));
    }

    [Fact]
    public async Task ValidateSceneRejectsNumericPropertiesOutsideSchemaBounds()
    {
        var root = TestPaths.CreateTempDirectory();
        var body = RekallAgeEntityDocument.Create("Body", ["physics"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Rigidbody3D",
                new JsonObject { ["Mass"] = -5 }));
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "physics"])
            .AddEntity(body);
        var sceneStore = new RekallAgeSceneStore();
        await sceneStore.SaveAsync(root, scene, CancellationToken.None);

        var report = await new RekallAgeProjectValidator(sceneStore)
            .ValidateSceneAsync(root, "Main", CancellationToken.None);

        var issue = Assert.Single(report.Issues, item =>
            item.Code == "REKALL_COMPONENT_PROPERTY_OUT_OF_RANGE");
        Assert.Equal("blocking", issue.Severity);
        Assert.Contains("0.0001", issue.Message, StringComparison.Ordinal);
        Assert.Contains(issue.SuggestedCommands!, command =>
            command.Tool == "rekall.component.set_property"
            && Equals(command.Arguments["projectRoot"], root)
            && Equals(command.Arguments["sceneName"], "Main")
            && Equals(command.Arguments["entityId"], body.Id)
            && Equals(command.Arguments["componentType"], "Rekall.Rigidbody3D")
            && Equals(command.Arguments["propertyName"], "Mass")
            && Equals(command.Arguments["value"], 0.0001));
    }

    [Fact]
    public async Task ValidateSceneAcceptsHighFidelityRenderingComponentsAndShadowProperties()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Environment", ["rendering"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Environment3D", new JsonObject
                {
                    ["ambientEnergy"] = 2,
                    ["ambientSkyColor"] = "#9fb3c2",
                    ["ambientGroundColor"] = "#795743",
                    ["backgroundColor"] = "#111a1d",
                    ["backgroundPolicy"] = "skybox",
                    ["exposure"] = -1.15,
                    ["toneMapper"] = "agx",
                    ["whitePoint"] = 11.2
                }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.ShadowSettings", new JsonObject
                {
                    ["cascadeCount"] = 3,
                    ["atlasResolution"] = 2048,
                    ["maximumDistance"] = 140,
                    ["splitPolicy"] = "practical",
                    ["bias"] = 0.0015,
                    ["normalBias"] = 0.018,
                    ["filter"] = "pcf",
                    ["stabilization"] = true
                })))
            .AddEntity(RekallAgeEntityDocument.Create("Mist", ["rendering"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.FogVolume", new JsonObject
                {
                    ["shape"] = "global",
                    ["density"] = 0.006,
                    ["albedo"] = "#374448",
                    ["emission"] = "#050707",
                    ["anisotropy"] = 0.25,
                    ["heightFalloff"] = 0.035,
                    ["blendDistance"] = 30,
                    ["priority"] = 1
                })))
            .AddEntity(RekallAgeEntityDocument.Create("Lit Mesh", ["rendering"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.MeshRenderer", new JsonObject
                {
                    ["castShadows"] = true,
                    ["receiveShadows"] = true
                })))
            .AddEntity(RekallAgeEntityDocument.Create("Practical", ["rendering"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.PointLight", new JsonObject
                {
                    ["intensity"] = 12,
                    ["color"] = "#ffcc88",
                    ["range"] = 18,
                    ["priority"] = 80,
                    ["shadowPriority"] = 40,
                    ["castShadows"] = true
                })))
            .AddEntity(RekallAgeEntityDocument.Create("Motes", ["rendering"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.ParticleEmitter3D", new JsonObject
                {
                    ["enabled"] = true,
                    ["role"] = "ambient-motes",
                    ["capacity"] = 256,
                    ["spawnRate"] = 12,
                    ["lifetime"] = 2,
                    ["simulationSpace"] = "world",
                    ["drawMode"] = "quad",
                    ["blendMode"] = "alpha"
                })));
        var sceneStore = new RekallAgeSceneStore();
        await sceneStore.SaveAsync(root, scene, CancellationToken.None);

        var report = await new RekallAgeProjectValidator(sceneStore)
            .ValidateSceneAsync(root, "Main", CancellationToken.None);

        Assert.DoesNotContain(report.Issues, issue =>
            issue.Code is "REKALL_COMPONENT_RESERVED_TYPE_UNKNOWN" or "REKALL_COMPONENT_PROPERTY_UNKNOWN");
    }

    [Fact]
    public async Task ValidateSceneRejectsNumericStringPropertiesOutsideSchemaBounds()
    {
        var root = TestPaths.CreateTempDirectory();
        var body = RekallAgeEntityDocument.Create("Body", ["physics"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Rigidbody3D",
                new JsonObject { ["Mass"] = "-2.5" }));
        var sceneStore = new RekallAgeSceneStore();
        await sceneStore.SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["physics"]).AddEntity(body),
            CancellationToken.None);

        var report = await new RekallAgeProjectValidator(sceneStore)
            .ValidateSceneAsync(root, "Main", CancellationToken.None);

        var issue = Assert.Single(report.Issues, item =>
            item.Code == "REKALL_COMPONENT_PROPERTY_OUT_OF_RANGE");
        Assert.Contains(issue.SuggestedCommands!, command =>
            command.Tool == "rekall.component.set_property"
            && Equals(command.Arguments["value"], 0.0001));
    }

    [Fact]
    public async Task ValidateSceneRejectsJsonEncodedStructuredPropertyWithNativeJsonRepair()
    {
        var root = TestPaths.CreateTempDirectory();
        var input = RekallAgeEntityDocument.Create("Input", ["input"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.InputActionMap",
                new JsonObject
                {
                    ["Actions"] = "[{\"name\":\"move.horizontal\",\"positiveKey\":\"D\",\"negativeKey\":\"A\"}]"
                }));
        var sceneStore = new RekallAgeSceneStore();
        await sceneStore.SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["input"]).AddEntity(input),
            CancellationToken.None);

        var report = await new RekallAgeProjectValidator(sceneStore)
            .ValidateSceneAsync(root, "Main", CancellationToken.None);

        var issue = Assert.Single(report.Issues, item =>
            item.Code == "REKALL_COMPONENT_PROPERTY_SHAPE_INVALID");
        Assert.Equal("blocking", issue.Severity);
        Assert.Contains("native JSON array", issue.Message, StringComparison.Ordinal);
        var repair = Assert.Single(issue.SuggestedCommands!, command =>
            command.Tool == "rekall.component.set_property");
        var repairedValue = Assert.IsType<JsonArray>(repair.Arguments["value"]);
        Assert.Equal("move.horizontal", repairedValue[0]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task ValidateSceneAcceptsAnAuthoredCollisionFilter()
    {
        var root = TestPaths.CreateTempDirectory();
        var body = RekallAgeEntityDocument.Create("Body", ["physics"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.BoxCollider3D", new JsonObject()))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.CollisionFilter",
                new JsonObject
                {
                    ["layer"] = "player",
                    ["collidesWith"] = new JsonArray("enemy")
                }));
        var scene = RekallAgeSceneDocument.Create("Main", ["physics"])
            .AddEntity(body);
        var sceneStore = new RekallAgeSceneStore();
        await sceneStore.SaveAsync(root, scene, CancellationToken.None);

        var report = await new RekallAgeProjectValidator(sceneStore)
            .ValidateSceneAsync(root, "Main", CancellationToken.None);

        Assert.DoesNotContain(report.Issues, issue => issue.Code == "REKALL_COMPONENT_RESERVED_TYPE_UNKNOWN");
    }

    [Fact]
    public async Task ValidateSceneRequiresDimensionMatchingTransformForPhysicsBodies()
    {
        var root = TestPaths.CreateTempDirectory();
        var body3D = RekallAgeEntityDocument.Create("Body 3D", ["physics"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Rigidbody3D", new JsonObject { ["Mass"] = 1 }))
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.BoxCollider3D", new JsonObject()));
        var body2D = RekallAgeEntityDocument.Create("Body 2D", ["physics"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Rigidbody2D", new JsonObject { ["Mass"] = 1 }))
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.BoxCollider2D", new JsonObject()));
        var scene = RekallAgeSceneDocument.Create("Main", ["physics"])
            .AddEntity(body3D)
            .AddEntity(body2D);
        var sceneStore = new RekallAgeSceneStore();
        await sceneStore.SaveAsync(root, scene, CancellationToken.None);

        var report = await new RekallAgeProjectValidator(sceneStore)
            .ValidateSceneAsync(root, "Main", CancellationToken.None);

        var missingTransforms = report.Issues
            .Where(issue => issue.Code == "REKALL_PHYSICS_BODY_NO_TRANSFORM")
            .ToArray();
        Assert.Equal(2, missingTransforms.Length);
        Assert.All(missingTransforms, issue => Assert.Equal("blocking", issue.Severity));
        Assert.Contains(
            missingTransforms.Single(issue => issue.Target == body3D.Id).SuggestedCommands!,
            command => command.Tool == "rekall.component.add"
                && Equals(command.Arguments["projectRoot"], root)
                && Equals(command.Arguments["sceneName"], "Main")
                && Equals(command.Arguments["entityId"], body3D.Id)
                && Equals(command.Arguments["componentType"], "Rekall.Transform3D"));
        Assert.Contains(
            missingTransforms.Single(issue => issue.Target == body2D.Id).SuggestedCommands!,
            command => command.Tool == "rekall.component.add"
                && Equals(command.Arguments["componentType"], "Rekall.Transform2D"));
    }

    [Fact]
    public async Task ValidateSceneRequiresDimensionMatchingColliderForPhysicsBodies()
    {
        var root = TestPaths.CreateTempDirectory();
        var body3D = RekallAgeEntityDocument.Create("Body 3D", ["physics"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject()))
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Rigidbody3D", new JsonObject { ["Mass"] = 1 }));
        var body2D = RekallAgeEntityDocument.Create("Body 2D", ["physics"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform2D", new JsonObject()))
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Rigidbody2D", new JsonObject { ["Mass"] = 1 }));
        var sceneStore = new RekallAgeSceneStore();
        await sceneStore.SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["physics"]).AddEntity(body3D).AddEntity(body2D),
            CancellationToken.None);

        var report = await new RekallAgeProjectValidator(sceneStore)
            .ValidateSceneAsync(root, "Main", CancellationToken.None);

        var missingColliders = report.Issues
            .Where(issue => issue.Code == "REKALL_PHYSICS_BODY_NO_COLLIDER")
            .ToArray();
        Assert.Equal(2, missingColliders.Length);
        Assert.All(missingColliders, issue => Assert.Equal("blocking", issue.Severity));
        Assert.Contains(
            missingColliders.Single(issue => issue.Target == body3D.Id).SuggestedCommands!,
            command => command.Tool == "rekall.component.add"
                && Equals(command.Arguments["projectRoot"], root)
                && Equals(command.Arguments["sceneName"], "Main")
                && Equals(command.Arguments["entityId"], body3D.Id)
                && Equals(command.Arguments["componentType"], "Rekall.BoxCollider3D"));
        Assert.Contains(
            missingColliders.Single(issue => issue.Target == body2D.Id).SuggestedCommands!,
            command => command.Tool == "rekall.component.add"
                && Equals(command.Arguments["componentType"], "Rekall.BoxCollider2D"));
    }

    [Fact]
    public async Task ValidateSceneRejectsDimensionMismatchedPhysicsCollidersWithExecutableRepairs()
    {
        var root = TestPaths.CreateTempDirectory();
        var body2D = RekallAgeEntityDocument.Create("Body 2D", ["physics"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform2D", new JsonObject()))
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Rigidbody2D", new JsonObject { ["Mass"] = 1 }))
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.BoxCollider3D", new JsonObject()));
        var floor2D = RekallAgeEntityDocument.Create("Floor 2D", ["physics"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform2D", new JsonObject()))
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.BoxCollider3D", new JsonObject()));
        var sceneStore = new RekallAgeSceneStore();
        await sceneStore.SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Physics2D", ["physics2d"])
                .AddEntity(body2D)
                .AddEntity(floor2D),
            CancellationToken.None);

        var report = await new RekallAgeProjectValidator(sceneStore)
            .ValidateSceneAsync(root, "Physics2D", CancellationToken.None);

        var issues = report.Issues
            .Where(issue => issue.Code == "REKALL_PHYSICS_COLLIDER_DIMENSION_MISMATCH")
            .ToArray();
        Assert.Equal(2, issues.Length);
        Assert.All(issues, issue => Assert.Equal("blocking", issue.Severity));
        Assert.All(issues, issue => Assert.Contains(
            issue.SuggestedCommands!,
            command => command.Tool == "rekall.component.remove"
                && Equals(command.Arguments["componentType"], "Rekall.BoxCollider3D")));
        Assert.All(issues, issue => Assert.Contains(
            issue.SuggestedCommands!,
            command => command.Tool == "rekall.component.add"
                && Equals(command.Arguments["componentType"], "Rekall.BoxCollider2D")));
    }

    [Fact]
    public async Task ValidateSceneRejectsUnqualifiedBuiltInAliasWithExecutableMigration()
    {
        var root = TestPaths.CreateTempDirectory();
        var body = RekallAgeEntityDocument.Create("Body", ["physics"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform3D", new JsonObject()))
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.BoxCollider3D", new JsonObject()))
            .AddComponent(RekallAgeComponentDocument.Create("Rigidbody3D", new JsonObject { ["Mass"] = 3 }));
        var sceneStore = new RekallAgeSceneStore();
        await sceneStore.SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["physics3d"]).AddEntity(body),
            CancellationToken.None);

        var report = await new RekallAgeProjectValidator(sceneStore)
            .ValidateSceneAsync(root, "Main", CancellationToken.None);

        var issue = Assert.Single(report.Issues, item =>
            item.Code == "REKALL_COMPONENT_BUILTIN_PREFIX_REQUIRED");
        Assert.Equal("blocking", issue.Severity);
        Assert.Contains("Rekall.Rigidbody3D", issue.Message, StringComparison.Ordinal);
        Assert.Collection(
            issue.SuggestedCommands!,
            command =>
            {
                Assert.Equal("rekall.component.remove", command.Tool);
                Assert.Equal("Rigidbody3D", command.Arguments["componentType"]);
            },
            command =>
            {
                Assert.Equal("rekall.component.add", command.Tool);
                Assert.Equal("Rekall.Rigidbody3D", command.Arguments["componentType"]);
                var properties = Assert.IsType<JsonObject>(command.Arguments["properties"]);
                Assert.Equal(3, properties["Mass"]!.GetValue<int>());
            });
    }

    [Fact]
    public async Task ValidateSceneReportsMultipleActiveCameras()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera A", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject { ["active"] = true })))
            .AddEntity(RekallAgeEntityDocument.Create("Camera B", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject { ["active"] = true })));
        var sceneStore = new RekallAgeSceneStore();
        await sceneStore.SaveAsync(root, scene, CancellationToken.None);

        var report = await new RekallAgeProjectValidator(sceneStore)
            .ValidateSceneAsync(root, "Main", CancellationToken.None);

        var issue = Assert.Single(report.Issues, item => item.Code == "REKALL_CAMERA_MULTIPLE_ACTIVE");
        Assert.Equal("warning", issue.Severity);
        Assert.Equal("Main", issue.Target);
    }

    [Fact]
    public async Task ValidateSceneReportsCameraCullingMaskWithNoMatchingRenderableLayer()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject
                {
                    ["active"] = true,
                    ["cullingMask"] = "world, helpers"
                })))
            .AddEntity(RekallAgeEntityDocument.Create("World Cube", ["prop"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.RenderLayer", new JsonObject { ["layer"] = "world" }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "cube" })));
        var sceneStore = new RekallAgeSceneStore();
        await sceneStore.SaveAsync(root, scene, CancellationToken.None);

        var report = await new RekallAgeProjectValidator(sceneStore)
            .ValidateSceneAsync(root, "Main", CancellationToken.None);

        var issue = Assert.Single(report.Issues, item => item.Code == "REKALL_CAMERA_CULLING_MASK_EMPTY_LAYER");
        Assert.Equal("warning", issue.Severity);
        Assert.Equal("Camera", issue.Target);
        Assert.Contains("helpers", issue.Message, StringComparison.Ordinal);
        Assert.Contains("CullingMask", issue.Message, StringComparison.Ordinal);
        Assert.Contains("'*'", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateSceneReportsRenderableLayerExcludedFromEveryActiveCamera()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject
                {
                    ["active"] = true,
                    ["cullingMask"] = "world"
                })))
            .AddEntity(RekallAgeEntityDocument.Create("World Cube", ["prop"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.RenderLayer", new JsonObject { ["layer"] = "world" }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "cube" })))
            .AddEntity(RekallAgeEntityDocument.Create("Helper Cube", ["debug"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.RenderLayer", new JsonObject { ["layer"] = "helpers" }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "cube" })));
        var sceneStore = new RekallAgeSceneStore();
        await sceneStore.SaveAsync(root, scene, CancellationToken.None);

        var report = await new RekallAgeProjectValidator(sceneStore)
            .ValidateSceneAsync(root, "Main", CancellationToken.None);

        var issue = Assert.Single(report.Issues, item => item.Code == "REKALL_RENDER_LAYER_NOT_VISIBLE");
        Assert.Equal("warning", issue.Severity);
        Assert.Equal("helpers", issue.Target);
        Assert.Contains("Helper Cube", issue.Message, StringComparison.Ordinal);
        Assert.Contains("CullingMask", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateSceneAcceptsRenderableLayersWhenActiveCameraUsesWildcardMask()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject
                {
                    ["active"] = true,
                    ["cullingMask"] = "*"
                })))
            .AddEntity(RekallAgeEntityDocument.Create("Helper Cube", ["debug"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.RenderLayer", new JsonObject { ["layer"] = "helpers" }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "cube" })));
        var sceneStore = new RekallAgeSceneStore();
        await sceneStore.SaveAsync(root, scene, CancellationToken.None);

        var report = await new RekallAgeProjectValidator(sceneStore)
            .ValidateSceneAsync(root, "Main", CancellationToken.None);

        Assert.DoesNotContain(report.Issues, item => item.Code == "REKALL_RENDER_LAYER_NOT_VISIBLE");
    }

    [Fact]
    public async Task ValidateSceneTreatsExcludedCameraMaskLayerAsNotVisible()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject
                {
                    ["active"] = true,
                    ["cullingMask"] = "*, !helpers"
                })))
            .AddEntity(RekallAgeEntityDocument.Create("Helper Cube", ["debug"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.RenderLayer", new JsonObject { ["layer"] = "helpers" }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.GeometryPrimitive", new JsonObject { ["primitive"] = "cube" })));
        var sceneStore = new RekallAgeSceneStore();
        await sceneStore.SaveAsync(root, scene, CancellationToken.None);

        var report = await new RekallAgeProjectValidator(sceneStore)
            .ValidateSceneAsync(root, "Main", CancellationToken.None);

        Assert.Contains(report.Issues, item =>
            item.Code == "REKALL_RENDER_LAYER_NOT_VISIBLE"
            && item.Target == "helpers");
        Assert.DoesNotContain(report.Issues, item =>
            item.Code == "REKALL_CAMERA_CULLING_MASK_EMPTY_LAYER"
            && item.Message.Contains("helpers", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidateVrSceneReportsMissingRigAndTrackedCamera()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d", "vr"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject
                {
                    ["active"] = true,
                    ["stereoMode"] = "mono"
                })));
        var sceneStore = new RekallAgeSceneStore();
        await sceneStore.SaveAsync(root, scene, CancellationToken.None);

        var report = await new RekallAgeProjectValidator(sceneStore)
            .ValidateSceneAsync(root, "Main", CancellationToken.None);

        Assert.Contains(report.Issues, issue =>
            issue.Code == "REKALL_XR_RIG_MISSING"
            && issue.Severity == "warning");
        Assert.Contains(report.Issues, issue =>
            issue.Code == "REKALL_XR_CAMERA_NOT_STEREO"
            && issue.Message.Contains("stereo", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.Issues, issue =>
            issue.Code == "REKALL_XR_CAMERA_POSE_SOURCE_MISSING"
            && issue.Message.Contains("XrPoseSource", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidateSceneWarnsWhenMultipleActiveStereoCamerasCanDriveHeadsetOutput()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d", "vr"])
            .AddEntity(RekallAgeEntityDocument.Create("Left Rig Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject
                {
                    ["active"] = true,
                    ["stereoMode"] = "stereo"
                })))
            .AddEntity(RekallAgeEntityDocument.Create("Debug Stereo Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject
                {
                    ["active"] = true,
                    ["stereoMode"] = "xr"
                })));
        var sceneStore = new RekallAgeSceneStore();
        await sceneStore.SaveAsync(root, scene, CancellationToken.None);

        var report = await new RekallAgeProjectValidator(sceneStore)
            .ValidateSceneAsync(root, "Main", CancellationToken.None);

        var issue = Assert.Single(report.Issues, item => item.Code == "REKALL_XR_MULTIPLE_ACTIVE_STEREO_CAMERAS");
        Assert.Equal("warning", issue.Severity);
        Assert.Equal("Main", issue.Target);
        Assert.Contains("Left Rig Camera", issue.Message, StringComparison.Ordinal);
        Assert.Contains("Debug Stereo Camera", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateVrSceneAcceptsRigPoseSourceAndControllers()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d", "vr"])
            .AddEntity(RekallAgeEntityDocument.Create("VrRig", ["xr"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.XrRig", new JsonObject { ["active"] = true })))
            .AddEntity(RekallAgeEntityDocument.Create("HeadCamera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject
                {
                    ["active"] = true,
                    ["stereoMode"] = "stereo",
                    ["stereoRenderMode"] = "single-pass-multiview"
                }))
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.XrPoseSource", new JsonObject
                {
                    ["source"] = "head"
                })))
            .AddEntity(RekallAgeEntityDocument.Create("LeftController", ["controller"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.XrController", new JsonObject { ["hand"] = "left" })))
            .AddEntity(RekallAgeEntityDocument.Create("RightController", ["controller"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.XrController", new JsonObject { ["hand"] = "right" })));
        var sceneStore = new RekallAgeSceneStore();
        await sceneStore.SaveAsync(root, scene, CancellationToken.None);

        var report = await new RekallAgeProjectValidator(sceneStore)
            .ValidateSceneAsync(root, "Main", CancellationToken.None);

        Assert.DoesNotContain(report.Issues, issue => issue.Code.StartsWith("REKALL_XR_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidateSceneCommandReturnsAgentReadableIssueSummary()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d", "vr"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera3D", new JsonObject
                {
                    ["active"] = true,
                    ["stereoMode"] = "mono"
                })));
        await new RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None);
        var context = new RekallAgeCommandContext(
            "test",
            RekallAgeTransaction.Begin("validate scene"),
            CancellationToken.None);

        var result = await new ValidateSceneCommand().ExecuteAsync(
            new ValidateSceneRequest(root, "Main"),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.Equal("ok", result.Value.Status);
        Assert.True(result.Value.WarningCount >= 3);
        Assert.Contains(result.Value.Issues, issue => issue.Code == "REKALL_XR_RIG_MISSING");
        Assert.Contains(
            result.Value.SuggestedNextActions,
            action => action.Tool == "rekall.module.search_component_schemas"
                && Equals(action.Arguments["query"], "XrRig"));
    }

    [Fact]
    public async Task ValidateProjectCommandAggregatesEverySceneAndComponentSchemaIssue()
    {
        var root = TestPaths.CreateTempDirectory();
        var store = new RekallAgeSceneStore();
        await store.SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world"]),
            CancellationToken.None);
        await store.SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Physics2D", ["world", "physics"])
                .AddEntity(RekallAgeEntityDocument.Create("Body", ["physics"])
                    .AddComponent(RekallAgeComponentDocument.Create(
                        "Rekall.Rigidbody2D",
                        new JsonObject { ["mass"] = 1, ["invalidProperty"] = true }))),
            CancellationToken.None);
        var context = new RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("validate project"),
            CancellationToken.None);

        var result = await new ValidateProjectCommand().ExecuteAsync(
            new ValidateProjectRequest(root),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.Equal(2, result.Value.SceneCount);
        Assert.Equal("blocked", result.Value.Status);
        Assert.True(result.Value.BlockingCount > 0);
        Assert.Contains(
            result.Value.Scenes.Single(scene => scene.SceneName == "Physics2D").Issues,
            issue => issue.Code == "REKALL_COMPONENT_PROPERTY_UNKNOWN");
    }

    [Fact]
    public async Task RepairProjectValidationExecutesAllEngineSuggestedRepairsInOneBoundedCall()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world"])
                .AddEntity(RekallAgeEntityDocument.Create("Body", [])
                    .AddComponent(RekallAgeComponentDocument.Create(
                        "Rekall.Transform3D",
                        new JsonObject { ["invalidOne"] = 1, ["invalidTwo"] = 2 }))),
            CancellationToken.None);
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new ValidateProjectCommand());
        registry.Register(new RemoveComponentPropertyCommand());
        registry.Register(new RepairProjectValidationCommand(registry));
        var context = new RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("batch validation repair"),
            CancellationToken.None);

        var result = await registry.ExecuteAsync<RepairProjectValidationRequest, RepairProjectValidationResult>(
            "rekall.validation.repair_project",
            new RepairProjectValidationRequest(root),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.Equal(2, result.Value.ExecutedRepairCount);
        Assert.Equal(0, result.Value.Validation.IssueCount);
        Assert.Equal("ok", result.Value.Validation.Status);
    }

    [Fact]
    public async Task RepairProjectValidationConvertsEncodedStructuredPropertyToNativeJson()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["input"])
                .AddEntity(RekallAgeEntityDocument.Create("Input", ["input"])
                    .AddComponent(RekallAgeComponentDocument.Create(
                        "Rekall.InputActionMap",
                        new JsonObject
                        {
                            ["Actions"] = "[{\"name\":\"move.horizontal\",\"positiveKey\":\"D\"}]"
                        }))),
            CancellationToken.None);
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new ValidateProjectCommand());
        registry.Register(new SetComponentPropertyCommand());
        registry.Register(new RepairProjectValidationCommand(registry));

        var result = await registry.ExecuteAsync<RepairProjectValidationRequest, RepairProjectValidationResult>(
            "rekall.validation.repair_project",
            new RepairProjectValidationRequest(root),
            new RekallAgeCommandContext(
                "agent",
                RekallAgeTransaction.Begin("structured property repair"),
                CancellationToken.None));

        Assert.True(result.Ok, result.Summary);
        Assert.Equal("clean", result.Value.TerminationReason);
        Assert.Equal(["rekall.component.set_property"], result.Value.ExecutedTools);
        var repaired = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
        var actions = Assert.IsType<JsonArray>(Assert.Single(repaired.Entities).Components.Single().Properties["Actions"]);
        Assert.Equal("move.horizontal", actions[0]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task RepairProjectValidationCanonicalizesCloseReservedComponentTypes()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world"])
                .AddEntity(RekallAgeEntityDocument.Create("Body", [])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.RigidBody3D"))
                    .AddComponent(RekallAgeComponentDocument.Create(
                        "Rekall.Transform3D",
                        new JsonObject { ["invalidProperty"] = 1 }))),
            CancellationToken.None);
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new ValidateProjectCommand());
        registry.Register(new AddComponentCommand());
        registry.Register(new RemoveComponentCommand());
        registry.Register(new RemoveComponentPropertyCommand());
        registry.Register(new RepairProjectValidationCommand(registry));
        var context = new RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("skip advisory validation suggestions"),
            CancellationToken.None);

        var result = await registry.ExecuteAsync<RepairProjectValidationRequest, RepairProjectValidationResult>(
            "rekall.validation.repair_project",
            new RepairProjectValidationRequest(root),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.True(result.Value.ExecutedRepairCount >= 3);
        Assert.Equal(0, result.Value.Validation.IssueCount);
        var repaired = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
        var components = Assert.Single(repaired.Entities).Components;
        Assert.Contains(components, component => component.Type == "Rekall.Rigidbody3D");
        Assert.DoesNotContain(components, component => component.Type == "Rekall.RigidBody3D");
    }

    [Fact]
    public async Task RepairProjectValidationCanonicalizesExactSuffixAliasWithoutGuessingInventedType()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world"])
                .AddEntity(RekallAgeEntityDocument.Create("Body", [])
                    .AddComponent(RekallAgeComponentDocument.Create(
                        "Rekall.Components.Transform3D",
                        new JsonObject { ["X"] = 2 }))
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.CompletelyInventedWarpDrive"))),
            CancellationToken.None);
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new ValidateProjectCommand());
        registry.Register(new AddComponentCommand());
        registry.Register(new RemoveComponentCommand());
        registry.Register(new RepairProjectValidationCommand(registry));
        var context = new RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("repair suffix alias"),
            CancellationToken.None);

        var result = await registry.ExecuteAsync<RepairProjectValidationRequest, RepairProjectValidationResult>(
            "rekall.validation.repair_project",
            new RepairProjectValidationRequest(root),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.Equal(2, result.Value.ExecutedRepairCount);
        Assert.Single(result.Value.Validation.Scenes.SelectMany(scene => scene.Issues), issue =>
            issue.Message.Contains("Rekall.CompletelyInventedWarpDrive", StringComparison.Ordinal));
        var repaired = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
        var components = Assert.Single(repaired.Entities).Components;
        var transform = Assert.Single(components, component => component.Type == "Rekall.Transform3D");
        Assert.Equal(2, transform.Properties["X"]!.GetValue<int>());
        Assert.DoesNotContain(components, component => component.Type == "Rekall.Components.Transform3D");
        Assert.Contains(components, component => component.Type == "Rekall.CompletelyInventedWarpDrive");
    }

    [Fact]
    public async Task RepairProjectValidationReportsNoProgressWhenOnlyAdvisoryActionsRemain()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "rendering2d"])
                .AddEntity(RekallAgeEntityDocument.Create("Visible", [])
                    .AddComponent(RekallAgeComponentDocument.Create("Rekall.SpriteRenderer"))),
            CancellationToken.None);
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new ValidateProjectCommand());
        registry.Register(new RepairProjectValidationCommand(registry));
        var context = new RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("report validation repair no progress"),
            CancellationToken.None);

        var result = await registry.ExecuteAsync<RepairProjectValidationRequest, RepairProjectValidationResult>(
            "rekall.validation.repair_project",
            new RepairProjectValidationRequest(root),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.Equal("no-progress", result.Value.TerminationReason);
        Assert.Equal(0, result.Value.RemainingAutomaticRepairCount);
        Assert.Contains("do not retry", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.Value.Validation.IssueCount > 0);
    }
}
