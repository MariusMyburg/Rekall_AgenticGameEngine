# Studio 3D Viewport Navigation Design

**Status:** Approved for implementation (user pre-approved; async execution).

## Context

Studio's modeling workspace (`ModelingWorkspace.xaml` + `RekallAgeStudioMeshViewportRenderer`)
renders the edited mesh through a fixed axonometric projection with no
camera state at all:

```csharp
private static Point Project(RekallAgeGeometryVector3 point) =>
    new((point.X - point.Z) / Math.Sqrt(2), (point.X + point.Z - 2 * point.Y) / Math.Sqrt(6));
```

There is no orbit, pan, zoom, or perspective/orthographic toggle anywhere in
`RekallAgeStudioViewModel`. The view auto-fits the mesh bounds and never
changes angle. This blocks every other modeling-parity gap identified in the
2026-08-26 audit (procedural node-graph canvas, direct-manipulation mesh
tools, animation rig preview) because none of them are usable if you can't
look at the geometry from more than one fixed angle.

This is the first of several planned sub-projects toward Studio exposing
everything `Rekall.Age.Modeling` supports with Blender-comparable feel. It is
scoped narrowly and deliberately: **camera navigation only**. Visual
node-graph editing, modal direct-manipulation tools (extrude/bevel/inset by
dragging), and the animation workspace are separate future sub-projects and
are out of scope here.

## Goal

Give the mesh viewport a real orbit camera — mouse-driven orbit, pan, and
zoom, plus an orthographic/perspective toggle — using conventions close to
Blender's defaults so the feel is familiar, without disturbing any existing
picking, gizmo, or preview behavior.

## Approach

Add an immutable `RekallAgeStudioViewportCamera` record (yaw, pitch, distance,
pan offset X/Y, orthographic flag) as an **optional** parameter to
`RekallAgeStudioMeshViewportRenderer.Render(...)`, defaulting to a camera
equivalent to today's fixed axonometric view. This means:

- Every existing call site and existing test keeps compiling and passing
  unchanged (default camera reproduces today's exact projection).
- `Project(...)` becomes a camera-aware method: rotate the point by
  yaw/pitch, then either apply a perspective divide (perspective mode) or an
  orthographic scale (ortho mode, the default and closest to today's look).
- Picking (`ElementCenters`), the transform gizmo, and preview coloring are
  unaffected because they already flow through `Project(...)`.

Camera state lives in `RekallAgeStudioViewModel` as mutable
`ViewportYaw`/`ViewportPitch`/`ViewportDistance`/`ViewportPanX`/`ViewportPanY`/
`ViewportOrthographic` properties, one set per open mesh session (reset when
a different mesh asset is opened, preserved across re-renders of the same
mesh so edits don't reset the view).

### Input bindings (Blender defaults, adapted to a single mouse + keyboard)

- **Middle-mouse drag**: orbit (yaw/pitch).
- **Shift + middle-mouse drag**: pan.
- **Scroll wheel**: dolly zoom (distance).
- **Left-mouse drag** keeps its current meaning: gizmo drag if a gizmo axis
  is hit, element selection otherwise. Orbit never steals a left-drag, so
  editing gestures are never ambiguous with navigation.
- **Period key (`.`) / a "Frame Selected" button**: re-centers pan/distance
  on the current selection (falls back to full mesh bounds when nothing is
  selected) — Blender's numpad-`.` equivalent.
- **`5` key / a toggle button**: switches orthographic/perspective.

`ModelingWorkspace.xaml.cs` gains the middle-drag/scroll handlers alongside
the existing left-drag handlers already there for the gizmo. WPF's `Image`
control receives `MouseWheel` and middle-button events the same way it
already receives left-button events, so this follows the exact pattern
already in the file — no new interaction infrastructure.

### Why not switch to the full runtime renderer (perspective + lighting + PBR)

`RekallAgeStudioPreviewSession` already runs the real scene renderer
(`RekallAgeRuntimeRenderFrameBuilder` + `RekallAgeRuntimeSoftwareRenderer`)
for scene/game preview, and it's tempting to point mesh editing at the same
pipeline for "free" lit/shaded preview. Rejected for this slice because it
would require synthesizing a throwaway scene/entity/material/lighting rig
around whatever mesh is being edited, and re-deriving picking regions from
that pipeline's projection matrices instead of the simple, already-tested
`ElementCenters` approach — a materially larger change than "add a camera."
Lit/shaded mesh preview is a reasonable fast-follow once this camera exists,
since the same yaw/pitch/distance state could drive either projection.

## Testing

- New unit tests in `StudioMeshViewportTests.cs` for the camera-aware
  `Project`: identity camera reproduces today's exact axonometric output
  (regression-proves the default-parameter compatibility claim); a 90°
  yaw rotation moves a known point to its expected projected location;
  zoom (distance) scales the projected spread; pan offsets recentre it;
  orthographic vs. perspective produce different results for points at
  different depths.
- A view-model-level test proving `Render` is invoked with the session's
  current camera state after an orbit/pan/zoom mutation, and that opening a
  different mesh asset resets the camera while re-rendering the same mesh
  preserves it.
- Existing `StudioMeshViewportTests`/`StudioViewportInteractionTests` must
  keep passing unmodified (proves no regression to picking/gizmo/preview).

## Non-goals (tracked for later sub-projects, not this one)

- Visual node-graph canvas (add/wire/reposition nodes).
- Modal direct-manipulation mesh tools (drag-to-extrude/bevel/inset).
- Materials/UV panel depth audit against `RekallAgeMaterialGraphContracts`.
- Animation workspace (rig/clip preview, mixer-layer editor) — sub-project 1,
  deferred pending this camera work per the user's own reordering.
