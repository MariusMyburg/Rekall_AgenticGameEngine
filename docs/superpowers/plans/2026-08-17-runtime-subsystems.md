# Generic Runtime Subsystems Implementation Plan

**Goal:** Replace Rekall AGE's projected/no-op audio and UI paths and its
transform-only animation path with deterministic, inspectable, agent-authorable
runtime systems that work in headless verification and the Windows player.

**Architecture:** Keep authored data in generic components and assets. Runtime
systems compile those facts into explicit state views and render/audio command
streams. Pure deterministic evaluators run without devices; platform adapters
consume the command streams in interactive players. Diagnostics remain
structured observations, and gameplay remains in agent-authored modules.

## Tranche 1: Audio foundation

- [x] Define PCM clip, voice, bus, listener, and mix-frame contracts.
- [x] Decode validated PCM WAV assets without platform dependencies.
- [x] Execute emitter lifecycle, looping, gain, pitch, bus gain, and generic 3D attenuation/pan.
- [x] Expose deterministic headless audio state and missing/invalid-asset observations.
- [x] Add a Windows playback adapter without coupling runtime simulation to the device.
- [x] Prove imported audio through scene execution, package relocation, and installed player.

## Tranche 2: Runtime UI

- [x] Define canvas, layout rectangle, visual, text, focus, and interaction state contracts.
- [ ] Implement deterministic anchors, offsets, stacking, padding, alignment, and clipping.
- [x] Project panels, labels, images, and buttons into concrete overlay draw data.
- [x] Render the overlay in software and Vulkan/windowed paths with deterministic text metrics.
- [x] Execute pointer/focus/navigation state and emit generic UI event facts.
- [ ] Add viewport, packaged-game, and installed-product visual proofs.

## Tranche 3: General animation

- [x] Define versioned clips with scalar/vector/color/sprite tracks and interpolation modes.
- [x] Implement deterministic sampling, loop/clamp/ping-pong time, playback speed, and events.
- [x] Apply generic component-property and transform tracks without genre assumptions.
- [ ] Implement sprite-frame animation and cross-fade/blend layers.
- [ ] Import and execute skeletal GLB tracks, skinning data, and blend diagnostics.
- [ ] Prove animation in headless state assertions and rendered captures.

## Tranche 4: Acceptance and hardening

- [ ] Add SDK helpers and schemas agents can discover without guessing property shapes.
- [ ] Add malformed-data, limit, fuzz, determinism, and long-run tests.
- [ ] Add audio/UI/animation tasks to the installed-engine agent benchmark.
- [x] Run the full suite twice and canonical Windows distribution acceptance.
- [ ] Update the maturity audit with measured evidence rather than projections.
