# Generic Physics Joints/Constraints Design

**Status:** Approved (bounded-architectural; user pre-approved this and all follow-on physics-breadth work in this session).

## Purpose

Rekall AGE has no way to rigidly or flexibly connect two dynamic bodies (BEPU's `BepuPhysics.Constraints` namespace is imported but never used for authored joints). This blocks ordinary authored content that needs connected bodies: ragdoll bone chains, swinging pendulums, rope/tether links, doors and wheels. This is the third and last of the physics-breadth sub-projects tracked in `docs/production/PROGRESS.md`'s current-gaps list (after collision layers/masks and the 2D material/angular-control work, both shipped earlier this session).

## Scope for this increment

Three constraint types, chosen as the most broadly useful and cheapest to author correctly, mapped directly onto real BEPU v2.4.0 constraint structs (confirmed by reading `BepuPhysics.xml`):

- **`Rekall.BallSocketJoint`** → BEPU `BallSocket`. Pins one local point per body together; free rotation. Ragdoll/chain attachments.
- **`Rekall.HingeJoint`** → BEPU `Hinge`. Pins one local point per body together *and* constrains relative rotation to one shared axis. Doors, wheels, pendulums.
- **`Rekall.DistanceJoint`** → BEPU `CenterDistanceConstraint`. Keeps two body *centers* (not arbitrary anchor points — the simplest correct distance constraint) at an authored target distance. Ropes, rigid rods, tethers.

**Explicitly deferred to a future increment** (documented here so nobody re-discovers this by surprise):
- Connecting a body to a fixed/static world anchor (a door hinged to a wall, not another door). BEPU constraints only connect `BodyHandle`s (dynamic/kinematic), not `StaticHandle`s; a fixed anchor needs a synthetic kinematic body, which is a separate design.
- Angle/distance *limits* and motors (`AngularHinge` limits, `TwistServo`, `LinearAxisMotor`, etc.) — only the three unconstrained/spring-based types above.
- `Weld` (fully rigid, no relative motion at all) and every other BEPU constraint type not listed above.
- Studio UI for authoring joints (components are already editable generically through Studio's JSON component editor; no dedicated joint-gizmo UI in this pass).

## Architecture

### Component contracts (new cross-entity-reference pattern)

Each joint component lives on the "source" entity and references a second ("connected") entity by ID — the same established pattern `Rekall.CameraTarget3D.TargetEntityId` already uses, applied here for the first time to physics. All three share the same anchor/spring authoring shape where applicable, matching `Rekall.PhysicsMaterial3D`'s existing `SpringFrequency`/`DampingRatio` naming:

```csharp
[RekallAgeComponent("Ball Socket Joint", Description = "...")]
public sealed class RekallAgeBallSocketJointComponent : RekallAgeComponent
{
    public string ConnectedEntityId { get; init; } = string.Empty;
    public double AnchorAX/AnchorAY/AnchorAZ { get; init; } // local offset in this entity's own space
    public double AnchorBX/AnchorBY/AnchorBZ { get; init; } // local offset in the connected entity's space
    public double SpringFrequency { get; init; } = 30;
    public double DampingRatio { get; init; } = 1;
}
```

`Rekall.HingeJoint` adds `AxisX/Y/Z` (default `0,1,0`): one authored axis vector, interpreted in *both* bodies' local frames identically. This is a deliberate simplification — correct only when both bodies start reasonably co-oriented, which covers the common authoring case (a door and its frame both axis-aligned) without requiring per-body world-to-local axis transforms. Documented as a known limitation, not silently wrong.

`Rekall.DistanceJoint` has `ConnectedEntityId`, `TargetDistance` (default 1), `SpringFrequency`, `DampingRatio` — no anchor offsets, since `CenterDistanceConstraint` connects body centers.

Both entities in a joint pair must be dynamic (have `Rekall.Rigidbody2D`/`3D` + a collider); a joint referencing a missing, static-only, or self (`ConnectedEntityId == own id`) entity is skipped with a structured runtime observation, not a crash.

### Runtime integration and persistence

`RekallAgeBepuPhysicsSystem.PersistentPhysicsWorld` gains a fourth persistent collection, `Dictionary<string, PersistentJoint>` keyed by `"{sourceEntityId}:{componentType}"` (an entity could in principle carry more than one joint component type), mirroring the exact persistence pattern `_dynamicBodies`/`_staticBodies` already use:

```csharp
private readonly record struct PersistentJoint(ConstraintHandle Handle, string Signature);
```

Each frame, after body sync (existing `AddDynamic`/`RemoveDynamic` etc. — unchanged), a new `SyncJoints` step:
1. Scans entities for `Rekall.BallSocketJoint`/`Rekall.HingeJoint`/`Rekall.DistanceJoint` components.
2. For each, resolves `ConnectedEntityId` to both bodies' *current* `BodyHandle`s via the now-current `_dynamicBodies` dictionary (skip + observation if either side is missing/not dynamic/self-referential).
3. Builds a signature string from the authored joint properties **and both resolved `BodyHandle.Value` ints**. Including the handle values is what makes this safe against handle recycling: if a connected body was removed and re-added this frame (a body-level shape/config change), its `BodyHandle.Value` changes, the joint's signature changes, and the joint is correctly torn down and rebuilt against the new handle — the same hazard already solved for `_dynamicFilters`/`_staticFilters` in the collision-layers work, solved here the same way but via the existing signature-diff mechanism instead of a second dictionary.
4. Joints whose signature is unchanged from last frame are left alone (BEPU's own solver state — warm-start impulses — persists naturally). Joints whose signature changed, or that no longer have a matching source, are removed via `Simulation.Solver.Remove(handle)` and (if still wanted) re-added via `Simulation.Solver.Add<T>(handleA, handleB, ref description)`.

This reuses `ConfigurationSignature`-style diffing already established for bodies rather than inventing a new lifecycle pattern.

## Error Handling

- A joint referencing a nonexistent, non-dynamic, or self entity id produces a `runtime.physics.joint_unresolved`-style observation (warning severity, matching the existing `runtime.destruction.terrain_entity_not_found` pattern) and is simply not added to the solver that frame — never a thrown exception or a blocked frame.
- No new validation error codes beyond the existing generic `REKALL_COMPONENT_PROPERTY_UNKNOWN`/`REKALL_COMPONENT_RESERVED_TYPE_UNKNOWN` machinery, which already covers unknown-property/unknown-type rejection for any new component automatically once registered.

## Testing

- Unit-level: a `BallSocketJoint` test proving two initially-separated dynamic spheres are pulled together and stay within anchor distance after simulating; a `HingeJoint` test proving relative rotation stays constrained to the authored axis while position stays pinned; a `DistanceJoint` test proving two bodies settle at the authored target distance.
- A persistence/handle-recycling regression: change a joint's authored `SpringFrequency` mid-run (forcing a body's own reconfiguration is not required for this specific case — but a body-shape change on one endpoint while a joint is attached must not crash or silently reference a stale handle).
- A missing/self-referential `ConnectedEntityId` regression: confirms a structured observation, not an exception, and confirms the source entity's own simulation is otherwise unaffected.
- Schema/catalog discoverability regression for all three new component types, matching the existing `ReservedComponentTypeCatalogMatchesIndexedBuiltInSchemas`/`SearchComponentSchemasCommand` pattern.
- Per repo policy (`AGENTS.md`): every test above runs as a narrowly targeted filter during implementation; full-suite runs only at an explicit final gate.
