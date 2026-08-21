# Scene Blueprint Component Normalization Plan

## Goal

Make the generic scene blueprint authoring boundary accept predictable,
unambiguous component representations emitted by agents while preserving the
canonical `{ "type": "...", "properties": { ... } }` contract and failing
closed on conflicting data.

## Evidence

Installed real-Qwen benchmark 23 compiled authored gameplay and produced a
nonblank frame, but nine blueprint calls failed and successful flat component
objects silently discarded sibling properties. The run exhausted its protected
repair reserve with 13 correctly reported final validation blockers and no
package.

## Contract

- Canonical component objects remain unchanged.
- A component with a discriminator plus sibling fields treats those siblings as
  component properties.
- Exact lowercase `type` is the discriminator when a component also needs a
  case-sensitive `Type` runtime property.
- A `Properties` name/value array is accepted only when every entry has one
  non-empty unique name and a value.
- Explicit and flattened properties may merge only when their names do not
  conflict case-insensitively.
- Missing types, duplicate/conflicting properties, malformed JSON, excessive
  nesting, and oversized encoded payloads continue to fail closed with an
  indexed JSON path.

## Verification

1. Add red dynamic-dispatch tests for safe flat/canonical forms and ambiguous
   conflicts.
2. Implement a component-specific JSON converter in the world contract.
3. Run focused command and blueprint tests.
4. Run the locked Release pipeline twice and the installed-distribution matrix.
5. Run the unchanged empty-project Lumen Vault benchmark with real
   `qwen3.5:35b`, record evidence, then commit and push.
