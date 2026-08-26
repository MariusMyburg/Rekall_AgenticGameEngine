# Procedural Mesh Skin Bindings Implementation Plan

**Goal:** Preserve native AGE mesh weights through cook and visibly deform an
authored mesh with the existing generic skeleton-pose renderer path.

**Spec:** `docs/superpowers/specs/2026-08-26-procedural-mesh-skin-binding-design.md`

### Task 1: Compile point-domain skin attributes

- [x] Add failing compiler tests for normalized bindings and invalid pairs.
- [x] Add `Int4` geometry value support if it is not already present.
- [x] Extend compiled vertices with backward-compatible optional bindings.
- [x] Read, validate, normalize, and corner-expand the canonical attributes.
- [x] Run focused compiler/codec tests.

### Task 2: Preserve bindings through runtime geometry

- [x] Add a failing compiled-model projection test.
- [x] Extend viewport geometry with optional skin bindings.
- [x] Map compiled bindings through the asset resolver/frame builder.
- [x] Keep unweighted geometry behavior unchanged.
- [x] Run focused viewport/model resolution tests.

### Task 3: Deform authored geometry

- [x] Add a failing two-joint Vulkan mesh-builder test.
- [x] Reuse the existing morph-then-skin implementation for authored geometry.
- [x] Prove finite normals and expected vertex displacement.
- [x] Run relevant rendering and skeletal-animation suites.

### Task 4: Playable consumer and evidence

- [ ] Author a small weighted Aetherfall character part through ordinary mesh
  attributes and a generic pose-producing module/component.
- [ ] Capture a real High Vulkan frame showing deformation.
- [ ] Re-run strict gameplay, validation, and Desktop60 budget gates.
- [ ] Update modeling matrix, Aetherfall acceptance, and production progress.
- [ ] Commit and push only after fresh verification succeeds.
