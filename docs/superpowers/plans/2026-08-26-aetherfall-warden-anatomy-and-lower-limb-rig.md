# Aetherfall Warden Anatomy and Lower-Limb Rig Plan

**Goal:** Replace the Warden's most obvious capsule/egg/ball construction with a more coherent armored humanoid silhouette and give gameplay locomotion real knee and foot articulation.

**Architecture:** Continue using AGE's generic editable modeling graph, envelope skinning, named rig, pose evaluation, material-slot, live-linked Model Asset, and runtime module contracts. Append stable lower-limb joints rather than renumbering existing joints. Author gameplay pose deltas in the Aetherfall module using elapsed time and semantic movement state. Preserve the existing attachment and gameplay contracts.

**Reference principles:** Blender's armature evaluation keeps rest hierarchy, pose channels, and deformation as distinct data, while its modifier stack lets authored base forms remain inspectable before bevel, smoothing, normals, UVs, and skinning. Godot's `Skeleton3D`/bone-attachment model likewise treats stable named bones and evaluated global pose as reusable scene data. AGE follows those principles through its own JSON contracts and row-vector math; no Blender or Godot code is copied.

## Intended authored changes

- Keep the existing ten joint IDs and append `shin_l`, `foot_l`, `shin_r`, and `foot_r`.
- Move the existing leg pivots to the authored hip line, then parent shin and foot joints in stable chains.
- Split each long arm capsule into upper-arm and forearm volumes so elbow deformation has real topology to move.
- Add cloth underlayers for thighs and shins so plate pieces no longer float around empty gaps.
- Replace the exposed dark egg head with a steel crown, cheek guards, nose guard, and sternum/collar construction.
- Add a second shoulder lamella layer and refine the cloth torso toward a narrower waist/broader upper-body silhouette.
- Extend envelope weights and movement-driven pose deltas to the new joints without introducing built-in humanoid locomotion behavior.

## Tasks

- [x] Add failing acceptance for the 14-joint hierarchy, lower-limb pose output, segmented anatomy nodes, and exact stable material ownership.
- [x] Extend the rig asset and movement-driven module pose.
- [x] Rebuild the editable Warden graph and envelope weights.
- [x] Bake the graph and rebuild the stable live-linked model.
- [x] Prove movement deformation, combined Aetherfall behavior, validation, and native Vulkan rendering.
- [x] Inspect the frame at original size and record residual gaps.
- [x] Commit, push, and continue.

## Acceptance

- The original ten joint IDs retain their indices and four new joints form two valid leg chains.
- Semantic movement produces non-trivial, phase-varying shin and foot matrices in addition to hip swing.
- The compiled mesh contains meaningful weights for at least ten distinct joints and produces no rig observations.
- New anatomy and armor nodes are reachable through ordinary graph validation and survive bake/publish.
- The native frame is materially more humanoid and layered, with zero missing assets or fallbacks.
- Existing gameplay state, semantic movement, root motion, runeblade/pauldron attachments, and 45-test Aetherfall acceptance remain effective.

## Verified outcome

- Graph revision 23 has 115 reachable nodes and bakes 18,622 points, 21,368 faces, and 79,696 corners with five stable authored surfaces.
- Rig revision 3 preserves the first ten stable joint indices and appends two valid `leg -> shin -> foot` chains. Runtime movement emits 13 named pose deltas, including non-trivial knee and foot rotation.
- The first model rebuild reliably failed because the generic compiled-output store reused the 64 MiB ordinary-document budget. AGE now gives validated content-addressed compiled meshes their own bounded 256 MiB budget while scenes, projects, catalogs, and other editable documents retain 64 MiB. The real 83,574,368-byte Warden artifact then published successfully as stable model revision 28.
- Combined Aetherfall acceptance passes 45/45; project and scene validation report zero issues; both modules remain ready under `windows-appcontainer-restricted` trust.
- Native moving proof is `Examples/AetherfallCitadel/Proof/Captures/WardenAnatomy/vulkan-scene-1280x720-20260826131526154.png`: High Vulkan on RTX 5090, 325 render-work draws, four dispatches, 66 renderables, 12,734 distinct colors, luminance 0.110, and zero observations, missing assets, or fallbacks. Static comparison is retained under `Proof/Captures/WardenAnatomyStatic`.
- High 2560x1440 `desktop60` passes at 6.753984 ms measured GPU time, 99 draws, 213,882 triangles, 1,095,508 vertices, and nine textures. The vertex count is correctly reported as near the 1,250,000 budget.
- Original-size review confirms stable deformation and more coherent helmet, arm, shoulder, torso, thigh, and shin construction. It also confirms that this remains visibly procedural and blocky rather than the requested production character: authored high-frequency forms, fitted armor transitions, cloth folds, texture/normal wear, real animation clips, and stronger close composition remain open.
- The 83 MiB pretty-JSON artifact is functional but inefficient. A compact versioned binary compiled-mesh format should replace JSON inflation later without holding up the current playable visual milestone.
