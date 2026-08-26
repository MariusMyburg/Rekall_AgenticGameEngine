# Split-Normal Authoring Design

Date: 2026-08-26

Status: approved for implementation under the controller's standing unattended
approval

## Purpose

Give agents and Studio a substantial, inspectable normal-authoring toolchain so
curved forms shade smoothly, hard architectural boundaries remain crisp, and
weighted normals no longer blur across edges that authors marked sharp. Prove
the generic contract in Aetherfall rather than adding character- or ruin-
specific rendering behavior.

## Reference findings

Blender stores shading intent on topology domains (`sharp_face` on faces and
`sharp_edge` on edges), then derives loop/corner normals by walking connected
smooth fans. Its auto-smooth behavior classifies manifold edges by the dot
product of adjacent face normals against an angle threshold. Bevel also
preserves or deliberately overrides sharp-edge state instead of treating the
final vertex normal as the only authoring fact.

Godot's renderer consumes explicit vertex normal and tangent arrays and keeps
normal/tangent packing and validation at the renderer boundary. AGE should use
the same separation: editable topology attributes remain source truth, while
the compiler consumes explicit finite corner normals and derives orthonormal
tangents for rendering.

AGE will independently implement these concepts in C#. No Blender GPL code is
copied.

## Chosen architecture

AGE will store two canonical policy attributes:

- `normal.smooth` — face-domain `Bool`; `true` permits smoothing and `false`
  forces every corner of that face to use its face normal.
- `normal.sharp` — edge-domain `Bool`; `true` separates the smooth fans on
  either side of a manifold edge. Boundary and non-manifold edges are always
  fan boundaries regardless of the stored value.

Derived output remains a corner-domain `Float3` attribute with semantic
`normal`. `weighted_normals` becomes the primary normal baker. It calculates
finite Newell face normals, builds deterministic per-point corner fans through
smooth manifold edges, and averages only faces in each fan. Face area and
corner angle are independent bounded weighting exponents. Flat faces bypass
the fan average. The compiler continues to consume the resulting explicit
corner values and derive tangents from the chosen UV set.

This is preferred over compiler-only smoothing because policy must be
inspectable, serializable, editable, and reusable before cooking. It is
preferred over destructive point splitting because AGE topology already has a
corner domain that represents renderer splits without losing source identity.

## Operations

### `shade_faces`

Consumes a face selection and a `smooth` Boolean. It creates or updates
`normal.smooth` while preserving values on unselected faces. The result changes
attributes only and records selected face IDs and the changed attribute.

### `mark_sharp`

Consumes an edge selection and a `sharp` Boolean. It creates or updates
`normal.sharp` while preserving values on unselected edges. Boundary and
non-manifold selections are allowed because authors may want the policy to
survive later topology edits even though those edges are already split during
the current bake.

### `auto_smooth`

Consumes the complete face selection and an `angleDegrees` value in the closed
range 0–180. It calculates adjacent face normals and writes `normal.sharp` for
every edge: manifold edges are sharp when the angle between their faces is
strictly greater than the threshold; boundary and non-manifold edges are
sharp. It does not bake final normals, allowing agents to inspect or override
the classification before `weighted_normals`.

### `weighted_normals`

Consumes the complete face selection and parameters `attribute`,
`faceAreaWeight`, `cornerAngleWeight`, `smoothAttribute`, and `sharpAttribute`.
Both exponents are finite in 0–4. Missing policy attributes mean all faces are
smooth and no manifold edges are sharp, preserving the existing graph behavior.
The default destination becomes `normal.authored`, with semantic `normal`.

At each source point, the operation partitions incident corners into connected
fans. Two corners connect only through a shared two-face manifold edge whose
sharp value is false and whose incident faces are smooth. Each smooth fan uses
the normalized sum of face normals weighted by face area and the corner's
interior angle. A flat face corner uses the exact face normal. Degenerate faces,
zero-length weighted sums, incompatible policy attributes, and non-finite
parameters fail with stable diagnostics.

## Descriptor surfaces

The operation catalog exposes all four typed operations. Modeling graphs expose
`rekall.modeling.shade_faces`, `rekall.modeling.mark_sharp`,
`rekall.modeling.auto_smooth`, and the extended
`rekall.modeling.weighted_normals`. Modifier stacks expose
`rekall.modifier.auto_smooth` followed by the existing weighted-normal
modifier. Graph and modifier evaluation call the same semantic executor and do
not duplicate normal algorithms.

The current generic graph selection rule applies each node to the complete
domain. Fine-grained partial selection remains available through ordinary mesh
edit commands and will later be wired to Studio selection modes.

## Aetherfall consumer

The Warden, hollow sentinel, rubble boulder, weathered ruin, broken arch,
runeblade, and pauldron graphs already end in weighted normals. The first
visible tranche will insert auto smoothing before weighted normals on at least
the Warden and weathered ruin and configure the baker to respect the generated
sharp edges. The same published Model Assets are rebuilt in place so every
existing scene instance receives the improvement without scene-specific engine
logic.

The Warden uses a more permissive angle to retain smooth curved primitive
branches. The ruin uses a tighter angle so masonry faces and bevel transitions
remain readable under the dark High lighting setup.

## Testing and acceptance

Focused tests must prove:

1. partial `shade_faces` and `mark_sharp` edits preserve unselected values;
2. auto smooth classifies coplanar, acute, boundary, and non-manifold edges
   deterministically at threshold boundaries;
3. weighted normals smooth a curved fan but split at marked edges and flat
   faces;
4. area and corner-angle weighting produce finite unit corner vectors;
5. incompatible attributes and invalid parameters return stable diagnostics;
6. graph and modifier descriptors/evaluation use the same operation contract;
7. compilation consumes the authored corner normal and produces finite
   orthonormal tangent frames.

Visible acceptance requires rebuilt Aetherfall models, a real High Vulkan
capture with zero observations or asset fallbacks, and inspection showing
smoother curved silhouettes with preserved hard masonry/armor edges. After the
final asset mutation, movement, combat, progression, and reset must still pass
2/4/4/5 strict assertions, project and scene validation must report zero
issues, and the High desktop60 budget must remain within every configured
limit.

## Explicitly deferred

- arbitrary per-corner custom-normal painting and transfer;
- incremental island-only rebakes and dirty-region caching;
- MikkTSpace-compatible tangent selection/regeneration commands;
- Studio face/edge overlay and direct normal-vector visualization;
- normal-aware SSAO buffers and denoising;
- generalized attribute interpolation through every remaining topology
  operator.

These remain required parts of comprehensive modeling, but they do not block
the first end-to-end split-normal authoring and visible-game milestone.
