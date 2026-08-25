using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modules.Commands;
using Rekall.Age.Project;
using Rekall.Age.Runtime.Abstractions;
using Rekall.Age.Runtime.Commands;
using Rekall.Age.Workflows.Commands;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Workflows;

public sealed class AgentAuthoringGauntletTests
{
    [Fact]
    public async Task GauntletAuthorsAnExistingEmptyEditorSceneBeforePackaging()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeProjectStore().SaveAsync(
            root,
            RekallAgeProjectManifest.Create("Editor Project", ["world", "rendering2d"]),
            CancellationToken.None);
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "rendering2d"]),
            CancellationToken.None);
        var context = new RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("existing empty editor scene gauntlet"),
            CancellationToken.None);

        var result = await new RunAgentAuthoringGauntletCommand().ExecuteAsync(
            new RunAgentAuthoringGauntletRequest(root, "Editor Project", "Main", Path.Combine(root, "Package")),
            context);

        Assert.True(result.Ok, result.Summary + Environment.NewLine + string.Join(
            Environment.NewLine,
            result.Errors.Select(error => $"{error.Code}: {error.Message}")));
        Assert.True(result.Value.Ready);
        Assert.Contains(result.Value.Checks, check => check is { Name: "scene-blueprint-authored", Passed: true });
        Assert.DoesNotContain(result.Value.Checks, check => check.Name == "scene-preserved");

        var runtime = await new InspectSceneRuntimeCommand().ExecuteAsync(
            new InspectSceneRuntimeRequest(
                root,
                "Main",
                Frames: 1,
                Inputs:
                [
                    new RekallAgeRuntimeInputFrame(SemanticActions:
                    [
                        new RekallAgeRuntimeSemanticActionSample(
                            "agent.gauntlet.advance",
                            Value: 1,
                            IsDown: true,
                            WasPressed: true)
                    ])
                    {
                        DeltaSeconds = 1
                    }
                ],
                Assertions:
                [
                    new InspectSceneRuntimeAssertion(
                        "Agent Authored Marker",
                        "component",
                        "exists")
                    {
                        ComponentType = "Game.Modules.AgentGauntlet.GauntletState"
                    },
                    new InspectSceneRuntimeAssertion(
                        "Agent Authored Marker",
                        "delta.component.property",
                        "equals")
                    {
                        ComponentType = "Game.Modules.AgentGauntlet.GauntletState",
                        PropertyName = "progress",
                        Expected = JsonValue.Create(1)
                    },
                    new InspectSceneRuntimeAssertion(
                        "Agent Authored Marker",
                        "delta.position2d.x",
                        "equals")
                    {
                        Expected = JsonValue.Create(1)
                    }
                ]),
            context);

        Assert.True(runtime.Ok, runtime.Summary + Environment.NewLine + string.Join(
            Environment.NewLine,
            runtime.Errors.Select(error => $"{error.Code}: {error.Message}")));
        Assert.Contains(result.Value.Checks, check => check is { Name: "module-scaffolded", Passed: true });
        Assert.Contains(result.Value.Checks, check => check is { Name: "module-source-authored", Passed: true });
        Assert.Contains(result.Value.Checks, check => check is { Name: "module-built", Passed: true });
        Assert.Contains(result.Value.Checks, check => check is { Name: "runtime-gameplay-proved", Passed: true });
        var moduleProjectPath = Path.Combine(root, "Modules", "AgentGauntlet", "AgentGauntlet.csproj");
        var moduleSourcePath = Path.Combine(root, "Modules", "AgentGauntlet", "AgentGauntletModule.cs");
        Assert.True(File.Exists(moduleProjectPath));
        Assert.True(File.Exists(moduleSourcePath));
        var moduleSource = await File.ReadAllTextAsync(moduleSourcePath);
        Assert.Contains("builder.RegisterComponent<GauntletState>();", moduleSource, StringComparison.Ordinal);
        Assert.Contains("builder.RegisterRuntimeSystem<AgentGauntletRuntimeSystem>();", moduleSource, StringComparison.Ordinal);
        Assert.Contains("world.InputActionValue(\"agent.gauntlet.advance\")", moduleSource, StringComparison.Ordinal);
        Assert.Contains("context.DeltaTime.TotalSeconds", moduleSource, StringComparison.Ordinal);
        Assert.Contains("WithComponentNumber", moduleSource, StringComparison.Ordinal);
        Assert.Contains("WithPosition2D", moduleSource, StringComparison.Ordinal);
        var authored = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
        var marker = Assert.Single(authored.Entities, entity => entity.Name == "Agent Authored Marker");
        var state = Assert.Single(marker.Components, component =>
            component.Type == "Game.Modules.AgentGauntlet.GauntletState");
        Assert.Equal(0, state.Properties["progress"]!.GetValue<double>());
        Assert.Equal(1, state.Properties["unitsPerSecond"]!.GetValue<double>());
        var inputMap = Assert.Single(marker.Components, component => component.Type == "Rekall.InputActionMap");
        var action = Assert.IsType<JsonObject>(Assert.Single(inputMap.Properties["actions"]!.AsArray()));
        Assert.Equal("agent.gauntlet.advance", action["name"]!.GetValue<string>());
        Assert.True(runtime.Value.AssertionsPassed);
        Assert.All(runtime.Value.AssertionResults, assertion => Assert.True(assertion.Passed, assertion.Summary));
        var progressDelta = Assert.Single(runtime.Value.AssertionResults, assertion =>
            assertion.Assertion.Subject == "delta.component.property");
        Assert.Equal(1, progressDelta.Actual!.GetValue<double>(), precision: 6);
        var markerState = Assert.Single(runtime.Value.EntityStates, entity =>
            entity.EntityName == "Agent Authored Marker");
        Assert.Equal(1, markerState.PositionDelta2D.X, precision: 6);
        Assert.Contains(runtime.Value.SystemsRun, system =>
            system.Contains("AgentGauntletRuntimeSystem", StringComparison.Ordinal));
        Assert.NotNull(result.Value.Package);
        Assert.True(File.Exists(result.Value.Package!.ArchivePath));
        Assert.NotNull(result.Value.Audit);
        Assert.True(result.Value.Audit!.Ready);
        Assert.True(result.Value.Audit.Capture.Captured);
        Assert.True(result.Value.Audit.Capture.NonBlank);
        Assert.True(File.Exists(result.Value.Audit.Capture.OutputPath));
    }

    [Fact]
    public async Task GauntletPackagesExistingThreeDimensionalGameWithoutReplacingAuthoredScene()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeProjectStore().SaveAsync(
            root,
            RekallAgeProjectManifest.Create("Existing 3D Game", ["world", "rendering3d"]),
            CancellationToken.None);
        var authoredScene = RekallAgeSceneDocument.Create("Main", ["world", "rendering3d"])
            .AddEntity(RekallAgeEntityDocument.Create("AuthoredCamera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.Camera3D",
                    new JsonObject { ["active"] = true })))
            .AddEntity(RekallAgeEntityDocument.Create("AuthoredCitadel", ["authored"])
                .AddComponent(RekallAgeComponentDocument.Create(
                    "Rekall.MeshRenderer",
                    new JsonObject { ["mesh"] = "rekall.primitive.cube" })));
        await new RekallAgeSceneStore().SaveAsync(root, authoredScene, CancellationToken.None);
        var output = Path.Combine(root, "Package");
        var context = new RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("existing 3d authoring gauntlet"),
            CancellationToken.None);
        var scaffold = await new ScaffoldPlayableModuleCommand().ExecuteAsync(
            new ScaffoldPlayableModuleRequest(root, "existing.game", "Existing Game", "ExistingGame"),
            context);
        Assert.True(scaffold.Ok, scaffold.Summary);

        var result = await new RunAgentAuthoringGauntletCommand().ExecuteAsync(
            new RunAgentAuthoringGauntletRequest(root, "Existing 3D Game", "Main", output),
            context);

        Assert.True(result.Ok, result.Summary + Environment.NewLine + string.Join(
            Environment.NewLine,
            result.Errors.Select(error => $"{error.Code}: {error.Message}")));
        Assert.True(result.Value.Ready);
        Assert.Contains(result.Value.Checks, check => check is { Name: "scene-preserved", Passed: true });
        var preserved = await new RekallAgeSceneStore().LoadAsync(root, "Main", CancellationToken.None);
        Assert.Contains(preserved.Entities, entity => entity.Name == "AuthoredCitadel");
        Assert.DoesNotContain(preserved.Entities, entity => entity.Name == "Agent Authored Marker");
    }

    [Fact]
    public async Task GauntletAuthorsPackagesAuditsAndCapturesGenericPlayableProject()
    {
        var root = TestPaths.CreateTempDirectory();
        var output = Path.Combine(root, "Package");
        var context = new Rekall.Age.Core.Commands.RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("agent authoring gauntlet"),
            CancellationToken.None);

        var result = await new RunAgentAuthoringGauntletCommand().ExecuteAsync(
            new RunAgentAuthoringGauntletRequest(root, "Gauntlet Proof", "Main", output),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.True(result.Value.Ready);
        Assert.Equal(root, result.Value.ProjectRoot);
        Assert.Equal("Main", result.Value.SceneName);
        Assert.NotNull(result.Value.Package);
        Assert.NotNull(result.Value.Audit);
        Assert.Contains(result.Value.Package!.Checks, check => check is { Name: "module-trust", Passed: true });
        Assert.True(File.Exists(result.Value.Package!.ArchivePath));
        Assert.True(File.Exists(result.Value.Audit!.Capture.OutputPath));
        Assert.All(result.Value.Checks, check => Assert.True(check.Passed, check.Summary));
        Assert.Contains(result.Value.Checks, check => check.Name == "project-created");
        Assert.Contains(result.Value.Checks, check => check.Name == "scene-blueprint-authored");
        Assert.Contains(result.Value.Checks, check => check.Name == "module-source-authored");
        Assert.Contains(result.Value.Checks, check => check.Name == "package-audited");
        Assert.Contains(result.Value.NextActions, action => action == "rekall.workflow.inspect_playable_package");
        Assert.Contains(result.Value.NextActions, action => action == "rekall.workflow.run_playable_package");
        Assert.Contains(result.Value.NextActions, action => action == "rekall.workflow.capture_playable_package_frame");
        Assert.DoesNotContain("template", result.Value.AuthoringMode, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(result.Value.NextActions, action => action.Contains("template", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GauntletStopsWithStructuredChecksWhenProjectRootIsUnsafe()
    {
        var context = new Rekall.Age.Core.Commands.RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("agent authoring gauntlet failure"),
            CancellationToken.None);

        var result = await new RunAgentAuthoringGauntletCommand().ExecuteAsync(
            new RunAgentAuthoringGauntletRequest(string.Empty, "Gauntlet Proof"),
            context);

        Assert.False(result.Ok);
        Assert.False(result.Value.Ready);
        Assert.Contains(result.Value.Checks, check => check is { Name: "project-root", Passed: false });
        Assert.Contains(result.Value.NextActions, action => action == "rekall.project.create");
        Assert.Contains(result.Errors, error => error.Code == "REKALL_AGENT_GAUNTLET_PROJECT_ROOT_INVALID");
    }
}
