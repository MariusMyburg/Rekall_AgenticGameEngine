# Native Rig Joint Attachments Design

## Purpose

AGE can evaluate named native-rig joints and deform weighted vertices, but an
ordinary child entity can only inherit the rig entity's root transform. A
weapon, armor plate, particle emitter, light, camera, collision helper, or
other authored entity cannot follow an animated hand, head, or limb. Aetherfall
currently compensates by animating equipment transforms separately, which is
fragile and prevents the rig from being the authoritative articulation source.

This slice adds a generic transform-composition primitive. It does not add
humanoid, equipment, combat, locomotion, or weapon behavior to engine core.

## Reference principles

Godot's `BoneAttachment3D` binds by bone name/index, listens for skeleton pose
updates, applies the selected bone's global pose, and reports configuration
warnings when the skeleton or bone cannot be resolved. Blender exposes bone
parenting as an ordinary object-parent relationship (`PARBONE`) and adds a
dependency from the parent bone pose to the child object transform. AGE follows
the same separation: the rig owns pose evaluation; the entity retains an
ordinary local transform; the world-transform resolver composes the two.

Reference sources:

- Godot `scene/3d/bone_attachment_3d.cpp` in the official Godot repository.
- Blender `source/blender/makesrna/intern/rna_object.cc` and
  `source/blender/depsgraph/intern/builder/deg_builder_relations.cc` in the
  supplied `F:/Dev/blender-reference` tree.

## Public component contract

The built-in component `Rekall.RigAttachment` contains:

- `jointId`: required, non-empty stable joint ID from the parent entity's
  native rig asset.
- `enabled`: optional boolean, default `true`. A disabled attachment behaves
  exactly like an ordinary parented entity.

The attachment entity must use its existing `parentId` to identify the rig
entity. AGE does not add a second entity-reference field or an external-
skeleton shortcut in this milestone. Agents can reparent entities with the
ordinary hierarchy contract.

## Pose evaluation contract

`RekallAgeRigEvaluator` must expose pose-global matrices as well as skin
matrices. Both arrays use rig joint order and contain finite row-major 4x4
matrices:

- pose-global matrices transform content from joint-local space into rig-local
  posed space;
- skin matrices remain inverse-bind multiplied by pose-global and continue to
  drive vertex deformation unchanged.

The evaluated result retains stable joint IDs so callers resolve attachments by
ID rather than persisting array indices. This mirrors the existing named pose-
delta contract and avoids stale index bindings after an authored rig revision.

## Transform composition

For an enabled attachment with local matrix `L`, selected joint pose-global
matrix `J`, and resolved parent world matrix `P`, AGE calculates:

`world = L * J * P`

This matches AGE's existing row-vector hierarchy convention. Without an enabled
attachment the existing composition remains `L * P`. The result is decomposed
through the same finite scale/rotation/translation validation used by ordinary
parented transforms, then consumed uniformly by meshes, particles, lights,
cameras, fog volumes, and any other render-frame projection using the shared
world-transform resolver.

Attachment resolution is cached per rig entity and frame by the resolver. It
must not mutate runtime entities, persist derived matrices into scene files, or
evaluate the rig separately for every child.

## Diagnostics and fallback

Invalid authoring never makes the child disappear. AGE emits at most one typed
viewport warning per entity and uses ordinary `L * P` composition when:

- `jointId` is missing or blank: `runtime.transform.rig_attachment_joint_missing`;
- the parent lacks `Rekall.RigPose`: `runtime.transform.rig_attachment_pose_missing`;
- the rig asset or pose cannot be evaluated:
  `runtime.transform.rig_attachment_pose_invalid`;
- the named joint is absent: `runtime.transform.rig_attachment_joint_unknown`.

Existing parent-missing, parent-cycle, and decomposition diagnostics remain
authoritative. An attachment never bypasses hierarchy validation.

## Aetherfall acceptance

The Warden runeblade attaches to `forearm_r`; the articulated pauldron attaches
to `upper_arm_l`. Their local transforms are rebased from Warden-root space to
joint-local offsets. Acceptance must build real runtime frames with two
different named joint poses and prove the rendered equipment world transforms
change even when the equipment entities' local transforms do not. It must also
prove root movement still carries the attachments exactly and that no
attachment observations are emitted.

This checkpoint proves generic rigid joint attachments. Socket authoring UI,
constraint offsets, physics handoff, imported glTF skeleton attachment targets,
and inverse attachment/pose override remain later work.
