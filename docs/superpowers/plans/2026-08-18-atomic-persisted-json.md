# Atomic Persisted JSON Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use
> superpowers:executing-plans and superpowers:test-driven-development.

**Goal:** Ensure every engine-owned JSON store loads one bounded immutable
snapshot and publishes complete durable files atomically.

**Spec:** `docs/superpowers/specs/2026-08-18-atomic-persisted-json-design.md`

### Task 1: Shared bounded snapshot and atomic publisher

**Files:**
- Create: `src/Rekall.Age.Core/Persistence/RekallAgeAtomicFile.cs`
- Create: `src/Rekall.Age.Core/Persistence/RekallAgeBoundedFileSnapshot.cs`
- Create: `tests/Rekall.Age.Tests/Core/AtomicPersistedFileTests.cs`

- [x] Write failing tests for exact reads, over-limit/growth/short-read failure,
  cancellation, durable replacement, prior-byte preservation, and temporary-file
  cleanup.
- [x] Implement one-handle bounded snapshots and same-directory atomic publish.
- [x] Run the focused core tests and `git diff --check`.

### Task 2: One-snapshot project and scene documents

**Files:**
- Modify: `src/Rekall.Age.Core/Compatibility/RekallAgeDocumentSchemaProbe.cs`
- Modify: `src/Rekall.Age.Project/RekallAgeProjectStore.cs`
- Modify: `src/Rekall.Age.World/RekallAgeSceneStore.cs`
- Modify: `tests/Rekall.Age.Tests/Project/ProjectManifestTests.cs`
- Modify: `tests/Rekall.Age.Tests/World/SceneStoreTests.cs`

- [x] Write failing over-depth, over-limit, immutable snapshot, repeated-reader,
  cancellation, and exact-existing-destination tests.
- [x] Make schema probe expose one bounded snapshot and deserialize that exact
  byte sequence with the same depth limit.
- [x] Publish project/scene saves only through the atomic writer.
- [x] Run project/world/compatibility/transaction regressions.

### Task 3: Migrate remaining engine-owned JSON stores

**Files:**
- Modify asset catalog/pipeline, prefab, render-plan, and transaction-log stores.
- Add focused store regression and adversarial tests.

- [x] Apply explicit per-document size/depth limits and bounded snapshot reads.
- [x] Replace direct live-file writes with the shared atomic publisher.
- [x] Prove serialized compatibility and no temporary-file enumeration/leaks.

### Task 4: Installed mutation proof and full product gate

**Files:**
- Create: `eng/accept-installed-atomic-json.ps1`
- Modify: `eng/accept-distribution.ps1`
- Modify: production audit/progress and this plan.

- [x] Exercise repeated shipped-CLI mutations and inspections while independent
  readers parse the live documents; require zero malformed reads and zero temp
  siblings.
- [x] Run the complete Debug suite.
- [x] Run the locked zero-warning two-pass Release/distribution gate.
- [x] Record exact evidence and the explicit lost-update limitation.
