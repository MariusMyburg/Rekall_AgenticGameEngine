# Collision Layers & Masks Design

**Status:** Approved (bounded, user pre-approved architecture and all follow-on work in this session; see brainstorming transcript 2026-08-27).

## Purpose

Rekall AGE's physics has no way to make two collidables ignore each other. Every collider collides with every other collider in the world, in both physical response (BEPU contact generation) and the two independent generic event systems (`collision.begin/stay/end`, `trigger.enter/stay/exit`). This blocks ordinary authored scenes that need selective collision (e.g. player bullets that should not hit the player, ragdoll self-collision, background dressing that should never physically interact).

This is the first of four independent physics-breadth sub-projects tracked in `docs/production/PROGRESS.md`'s current-gaps list (collision layers/masks, generic joints/constraints, a 2D physics material contract, and authored 2D angular control); the other three are separate, later specs.

## Global Constraints

- Engine contracts remain generic; no gameplay-specific layer names are built in.
- Existing authored scenes with no `Rekall.CollisionFilter` component must behave exactly as they do today (collide with everything) — this is strictly additive, opt-in behavior.
- Applies uniformly to 2D and 3D colliders, to physical collision response, and to both generic event systems (`RekallAgeCollisionEventSystem`, `RekallAgeTriggerEventSystem`) — one shared rule, not three separate ones that could drift.
- No new project-file or module-registration syntax; this is a new built-in component type, following the same catalog/schema/validation registration path every other built-in component uses.

## Architecture

### Component contract

A new built-in component, `Rekall.CollisionFilter`, attached alongside any collider or trigger component on the same entity — the same composition pattern as the existing separate `Rekall.Trigger` marker component, not new fields duplicated across the six collider component types.

```json
{ "layer": "player", "collidesWith": ["enemy", "terrain", "pickup"] }
```

- `layer` (string, default `"default"`): the name this entity's collidable belongs to.
- `collidesWith` (string array, default/absent = collides with everything): the layers this entity's collidable is allowed to interact with.

### Matching rule

A pair of entities is allowed to interact only if **each side accepts the other's layer**:

- Entity A interacts with entity B only if: A has no `Rekall.CollisionFilter`, OR A's `collidesWith` is null/absent, OR A's `collidesWith` contains B's `layer`.
- The same check applies from B's side.
- Both directions must pass (symmetric AND). This avoids one-sided surprises where A allows B but B silently excludes A.

This rule lives in one new file, `src/Rekall.Age.Runtime/RekallAgeCollisionFilter.cs`, as a small pure static helper:

```csharp
public static class RekallAgeCollisionFilter
{
    public static bool Allows(RekallAgeRuntimeEntity a, RekallAgeRuntimeEntity b);
    public readonly record struct Rule(string Layer, IReadOnlySet<string>? CollidesWith)
    {
        public static Rule From(RekallAgeRuntimeEntity entity);
        public bool Accepts(string otherLayer);
    }
}
```

`Allows` reads each entity's `Rekall.CollisionFilter` component (if any) into a `Rule`, and returns `left.Accepts(right.Layer) && right.Accepts(left.Layer)`.

### Integration points (three independent systems; confirmed by reading each — none share pair-detection logic today)

1. **`RekallAgeBepuPhysicsSystem`** (physical response). BEPU already exposes exactly the right hook: `RekallAgeBepuNarrowPhaseCallbacks.AllowContactGeneration(int workerIndex, CollidableReference a, CollidableReference b, ref float speculativeMargin)` currently always returns `true`. A new `CollidableProperty<RekallAgeCollisionFilter.Rule> _filters` is allocated identically to the existing `CollidableProperty<PhysicsMaterial> _materials` (same two allocation sites: `AddDynamic`/`AddStatic`, immediately next to the existing `_materials.Allocate(handle) = item.Material;` lines). `AllowContactGeneration` becomes `return _filters[a].Accepts(_filters[b].Layer) && _filters[b].Accepts(_filters[a].Layer);` (constructed once via a small helper taking the two `Rule`s, to keep the symmetric-AND logic in the one shared place rather than duplicated inline). This suppresses contact generation entirely before any contact math runs — cheapest possible rejection point.
2. **`RekallAgeCollisionEventSystem`** (`collision.*` event facts). Its existing pairwise loop (`Overlaps(left, right)`) gets a `&& RekallAgeCollisionFilter.Allows(left.Entity, right.Entity)` guard alongside the existing overlap check.
3. **`RekallAgeTriggerEventSystem`** (`trigger.*` event facts). Same guard added to its own pairwise overlap loop.

### Registration surface

Built-in components are declared once, declaratively, in
`src/Rekall.Age.Modules/BuiltIns/RekallAgeBuiltInModule.cs` (confirmed by
reading the real `Rekall.Trigger`/`Rekall.PhysicsMaterial3D`/
`Rekall.InputActionMap` declarations there) — reflection
(`RekallAgeModuleIndexer`) turns each `[RekallAgeComponent]`-attributed class
into the schema every discovery/validation/Studio path consumes, and
`RekallAgeBuiltInComponentTypeCatalog.Types` into its known-type set. There is
no separate hand-maintained schema dictionary to update. Concretely:

```csharp
[RekallAgeComponent("Collision Filter", Description = "Restricts which collidables this entity's collider/trigger physically interacts with and generates collision/trigger events against. An entity with no Rekall.CollisionFilter, or an empty/absent collidesWith, interacts with every layer (default, zero-authoring-change behavior).")]
public sealed class RekallAgeCollisionFilterComponent : RekallAgeComponent
{
    [RekallAgeProperty(Description = "The layer name this entity's collidable belongs to.")]
    public string Layer { get; init; } = "default";

    [RekallAgeProperty(Description = "Native JSON array of layer names this entity's collidable is allowed to interact with. Pass a native array, never an encoded string. Absent/empty means it interacts with every layer.")]
    public string[]? CollidesWith { get; init; }
}
```

Registered with `builder.RegisterComponent<RekallAgeCollisionFilterComponent>();`
next to `RegisterComponent<RekallAgeTriggerComponent>()` in the same file's
`Configure`. Separately, `RekallAgeBuiltInComponentTypeCatalog.Types` (in
`src/Rekall.Age.World/RekallAgeBuiltInComponentTypeCatalog.cs`) is a distinct,
hand-maintained `HashSet<string>` gating `IsUnknownReserved` — an authored
component type starting with `Rekall.` that isn't in this set is rejected as
an unrecognized reserved type before it ever reaches the schema/reflection
path above. `"Rekall.CollisionFilter"` must be added there too (alphabetically
after `"Rekall.Trigger"`), or authoring the component would be rejected.

The `CollidesWith` array's `[]`-suffixed .NET type name makes
`RekallAgeProjectValidator`'s existing `ExpectedStructuredShape` check treat it
as a required native JSON array automatically (the same mechanism that already
rejects a JSON-encoded string for `Rekall.InputActionMap.Actions`) — no new
validation code needed for that part.

At runtime, `RekallAgeCollisionFilter.Rule.From` reads the component the same
way `RekallAgeInputActionSystem` already reads `Rekall.InputActionMap.Actions`:
`entity.FindComponent("Rekall.CollisionFilter")`, then
`TryGetPropertyValue(component.Properties, "layer", ...)` and
`TryGetPropertyValue(component.Properties, "collidesWith", ...)` expecting a
`JsonArray` of strings (property names are camelCased at the JSON boundary,
matching every other built-in component).

No new CLI/MCP command is needed: components are already authored through the
existing generic `rekall.scene.*` entity/component mutation commands, matching
the "generic mutation helpers" rule in `AGENTS.md`.

## Error Handling

- No new error codes. An unset or empty `collidesWith` is valid (means "collides with everything"), not an error.
- `layer`/entries in `collidesWith` are free-form authored strings, not a registered enum — no validation rejects an unrecognized layer name, matching the existing generic tag-system philosophy (`WithTag`/`WithoutTag`).

## Testing

- Unit tests for `RekallAgeCollisionFilter.Allows`/`Rule` covering: no component on either side (allowed), one-sided exclusion (symmetric AND blocks it), matching layers on both sides (allowed), default `collidesWith` absent (allowed with everything).
- `RekallAgeBepuPhysicsSystem` regression: two overlapping dynamic bodies on non-accepting layers pass through each other (no physical response) across several simulated frames; two bodies on accepting layers still collide as before (regression coverage for the zero-authoring-change default).
- `RekallAgeCollisionEventSystem`/`RekallAgeTriggerEventSystem` regression: an overlapping pair on non-accepting layers emits no `collision.begin`/`trigger.enter` event; an overlapping pair with no filter still emits events exactly as before.
- `RekallAgeBuiltInComponentTypeCatalog`/validation regression: `Rekall.CollisionFilter` is a known type with a discoverable schema.
- Per the repo's current testing policy (`AGENTS.md`), all of the above run as narrowly targeted filters during implementation; no full-suite runs except at an explicit final delivery gate.

## Out of Scope

- Generic joints/constraints, a dedicated 2D physics material contract, and authored 2D angular control are separate, later sub-projects (already scoped in `PROGRESS.md`'s gap list).
- No Studio UI for editing layers in this pass (Studio already edits arbitrary component JSON generically; a dedicated layer-picker widget is a future Studio ergonomics improvement, not required for the engine capability to exist and be authorable).
- No layer-count limit or bitmask-based storage; string sets are the storage format, prioritizing agent-authorability over raw performance at this scale (matches "collision layers/masks" being requested as a discoverable, inspectable primitive, not a hand-tuned bitmask).
