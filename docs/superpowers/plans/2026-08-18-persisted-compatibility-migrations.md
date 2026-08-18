# Persisted Compatibility And Migration Implementation Plan

Date: 2026-08-18

Design: `docs/superpowers/specs/2026-08-18-persisted-compatibility-migrations-design.md`

## Task 1: Establish document schema boundaries

- [x] Add failing project/scene store tests for current, missing, future,
  malformed, negative, and non-integral schema versions.
- [x] Add a typed compatibility exception and bounded raw schema probe.
- [x] Add explicit scene schema version 1 while treating a missing version as
  legacy schema 0 in memory.
- [x] Enforce the same boundary in both stores and keep load read-only.
- [x] Run focused tests and commit.

Verified 2026-08-18: 14 focused project/scene store tests pass. Both stores
probe bounded JSON before typed deserialization, normalize implicit schema 0 to
the current in-memory contract without rewriting on load, persist explicit
schema 1, and return typed stable failures for malformed, invalid, or future
schema facts.

## Task 2: Add deterministic whole-project inspection

- [x] Add fixtures for current, legacy, mixed, future, malformed, missing,
  oversized, excessive-count, and reparse-path cases.
- [x] Implement bounded, ordinal, read-only compatibility inspection across the
  manifest and live scene glob.
- [x] Return stable status/code/version/migratability facts, blockers,
  limitations, and exact next actions.
- [x] Register `rekall.compatibility.inspect_project` for direct, CLI, and MCP
  discovery and advertise it in engine status.
- [x] Run focused tests and commit.

Verified 2026-08-18: focused compatibility inspection, real CLI process, MCP
catalog, and engine-status coverage pass at 14/14. Inspection keeps source bytes
unchanged; orders the project manifest before ordinal scene paths; isolates
legacy, current, future, malformed, missing, oversized, and excessive-count
facts; refuses reparse traversal through a real Windows junction; and returns
exact blockers and next actions.

## Task 3: Add explicit atomic migration

- [ ] Add failing tests for dry-run immutability, apply, exact backup bytes,
  idempotence, blocker refusal, rollback, containment, and reparse rejection.
- [ ] Implement schema-0-to-1 typed transforms for project and scene documents.
- [ ] Stage all output, create a bounded backup set, replace atomically, and
  roll back partial replacement failures.
- [ ] Register `rekall.compatibility.migrate_project` with dry-run default and
  explicit apply mode.
- [ ] Run focused tests and commit.

## Task 4: Product integration and release evidence

- [ ] Document compatibility policy and operator/agent workflow.
- [ ] Extend installed acceptance with legacy inspection, dry-run, migration,
  current reinspection, and future-version refusal using shipped binaries.
- [ ] Run strict builds, complete Debug, and two-pass Release installed gate.
- [ ] Record counts, codes, archive hash, limitations, and next security
  priority in `docs/production/PROGRESS.md`.
- [ ] Review, commit, and preserve `codex/production-foundation`.
