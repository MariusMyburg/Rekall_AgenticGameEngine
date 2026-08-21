# Schema-aware component admission plan

## Goal

Prevent invalid built-in component properties from entering a project through
the production authoring tools, eliminating late validation cleanup loops while
preserving the generic world layer and agent-owned component freedom.

## Architecture

1. Define a world-layer component-property admission interface that returns
   structured command errors and can be omitted by low-level consumers.
2. Implement the production policy above the world layer from indexed built-in
   component schemas.
3. Inject it into direct component add, property set, and scene blueprint tools
   in the default engine registry.
4. Reject unknown property names, invalid structured JSON shapes, and numeric
   values outside declared ranges before transaction capture or persistence.
5. Return exact allowed properties and component-schema search recovery.
6. Continue allowing arbitrary `Game.*` properties and valid built-in values.

## Verification

- Prove add, set, and blueprint rejection red-first, including unchanged scene
  revisions/resources, valid built-in acceptance, and `Game.*` acceptance.
- Run focused policy/world/dispatch/validation tests.
- Run the locked installed-distribution gate.
- Repeat the unchanged real-Qwen benchmark and record the next blocker.
