# Optimistic Document Revisions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use
> superpowers:executing-plans and superpowers:test-driven-development.

**Goal:** Detect stale project and scene writers before publication and return
inspectable recovery facts.

**Spec:** `docs/superpowers/specs/2026-08-18-optimistic-document-revisions-design.md`

### Task 1: Generic revisioned compare-and-publish

- [ ] Write failing revision, stale-write, missing/existing, busy, cancellation,
  cleanup, and concurrent-writer tests.
- [ ] Add exact snapshot revision tokens and bounded sibling-lock publication.
- [ ] Return stable coded conflict/busy failures and preserve destination bytes.

### Task 2: Project and scene mutation integration

- [ ] Add versioned project/scene loads without changing persisted shapes.
- [ ] Convert every ordinary project/scene mutation to conditional save.
- [ ] Prove intervening changes survive and stale commands return recovery facts.

### Task 3: Agent inspection and transaction append serialization

- [ ] Expose bounded project/scene revisions through CLI/MCP inspection.
- [ ] Serialize cooperating transaction-log appends so concurrent commands do
  not discard history.
- [ ] Add schema/discovery and repeated concurrent command coverage.

### Task 4: Installed conflict proof and complete gate

- [ ] Exercise competing shipped-CLI writers, exact conflict reporting,
  reload/retry, validation, and no lock/temp leaks.
- [ ] Run complete Debug and locked two-pass Release/distribution verification.
- [ ] Record exact evidence and remaining content-merge boundary in progress.
