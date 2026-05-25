# Rekall AGE Runtime Viewport Rendering Design

Date: 2026-05-25

## Purpose

Scene Runtime Foundation made runtime snapshots canonical, but the current preview path still renders from authoring scene documents and the Studio viewport still summarizes project data. The next production slice should make the viewport and screenshot loop consume runtime render projections.

This design defines **Runtime Viewport Rendering 1**. It does not attempt a full GPU renderer. It creates the renderer-facing frame contract and a deterministic software renderer that draws from `RekallAgeRuntimeWorld`, so CLI, MCP, tests, and Studio all inspect the same runtime state.

## Master Design Alignment

This design extends:

- `docs/superpowers/specs/2026-05-24-rekall-age-agentic-engine-design.md`
- `docs/superpowers/specs/2026-05-25-rekall-age-production-workbench-design.md`
- `docs/superpowers/specs/2026-05-25-rekall-age-scene-runtime-foundation-design.md`

The same constraints remain:

- authoring files are not mutated by viewport or play-mode preview
- renderer state is derived from runtime snapshots
- CLI, MCP, Studio, and tests share command-backed services
- diagnostics and captures are compact, structured, and agent-readable

## Why This Slice

Runtime data now knows cameras, sprites, meshes, lights, UI layers, frame index, and observations. The old software preview ignores that and draws markers from raw scene order. A production editor needs a viewport frame model that can eventually target software, Vulkan, Direct3D, and headless capture without changing tool APIs.

Runtime Viewport Rendering 1 gives later renderer work a stable contract:

- capture requests can specify frame count, size, and debug overlay intent
- active camera selection comes from the runtime render view
- visible renderable counts come from runtime projections
- screenshots record the runtime frame index and observations
- Studio can show live runtime viewport readiness instead of a static scene summary

## Scope

Runtime Viewport Rendering 1 should include:

- runtime render frame records in `Rekall.Age.Rendering.Abstractions`
- a runtime-to-render-frame builder in `Rekall.Age.Rendering`
- a deterministic software runtime renderer that draws from runtime render views
- screenshot capture backed by runtime snapshots rather than direct scene scans
- a new command `rekall.render.capture_runtime_viewport`
- CLI route `render viewport capture <root> <scene> <frames> <outputDirectory>`
- MCP exposure through ordinary command registration
- Studio read-model fields for viewport camera, frame index, renderable counts, and last capture metadata
- tests proving runtime render projection drives captures and viewport models

It should defer:

- real textured sprite sampling
- real mesh rasterization
- GPU viewport swapchains
- WPF image streaming or live play controls
- camera matrices and clipping
- asset decoding beyond metadata references

## Alternatives Considered

### WPF Viewport Polish First

This would improve the editor's look quickly, but the viewport would still be a display shell without a renderer-backed frame contract.

### Vulkan Renderer First

This would move toward high-end rendering, but Vulkan needs a frame model and renderable contract. Building GPU capture before runtime-backed frame construction would duplicate classification logic.

### Runtime Viewport Frame First

This is the recommended path. A renderer-facing runtime frame and deterministic software renderer make screenshots, Studio, and future GPU renderers share the same source.

## Architecture

Add rendering contract records to `Rekall.Age.Rendering.Abstractions`:

```text
RekallAgeRuntimeViewportFrame
RekallAgeRuntimeViewportCamera
RekallAgeRuntimeViewportRenderable
RekallAgeRuntimeViewportOverlay
RekallAgeRuntimeViewportCapture
```

Add concrete services to `Rekall.Age.Rendering`:

```text
RekallAgeRuntimeRenderFrameBuilder
RekallAgeRuntimeSoftwareRenderer
Commands/CaptureRuntimeViewportCommand
```

The service flow should be:

```text
load scene -> build runtime world -> run N fixed frames -> build render frame -> software render PNG -> return capture report
```

The old `rekall.capture.screenshot` command should remain working. It may delegate to the runtime renderer internally once compatibility fields remain stable.

## Render Frame Model

`RekallAgeRuntimeViewportFrame` should contain:

- scene name
- frame index
- elapsed seconds
- output width and height
- active camera
- cameras
- renderables
- lights
- UI layer count
- observations

Renderable records should normalize sprites, meshes, lights, and UI layers into stable draw candidates with:

- entity id
- entity name
- kind
- asset id when available
- 2D and 3D transform summaries
- sort key

This is intentionally metadata-rich and raster-light. It should be useful to agents before real material and asset decoding exists.

## Software Runtime Renderer

The first renderer should be deterministic and nonblank:

- background color should vary by scene name and frame index
- active camera should affect frame metadata
- sprite renderables should draw distinct 2D markers
- mesh renderables should draw distinct 3D-style markers
- lights should draw small bright markers
- UI layers should draw top-band outlines
- optional debug overlay should encode counts in pixel-safe bands rather than text

The renderer should write PNGs through the existing `RekallAgePngWriter`.

## Commands

Add `rekall.render.capture_runtime_viewport`.

Request:

```json
{
  "projectRoot": ".age-sandbox",
  "sceneName": "Main",
  "frames": 3,
  "outputDirectory": ".age-sandbox/Artifacts/Viewport",
  "width": 320,
  "height": 180,
  "debugOverlay": true
}
```

Result:

```json
{
  "captured": true,
  "screenshotPath": ".age-sandbox/Artifacts/Viewport/Main_runtime_003.png",
  "width": 320,
  "height": 180,
  "nonBlank": true,
  "frameIndex": 3,
  "activeCamera": "MainCamera",
  "renderableCount": 4,
  "observationCount": 0
}
```

CLI route:

```powershell
dotnet run --project src/Rekall.Age.Cli -- render viewport capture .age-sandbox Main 3 .age-sandbox/Artifacts/Viewport
```

## Studio Integration

Studio should not stream images yet. It should surface runtime viewport metadata in the existing view model:

- scene name and frame index
- active camera name
- renderable count
- runtime observations
- capture command hint

This gives the editor a more truthful viewport panel while leaving real image display and play controls for the next slice.

## Testing Strategy

Tests should prove:

- render frame builder uses runtime world projections rather than raw scene scans
- active camera selection is deterministic
- runtime capture writes a nonblank PNG with the requested dimensions
- capture result includes frame index, active camera, renderable count, and observation count
- legacy `rekall.capture.screenshot` still works
- CLI route prints runtime viewport capture metadata
- MCP catalog exposes `rekall.render.capture_runtime_viewport`
- Studio view model includes runtime viewport metadata
- full build and test suite remain clean

## Acceptance Criteria

Runtime Viewport Rendering 1 is ready when:

- a runtime world can be converted into a renderer-facing viewport frame
- a runtime-backed PNG capture can be produced for a requested frame count and size
- capture output is deterministic, nonblank, and records frame metadata
- `rekall.render.capture_runtime_viewport` is available through command bus, CLI, and MCP catalog registration
- Studio viewport summaries are based on runtime viewport data
- existing screenshot command remains compatible
- tests cover frame building, capture, CLI, MCP, Studio metadata, and legacy screenshot compatibility
- full build and test suite pass

## Open Risks

- The first software renderer is diagnostic rather than visually rich. That is acceptable because this slice creates the frame contract future renderers will share.
- Renderable classification still depends on component type strings. Source-generated component metadata should later replace convention checks.
- WPF image display is intentionally deferred so the data contract can settle before UI wiring.

## Spec Self-Review

- Marker scan: clean.
- Consistency check: rendering now consumes runtime snapshots and keeps authoring files immutable.
- Scope check: this slice is focused on viewport frame contracts and deterministic software capture, not GPU rendering or live editor play mode.
- Ambiguity check: command names, output fields, deferred work, and acceptance criteria are explicit.
