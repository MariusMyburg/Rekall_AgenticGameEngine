# Scene Runtime Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a canonical runtime scene model that can load authoring scenes into immutable snapshots, run deterministic fixed-step frames, expose subsystem projections, and report compact runtime inspection data through commands, CLI, MCP, and editor read models.

**Architecture:** Keep authoring documents as the source of truth, then project them into runtime records in `Rekall.Age.Runtime.Abstractions`. Put concrete scene loading, projection, execution, and command logic in `Rekall.Age.Runtime`. Add small editor contract/read-model additions so Studio can show runtime readiness without owning simulation state.

**Tech Stack:** C# 13, .NET 10, xUnit, existing Rekall command bus, existing scene store, existing MCP catalog projection, existing CLI switch routing.

---

## File Structure

- Modify `src/Rekall.Age.Runtime.Abstractions/RekallAgeRuntimeContracts.cs`
  - Add runtime world, entity, component, transform, subsystem view, observation, execution-loop, and system registry contracts.
- Modify `src/Rekall.Age.Runtime/Rekall.Age.Runtime.csproj`
  - Reference `Rekall.Age.Runtime.Abstractions` and `Rekall.Age.Rendering.Abstractions`.
- Create `src/Rekall.Age.Runtime/RekallAgeRuntimeWorldBuilder.cs`
  - Convert `RekallAgeSceneDocument` into `RekallAgeRuntimeWorld` without mutating the scene.
- Create `src/Rekall.Age.Runtime/RekallAgeRuntimeProjectionBuilder.cs`
  - Build render, physics, audio, animation, and UI projections from runtime entities.
- Create `src/Rekall.Age.Runtime/RekallAgeRuntimeExecutionLoop.cs`
  - Advance fixed-step frames in stable system order and return the final world.
- Create `src/Rekall.Age.Runtime/RekallAgeRuntimeSnapshotService.cs`
  - Load a scene, build a runtime world, run frames, and return the snapshot.
- Create `src/Rekall.Age.Runtime/Commands/InspectSceneRuntimeCommand.cs`
  - Register `rekall.runtime.inspect_scene`.
- Modify `src/Rekall.Age.Runtime/RekallAgeRuntimeObservation.cs`
  - Replace the legacy observation record with the structured observation shape while keeping compatibility properties for existing tests.
- Modify `src/Rekall.Age.Runtime/RekallAgeGameplayInterpreter.cs`
  - Emit structured observations with compatibility values.
- Modify `src/Rekall.Age.Runtime/RekallAgeRuntimeResult.cs`
  - Keep the existing `ActiveSystems` behavior based on observation `System`.
- Modify `src/Rekall.Age.Cli/Program.cs`
  - Register the runtime inspection command and add `runtime inspect <root> <scene> <frames>`.
- Modify `src/Rekall.Age.Editor.Contracts/RekallAgeWorkbenchModel.cs`
  - Add runtime panel model records.
- Modify `src/Rekall.Age.Editor/Rekall.Age.Editor.csproj`
  - Reference `Rekall.Age.Runtime` if the builder uses the runtime snapshot service directly.
- Modify `src/Rekall.Age.Editor/RekallAgeWorkbenchModelBuilder.cs`
  - Include runtime counts and runtime observations in the workbench model.
- Add tests under `tests/Rekall.Age.Tests/Runtime/SceneRuntimeFoundationTests.cs`
  - Cover world building, transforms, projections, execution, command output, and non-mutation.
- Add tests under `tests/Rekall.Age.Tests/Cli/RuntimeInspectCliTests.cs`
  - Cover CLI route output.
- Modify `tests/Rekall.Age.Tests/Mcp/WorkbenchMcpCatalogTests.cs`
  - Cover MCP catalog visibility for `rekall.runtime.inspect_scene`.
- Modify `tests/Rekall.Age.Tests/Editor/WorkbenchReadModelTests.cs`
  - Cover runtime panel counts in Studio read models.
- Modify `tests/Rekall.Age.Tests/VerticalSlice/WorkbenchFoundationTests.cs`
  - Cover runtime inspection in the authoring loop.
- Modify `README.md`
  - Document the new runtime inspection command and the runtime snapshot boundary.

## Task 1: Runtime World Contracts And Builder

**Files:**
- Modify: `src/Rekall.Age.Runtime.Abstractions/RekallAgeRuntimeContracts.cs`
- Modify: `src/Rekall.Age.Runtime/Rekall.Age.Runtime.csproj`
- Create: `src/Rekall.Age.Runtime/RekallAgeRuntimeWorldBuilder.cs`
- Test: `tests/Rekall.Age.Tests/Runtime/SceneRuntimeFoundationTests.cs`

- [ ] **Step 1: Write the failing world-builder tests**

Add these test methods to the new test class:

```csharp
[Fact]
public void BuilderPreservesSceneIdsHierarchyVisibilityAndComponents()
{
    var parent = RekallAgeEntityDocument.Create("Root", ["level"]);
    var child = RekallAgeEntityDocument.Create("Player", ["player"]) with
    {
        ParentId = parent.Id,
        PrefabSourceId = "prefab_player",
        Locked = true
    };
    child = child.AddComponent(RekallAgeComponentDocument.Create(
        "Rekall.Transform2D",
        new JsonObject { ["x"] = 12.5, ["y"] = -2, ["rotation"] = 45, ["scaleX"] = 2, ["scaleY"] = 3 }));
    var scene = RekallAgeSceneDocument.Create("Main", ["world"]).AddEntity(parent).AddEntity(child);

    var world = new RekallAgeRuntimeWorldBuilder().Build(scene);
    var runtimeChild = world.Entities.Single(entity => entity.Id == child.Id);

    Assert.Equal(scene.Id, world.SceneId);
    Assert.Equal("Main", world.SceneName);
    Assert.Equal(parent.Id, runtimeChild.ParentId);
    Assert.Equal("prefab_player", runtimeChild.PrefabSourceId);
    Assert.True(runtimeChild.Locked);
    Assert.True(runtimeChild.Visible);
    Assert.Equal("Rekall.Transform2D", Assert.Single(runtimeChild.Components).Type);
    Assert.Equal(12.5, runtimeChild.Transform.Position2D.X);
    Assert.Equal(-2, runtimeChild.Transform.Position2D.Y);
    Assert.Equal(45, runtimeChild.Transform.Rotation2D);
    Assert.Equal(2, runtimeChild.Transform.Scale2D.X);
    Assert.Equal(3, runtimeChild.Transform.Scale2D.Y);
}

[Fact]
public void BuilderExtracts3DTransformAndDoesNotMutateAuthoringScene()
{
    var entity = RekallAgeEntityDocument.Create("Camera", ["camera"])
        .AddComponent(RekallAgeComponentDocument.Create(
            "Rekall.Transform3D",
            new JsonObject
            {
                ["x"] = 1,
                ["y"] = 2,
                ["z"] = 3,
                ["pitch"] = 10,
                ["yaw"] = 20,
                ["roll"] = 30,
                ["scaleX"] = 4,
                ["scaleY"] = 5,
                ["scaleZ"] = 6
            }));
    var scene = RekallAgeSceneDocument.Create("Main", ["world"]).AddEntity(entity);
    var before = scene.Entities.Single().Components.Single().Properties.ToJsonString();

    var world = new RekallAgeRuntimeWorldBuilder().Build(scene);
    var after = scene.Entities.Single().Components.Single().Properties.ToJsonString();

    Assert.Equal(before, after);
    Assert.Equal(new RekallAgeRuntimeVector3(1, 2, 3), world.Entities.Single().Transform.Position3D);
    Assert.Equal(new RekallAgeRuntimeVector3(10, 20, 30), world.Entities.Single().Transform.Rotation3D);
    Assert.Equal(new RekallAgeRuntimeVector3(4, 5, 6), world.Entities.Single().Transform.Scale3D);
}
```

- [ ] **Step 2: Run the focused tests and confirm they fail to compile**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter SceneRuntimeFoundationTests -p:UseSharedCompilation=false`

Expected: compile failure naming `RekallAgeRuntimeWorldBuilder` and runtime vector/world types.

- [ ] **Step 3: Add runtime contracts and builder**

Add records for:

```csharp
public sealed record RekallAgeRuntimeWorld(
    string SceneId,
    string SceneName,
    int FrameIndex,
    TimeSpan ElapsedTime,
    IReadOnlyList<RekallAgeRuntimeEntity> Entities,
    RekallAgeRuntimeSubsystemViews Subsystems,
    IReadOnlyList<RekallAgeRuntimeObservation> Observations);

public sealed record RekallAgeRuntimeEntity(
    string Id,
    string Name,
    IReadOnlyList<string> Tags,
    string? ParentId,
    string? PrefabSourceId,
    bool Visible,
    bool Locked,
    RekallAgeRuntimeTransform Transform,
    IReadOnlyList<RekallAgeRuntimeComponent> Components);

public sealed record RekallAgeRuntimeComponent(string Type, JsonObject Properties);
public sealed record RekallAgeRuntimeVector2(double X, double Y);
public sealed record RekallAgeRuntimeVector3(double X, double Y, double Z);
```

The builder must deep-clone component property bags, sort entities by name/id, use identity transforms when no transform component exists, read `x`, `y`, `z`, `rotation`, `pitch`, `yaw`, `roll`, `scaleX`, `scaleY`, and `scaleZ`, and return frame index `0`.

- [ ] **Step 4: Run the focused tests and confirm they pass**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter SceneRuntimeFoundationTests -p:UseSharedCompilation=false`

Expected: the two tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Rekall.Age.Runtime.Abstractions/RekallAgeRuntimeContracts.cs src/Rekall.Age.Runtime/Rekall.Age.Runtime.csproj src/Rekall.Age.Runtime/RekallAgeRuntimeWorldBuilder.cs tests/Rekall.Age.Tests/Runtime/SceneRuntimeFoundationTests.cs
git commit -m "feat: add runtime world builder"
```

## Task 2: Subsystem Projections And Observations

**Files:**
- Create: `src/Rekall.Age.Runtime/RekallAgeRuntimeProjectionBuilder.cs`
- Modify: `src/Rekall.Age.Runtime/RekallAgeRuntimeWorldBuilder.cs`
- Modify: `src/Rekall.Age.Runtime/RekallAgeRuntimeObservation.cs`
- Modify: `src/Rekall.Age.Runtime/RekallAgeGameplayInterpreter.cs`
- Test: `tests/Rekall.Age.Tests/Runtime/SceneRuntimeFoundationTests.cs`

- [ ] **Step 1: Write failing projection tests**

Add tests that create entities with `Rekall.Camera2D`, `Rekall.SpriteRenderer`, `Rekall.MeshRenderer`, `Rekall.PointLight`, `Rekall.Rigidbody2D`, `Rekall.BoxCollider2D`, `Rekall.AudioEmitter`, `Rekall.AnimationPlayer`, `Rekall.UiElement`, and `Rekall.UiCanvas`. Assert:

```csharp
Assert.Single(world.Subsystems.Rendering.Cameras);
Assert.Single(world.Subsystems.Rendering.Sprites);
Assert.Single(world.Subsystems.Rendering.Meshes);
Assert.Single(world.Subsystems.Rendering.Lights);
Assert.Single(world.Subsystems.Physics.RigidBodies);
Assert.Single(world.Subsystems.Physics.Colliders);
Assert.Single(world.Subsystems.Audio.Emitters);
Assert.Single(world.Subsystems.Animation.Players);
Assert.Single(world.Subsystems.Ui.Canvases);
Assert.Single(world.Subsystems.Ui.Elements);
Assert.Contains(world.Observations, item => item.Code == "REKALL_AUDIO_NO_LISTENER" && item.Severity == "warning");
```

Also add a compatibility assertion:

```csharp
var observation = new RekallAgeGameplayInterpreter()
    .Observe(scene, 3)
    .Single(item => item.System == "Camera2D");
Assert.Equal(3, observation.Frame);
Assert.Equal("rendering", observation.Subsystem);
Assert.Equal("info", observation.Severity);
```

- [ ] **Step 2: Run focused tests and confirm they fail**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter SceneRuntimeFoundationTests -p:UseSharedCompilation=false`

Expected: missing subsystem projection properties and structured observation members.

- [ ] **Step 3: Implement projection records and builder**

Add projection records for rendering, physics, audio, animation, and UI views. `RekallAgeRuntimeProjectionBuilder` should classify component types by exact names and suffix checks:

```csharp
private static bool IsLight(string type) =>
    type.Contains("Light", StringComparison.Ordinal);
```

Emit observations with:

```csharp
new RekallAgeRuntimeObservation(
    frame: world.FrameIndex,
    code: "REKALL_AUDIO_NO_LISTENER",
    severity: "warning",
    subsystem: "audio",
    targetId: emitter.EntityId,
    targetName: emitter.EntityName,
    system: "AudioEmitter",
    message: "Audio emitters exist but no Rekall.AudioListener is active.",
    suggestedCommands: Array.Empty<string>())
```

Keep compatibility properties named `Frame`, `EntityId`, `EntityName`, `System`, and `Message` on the observation record.

- [ ] **Step 4: Run focused tests and confirm they pass**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter SceneRuntimeFoundationTests -p:UseSharedCompilation=false`

Expected: all runtime foundation tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Rekall.Age.Runtime.Abstractions/RekallAgeRuntimeContracts.cs src/Rekall.Age.Runtime/RekallAgeRuntimeProjectionBuilder.cs src/Rekall.Age.Runtime/RekallAgeRuntimeWorldBuilder.cs src/Rekall.Age.Runtime/RekallAgeRuntimeObservation.cs src/Rekall.Age.Runtime/RekallAgeGameplayInterpreter.cs tests/Rekall.Age.Tests/Runtime/SceneRuntimeFoundationTests.cs
git commit -m "feat: add runtime subsystem projections"
```

## Task 3: Fixed-Step Runtime Execution

**Files:**
- Create: `src/Rekall.Age.Runtime/RekallAgeRuntimeExecutionLoop.cs`
- Test: `tests/Rekall.Age.Tests/Runtime/SceneRuntimeFoundationTests.cs`

- [ ] **Step 1: Write failing execution tests**

Add:

```csharp
[Fact]
public async Task ExecutionLoopAdvancesFramesDeterministically()
{
    var scene = RekallAgeSceneDocument.Create("Main", ["world"]);
    var initial = new RekallAgeRuntimeWorldBuilder().Build(scene);
    var loop = RekallAgeRuntimeExecutionLoop.CreateDefault();

    var result = await loop.RunAsync(initial, frames: 3, CancellationToken.None);

    Assert.Equal(3, result.World.FrameIndex);
    Assert.Equal(TimeSpan.FromSeconds(3.0 / 60.0), result.World.ElapsedTime);
    Assert.Equal(3, result.FramesSimulated);
    Assert.Equal(["runtime.animation", "runtime.audio", "runtime.physics", "runtime.rendering", "runtime.transform", "runtime.ui"], result.SystemsRun);
}
```

- [ ] **Step 2: Run focused tests and confirm they fail**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter ExecutionLoopAdvancesFramesDeterministically -p:UseSharedCompilation=false`

Expected: missing execution loop.

- [ ] **Step 3: Implement the loop**

Create `RekallAgeRuntimeExecutionLoop`, `RekallAgeRuntimeSystemRegistration`, `IRekallAgeRuntimeWorldSystem`, and `RekallAgeRuntimeRunResult`. Default systems should have stable ids:

```csharp
runtime.animation
runtime.audio
runtime.physics
runtime.rendering
runtime.transform
runtime.ui
```

Use a default fixed delta of `TimeSpan.FromSeconds(1.0 / 60.0)`. Each frame should call systems ordered by priority then id, rebuild subsystem projections, and return a world with updated frame index and elapsed time.

- [ ] **Step 4: Run focused tests and confirm they pass**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter ExecutionLoopAdvancesFramesDeterministically -p:UseSharedCompilation=false`

Expected: pass.

- [ ] **Step 5: Commit**

```bash
git add src/Rekall.Age.Runtime.Abstractions/RekallAgeRuntimeContracts.cs src/Rekall.Age.Runtime/RekallAgeRuntimeExecutionLoop.cs tests/Rekall.Age.Tests/Runtime/SceneRuntimeFoundationTests.cs
git commit -m "feat: add deterministic runtime execution loop"
```

## Task 4: Runtime Snapshot Service And Inspect Command

**Files:**
- Create: `src/Rekall.Age.Runtime/RekallAgeRuntimeSnapshotService.cs`
- Create: `src/Rekall.Age.Runtime/Commands/InspectSceneRuntimeCommand.cs`
- Test: `tests/Rekall.Age.Tests/Runtime/SceneRuntimeFoundationTests.cs`

- [ ] **Step 1: Write failing command tests**

Add:

```csharp
[Fact]
public async Task InspectSceneRuntimeCommandReturnsCompactSubsystemCounts()
{
    var root = TestPaths.CreateTempDirectory();
    var sceneStore = new RekallAgeSceneStore();
    var scene = RekallAgeSceneDocument.Create("Main", ["world"])
        .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera2D", new JsonObject { ["active"] = true })))
        .AddEntity(RekallAgeEntityDocument.Create("Actor", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Transform2D", new JsonObject { ["x"] = 1, ["y"] = 2 }))
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.SpriteRenderer", new JsonObject { ["sprite"] = "asset_actor" }))
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.Rigidbody2D", new JsonObject { ["mass"] = 1 }))
            .AddComponent(RekallAgeComponentDocument.Create("Rekall.AudioEmitter", new JsonObject { ["clip"] = "asset_step" })));
    await sceneStore.SaveAsync(root, scene, CancellationToken.None);

    var result = await new InspectSceneRuntimeCommand().ExecuteAsync(
        new InspectSceneRuntimeRequest(root, "Main", 2),
        new RekallAgeCommandContext("test", RekallAgeTransaction.Begin("inspect runtime"), CancellationToken.None));

    Assert.True(result.Ok);
    Assert.Equal("Main", result.Value.SceneName);
    Assert.Equal(2, result.Value.FrameIndex);
    Assert.Equal(2, result.Value.EntityCount);
    Assert.Equal(2, result.Value.RenderableCount);
    Assert.Equal(1, result.Value.PhysicsBodyCount);
    Assert.Equal(1, result.Value.AudioEmitterCount);
    Assert.Contains(result.Value.Observations, item => item.Code == "REKALL_AUDIO_NO_LISTENER");
}
```

- [ ] **Step 2: Run command test and confirm it fails**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter InspectSceneRuntimeCommandReturnsCompactSubsystemCounts -p:UseSharedCompilation=false`

Expected: missing command and snapshot service.

- [ ] **Step 3: Implement snapshot service and command**

`InspectSceneRuntimeRequest` must contain `ProjectRoot`, `SceneName`, and `Frames`. `InspectSceneRuntimeResult` must contain `SceneName`, `FrameIndex`, `ElapsedSeconds`, `EntityCount`, `RenderableCount`, `PhysicsBodyCount`, `PhysicsColliderCount`, `AudioListenerCount`, `AudioEmitterCount`, `AnimationPlayerCount`, `UiElementCount`, and observations.

Reject negative frames with a command error code `REKALL_RUNTIME_INVALID_FRAMES`. Use `Math.Max(0, request.Frames)` inside the service only after command validation has accepted the request.

- [ ] **Step 4: Run command test and confirm it passes**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter InspectSceneRuntimeCommandReturnsCompactSubsystemCounts -p:UseSharedCompilation=false`

Expected: pass.

- [ ] **Step 5: Commit**

```bash
git add src/Rekall.Age.Runtime/RekallAgeRuntimeSnapshotService.cs src/Rekall.Age.Runtime/Commands/InspectSceneRuntimeCommand.cs tests/Rekall.Age.Tests/Runtime/SceneRuntimeFoundationTests.cs
git commit -m "feat: add runtime scene inspection command"
```

## Task 5: CLI And MCP Exposure

**Files:**
- Modify: `src/Rekall.Age.Cli/Program.cs`
- Create: `tests/Rekall.Age.Tests/Cli/RuntimeInspectCliTests.cs`
- Modify: `tests/Rekall.Age.Tests/Mcp/WorkbenchMcpCatalogTests.cs`

- [ ] **Step 1: Write failing CLI and MCP tests**

CLI test:

```csharp
[Fact]
public async Task RuntimeInspectPrintsSubsystemCounts()
{
    var root = TestPaths.CreateTempDirectory();
    await new RekallAgeProjectStore().SaveAsync(
        root,
        RekallAgeProjectManifest.Create("Runtime CLI", ["world"]),
        CancellationToken.None);
    await new RekallAgeSceneStore().SaveAsync(
        root,
        RekallAgeSceneDocument.Create("Main", ["world"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera2D", new JsonObject { ["active"] = true }))),
        CancellationToken.None);

    var result = await RunAsync(FindCliAssemblyPath(), "runtime", "inspect", root, "Main", "2");

    Assert.Equal(0, result.ExitCode);
    Assert.Contains("Runtime Main frame 2", result.Output);
    Assert.Contains("Entities: 1", result.Output);
    Assert.Contains("Renderable: 1", result.Output);
}
```

MCP test addition:

```csharp
registry.Register(new InspectSceneRuntimeCommand());
Assert.Contains("rekall.runtime.inspect_scene", names);
```

- [ ] **Step 2: Run focused tests and confirm they fail**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "RuntimeInspectPrintsSubsystemCounts|WorkbenchCommandsAreVisibleToMcpCatalog" -p:UseSharedCompilation=false`

Expected: CLI route and MCP registration absent.

- [ ] **Step 3: Add CLI route and registry registration**

Update usage to include `runtime`, add switch route:

```csharp
["runtime", "inspect", var root, var scene, var frames] =>
    await InspectRuntimeAsync(registry, context, root, scene, frames),
```

Register:

```csharp
registry.Register(new InspectSceneRuntimeCommand());
```

Add `InspectRuntimeAsync` that parses frames, executes `rekall.runtime.inspect_scene`, prints the summary and counts, then prints observations as `severity code subsystem target: message`.

- [ ] **Step 4: Run focused tests and confirm they pass**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "RuntimeInspectPrintsSubsystemCounts|WorkbenchCommandsAreVisibleToMcpCatalog" -p:UseSharedCompilation=false`

Expected: pass.

- [ ] **Step 5: Commit**

```bash
git add src/Rekall.Age.Cli/Program.cs tests/Rekall.Age.Tests/Cli/RuntimeInspectCliTests.cs tests/Rekall.Age.Tests/Mcp/WorkbenchMcpCatalogTests.cs
git commit -m "feat: expose runtime inspection through cli and mcp"
```

## Task 6: Studio Runtime Panel And Vertical Slice

**Files:**
- Modify: `src/Rekall.Age.Editor.Contracts/RekallAgeWorkbenchModel.cs`
- Modify: `src/Rekall.Age.Editor/Rekall.Age.Editor.csproj`
- Modify: `src/Rekall.Age.Editor/RekallAgeWorkbenchModelBuilder.cs`
- Modify: `tests/Rekall.Age.Tests/Editor/WorkbenchReadModelTests.cs`
- Modify: `tests/Rekall.Age.Tests/VerticalSlice/WorkbenchFoundationTests.cs`

- [ ] **Step 1: Write failing editor/vertical-slice assertions**

Add assertions:

```csharp
Assert.Equal("Main", model.Runtime.SceneName);
Assert.Equal(0, model.Runtime.FrameIndex);
Assert.True(model.Runtime.EntityCount >= 1);
Assert.True(model.Runtime.RenderableCount >= 1);
Assert.DoesNotContain(model.Runtime.Observations, observation => observation.Severity == "blocking");
```

In the vertical slice, register `InspectSceneRuntimeCommand`, execute it after prefab instantiation, and assert:

```csharp
Assert.True(runtime.Ok);
Assert.True(runtime.Value.EntityCount >= 3);
Assert.True(runtime.Value.RenderableCount >= 1);
```

- [ ] **Step 2: Run focused tests and confirm they fail**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "WorkbenchReadModelTests|AgentAndStudioCanShareAuthoringLoop" -p:UseSharedCompilation=false`

Expected: missing runtime panel property and command registration in the vertical test.

- [ ] **Step 3: Add editor runtime model**

Add records:

```csharp
public sealed record RekallAgeRuntimePanelModel(
    string SceneName,
    int FrameIndex,
    int EntityCount,
    int RenderableCount,
    int PhysicsBodyCount,
    int AudioEmitterCount,
    int AnimationPlayerCount,
    int UiElementCount,
    IReadOnlyList<RekallAgeRuntimePanelObservation> Observations);

public sealed record RekallAgeRuntimePanelObservation(
    string Code,
    string Severity,
    string Subsystem,
    string Target,
    string Message);
```

Append `RekallAgeRuntimePanelModel Runtime` to `RekallAgeWorkbenchModel`. In `RekallAgeWorkbenchModelBuilder`, build a zero-frame runtime snapshot and map observations into the panel.

- [ ] **Step 4: Run focused tests and confirm they pass**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "WorkbenchReadModelTests|AgentAndStudioCanShareAuthoringLoop" -p:UseSharedCompilation=false`

Expected: pass.

- [ ] **Step 5: Commit**

```bash
git add src/Rekall.Age.Editor.Contracts/RekallAgeWorkbenchModel.cs src/Rekall.Age.Editor/Rekall.Age.Editor.csproj src/Rekall.Age.Editor/RekallAgeWorkbenchModelBuilder.cs tests/Rekall.Age.Tests/Editor/WorkbenchReadModelTests.cs tests/Rekall.Age.Tests/VerticalSlice/WorkbenchFoundationTests.cs
git commit -m "feat: surface runtime state in studio models"
```

## Task 7: Documentation And Full Verification

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Update README**

Add a short `Scene Runtime Foundation` section with:

```markdown
### Scene Runtime Foundation

Inspect a deterministic runtime snapshot without mutating authoring files:

```powershell
dotnet run --project src/Rekall.Age.Cli -- runtime inspect .age-sandbox Main 3
```

The command reports entity counts, renderable counts, physics/audio/animation/UI readiness, and structured runtime observations for agents and Studio.
```

- [ ] **Step 2: Run marker scan**

Run: `$env:REKALL_SCAN_PATTERN='TO' + 'DO|TB' + 'D|place' + 'holder|lor' + 'em|FIX' + 'ME'; rg -n $env:REKALL_SCAN_PATTERN docs/superpowers/plans/2026-05-25-rekall-age-scene-runtime-foundation.md docs/superpowers/specs/2026-05-25-rekall-age-scene-runtime-foundation-design.md`

Expected: no matches.

- [ ] **Step 3: Run build**

Run: `dotnet build Rekall.AGE.sln -p:UseSharedCompilation=false`

Expected: build succeeds with 0 errors.

- [ ] **Step 4: Run full tests**

Run: `dotnet test Rekall.AGE.sln -p:UseSharedCompilation=false`

Expected: all tests pass.

- [ ] **Step 5: Commit**

```bash
git add README.md
git commit -m "docs: document scene runtime foundation"
```

## Self-Review

- Spec coverage: tasks cover immutable runtime worlds, normalized transforms, projections, observations, deterministic fixed-step execution, command bus, CLI, MCP catalog, editor read models, vertical slice, docs, build, and tests.
- Marker scan target: the plan avoids open-ended markers except inside the explicit scan command.
- Type consistency: `RekallAgeRuntimeWorld`, `RekallAgeRuntimeEntity`, `RekallAgeRuntimeObservation`, `RekallAgeRuntimeSubsystemViews`, `InspectSceneRuntimeRequest`, and `InspectSceneRuntimeResult` names are consistent across tasks.
- Scope check: no real physics solver, audio mixer, animation curve evaluator, GPU renderer, asset decoder, or WPF play controls are included in this slice.
