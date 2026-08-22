# Studio Interaction and Layout Design

## Purpose

Make Rekall AGE Studio practical for scene construction before the Galaga
acceptance run. The milestone adds direct viewport selection and transform
editing, controllable simulation, richer transactional scene operations, and a
persistent dockable workspace while preserving the engine's generic authoring
contracts and non-destructive Simulate mode.

## Product boundaries

- Game semantics remain in agent-authored modules. Studio edits generic scene
  documents and invokes the same commands exposed to MCP clients.
- Edit-mode manipulation mutates authored scene state through transactions and
  remains undoable. Simulate and Play never mutate authored scene state.
- Viewport picking and gizmos consume the same runtime render frame as the
  preview image so camera, transforms, UI layout, and visibility agree.
- Workspace layout is a user preference, not project content. It must never be
  packaged with a game or affect deterministic runtime behavior.
- The implementation uses WPF and existing AGE projects only. It adds no
  network-restored docking dependency.

## Interaction model

### Viewport selection

The preview session returns a bounded interaction snapshot alongside its frozen
bitmap. The snapshot contains pick regions generated from visible renderables:
UI uses its clipped pixel rectangle, 2D content uses camera-aware projected
bounds, and 3D content uses camera projection plus geometry/primitive bounds.
Regions retain entity id, entity name, depth, sort key, and screen bounds.

A click is mapped from the WPF `Image`'s displayed letterboxed rectangle into
preview pixels. Selection chooses the topmost region containing the pixel,
orders UI before world content and nearer content before farther content, and
then calls the ordinary workbench entity-selection path. Empty-space clicks
clear selection. Locked entities remain selectable but cannot be manipulated.

### Transform gizmos

The toolbar exposes Select, Move, Rotate, and Scale modes plus Local/World
orientation and optional grid snapping. The first milestone supports generic
`Rekall.Transform2D` and `Rekall.Transform3D` properties:

- Move: X and Y axis handles plus a planar center handle. A 3D Z handle is
  visible and editable through projected screen motion.
- Rotate: one 2D rotation ring; 3D pitch/yaw/roll handles selected by axis.
- Scale: per-axis handles and a uniform center handle.

Pointer-down captures an immutable drag origin. Pointer movement updates a
transient overlay preview. Pointer-up commits the final values through a single
transactional component-property update, then refreshes the edit preview.
Escape cancels without mutation. Snapping is applied at commit and supports
configurable translation, rotation, and scale increments. Gizmos are disabled
outside Edit mode and for locked entities.

## Rich scene manipulation

Studio exposes generic operations for create, duplicate, rename, delete,
reparent/unparent, visibility, lock, and transform reset. Multi-selection is not
part of this milestone; all operations target the current stable entity id.

Existing create, duplicate, delete, parent, component, and transaction commands
remain authoritative. A generic `rekall.scene.entity.update_metadata` command
adds partial updates for name, visible, locked, and parent id with revision-safe
persistence, parent existence/cycle validation, structured errors, and MCP
discoverability. Studio never rewrites scene JSON directly.

Delete requires an explicit second action in the UI and defines child behavior
clearly: children are reparented to the deleted entity's parent, matching the
engine delete contract. Rename rejects blank names. Reparent rejects self and
descendant cycles. Duplicate selects the newly created entity. Visibility and
lock state are shown in hierarchy rows.

## Simulation controls

Simulate starts at authored frame zero and runs on the existing fixed-step
runtime loop. The state model adds `IsSimulationPaused`:

- Pause prevents periodic cadence calls from stepping the runtime.
- Resume continues from the same runtime world and frame index.
- Step is available only while Simulate is paused and advances exactly one
  fixed frame per invocation.
- Stop discards runtime state, returns to Edit, and rebuilds frame zero from the
  unchanged authored scene.

Live viewport controls whether automatically advanced frames are rendered; it
does not redefine Pause. Single-step always renders its resulting frame.

## Docking and layout persistence

The Studio shell is composed of named dock panels: Hierarchy, Inspector, and
Output. Each panel has a dock region (`left`, `right`, or `bottom` where
applicable), visibility, size, and order. Grid splitters resize regions. Panel
headers provide dock-location commands, hide, and restore. A View menu restores
hidden panels, applies Default/Authoring/Debug presets, and resets layout.

`RekallAgeStudioLayoutStore` persists a versioned JSON document atomically under
the user's local application-data directory. The store validates finite bounded
sizes, known panels/regions/tabs, and falls back to defaults on missing, corrupt,
future-version, or invalid data. Window bounds are restored only when they
intersect a current monitor work area. Saves are debounced and flushed during
window shutdown. Tests inject a path so they never write real user settings.

## Structure

- `RekallAgeStudioViewportInteraction.cs`: coordinate mapping, pick ordering,
  gizmo projection, drag state, and transform edit requests.
- `RekallAgeStudioLayout.cs`: immutable layout model, validation, presets, and
  atomic store.
- `RekallAgeStudioViewModel.SceneEditing.cs`: selection and transactional scene
  manipulation commands.
- `RekallAgeStudioViewModel.Simulation.cs`: pause/resume/step state transitions.
- `RekallAgeStudioViewModel.cs`: shared state and orchestration.
- `MainWindow.xaml/.cs`: dock regions, overlay visuals, pointer routing,
  keyboard cancellation, and layout lifecycle.
- `UpdateEntityMetadataCommand.cs`: portable generic metadata mutation.

## Failure handling

Picking an obsolete entity id refreshes the model and reports a bounded Studio
observation. Failed drag commits leave the authored scene untouched and restore
the last rendered frame. Layout read/write failures never prevent Studio from
opening and surface a non-blocking status. Preview stepping remains serialized;
Pause and Step cannot overlap a cadence advance or mode transition.

## Verification

- Unit tests hand-check letterbox coordinate mapping, overlapping pick order,
  locked manipulation, 2D/3D transform deltas, snapping, and drag cancellation.
- Command tests prove metadata updates, optimistic revision behavior, parent
  cycle rejection, and transaction history.
- View-model tests prove duplicate/delete/rename/visibility/lock/reparent flows,
  pause preventing cadence advance, exact one-frame stepping, Stop reset, and
  mode command guards.
- Layout tests prove round-trip, corrupt/future fallback, clamping, presets, and
  atomic replacement.
- XAML/UI smoke tests and real Windows inspection prove selection, gizmo drag,
  dock resize/location, persistence across restart, pause, step, and reset.
- The complete Studio and engine suites plus Debug and Release solution builds
  must pass without warnings before merge.

