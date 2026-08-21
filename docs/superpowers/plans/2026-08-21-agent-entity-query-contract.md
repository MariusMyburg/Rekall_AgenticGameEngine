# Agent Entity Query Contract Implementation Plan

Date: 2026-08-21

Status: complete

## Objective

Make the common single-entity query safe and unsurprising for agent-authored
modules while preserving existing opaque-id behavior. `FindEntity` will resolve
an exact id first, then one unique exact name; ambiguous names fail closed and
`EntitiesNamed` remains the explicit multi-match API.

This is a generic query primitive, not a game-specific entity class or scene
scan.

## Measured failure

Fresh installed Lumen Vault benchmark 19 compiled a valid module and projected
native semantic actions. Its system called `FindEntity(world, "OrbPlayer")`.
The helper's name appeared general but its implementation accepted only an
opaque entity id, returned null for the unique authored name, and let the system
exit without movement or a diagnostic.

## Tasks

- [x] Add failing SDK tests for id precedence, unique exact-name fallback,
  case behavior, and ambiguous-name isolation.
- [x] Add an executable project-module test proving a module written with a
  unique authored name can mutate the intended runtime entity.
- [x] Implement the backward-compatible lookup and exact `FindEntity` /
  `EntitiesNamed` SDK descriptions and examples.
- [x] Pass focused and full Release verification, rebuild the installed
  distribution, and rerun the unchanged real-Qwen Lumen Vault benchmark.
- [x] Record evidence, commit, and push before continuing to the next measured
  blocker.

## Outcome

The TDD selection failed in all three new lookup scenarios before the SDK
change and passed 7/7 afterward. Full verification passed 976/976 engine tests
and 7/7 Studio tests. The locked Release pipeline then passed both independent
test runs with zero build warnings/errors and completed the installed-product
acceptance matrix. The resulting 1,186-file win-x64 archive is 201,523,273
bytes with SHA-256
`F8AF2C5D45182FF3FB5BB7A663BA05F74734ABDFD545435883784784AF5740A7`.

Unchanged real-Qwen benchmark 20 did not complete. It reached 64 tool calls
(32 successful, 32 rejected), produced a nonblank 960x540 frame with two
renderables, but no package. The query contract was no longer the immediate
failure: Qwen repeatedly supplied `inputs` and `assertions` as JSON-encoded
strings, so the protected executable checkpoint rejected them before runtime
execution. That measured typed-argument boundary is the next generic tranche.
