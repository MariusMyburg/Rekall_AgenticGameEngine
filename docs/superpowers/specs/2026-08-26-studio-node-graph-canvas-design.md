# Studio Node-Graph Canvas Design

**Status:** Approved for implementation (user pre-approved all 4 remaining modeling-parity items; async execution, no further check-ins between items).

## Context

`Rekall.Age.Modeling`'s procedural graph already has everything a visual node
editor needs at the data layer: `RekallAgeModelingGraphAsset.Nodes`/`Links`,
a full node-type catalog with port descriptors
(`RekallAgeModelingNodeCatalog`), and a patch service supporting
`AddNode`/`RemoveNode`/`AddLink`/`RemoveLink`/`SetParameter` (`RekallAgeModelingGraphPatchService`).
Studio's "Procedural Geometry" panel exposes none of this visually — it's a
flat list of nodes with parameter forms and raw incoming/outgoing *link
counts*. There's no way to see the graph's actual wiring or connect two
nodes.

## Goal

Add a visual canvas that shows nodes as positioned boxes with port dots and
draws real lines for `Graph.Links`, so the topology is actually visible, and
lets a user create a new link by clicking an output port then a compatible
input port.

## Scope

**In:** node positions (client-side only — auto-laid-out by dependency depth
on open, draggable, not persisted to the asset file, so this needs zero
backend schema change), a raster-rendered canvas following the exact
established pattern from `RekallAgeStudioMeshViewportRenderer` (pure renderer
class + `Image` control + normalized-coordinate hit testing), click-to-select
a node (drives the existing parameter panel unchanged), drag-to-reposition,
and click-port-then-click-port to `AddLink` via the existing patch pipeline.

**Out (tracked as follow-ups, not attempted here):** visual add/remove-node
gestures (the existing panel has no add/remove node command either — pre-
existing gap, not introduced by this slice), link deletion by clicking a
line, port-type-compatibility validation in the UI (the evaluator already
surfaces invalid-link diagnostics through the existing diagnostics panel, so
an incompatible link attempt fails visibly rather than silently).

## Approach

- `RekallAgeStudioModelingGraphLayout.ComputeDefaultPositions(nodes, links)`:
  pure function, topologically layers nodes by dependency depth (nodes with
  no incoming links are column 0; each node's column is
  `1 + max(upstream columns)`), spacing columns/rows evenly. Testable with no
  WPF dependency.
- `RekallAgeStudioModelingGraphCanvasRenderer.Render(nodes, links, positions,
  selectedNodeId, width, height)`: pure renderer (same shape as the mesh
  viewport renderer) producing a frame with the raster image, per-node hit
  rectangles, and per-port hit points (keyed by `(nodeId, portId, isOutput)`).
- View-model: `ModelingGraphNodePositions` (`Dictionary<string, Point>`,
  merged — not replaced — on open/patch so existing nodes keep their spot and
  only new nodes get auto-layout), `BeginGraphNodeDrag`/`UpdateGraphNodeDrag`
  methods (reposition, no patch), `SelectGraphPortAsync`/pending-port state
  (first port click arms it; a second click on a compatible-direction port
  calls `ApplyPatchAsync` with an `AddLink` operation; clicking the same port
  again or a node body cancels the pending link).

## Testing

- `RekallAgeStudioModelingGraphLayout` unit tests: a linear chain gets
  strictly increasing columns; two independent nodes with no links land in
  column 0; a diamond (A→B, A→C, B→D, C→D) puts D after both B and C.
- `RekallAgeStudioModelingGraphCanvasRenderer` unit tests: node hit rectangles
  are pickable at their center; port hit points resolve to the correct
  `(nodeId, portId)`; link lines connect the correct two port points.
- View-model test: two port clicks across compatible nodes produce a new
  entry in `Graph.Links` after `ApplyPatchAsync` completes.
