# Structured Component Authoring Implementation Plan

Date: 2026-08-21

Status: complete; unchanged benchmark exposed the next independent blocker

## Objective

Make array/object component properties fail closed and remain directly
inspectable. Agent tools must author native JSON arrays and objects rather than
JSON encoded inside strings, validation must offer a lossless repair when the
encoded value is parseable, and runtime input diagnostics must explain why a
semantic sample did not reach a declared action.

This is a generic component-authoring and diagnostics contract. It does not add
game-specific controls or behavior.

## Measured failure

Fresh installed Lumen Vault benchmark 18 proved typed semantic input by using
the exact `semanticActions` shape in all six runtime checkpoints. AGE then
accepted `Rekall.InputActionMap.Actions` as an encoded JSON string. Runtime
correctly projected no actions but returned only an empty action list, so the
model repeatedly revised valid C# and exhausted its turn budget.

## Tasks

- [x] Add failing runtime tests for malformed action-map structure and
  undeclared injected semantic actions.
- [x] Add failing validation tests for structured values encoded as strings,
  including a lossless native-JSON repair suggestion.
- [x] Document the exact binding-array shape in component schema and mutation
  tool guidance.
- [x] Emit bounded runtime observations for malformed maps and undeclared
  semantic samples.
- [x] Block malformed known structured property kinds in project validation.
- [x] Pass focused and full Release verification, rebuild installed binaries,
  and rerun the unchanged real-Qwen Lumen Vault benchmark.
- [x] Record evidence, commit, and push before continuing to the next measured
  blocker.

Benchmark 19 authored native action binding arrays and runtime exposed 13
declared actions, proving the benchmark-18 blocker is removed. The run then
exposed a separate query-contract ambiguity: authored C# passed a unique entity
name to `FindEntity`, whose implementation accepted only an opaque id, returned
null, and let the module exit silently. That is the next generic repair target;
this plan does not claim the complete Lumen Vault acceptance is green.
