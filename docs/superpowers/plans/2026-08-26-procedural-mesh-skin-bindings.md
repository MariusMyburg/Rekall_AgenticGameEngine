# Procedural Mesh Skin Bindings Implementation Plan

**Goal:** Preserve native AGE mesh weights through cook and visibly deform an
authored mesh with the existing generic skeleton-pose renderer path.

**Spec:** `docs/superpowers/specs/2026-08-26-procedural-mesh-skin-binding-design.md`

### Task 1: Compile point-domain skin attributes

- [ ] Add failing compiler tests for normalized bindings and invalid pairs.
- [ ] Add `Int4` geometry value support if it is not already present.
- [ ] Extend compiled vertices with backward-compatible optional bindings.
- [ ] Read, validate, normalize, and corner-expand the canonical attributes.
- [ ] Run focused compiler/codec tests.

### Task 2: Preserve bindings through runtime geometry

- [ ] Add a failing compiled-model projection test.
- [ ] Extend viewport geometry with optional skin bindings.
- [ ] Map compiled bindings through the asset resolver/frame builder.
- [ ] Keep unweighted geometry behavior unchanged.
- [ ] Run focused viewport/model resolution tests.

### Task 3: Deform authored geometry

- [ ] Add a failing two-joint Vulkan mesh-builder test.
- [ ] Reuse the existing morph-then-skin implementation for authored geometry.
- [ ] Prove finite normals and expected vertex displacement.
- [ ] Run relevant rendering and skeletal-animation suites.

### Task 4: Playable consumer and evidence

- [ ] Author a small weighted Aetherfall character part through ordinary mesh
  attributes and a generic pose-producing module/component.
- [ ] Capture a real High Vulkan frame showing deformation.
- [ ] Re-run strict gameplay, validation, and Desktop60 budget gates.
- [ ] Update modeling matrix, Aetherfall acceptance, and production progress.
- [ ] Commit and push only after fresh verification succeeds.
