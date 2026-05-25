# Asset-Backed Viewport Rendering Design

Date: 2026-05-25

## Context

Rekall AGE now has a runtime-backed viewport frame and deterministic software capture command. That slice made screenshots, CLI, MCP, and Studio read from `RekallAgeRuntimeWorld` render projections instead of scanning authoring scenes directly.

The current renderer still draws diagnostic markers for sprites, meshes, lights, and UI layers. The master design calls for basic asset import, basic 2D rendering, visual validation, and screenshots as first-class parts of the agent loop. The next smallest production step is to let sprite renderables use imported image assets when those assets are available, while keeping deterministic fallback output when they are not.

## Goal

Build **Asset-Backed Viewport Rendering 1**: runtime software viewport captures should resolve `Rekall.SpriteRenderer` asset IDs through the project asset catalog, decode supported PNG assets, and draw those pixels into the captured frame.

## Non-Goals

- GPU sprite batching
- material graphs
- atlas packing
- texture compression
- filtering modes beyond nearest-neighbor software scaling
- arbitrary image format support
- animation clips or sprite sheets
- live WPF image streaming

Those are follow-up slices once the asset-backed frame path is stable.

## Recommended Approach

Use the existing runtime viewport frame and add a renderer-local texture path.

Alternatives considered:

1. **Draw markers only and improve metadata.** This is easy but does not advance visual validation toward real assets.
2. **Build a larger asset cooking pipeline first.** This is architecturally tempting, but it delays the feedback loop and risks overbuilding before the viewport has a concrete consumer.
3. **Add a small PNG-backed texture resolver now.** This is the recommended path. It gives agents visible proof that imported assets influence screenshots, keeps the renderer deterministic, and creates a clean contract for later asset cooking.

## Architecture

`Rekall.Age.Rendering` gains internal software texture support:

- `RekallAgeRgbaImage` stores decoded width, height, and RGBA pixels.
- `RekallAgePngReader` decodes simple non-interlaced PNG files:
  - color type 6 RGBA, 8-bit
  - color type 2 RGB, 8-bit
  - PNG filters 0 through 4
  - multiple `IDAT` chunks
- unsupported or invalid PNGs return structured load failures rather than crashing viewport capture.

`RekallAgeRuntimeSoftwareRenderer` should remain independent of project storage. It receives a decoded asset set keyed by asset ID and decides per renderable:

- sprite with decoded image: draw image pixels into the frame
- sprite with missing/unsupported image: draw the existing deterministic fallback marker
- mesh, light, and UI: draw the existing deterministic fallback markers for this slice

The capture path reports:

- total renderables
- asset-backed renderables
- fallback renderables
- missing asset count
- unsupported asset count
- short asset issue codes

`CaptureRuntimeViewportCommand` is the project-aware adapter. It loads `Assets/assets.age.catalog.json`, decodes supported sprite assets, passes them to the renderer, and returns asset-backed capture metadata. The legacy `rekall.capture.screenshot` path may use the same resolver internally, but its public result remains compatible.

## Rendering Behavior

Sprite images are drawn with nearest-neighbor sampling. The first implementation should:

- use renderable `X` and `Y` plus stable entity ID hashing to choose a deterministic viewport anchor
- preserve aspect ratio
- scale tiny sprites up to a visible minimum size
- clamp very large sprites to a bounded preview size
- alpha-blend RGBA source pixels over the deterministic background
- keep debug overlay drawing last

This intentionally produces a useful proof image, not a final art-accurate renderer.

## Error Handling

Viewport capture must not fail because one sprite asset is missing or unsupported. It should:

- draw the fallback marker
- include an asset issue code such as `REKALL_RENDER_ASSET_MISSING` or `REKALL_RENDER_ASSET_UNSUPPORTED`
- keep the PNG nonblank and deterministic

Hard failures remain appropriate for invalid output paths, invalid dimensions, and scene loading failures.

## CLI And MCP

`rekall.render.capture_runtime_viewport` remains the command name.

The CLI route stays:

```powershell
dotnet run --project src/Rekall.Age.Cli -- render viewport capture .age-sandbox Main 3 .age-sandbox/Artifacts/Viewport
```

CLI output should add asset-backed renderable counts and issue codes when present. MCP visibility is already command-catalog based and should continue to expose the expanded result schema.

## Studio

No live image streaming is required in this slice. Studio metadata may continue showing frame, camera, renderable, and observation counts. Asset-backed capture metadata becomes available through the viewport capture tool result for later UI work.

## Tests

Required coverage:

- PNG reader decodes RGBA PNGs produced by `RekallAgePngWriter`
- software renderer draws decoded sprite pixels into a runtime viewport capture
- runtime viewport capture command resolves imported sprite assets from the asset catalog
- missing or unsupported sprite assets fall back to deterministic markers and report issue codes
- CLI route prints asset-backed counts
- legacy screenshot command still returns the same compatible result shape

## Acceptance Criteria

- A scene with a `Rekall.SpriteRenderer` referencing an imported PNG asset produces a viewport PNG containing pixels from that asset.
- `rekall.render.capture_runtime_viewport` reports asset-backed and fallback renderable counts.
- Missing or unsupported sprite assets do not abort capture and are reported structurally.
- Existing runtime viewport, screenshot, MCP, Studio read-model, and CLI behavior remains compatible.
- Full solution build and test suite pass.

## Self-Review

- Placeholder scan: no incomplete implementation markers remain.
- Consistency check: project-aware asset catalog loading stays in the command/adapter layer; the renderer consumes decoded images.
- Scope check: this slice is limited to PNG-backed sprite rendering in the deterministic software renderer.
- Ambiguity check: unsupported assets fall back with issue codes instead of failing capture.
