# Persisted Document Recovery Design

## Purpose

Atomic publication and optimistic revisions prevent engine-caused torn and
lost writes, but cannot undo storage damage, external-tool truncation, or an
invalid manual edit. Rekall AGE needs an explicit recovery contract that an
agent can inspect and invoke without silently hiding corruption.

## Contract

Successful conditional project and scene replacements retain exactly one
previous, already-loaded byte snapshot beneath `.rekall/recovery`, preserving
the document's project-relative path. The previous snapshot is published as
part of the same replacement boundary where the host filesystem supports it.
Creation has no previous version; unconditional compatibility migration keeps
its separate exact-backup contract.

Recovery inspection accepts only the project manifest or a named scene. It
reports bounded primary/previous availability, exact SHA-256 revisions, schema
and parse status, recoverability, stable diagnostic codes, and next actions. It
never rewrites the project.

Explicit restore requires the caller's expected current revision, validates the
previous snapshot against the document schema and size/depth limits, and
atomically restores it only if the damaged primary still matches. The displaced
primary is quarantined with its revision for diagnosis. Quarantines are bounded
and deterministically pruned. A restored document is then loaded and validated
normally; no read path silently falls back.

## Boundaries

This tranche covers the project manifest and scene documents, which define the
authoring graph. Asset/cache regeneration and transaction preimage restoration
retain their existing subsystem-specific policies. Recovery is one-version
rollback, not arbitrary history, autosave, content merge, or cloud backup.

## Verification

Tests cover exact previous-byte retention, no backup on failed/stale writes,
malformed/oversized previous rejection, stale restore rejection, atomic restore,
quarantine bounds, path confinement, legacy/current compatibility, MCP schemas,
and successful mutation after restore. Installed proof damages a shipped-CLI
scene, observes a coded normal failure, inspects recovery, explicitly restores,
validates, mutates, and verifies cleanup before the complete product gate.
