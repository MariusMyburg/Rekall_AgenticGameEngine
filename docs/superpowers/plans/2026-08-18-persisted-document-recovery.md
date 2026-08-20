# Persisted Document Recovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use
> superpowers:executing-plans and superpowers:test-driven-development.

**Goal:** Make project/scene corruption explicitly inspectable and recoverable
from one bounded last-known-good version.

**Spec:** `docs/superpowers/specs/2026-08-18-persisted-document-recovery-design.md`

### Task 1: Atomic previous-version retention

- [x] Write failing exact-backup, stale/failure preservation, cancellation,
  creation, and cleanup tests.
- [x] Extend conditional publication with an atomic same-volume previous target.
- [x] Keep all paths confined and bounded.

### Task 2: Project and scene recovery store

- [x] Retain previous validated bytes for conditional project/scene saves.
- [x] Add read-only inspection with primary/previous revisions and stable codes.
- [x] Add explicit revision-guarded restore and bounded corrupt quarantine.

### Task 3: Agent commands, CLI, and MCP

- [x] Expose generic inspect/restore commands for manifest or named scene.
- [x] Return executable next actions without silent fallback.
- [x] Prove schemas, compatibility, validation, and post-restore mutation.

### Task 4: Installed damage/recovery proof and complete gate

- [ ] Damage a shipped-CLI-authored scene, inspect, restore, validate, mutate,
  and verify bounded recovery artifacts.
- [ ] Run complete Debug and locked two-pass Release/distribution verification.
- [ ] Record exact evidence and remaining backup/history boundaries.
