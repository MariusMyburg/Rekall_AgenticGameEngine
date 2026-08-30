# Rekall AGE Studio Content Browser Design

**Date:** 2026-08-30  
**Status:** Approved for implementation under the user's explicit autonomous pre-approval

## Purpose

Studio needs one obvious place to find, inspect, import, and open all project content. The existing `Assets` output tab is a flat diagnostic string list limited to imported catalog entries. Authored meshes, modeling graphs, material graphs, model assets, shaders, and C# module sources live in separate stores and are discoverable only from their specialized workspaces.

The Content Browser will be a first-class Studio authoring surface backed by canonical engine stores and commands. It must not create a second asset database or bypass the transaction/import pipeline.

## Goals

- Show imported and engine-authored project content in one searchable, grouped browser.
- Include models, editable meshes, procedural modeling graphs, material graphs and instances, textures, audio, shaders, C# sources, curves, rigs, and other catalogued assets.
- Open an item in its appropriate Studio workspace or safe external editor when Studio has no native editor.
- Accept multi-file operating-system drag and drop for common model, image, texture, audio, shader, and source formats.
- Import through the canonical report-producing asset pipeline and expose progress, diagnostics, provenance, and failures.
- Refresh the live viewport and Inspector asset choices after successful imports.
- Provide generic internal drag payloads so assets can later be placed in the world or assigned to compatible Inspector properties without genre-specific behavior.

## Non-goals

- Replacing specialized Modeling, Materials, or Code workspaces.
- Building full native image, waveform, or audio editors in the first slice.
- Inventing a parallel asset GUID, metadata, dependency, or reimport format.
- Automatically guessing gameplay meaning for an imported file.
- Treating arbitrary unsupported files as safe runtime content.

## User Experience

The Content Browser appears as a resizable bottom panel in the World workspace, replacing the diagnostic-only Assets tab as the primary asset surface. It remains available from `View > Content Browser` and can be restored if hidden. The default panel is tall enough to show useful cards without consuming the world viewport.

The header contains:

- breadcrumb/category navigation;
- back and home actions;
- search text;
- category/type filter;
- card/list view selector;
- refresh and Import buttons;
- compact import-status indicator.

The left side provides stable groups such as All Content, Imported, Models, Meshes, Materials, Textures, Audio, Code, Shaders, Animation, and Other. Groups are projections over content metadata rather than physical filesystem ownership.

The main area displays content items with a type icon or thumbnail, display name, content kind, and a concise health/status badge. Selection shows useful metadata in a details region: stable ID, source/imported path, hash, dimensions or GLB counts when known, publication health, and the last import diagnostic.

Double-click opens the selected item. Enter performs the same action. Context actions include Open, Open Externally, Reveal in Explorer, Copy Asset ID, Reimport when supported, and Show Details. Unsupported actions remain hidden or disabled with a tooltip explaining why.

The browser surface and its empty state are file drop targets. Dragging supported files over the surface shows `Import N files into <project>`. Unsupported files are identified before drop and do not make the supported batch fail silently. The drop result lists each success or failure.

## Unified Content Index

Extend the editor contract from imported-assets-only records to a provider-neutral content model. Each item contains:

- stable content ID;
- display name;
- content family and specific kind;
- origin (`Imported`, `Authored`, `Generated`, or `ProjectSource`);
- canonical path when one exists;
- source path when one exists;
- content hash/revision when known;
- editor route ID and capabilities;
- health state and concise diagnostic;
- optional preview metadata such as image dimensions or GLB mesh/material counts.

The index builder reads existing canonical stores:

- imported asset catalog;
- mesh, modeling graph, material graph, material instance, curve, and rig stores;
- published model asset store;
- project module source commands;
- shader authoring commands.

Missing or corrupt one-family content must produce a bounded diagnostic item or family warning rather than blanking the entire browser. Index ordering is deterministic by family, display name, and stable ID.

The workbench read model remains the transport into Studio. The Content Browser never scans arbitrary project directories as its source of truth. Filesystem watching may trigger a refresh, but canonical stores decide what content exists.

## Open Routing

Studio owns one `IRekallAgeStudioContentOpenRouter`-style service. Routes are data-driven by content kind/capability and return a structured result rather than switching UI directly from every list event.

Initial routes:

- editable mesh or published model source → Modeling workspace and selected mesh/model;
- modeling graph → Modeling workspace, Procedural Geometry surface, selected graph;
- material graph or instance → Modeling workspace, Materials surface, selected asset;
- C# module source → Code workspace and selected source;
- shader/include → Studio text editor route when available, otherwise associated external editor;
- imported image/texture → Studio image preview/details, with Open Externally available;
- imported audio → lightweight playback/details when supported, otherwise associated external editor;
- unknown but safe imported file → associated external editor or Explorer reveal;
- item without a usable path or editor → stable actionable diagnostic, never an exception dialog with raw internals.

Routing reuses existing workspace selection commands and sessions. It must not reconstruct editor state or open a duplicate editor window.

## Import Pipeline

External drops and the Import button share one `RekallAgeStudioContentImportSession`. The session:

1. validates an open project and normalizes distinct absolute source paths;
2. classifies supported extensions with a central policy;
3. creates visible queued items;
4. executes `rekall.asset.import_report` per file with bounded concurrency;
5. records success, warning, or failure without aborting unrelated files;
6. refreshes the content index, Inspector asset choices, and viewport dependencies once per completed batch;
7. selects the first successful import and exposes its report.

Initial accepted external formats:

- models: `.glb`, `.gltf`;
- images/textures: `.png`, `.jpg`, `.jpeg`, `.dds`, `.ktx2`;
- audio: `.wav`, `.mp3`;
- shaders/text sources when supported by project policy: `.glsl`, `.vert`, `.frag`, `.comp`, `.hlsl`;
- C# source: `.cs` only through an explicit project-module import route; it must not be copied into an arbitrary asset kind.

MP3 must be added to the asset-pipeline media-type mapping because the runtime already has MP3 decoding. Unsupported extensions are reported with a stable code and a list of accepted formats.

The import session never overwrites a source file, never trusts a dropped relative path, and never follows a dropped directory recursively in the first slice. Canonical importer hashing and stable IDs handle duplicate content. Reimport remains a separate explicit operation when source provenance is available.

## Dragging Content Into Authoring Surfaces

Content items expose a Studio-internal drag payload containing only stable ID, content kind, and allowed operations. The payload does not carry mutable file bytes or genre meaning.

Initial consumers:

- Inspector asset-reference editors accept compatible content kinds and set the property through the ordinary component property command.
- World viewport accepts placeable model content and uses the generic model-asset instantiation path at the resolved world hit or a deterministic camera-front fallback.

Compatibility is determined by component schema `AssetKind` and content capabilities. Incompatible drops show a clear non-destructive explanation.

## Preview and Health

The first release provides type icons for every family and real thumbnails where they can be generated safely and cheaply:

- image assets use decoded image thumbnails;
- model/mesh thumbnails use the existing Vulkan/modeling preview path asynchronously when available;
- other families use distinctive icons and metadata summaries.

Preview failures fall back to the type icon and a health badge. Thumbnail work is cancellable, cached by content ID plus hash/revision, and cannot block index loading or UI input.

Published model assets surface existing publication/dependency health. Imported assets surface report/provenance facts. Authored assets surface revision/readability status. The browser does not invent health independently of canonical stores.

## Error Handling and Accessibility

- Import and open operations return stable codes and concise remediation text.
- Raw file bytes, secrets, full exception dumps, and unbounded remote response bodies never appear in UI summaries.
- Cancellation is distinct from failure.
- Keyboard navigation supports category selection, search, item movement, open, context menu, and import.
- All icon-only buttons have accessible names and readable tooltips.
- Selection and drag/drop state must remain visible under the dark theme and high-DPI scaling.
- Empty states explain both Import and drag/drop.

## Testing and Acceptance

Focused tests must prove:

- deterministic unified indexing across imported and authored families;
- one broken family does not erase healthy content;
- search, category, capability, and health projections;
- route selection for mesh, graph, material, code, shader, texture, audio, and unknown items;
- extension classification including MP3 and case-insensitive names;
- multi-file drop normalization, unsupported-file reporting, partial success, cancellation, and single batch refresh;
- no import bypasses `rekall.asset.import_report`;
- Inspector drop compatibility and canonical property mutation;
- viewport model drop uses generic instantiation;
- WPF source/layout bindings, keyboard activation, accessibility names, and drop target state.

Live acceptance uses a disposable project and drops at least one GLB, PNG, WAV, MP3, and unsupported file. The accepted files must appear with correct families, open through their routes, survive Studio restart, update viewport dependencies, and retain catalog/report metadata. A model is also dragged into the viewport and a texture into a compatible Inspector property, with persisted scene assertions proving the changes.

## Implementation Boundaries

The work should be delivered in this order:

1. unified editor content contracts and index builder;
2. central open router and route tests;
3. import policy/session and MP3 pipeline mapping;
4. Content Browser WPF surface and selection/details;
5. OS file drag/drop and visible import queue;
6. Inspector and viewport internal drag consumers;
7. previews, documentation, and disposable-project acceptance.

This ordering makes every UI feature a client of generic, inspectable contracts and keeps the engine suitable for arbitrary games rather than a particular genre or asset workflow.
