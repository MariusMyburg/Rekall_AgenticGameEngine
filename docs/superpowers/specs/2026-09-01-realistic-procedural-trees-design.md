# Realistic Procedural Trees Design

## Goal

Replace the example-only cartoon tree mesh with a deterministic, reusable AGE
modeling facility that creates credible deciduous trees at game-ready budgets.

## Research basis

The design combines the production workflow described in Quentin Rapicault's
realistic oak breakdown with the space-colonization literature and SpeedTree's
published branch/LOD guidance. Realism comes primarily from hierarchical
silhouette, crown competition, believable taper, branchlets, leaf orientation,
material separation, and variation. Performance comes from explicit quality
budgets, alpha-tested leaf cards, progressively reduced branch/radial detail,
and ordinary AGE LOD/material contracts.

## Architecture

`Rekall.Age.Modeling.Contracts` owns serializable tree parameters and generated
surface/LOD results. `Rekall.Age.Modeling` owns the deterministic generator. It
returns separate bark and foliage meshes for every requested LOD so callers can
assign normal PBR bark and two-sided alpha-cutout foliage materials without a
tree-specific renderer path. Game modules, modeling graphs, Studio, CLI, and
agents can all consume the same generator.

The growth pass builds a crown-aware branch skeleton using deterministic
phyllotaxis, apical dominance, upward/light tropism, gravity-induced droop,
controlled noise, branch shedding, and Murray/Leonardo-style radius falloff.
The meshing pass creates irregular parallel-transport tubes with longitudinal
UVs and base flare, then distributes crossed leaf cards along terminal
branchlets. LOD presets reduce generations, centerline/radial resolution and
leaf density while preserving the trunk and primary silhouette.

## Contracts and invariants

- Equal settings and seed produce byte-equivalent topology and attributes.
- Species presets are data, not renderer branches; the initial preset is a
  broad-crown temperate oak.
- Bark and foliage are separate valid `RekallAgeMeshAsset` surfaces.
- Bark has coherent `position`, `normal`, `uv0`, `color`, and generation data.
- Foliage consists of leaf cards with UVs suitable for a texture atlas; it is
  not made from blobs.
- Each LOD has a hard triangle and leaf-card budget and monotonically decreases
  in complexity.
- No software rendering path or vegetation-specific Vulkan shader is added.

## Rendering integration

Callers use existing `Rekall.Material` PBR texture slots, `AlphaMode=mask`,
`AlphaCutoff`, `DoubleSided`, and `Rekall.LodGroup`. Existing generic vegetation
wind parameters remain the animation path. A later renderer milestone may add
generic transmission/subsurface foliage shading and billboard/impostor baking;
neither is required to replace the current octahedral foliage convincingly.

## Acceptance

Focused tests must prove determinism, valid topology, broad-crown proportions,
base flare and taper, UV range/variation, leaf-card topology, distinct material
surfaces, and monotonic LOD budgets. Midnight Rider must compile against the
generic generator rather than its private implementation.
