# Studio Live Preview and Ergonomics Design

## Goal

Make Rekall AGE Studio a credible daily authoring surface before the Galaga
acceptance: a persistent live edit viewport, an in-editor simulation mode, a
clearly separate external play mode, and one coherent modern dark theme.

## Product decisions

- Edit, Simulate, and Play are explicit mutually exclusive editor modes.
- Edit mode automatically renders the current authored scene after successful
  mutations. The existing Capture action remains an explicit proof-frame tool.
- Simulate owns an in-memory runtime world and execution loop. It advances the
  same generic runtime systems used by games at 60 fixed steps per second and
  renders a 10 Hz Studio preview without writing simulation state to the scene.
- Stop exits either Simulate or Play and returns to a clean Edit preview.
- Play remains the windowed production Player process so keyboard, mouse,
  Vulkan, audio, custom shaders, and later OpenXR use the real runtime path.
  Studio must label this honestly rather than implying embedded play-in-editor.
- A Live toggle controls automatic preview refresh and is enabled by default.
- Simulation and live preview are generic engine capabilities; no game loop,
  controller, genre, or Galaga-specific behavior belongs in Studio.

## Architecture

`RekallAgeStudioPreviewSession` owns the preview runtime world, project runtime
execution loop, frame builder, asset resolver, and software renderer. It can
reset from authored scene state, step a bounded number of frames, and return an
immutable frozen WPF bitmap plus structured frame facts. The view model owns the
mode state and commands. `MainWindow` owns only the WPF dispatcher timer that
requests six runtime steps every 100 ms while Simulate is active.

The preview session is discarded when the project/scene changes, when Play
starts, and on disposal. It never persists its runtime world. Agent and manual
scene edits continue through the canonical workbench session and reset the
preview from disk.

## Theme

Application resources define a single neutral charcoal surface hierarchy,
Segoe UI typography, cyan accent, focus/hover/disabled states, rounded buttons,
dark editors, dark lists/trees/tabs, and restrained separators. The center
viewport gains a mode badge, Live toggle, and an intentional empty state. Raw
white default WPF control surfaces are not acceptable.

## Verification

- View-model tests prove legal mode transitions, command availability, stop
  behavior, and that Simulate is not Play.
- Preview-session tests prove persistent frame advancement and reset semantics.
- Source/style tests reject accidental loss of the shared dark control styles.
- Existing Studio automation and the full engine suite remain green.
- A running Studio window is captured and visually inspected after the build.
