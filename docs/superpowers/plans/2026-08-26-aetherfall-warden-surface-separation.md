# Aetherfall Warden Surface Separation Plan

**Goal:** Make the playable Warden read as constructed dark-fantasy equipment instead of one bright metallic mass, using AGE's existing generic material-slot authoring and model publishing contracts.

**Architecture:** Keep geometry, skin weights, rig evaluation, gameplay, and joint attachments unchanged. Split the editable modeling graph into five inspectable surfaces: charcoal cloth, aged steel, dark leather, restrained antique bronze trim, and restrained aether emission. Rebuild through the ordinary graph-to-mesh-to-model pipeline and accept the result from a native Vulkan gameplay frame.

**Scope:** This is a consumer-driven acceptance slice, not a new genre-specific renderer or character system. Any engine defect exposed by the graph bake, surface preservation, material resolution, packaging, or runtime frame must be repaired generically.

## Tasks

- [x] Add an executable acceptance contract for the five authored material slots and their intended graph ownership.
- [x] Author dark leather and antique bronze material graphs.
- [x] Route belt, tassets, and boots through leather; route buckle, gorget, rivets, clasp, and helmet brow through bronze trim.
- [x] Bake the Warden graph and rebuild the live-linked model without changing stable asset IDs.
- [x] Run focused acceptance, project/scene validation, and native Vulkan capture.
- [x] Compare the native frame at original size, record exact evidence and remaining visual gaps, commit, and push.

## Acceptance

- The compiled Warden mesh publishes exactly five stable material surfaces in the intended order.
- Acceptance proves graph-level ownership rather than merely checking that material files exist.
- The native frame contains no missing-material or model fallback observations.
- Leather remains low-metallic/high-roughness and near-black brown; trim remains dark, muted, and subordinate to the steel silhouette.
- The gameplay rig, named-joint attachments, semantic controls, and agent-owned gameplay state continue to pass unchanged.

## Verified result

- Graph revision 22 validates with 93 reachable nodes and no diagnostics. The
  ordinary bake publishes 12,898 points, 15,126 faces, 55,816 corners, and five
  exact surfaces; the stable live-linked model rebuilds at revision 27.
- The surfaces are aged steel (43,740 indices), charcoal cloth (15,636),
  restrained aether (36), blackened leather (6,540), and antique bronze trim
  (10,740). Acceptance proves both the material values and graph ownership of
  the intended equipment parts.
- Aetherfall acceptance passes 45/45. Project and Main scene validation both
  report zero issues.
- Native High Vulkan evidence is
  `Proof/Captures/WardenSurfaceSeparation/vulkan-scene-1280x720-20260826125234255.png`:
  RTX 5090, 313 draws, four dispatches, 65 renderables, 11,950 distinct colors,
  mean luminance 0.112, and zero observations, fallbacks, or missing assets.
- Original-size inspection confirms stronger material separation, especially at
  the boots, belt/tassets, and small trim. It does not make the character final:
  armor anatomy, silhouette, authored surface wear, cloth construction, and
  production animation remain visibly below the reference-quality target.
