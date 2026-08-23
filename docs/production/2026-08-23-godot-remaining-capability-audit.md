# Remaining Godot Capability Audit

Date: 2026-08-23

Reference: Godot `893cf5cbfe789ae67c9389708e1428141bb39b18`

This was a read-only source comparison. No Godot implementation code was
copied into Rekall AGE. Concepts are translated into proprietary, 100% C#,
agent-first contracts.

## Closed by the WebGPU tranche

AGE now has the explicit opaque-resource and immutable-command boundary that
motivated the first Godot RenderingDevice comparison: buffers, textures,
samplers, shaders, layouts/sets, render and compute pipelines, targets,
uploads, storage resources, indirect commands, validation, transactional
compilation, inspection, native Vulkan execution, and physically proven browser
WebGPU/WGSL execution. This closes the low-level device gap; it does not make a
complete browser game export.

Godot references:

- `servers/rendering/rendering_device.h`
- `servers/rendering/rendering_device_graph.*`

## Prioritized remaining generic gaps

| Order | Capability | Representative Godot source | AGE boundary to add |
| ---: | --- | --- | --- |
| 1 | declarative render/resource graph | `servers/rendering/rendering_device_graph.*`, `storage/render_scene_buffers.*` | bounded pass DAG, named inputs/outputs, read/write declarations, transient lifetime/hazard analysis, automatic barriers, timing/capture, and agent inspection |
| 2 | first-class material assets and instances | `storage/material_storage.h`, `renderer_rd/storage_rd/material_storage.*`, `scene/resources/shader.cpp` | versioned shader-backed material assets with reflected typed parameters, defaults, overrides, texture/sampler/buffer bindings, and global/material/instance scopes |
| 3 | editable multi-surface mesh assets | `scene/resources/mesh.h`, `surface_tool.*`, `mesh_data_tool.*`, `storage/mesh_storage.*` | persistent reusable mesh documents, named surfaces/materials/topologies, UInt32 indices, tangents and arbitrary channels, region updates, bounds, and transactional topology operations |
| 4 | scene-level batched instancing | `scene/resources/multimesh.h`, `storage/mesh_storage.*` | inspectable instance-set assets/components with shared mesh/material, transforms, colors/custom data, previous transforms, bounds, partial updates, culling output, and deterministic fallback |
| 5 | shader variants and pipeline cache | `renderer_rd/shader_rd.*`, `pipeline_cache_rd.*`, `scene_shader_forward_*` | typed variant axes, backend/profile manifests, persistent cache identity, prewarming, fallback selection, package inventory, and missing-variant diagnostics |
| 6 | generic GPU particles | `renderer_rd/storage_rd/particles_storage.*`, `renderer_rd/shaders/particles*.glsl` | deterministic particle schemas and workloads for emitter distributions, lifetime/seed/events, custom simulation, collision inputs, sorting, trails, bounds, mesh/billboard draws, and summaries |
| 7 | scene attachments and compositor stages | `storage/compositor_storage.*`, `rendering_server_enums.h` | generic named color/depth/normal/motion/object-ID attachments plus declared injection stages and capability fallbacks |
| 8 | production scene rendering services | `renderer_scene_cull.*`, `storage/environment_storage.*`, `storage/light_storage.*`, `renderer_rd/effects/*` | shadows, sky/IBL, probes, decals, fog volumes, occlusion, baked data, quality budgets, and portable profile fallbacks |
| 9 | extensible deterministic asset cooking | `editor/import/editor_import_plugin.*`, `resource_importer_*`, `resource_importer_scene.*` | C# importer/processor plugins, settings/dependencies, content-addressed cooked artifacts, mesh/texture target processing, incremental invalidation, and inspectable cook reports |
| 10 | complete web game export | `platform/web/export/export_plugin.cpp`, `platform/web/js/*`, web input/audio/display sources | AOT scenes/modules, assets, semantic input, audio, storage, networking, resize/focus/fullscreen, package/audit, offline policy, and deterministic gameplay acceptance |
| 11 | agent-visible modelling workspace | Godot shader/mesh editor plugins and resource APIs | semantic vertex/edge/face/surface selection and diffs, material inspection, shader markers/reflection, dependency graphs, previews, before/after captures, and structured repairs |
| 12 | advanced renderer services | Godot TAA/SMAA/FSR2/SS effects/DoF/tonemap/GI sources | reusable render-graph nodes with temporal history, motion inputs, quality profiles, and captured diagnostics |

## Execution order

The runtime-facing order is render graph/attachments, typed materials, editable
multi-surface meshes, instancing, shader variants, particles, production scene
services, and deterministic cooking. Complete browser game export remains a
parallel platform lane. Blender now becomes the reference for mesh topology,
geometry operations, modifiers, UVs, material authoring, validation, undo, and
interchange; Godot remains the reference for projecting those assets efficiently
into runtime surfaces, instances, materials, and passes.

AGE must expose compact semantic modelling operations and evidence. Agents
should not have to emit megabytes of raw vertex JSON, and the engine must not
author content on their behalf.
