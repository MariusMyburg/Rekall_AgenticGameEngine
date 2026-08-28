# Runtime performance investigation — where the frame actually goes

Date: 2026-08-28
Status: Findings, plus recommendation 1 implemented and verified.
See "Outcome of recommendation 1" at the end.

## Summary

The interactive player is **100% CPU-bound with an idle GPU**. Rendering — the
draw loop, state changes, and submission — accounts for **under 2% of the frame**.

The frame is dominated by two things, neither of which is rendering:

1. **40%** — simulation (runtime execution loop + Bepu physics).
2. **38% of `RenderFrame` (~29% of wall time)** — re-parsing authored mesh
   geometry out of live JSON *every frame*, plus the knock-on cost of a
   geometry cache whose key is recomputed from scratch every frame because the
   re-parse destroys the object identity the memo table is keyed on.

The single highest-value fix is to memoize parsed geometry on the component's
`JsonObject` identity. That one change targets roughly **29% of wall time** and
requires no new subsystem.

Classic renderer optimizations — backface culling, frustum culling, occlusion
culling, depth prepass, instancing, GPU-driven/indirect rendering — would buy
approximately **nothing today**, because the GPU is not the constraint. They
become relevant only after the CPU side is fixed and scenes get much heavier.

## Method

Scene: `Examples/MidnightRider`, `Main` — the densest example available
(procedural roadside trees). 46 renderables, 14,260 vertices.

```
Rekall.Age.Player.Windows.exe Examples\MidnightRider Main \
  --graphics --backend vulkan --no-vsync --debug-hud --frames 1200
```

Two measurements:

- The player's own frame profiler (`RecordFrameProfile`), which splits the
  frame into `simulation / frameBuild / packet / ui / submit`.
- A `dotnet-trace` sampling profile (`Microsoft-DotNETCore-SampleProfiler`),
  aggregated into an inclusive call tree over the render thread.

**Vsync must be disabled for any of this to mean anything.** It is on by
default (`Program.cs:92`). A first run without `--no-vsync` reported a flat 120
FPS — exactly the display refresh rate — which is unfalsifiable as a baseline.

## Measurement 1 — the frame is CPU-bound

Steady-state averages at 1280×720, ~170–210 FPS:

| Bucket | ms | share |
|---|---|---|
| `simulation` | 3.00 | 53% |
| `frameBuild` | 1.90 | 34% |
| `packet` | 0.43 | 8% |
| `ui` | 0.08 | 1% |
| `submit` | 0.24 | 4% |
| **total** | **~5.65** | |

5.65 ms of measured CPU work predicts ~177 FPS, and observed FPS is ~170–210.
**The CPU time fully accounts for the frame time** — there is no hidden GPU
stall hiding behind the async submit.

### The GPU is idle — proof

Re-running with `--ssaa 3` renders the scene at 3840×2160 internally — **9× the
pixel count** — and presents it downsampled.

FPS was **unchanged** (169–213 vs. 172–237).

The supersample factor was verified to actually apply:
`CreateSceneRenderTarget` (`Program.cs:3194`) sizes the target as
`displayWidth * sceneSupersampleFactor`. (No "Recreated supersampled scene
target" line appears in the log for this run because that message only fires on
a *window resize*, not on initial creation — its absence is expected and is not
evidence the factor was ignored.)

Nine times the rasterization, shading, and blending work costs nothing
measurable. The GPU has enormous headroom at this scene complexity.

**Independent corroboration:** `vkQueuePresentKHR` is only 3.66% of the sampling
trace. A saturated GPU would back-pressure the presentation queue and inflate
that number substantially. Two independent signals agree.

**What does *not* prove this:** the `submit` bucket. With vsync off and no fence
wait, `SubmitCommands`/`SwapBuffers` return without the GPU having finished, so
`submit` measures command recording, not GPU execution. It was 0.23 ms in both
runs — identical to two decimals across a 9× pixel change — precisely because it
never observed the GPU at all. The pixel-count experiment and the present-queue
share are the real evidence; `submit` is not.

## Measurement 2 — where the CPU time goes

Inclusive call tree under `RenderFrame` (4335 ms of 5757 ms wall):

```
 40.77%  AdvanceSimulationToWallClock
   40.54%    RekallAgeRuntimeSimulationClock.AdvanceToAsync
     39.96%      RekallAgeRuntimeExecutionLoop.RunAsync
 35.95%  RekallAgeRuntimeRenderFrameBuilder.Build
   31.99%    BuildRenderables
     28.36%      ReadGeometryMesh              <-- JSON re-parse, every frame
       12.28%        CreateGeometryVertices
          7.29%          InferNormals          <-- recomputed every frame
        5.09%        ReadNumber / TryGetPropertyValue
      2.59%      RekallAgeRuntimeWorldTransformResolver.Resolve
    3.08%    BuildColliderDebugRenderables     (--debug-hud only)
 12.53%  GetRenderPacket
    9.98%    CreateGeometryCacheKey            <-- cache key costs more than the cache saves
      7.54%      RekallAgeRuntimeGeometrySignature.For
        4.57%        Monitor.Enter_Slowpath    <-- lock contention
    2.45%    BuildRenderPacket
  3.74%  GraphicsDevice.SwapBuffers
  1.84%  UpdateHudTexture                      (--debug-hud only)
  1.68%  DrawScenePacket                       <-- ALL actual rendering
    1.55%    CommandList.DrawIndexed
```

Note ~5% of the above (`BuildColliderDebugRenderables`, `UpdateHudTexture`) only
runs because `--debug-hud` was needed to enable the profiler. A shipping frame is
correspondingly cheaper, which makes the rendering share even smaller.

## Root cause: geometry is re-parsed from JSON every frame

`RekallAgeRuntimeRenderFrameBuilder.ReadGeometryMesh`
(`RekallAgeRuntimeRenderFrameBuilder.cs:1855`) walks the `Rekall.GeometryMesh`
component's `JsonArray` of vertices and indices and builds a **brand-new**
`RekallAgeRuntimeViewportGeometryMesh` on every frame — reading each coordinate
through `JsonValue` boxing, re-inferring normals, and re-parsing colors, for
geometry that has not changed.

This then poisons the cache that was supposed to protect against it:

- `RekallAgeRuntimeGeometrySignature.For` memoizes a mesh's content hash in a
  `ConditionalWeakTable` **keyed on object identity**
  (`RekallAgeRuntimeGeometrySignature.cs:8-15`).
- Because `ReadGeometryMesh` yields a *new object every frame*, that memo
  **never hits**. The full hash is recomputed over every vertex and index,
  every frame.
- Each miss takes the table's write lock, so `Monitor.Enter_Slowpath` alone is
  4.6% of the frame.

The downstream geometry cache does hit (`geometryCache=120h/0m`) — but the key
used to prove the hit costs more than the vertex upload it avoids.

So this is one defect with three symptoms. Everything below `ReadGeometryMesh`
in the tree is a consequence of it.

### Why the fix is safe

`RekallAgeRuntimeComponent` is `record(string Type, JsonObject Properties)`
(`RekallAgeRuntimeContracts.cs:34`), and the runtime carries untouched
components through by reference — `RekallAgeBepuPhysicsSystem.UpsertPhysicsState`
replaces only the physics-state component and preserves the rest of the list.
So a static mesh's `JsonObject` identity **is** stable across frames.

That makes a `ConditionalWeakTable<JsonObject, ParsedMesh>` in front of
`ReadGeometryMesh` self-invalidating and correct by construction: if a module
mutates the component, it produces a new `JsonObject`, which misses and
re-parses — exactly the right behavior, with no manual invalidation.

## Recommendations, in priority order

### 1. Memoize parsed geometry on component identity — ~29% of wall time

Wrap `ReadGeometryMesh` in a `ConditionalWeakTable<JsonObject, …>`. This kills
the 28%-of-`RenderFrame` re-parse *and* restores the existing signature memo
(a further ~10%, including the lock contention), because parsed meshes regain
stable identity.

Highest value, smallest change, no new subsystem, no content risk.

The projected saving assumes the memo actually hits for MidnightRider's trees.
That is expected from the identity analysis above, but it is a prediction —
confirm it with one profiled run once implemented rather than assuming it.

### 2. Attack the simulation — ~40% of `RenderFrame`

Now the largest single bucket, and untouched by this investigation.

Note this is **per-step work reported per frame**: `AdvanceToAsync` runs a
variable number of fixed simulation steps per frame, and the 120-frame windows
ranged from **1.20 ms to 3.24 ms** — a 2.7× spread consistent with a varying
step count. The per-frame average is therefore not a per-step cost, and the
real per-step figure could be materially better or worse than it suggests.

Before optimizing anything here, log steps-per-frame so the cost is attributed
correctly, then profile the module execution loop separately from Bepu's solve.

### 3. Cheap per-frame churn in the render path — small but nearly free

All in `Program.cs:RenderFrame`:

- `new RekallAgeVulkanHighFidelityFrameRenderer()` allocated **every frame**
  (`:1768`), with shadow/fog/AO re-planned from scratch each frame.
- A LINQ `OrderBy`/`ThenBy` over quality profiles every frame (`:1748`).
- `uiVertices.Concat(hudVertices).ToArray()` every frame (`:1875`).

### 4. Draw-loop hygiene — correct, but currently worth ~1%

Do these when they become measurable, not now:

- One `UpdateBuffer` per draw per frame (`:1828`) into a stride-addressed
  buffer that could take a single contiguous write.
- `SetGraphicsResourceSet(0, _frameSet)` is invariant across the pass but is
  re-set for every draw (`:2112`) — hoist it.
- No sorting by pipeline/material; every draw re-sets pipeline + 3 resource
  sets (`:2111-2118`).

### 5. Backface culling — a content decision, not an optimization

All five pipelines use `RasterizerStateDescription.CullNone`
(`Program.cs:648-682`), and the codebase assumes both windings render
(see the comment at `RekallAgeRuntimeRenderFrameBuilder.cs:2184`).

Flipping this globally risks making authored meshes with inverted winding
render inside-out or vanish. Per `AGENTS.md`'s generic-primitives rule, the
engine-shaped change is a **per-material cull-mode property** with a
deliberate default, plus a diagnostic when winding looks inverted — verified
visually against the examples, not just by a clean build.

It would also currently save nothing, because the GPU is idle.

### Not yet: frustum culling, occlusion culling, LOD, GPU-driven rendering

- **Frustum culling** needs per-renderable world bounds, which
  `RekallAgeVulkanSceneBatchBuilder` does not compute today (it derives only a
  single whole-scene `SceneBounds` for camera fallback). It also cannot help
  while the cost is *building* the frame rather than drawing it — culling
  happens downstream of the 28% re-parse.
- **Occlusion culling / GPU-driven indirect rendering** would be a rewrite of
  the per-draw dynamic-offset uniform pattern, aimed at a GPU that is currently
  doing nothing.

Revisit all of these once the CPU side is fixed and a genuinely heavy scene
exists to justify them.

## A note on the framing

The Alan Wake comparison is not the useful yardstick — that is a bespoke
deferred renderer with a decade of tuning. The honest finding here is narrower
and more actionable: **this engine is not slow at rendering. It is slow at
deciding what to render**, and it re-derives that decision from JSON on every
single frame.

## Reproducing

```powershell
dotnet build src\Rekall.Age.Player.Windows\Rekall.Age.Player.Windows.csproj -c Release
dotnet tool install --global dotnet-trace

# frame-bucket profile (log: %LOCALAPPDATA%\Rekall AGE\Player\Logs\)
.\src\Rekall.Age.Player.Windows\bin\Release\net10.0-windows\Rekall.Age.Player.Windows.exe `
  .\Examples\MidnightRider Main --graphics --backend vulkan --no-vsync --debug-hud --frames 900

# GPU-headroom check: 9x the pixels, expect unchanged FPS
#   ...same command plus --ssaa 3

# sampling profile
dotnet-trace collect --format Speedscope --providers Microsoft-DotNETCore-SampleProfiler `
  -- <player.exe> .\Examples\MidnightRider Main --graphics --backend vulkan `
     --no-vsync --debug-hud --frames 1200
```

Caveat: `--debug-hud` is required to enable `RecordFrameProfile`, and it adds
its own ~5% (collider debug renderables + HUD texture upload). Numbers here are
therefore slightly pessimistic relative to a shipping frame.

---

# Outcome of recommendation 1

Implemented: `ReadGeometryMesh` now memoizes parsed geometry in a
`ConditionalWeakTable<JsonObject, …>` keyed on the component's properties
object. Covered by `RuntimeGeometryMeshReuseTests`.

Verified safe before writing it: `RekallAgeRuntimeModuleSdk.UpdateComponent`
(`:532`) calls `.Properties.DeepClone()` before handing properties to a mutator,
so every SDK mutation path yields a *new* `JsonObject`. A changed component
therefore misses the cache and is re-read. No manual invalidation exists or is
needed.

## Correctness verification

**No in-place mutation path exists.** The risk with keying on `JsonObject`
identity is a writer that mutates a *nested* node — e.g.
`Properties["vertices"].AsArray()[0]["y"] = …` — leaving the keyed object's
identity intact and serving stale geometry forever. Audited and ruled out:

- Every write to `["vertices"]` / `["indices"]` is in an authoring command
  (`CreateGeometryMeshCommand`, `CreateGeometryExtrusionCommand`) or the GLB
  exporter, and all construct **new** `JsonObject`s.
- The runtime only ever *reads* `Rekall.GeometryMesh`
  (`RekallAgeBepuPhysicsSystem`).
- The only nested-array writer in `src` is `RekallAgeReversibleJsonDelta`, which
  operates on persisted **scene documents**, not live runtime components.
- The player's live-edit path (`apply_scene_diff`, `reload_scene`) routes
  through `ApplySceneDocument`, which rebuilds the entire runtime world from the
  document — new components, new identities.

**Rendered output is byte-identical.** Captured frame 90 of MidnightRider at
640×360 with the cache enabled and with it bypassed:

```
before  Main_runtime_090.png  6931 bytes  sha256 44687062876888ad…
after   Main_runtime_090.png  6931 bytes  sha256 44687062876888ad…
```

Identical hash — the change is provably invisible to the renderer. MidnightRider
is a good subject here because it spawns trees with fresh `JsonObject`s as road
chunks recycle, so new geometry enters the world continuously during the run.

Tests: `RuntimeGeometryMeshReuseTests` (new), plus
`Rekall.Age.Tests.Rendering` (736), `Rekall.Age.Tests.Runtime` (322), and
`Rekall.Age.Studio.Tests` (110) all pass.

## Measured result

Same scene and command as above (MidnightRider, 1280×720, vsync off):

| Bucket | before | after |
|---|---|---|
| `simulation` | 3.00 | 0.47 – 1.03 |
| `frameBuild` | 1.90 | **0.29** |
| `packet` | 0.43 | **0.18** |
| `ui` | 0.08 | 0.01 |
| `submit` | 0.24 | 0.21 |
| **total** | **~5.65 ms** | **~1.75 ms** |

**FPS: ~180 → ~640–710.**

In the sampling profile, `ReadGeometryMesh` and everything under it
(`CreateGeometryVertices`, `InferNormals`, `ReadNumber`) is **gone entirely** —
it does not appear in the trace at all. `RenderFrameBuilder.Build` fell from
35.95% to 19.09% of `RenderFrame`.

## The `simulation` drop is an artifact — do not credit it to this change

The per-frame `simulation` bucket fell from 3.00 ms to ~0.7 ms, but **no
simulation work got faster.** Normalizing against wall time:

- before: 40.77% of 4335 ms, over 5757 ms wall = **30.7% of wall**
- after: 41.97% of 3861 ms, over 5284 ms wall = **30.7% of wall**

Identical. `AdvanceToAsync` runs fixed-size steps to catch up to the wall clock,
so it performs a fixed amount of work *per second*, not per frame. Shorter
frames simply mean fewer steps per frame. This is exactly the per-step /
per-frame confusion flagged in recommendation 2, now confirmed empirically:
**simulation does not limit frame rate — it consumes a constant ~31% of CPU.**

The genuine saving is `frameBuild` + `packet`: **2.33 ms → 0.47 ms per frame.**

## What is left, and one correction

`CreateGeometryCacheKey` only fell from 9.98% to 6.75%, and
`Monitor.Enter_Slowpath` is still 6.55%. The residual is **not** from meshes —
it is `BuildColliderDebugRenderables` rebuilding wire capsules every frame,
which produces fresh `LineSegments` objects that miss the identity-keyed
signature memo. That path is `--debug-hud`-only and does not exist in a shipping
frame, so it is an artifact of the measurement setup rather than a real cost.
Worth fixing only if the debug HUD's own overhead becomes a problem.

Remaining shares of `RenderFrame` after the change: simulation 42%,
frame build 19% (about half of it debug-HUD collider wireframes),
packet 13%, present 12%, draw submission 4.7%.

Revised priorities:

1. ~~Memoize parsed geometry~~ — done.
2. The simulation is now the dominant cost, but it is a constant ~31% of CPU
   rather than a per-frame tax. Optimizing it raises the CPU ceiling for
   heavier scenes; it will not raise FPS on a scene this light.
3. Recommendations 3–5 and the deferred renderer work are unchanged, and are
   even less urgent now: draw submission is 4.7% of a frame that is 3× shorter.
