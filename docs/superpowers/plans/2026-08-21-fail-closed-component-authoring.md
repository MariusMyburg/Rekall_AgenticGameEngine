# Fail-closed component authoring plan

## Goal

Reject invented engine-reserved component types at every primary scene mutation
boundary before they enter persisted content, while preserving arbitrary
agent-owned module component authoring.

## Contract

1. Define one indexed catalog for exact built-in `Rekall.*` component types and
   conservative spelling suggestions.
2. Make `rekall.component.add` reject an unknown reserved type without writing
   the scene; return `REKALL_COMPONENT_RESERVED_TYPE_UNKNOWN` plus a direct
   component-schema search action.
3. Apply the same rule with indexed targets in
   `rekall.scene.apply_blueprint` before its transaction begins.
4. Continue accepting non-reserved agent/module types such as `Game.*`.
5. Reuse the shared catalog in project validation so authoring and final audit
   cannot drift.

## Verification

- Add failing tests for direct mutation, blueprint mutation, persistence
  immutability, precise targets, suggestions, and `Game.*` acceptance.
- Run focused world/validation/dispatch tests.
- Run the locked build and installed-distribution matrix.
- Repeat the unchanged real-Qwen benchmark and record the next measured result.
