# Persisted Compatibility And Migration Implementation Plan

Date: 2026-08-18

Design: `docs/superpowers/specs/2026-08-18-persisted-compatibility-migrations-design.md`

## Task 1: Establish document schema boundaries

- [ ] Add failing project/scene store tests for current, missing, future,
  malformed, negative, and non-integral schema versions.
- [ ] Add a typed compatibility exception and bounded raw schema probe.
- [ ] Add explicit scene schema version 1 while treating a missing version as
  legacy schema 0 in memory.
- [ ] Enforce the same boundary in both stores and keep load read-only.
- [ ] Run focused tests and commit.

## Task 2: Add deterministic whole-project inspection

- [ ] Add fixtures for current, legacy, mixed, future, malformed, missing,
  oversized, excessive-count, and reparse-path cases.
- [ ] Implement bounded, ordinal, read-only compatibility inspection across the
  manifest and live scene glob.
- [ ] Return stable status/code/version/migratability facts, blockers,
  limitations, and exact next actions.
- [ ] Register `rekall.compatibility.inspect_project` for direct, CLI, and MCP
  discovery and advertise it in engine status.
- [ ] Run focused tests and commit.

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
