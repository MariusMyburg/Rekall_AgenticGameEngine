# Summit Run 2D Full-Game Acceptance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an asset-free generic 2D shape renderer, then prove Studio's English-to-game workflow by authoring, running, inspecting, and packaging the original 2D hill-driving game Summit Run.

**Architecture:** `Rekall.ShapeRenderer2D` projects through the existing runtime mesh collection and becomes an XY-plane `RekallAgeRuntimeViewportGeometryMesh` in the frame builder, allowing existing software and Vulkan paths to render identical geometry. Vehicle, fuel, score, camera, and game-state behavior remains in a project-owned C# module authored through Studio chat; generic defects discovered by the trial receive focused red-green fixes before authoring resumes.

**Tech Stack:** .NET 10, C#, xUnit, Avalonia Studio, Rekall runtime/MCP tools, BEPU 2D physics projection, Vulkan and software rendering, Ollama-backed Studio chat.

**Spec:** `docs/superpowers/specs/2026-08-29-summit-run-2d-acceptance-design.md`

## Global Constraints

- Do not add hill-driving, fuel, score, or vehicle-controller behavior to engine core.
- Use `Rekall.InputActionMap`, semantic actions, and engine delta time in game code.
- Keep a `Game.SummitRunState` component attached and assert a strict gameplay mutation after the final authored change.
- Render the same asset-free shapes in Studio, software capture, Vulkan Player, and the packaged Player.
- Run only focused tests during development; use broader gates only for final acceptance.
- Preserve unrelated dirty lockfiles and Stellar Dominion artifacts.

## File Structure

- `src/Rekall.Age.Modules/BuiltIns/RekallAgeBuiltInModule.cs`: component schema.
- `src/Rekall.Age.World/RekallAgeBuiltInComponentTypeCatalog.cs`: built-in discovery.
- `src/Rekall.Age.Runtime/RekallAgeRuntimeProjectionBuilder.cs`: shape-to-mesh projection.
- `src/Rekall.Age.Rendering/RekallAgeRuntimeRenderFrameBuilder.cs`: XY rectangle/circle geometry.
- Existing agent/schema/runtime classifier files: expose the component consistently to authoring agents.
- `tests/Rekall.Age.Tests/Rendering/ShapeRenderer2DTests.cs`: focused engine coverage.
- `Examples/SummitRun/`: Studio-authored project and module.
- `tests/Rekall.Age.Tests/Examples/SummitRunAcceptanceTests.cs`: stable gameplay/presentation proof.

---

### Task 1: Register and Discover `Rekall.ShapeRenderer2D`

**Files:**
- Modify: `src/Rekall.Age.Modules/BuiltIns/RekallAgeBuiltInModule.cs`
- Modify: `src/Rekall.Age.World/RekallAgeBuiltInComponentTypeCatalog.cs`
- Modify: `src/Rekall.Age.Agent/RekallAgeContextBuilder.cs`
- Modify: `src/Rekall.Age.Modules/Commands/SearchComponentSchemasCommand.cs`
- Modify: `src/Rekall.Age.Runtime/RekallAgeGameplayInterpreter.cs`
- Test: `tests/Rekall.Age.Tests/Rendering/ShapeRenderer2DTests.cs`

**Interfaces:**
- Consumes: built-in module registration and component/property attributes.
- Produces: `Rekall.ShapeRenderer2D` with `Shape`, `Width`, `Height`, `Radius`, `Color`, and `Active`.

- [ ] **Step 1: Write the failing schema test.** Build the same registry used by adjacent schema tests; assert the exact type, `rectangle`, `1`, `1`, `0.5`, `#ffffff`, and `true` defaults; assert the built-in type catalog includes the exact type string.
- [ ] **Step 2: Run the red test.** Run `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "FullyQualifiedName~ShapeRenderer2DTests" --no-restore`; expect failure because the type is absent.
- [ ] **Step 3: Implement the schema.** Register `RekallAgeShapeRenderer2DComponent` beside the sprite renderer. Use minimum `0.0001` for dimensions, `Kind = "color"` for color, and descriptions stating that Transform2D supplies position/rotation/scale. Add the exact type to the catalog and every explicit rendering classifier that recognizes SpriteRenderer.
- [ ] **Step 4: Run focused discovery coverage.** Run `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "FullyQualifiedName~ShapeRenderer2DTests|FullyQualifiedName~SearchComponentSchemasCommandTests|FullyQualifiedName~AgentContextTests" --no-restore`; expect pass.
- [ ] **Step 5: Commit.** Stage only the five source files and new test; commit `feat: add generic 2D shape renderer schema`.

### Task 2: Project Shapes into Runtime Rendering

**Files:**
- Modify: `src/Rekall.Age.Runtime/RekallAgeRuntimeProjectionBuilder.cs`
- Test: `tests/Rekall.Age.Tests/Rendering/ShapeRenderer2DTests.cs`

**Interfaces:**
- Consumes: `Rekall.ShapeRenderer2D` and `RekallAgeRuntimeRenderMesh`.
- Produces: one built-in mesh per active visible shape with variant `rekall.shape2d`, material color, sort key 100, and resolved render layer.

- [ ] **Step 1: Add failing projection cases.** Project a colored shape with a render layer and assert one mesh, built-in provenance, `Kind == "mesh"`, variant `rekall.shape2d`, color, and layer. Add cases proving inactive/invisible shapes do not project and authored module meshes survive reprojection.
- [ ] **Step 2: Run the focused test; expect no projected mesh.**
- [ ] **Step 3: Implement built-in projection.** In the component switch, skip inactive shapes and add a `RekallAgeRuntimeRenderMesh` with null asset, `Variant: "rekall.shape2d"`, component color or `#ffffff`, `Kind: "mesh"`, `SortKey: 100`, built-in projection source, and current render layer. Add the type to visibility gating.
- [ ] **Step 4: Rerun `ShapeRenderer2DTests`; expect pass.**
- [ ] **Step 5: Commit** `feat: project 2D shapes into runtime rendering`.

### Task 3: Generate Rectangle and Circle Production Geometry

**Files:**
- Modify: `src/Rekall.Age.Rendering/RekallAgeRuntimeRenderFrameBuilder.cs`
- Test: `tests/Rekall.Age.Tests/Rendering/ShapeRenderer2DTests.cs`

**Interfaces:**
- Consumes: meshes with variant `rekall.shape2d` and the same entity's shape component.
- Produces: ordinary `kind: "mesh"` viewport renderables carrying asset-free XY geometry.

- [ ] **Step 1: Add failing geometry tests.** A rectangle width 4/height 2 must produce four vertices, six indices, local bounds `[-2,2] x [-1,1]`, and retain position/rotation/Transform2D scale. A radius 1.5 circle must produce a center plus 49 closed rim vertices, 144 indices, finite values, and bounds within 0.001 of ±1.5. Non-positive dimensions clamp to 0.0001; unknown shapes normalize to rectangle.
- [ ] **Step 2: Run the focused test; expect `GeometryMesh` to be null.**
- [ ] **Step 3: Implement `ReadShape2DGeometry`.** Retain the shape component during the existing single component scan. Generate counter-clockwise XY triangles at Z=0 with a consistent camera-facing normal and UVs shaped like existing geometry. Use a 48-segment fan for circles. Preserve Transform2D scale in the renderable so dimensions are multiplied exactly once.
- [ ] **Step 4: Add cross-renderer proof.** Assert `RekallAgeVulkanSceneMeshBuilder.BuildMeshes` returns one mesh and software rendering reports zero fallback/missing assets and nonblank pixels.
- [ ] **Step 5: Run `ShapeRenderer2DTests`; expect pass, then commit** `feat: render 2D shapes in software and Vulkan paths`.

### Task 4: Verify Studio Component Authoring and Create/Open UX

**Files:**
- Modify if a defect is observed: exact files under `src/Rekall.Age.Studio/`
- Test if a defect is observed: exact files under `tests/Rekall.Age.Studio.Tests/`

**Interfaces:**
- Consumes: the schema through Studio's existing engine-host pipeline.
- Produces: Create project form, native Open/Browse dialog, discoverable shapes, viewport selection, and inspector editing.

- [ ] **Step 1: Build Studio.** Run `dotnet build src/Rekall.Age.Studio/Rekall.Age.Studio.csproj --no-restore`.
- [ ] **Step 2: Exercise UI.** With computer control, click Create and create `Examples/SummitRun` through the form; click Open and verify a native browse dialog. Add/select a temporary shape and edit shape/dimensions/color in the inspector.
- [ ] **Step 3: Diagnose any failure first.** Capture visible state/log evidence, trace command to service, and add the narrowest failing Studio test for the exact behavior.
- [ ] **Step 4: Implement only an observed generic repair.** Name the focused class `CreateOpenAndShapeAuthoringTests` and run `dotnet test tests/Rekall.Age.Studio.Tests/Rekall.Age.Studio.Tests.csproj --filter "FullyQualifiedName~CreateOpenAndShapeAuthoringTests" --no-restore`. If no defect is observed, make no speculative Studio change or test.
- [ ] **Step 5: Commit any repair separately.** Never stage the pre-existing lockfile modifications.

### Task 5: Author Summit Run through One Studio Chat Request

**Files:**
- Create through Studio: `Examples/SummitRun/project.age.json`
- Create through Studio: `Examples/SummitRun/Scenes/Main.scene.json`
- Create through Studio: `Examples/SummitRun/Modules/SummitRun.Game/`

**Interfaces:**
- Consumes: configured Ollama Studio chat and standard schema/scene/module/build/runtime tools.
- Produces: a complete valid project with attached `Game.SummitRunState` and executable gameplay.

- [ ] **Step 1: Send exactly one cohesive prompt.**

```text
Create a complete original 2D side-view hill-driving game called Summit Run in this project. Make it immediately playable and visually polished with the engine's asset-free 2D shapes: a colorful two-wheel rover with a physics chassis and motorized wheels, a readable rolling course made from multiple sloped terrain segments, energy-cell collectibles, a finish beacon, a following orthographic camera, and a clear HUD for fuel, distance, cells, status, and controls. Use semantic actions so D/Right and A/Left drive, W/Up and S/Down lean the rover, and R resets. Put all game-specific behavior in an attached project C# runtime module, use delta time, expose inspectable Game.SummitRunState, emit useful observations when authored dependencies are missing, and finish by building, validating, running deterministic representative input with strict gameplay assertions, and preparing the project for a production Player run. Keep iterating on your own tool results until the full game works; do not merely describe what should be built.
```

- [ ] **Step 2: Monitor chat tools, Studio logs, and files.** Record failures, loops, invalid schema guesses, checkpoint deadlocks, provider/account UI leakage, build diagnostics, and self-recovery.
- [ ] **Step 3: Repair generic blockers with systematic debugging and TDD.** Reproduce, trace, add a focused red test, make the smallest generic fix, rerun, rebuild/relaunch, and resume the same objective. Never add Summit Run behavior to core.
- [ ] **Step 4: Inspect generated content.** Require representative action mappings, actual chassis/wheel joints and colliders, attached state, delta-time usage, generic queries/mutations, camera follow, reset, collectibles, success/failure, and structured observations.
- [ ] **Step 5: Commit engine/Studio fixes separately from the authored example.** Exclude package/build/capture output unless repository convention tracks it.

### Task 6: Add Deterministic Example Acceptance Coverage

**Files:**
- Create: `tests/Rekall.Age.Tests/Examples/SummitRunAcceptanceTests.cs`
- Modify if a generic defect is proved: exact runtime/physics/module source and focused test.

**Interfaces:**
- Consumes: the final project, runtime session, fixed 60 Hz input frames, and built module.
- Produces: repeatable composition, gameplay, camera, and render assertions.

- [ ] **Step 1: Write acceptance tests against actual authored IDs.** Assert one attached `Game.SummitRunState`, an input action map, at least ten shapes, two wheel circle colliders, hinge joints, and a Camera2D. Drive a bounded sequence at `DeltaSeconds = 1d/60d`; require nonzero chassis X delta and a changed state property. Send reset and assert pose/state return within explicit tolerance.
- [ ] **Step 2: Run `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "FullyQualifiedName~SummitRunAcceptanceTests" --no-restore`.** Repair authored content through chat first; if it is generic, create a focused engine red test before fixing.
- [ ] **Step 3: Add presentation assertions.** At 1280x720 require active Camera2D, at least ten mesh renderables, visible UI, finite geometry/transforms, nonblank pixels, and zero missing/fallback assets.
- [ ] **Step 4: Run both focused classes.** Use `--filter "FullyQualifiedName~ShapeRenderer2DTests|FullyQualifiedName~SummitRunAcceptanceTests"`; expect pass.
- [ ] **Step 5: Commit** `feat: add Summit Run 2D acceptance game` with only source project files and tests.

### Task 7: Closed-Loop Runtime, Player, and Package Proof

**Files:**
- Output: `Examples/SummitRun/Captures/`
- Output: `Examples/SummitRun/Builds/`

**Interfaces:**
- Consumes: standard Rekall runtime and workflow commands.
- Produces: strict runtime evidence, gauntlet result, Vulkan Player proof, package audit, and packaged capture.

- [ ] **Step 1: Run `rekall.runtime.inspect_scene` after the final mutation.** Use fixed 60 Hz frames and final semantic action payload. Require strict chassis-transform and `Game.SummitRunState` assertions; never weaken a failure merely to pass.
- [ ] **Step 2: Run `rekall.workflow.agent_authoring_gauntlet`.** Follow returned next actions until validation, module build, runtime proof, capture, package, and audit are green.
- [ ] **Step 3: Launch the production Vulkan Player.** Use computer control to test throttle, lean, and reset. Inspect the window/logs for smooth edges, camera follow, readable terrain, wheel motion, HUD updates, and recovery.
- [ ] **Step 4: Capture production and packaged frames.** Open the PNGs and reject fallback markers, blank view, clipped HUD, or debug-overlay dependence.
- [ ] **Step 5: Audit the package.** Reproduce any generic packaging omission in a focused test before fixing it.

### Task 8: Final Verification and Independent Review

**Files:**
- Modify only for confirmed review findings: exact affected source/tests.

**Interfaces:**
- Consumes: all implementation commits and artifacts.
- Produces: fresh verification, reviewed diff, and an evidence-backed delivery summary.

- [ ] **Step 1: Run fresh verification.** Run the two focused engine/example test classes, any exact Studio test classes changed, and `dotnet build src/Rekall.Age.Studio/Rekall.Age.Studio.csproj --no-restore`; record exit codes/test counts.
- [ ] **Step 2: Inspect scope.** Run `git status --short`, `git diff --check` for changed committed ranges, and `git log --oneline -12`; confirm unrelated dirty files remain untouched.
- [ ] **Step 3: Request independent review.** Ask the existing reviewer to inspect the complete commit range for correctness, regressions, generic architecture, test quality, and whether evidence proves playable behavior. Fix confirmed findings test-first.
- [ ] **Step 4: Report accurately.** Include project path, improvements, exact tests/acceptance commands, capture/package paths, residual limitations, and whether one-prompt authoring finished unaided or needed generic repair.
