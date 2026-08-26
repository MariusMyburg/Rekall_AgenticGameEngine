# Rekall AGE Virtual Geometry Design

## Goal

Rekall AGE should let agents use very high triangle-count meshes without forcing every visible triangle through the current Vulkan draw path. The first milestone is a near-term CPU-side virtual geometry path that reduces selected triangles before batching. The later milestone moves the same generic contract toward cooked cluster files, GPU culling, mesh shaders, and streaming.

## Research Summary

Unreal Engine Nanite is a full virtualized geometry system, not a single optimization. Public Epic documentation describes Nanite as importing dense meshes into hierarchical triangle clusters, choosing detail based on screen visibility, and streaming only needed geometry data. The practical ingredients are an offline build step, cluster hierarchy, error metrics, culling, fallback meshes, diagnostics, and a GPU-heavy renderer path.

Rekall AGE should not copy Nanite as a branded or genre-specific feature. It should expose generic authoring primitives and diagnostics so agents can author scenes and choose performance trade-offs themselves.

## Architecture

The first implementation adds a generic `Rekall.VirtualGeometry` component. Runtime projection carries the component into `RekallAgeRuntimeViewportRenderable` as settings. The Vulkan mesh builder checks those settings after resolving ordinary authored or imported meshes, then applies CPU-side clustered LOD reduction before the batch builder flattens vertices and indices.

This keeps the existing renderer path intact. It does not require mesh shaders, compute culling, or a new storage format for the first milestone. It also means existing render commands, captures, and performance budget inspection keep working while seeing fewer selected triangles.

## Component Contract

`Rekall.VirtualGeometry` is engine-general render metadata. It does not author content for agents and does not imply any game genre.

Initial properties:

- `enabled`: opt-in switch, default `true`.
- `targetPixelError`: distance/detail knob for current screen-space-inspired selection.
- `clusterTriangleCount`: CPU cluster size, default `128`.
- `maxSelectedTriangles`: per-renderable triangle cap, default `0` meaning no cap.
- `maxLodLevel`: maximum reduction level, default `8`.
- `debugMode`: optional string for diagnostics, default `off`.

## Runtime Selection

The CPU MVP reduces triangle pressure deterministically:

1. Build ordinary `RekallAgeVulkanSceneMesh` data from existing primitives, authored geometry, or imported GLB assets.
2. If virtual geometry is disabled or missing, return the ordinary meshes.
3. Compact each surface to referenced vertices and analyze indexed/geometric connectivity, including render seams and disconnected coincident components.
4. Build progressively reduced connected-cluster levels; reject candidates that worsen component count, boundary edges, or maximum edge use.
5. Pick a level from camera distance, `targetPixelError`, `clusterTriangleCount`, and the whole-renderable `maxSelectedTriangles` cap, apportioned deterministically across material surfaces.
6. Return selected mesh chunks with preserved material bindings and entity identity, plus source count, selected LOD, and truthful budget-satisfaction metadata.

Stable imported/model geometry is reduced before material base-color materialization and cached by source identity, settings, and distance-LOD bucket. Web and OpenXR retain their mesh builder across frames; the Windows player also incorporates virtual-geometry selection into its static-geometry cache signature. Skinned and morph-target meshes currently remain at source resolution because their vertex-indexed deformation payloads require explicit remapping.

This is not a final Nanite-quality simplifier. It is a bounded, inspectable near-term performance step that makes dense meshes cheaper to submit today.

## Diagnostics

Performance budget inspection should report both source and selected virtual geometry counts:

- virtual geometry renderables
- source triangles
- selected triangles
- reduced triangles
- per-renderable maximum and budget-satisfaction state

Budget blockers should evaluate selected triangles, because those are the triangles sent to the current draw path.

## Future GPU Phase

After the CPU path works, the cooked format should become explicit:

- cluster/meshlet records
- hierarchy nodes
- bounding volumes
- geometric error
- material ranges
- page ids
- fallback mesh records

The renderer can then move selection from CPU to GPU compute or Vulkan mesh shaders and stream pages as needed. The component and diagnostics should remain stable so agents do not need to rewrite authored scenes.

## Tests

The implementation should cover:

- built-in component schema exposure
- runtime frame propagation from `Rekall.VirtualGeometry`
- Vulkan mesh builder triangle reduction for imported meshes
- topology preservation across split seams and coincident disconnected open/closed components
- whole-renderable multi-surface caps and explicit impossible-cap reporting
- materialized asset cache reuse and distance/pixel-error output monotonicity
- performance budget reporting selected and reduced virtual geometry triangles

## References

- Epic Games, Nanite Virtualized Geometry documentation: https://dev.epicgames.com/documentation/unreal-engine/nanite-virtualized-geometry-in-unreal-engine
- Epic Games, Nanite technical details: https://dev.epicgames.com/documentation/en-us/unreal-engine/nanite-technical-details
- Brian Karis / Epic Games SIGGRAPH Nanite deep-dive material: https://www.wihlidal.com/projects/nanite-deepdive/
