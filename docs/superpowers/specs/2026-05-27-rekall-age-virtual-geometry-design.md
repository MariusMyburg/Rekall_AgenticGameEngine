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
- `targetPixelError`: distance/detail knob for future screen-space selection.
- `clusterTriangleCount`: CPU cluster size, default `128`.
- `maxSelectedTriangles`: per-renderable triangle cap, default `0` meaning no cap.
- `maxLodLevel`: maximum reduction level, default `8`.
- `debugMode`: optional string for diagnostics, default `off`.

## Runtime Selection

The CPU MVP reduces triangle pressure deterministically:

1. Build ordinary `RekallAgeVulkanSceneMesh` data from existing primitives, authored geometry, or imported GLB assets.
2. If virtual geometry is disabled or missing, return the ordinary meshes.
3. If enabled, build progressively reduced triangle levels from the source mesh.
4. Pick a level from camera distance and `maxSelectedTriangles`.
5. Return selected mesh chunks with preserved material bindings and entity identity.

This is not a final Nanite-quality simplifier. It is a bounded, inspectable near-term performance step that makes dense meshes cheaper to submit today.

## Diagnostics

Performance budget inspection should report both source and selected virtual geometry counts:

- virtual geometry renderables
- source triangles
- selected triangles
- reduced triangles

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
- performance budget reporting selected and reduced virtual geometry triangles

## References

- Epic Games, Nanite Virtualized Geometry documentation: https://dev.epicgames.com/documentation/unreal-engine/nanite-virtualized-geometry-in-unreal-engine
- Epic Games, Nanite technical details: https://dev.epicgames.com/documentation/en-us/unreal-engine/nanite-technical-details
- Brian Karis / Epic Games SIGGRAPH Nanite deep-dive material: https://www.wihlidal.com/projects/nanite-deepdive/
