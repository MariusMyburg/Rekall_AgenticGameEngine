# Optimistic Document Revisions Design

## Purpose

Atomic publication prevents malformed partial files, but two valid writers can
still overwrite one another. Rekall AGE needs a generic optimistic-concurrency
contract so CLI, MCP, Studio, and future clients can detect stale project and
scene edits instead of silently losing them.

## Contract

Every bounded document snapshot has a lowercase SHA-256 revision over its exact
bytes. Versioned loads return the typed value and that revision. Conditional
publication accepts the expected revision and succeeds only when the current
destination has the same revision. Creation uses an explicit missing revision.

The compare and atomic publication execute while holding a bounded exclusive
sibling lock. This closes the compare/publish race across cooperating processes;
ordinary readers remain lock-free and continue to observe only complete files.
Lock acquisition is cancellable and fails with a stable busy diagnostic after a
short bounded interval.

A mismatch fails closed with `REKALL_DOCUMENT_REVISION_CONFLICT`, identifies the
document, and reports expected and current revision tokens. It does not publish,
delete, or truncate either document. A caller can reload, reapply its semantic
operation, and retry.

## Integration

Project and scene mutation commands load versioned snapshots and conditionally
save against the exact loaded revision. Creation and explicit compatibility
migration retain intentional create/unconditional semantics. Inspection exposes
revision tokens in bounded results so agents can coordinate explicit workflows
without dumping document bytes.

The transaction log remains append-history infrastructure and requires a
separate append-serialization policy; this tranche must not imply content merge,
CRDT behavior, or multi-user collaboration UX.

## Verification

Focused tests prove revision determinism, missing/existing compare-and-publish,
stale rejection with prior-byte preservation, cancellation/busy handling, and
cross-process-safe serialization. Command tests prove stale project and scene
mutations cannot silently overwrite intervening changes. Installed binaries run
competing mutations, surface the exact conflict code, reload/retry successfully,
validate the result, and leave no lock/temp debris before the full product gate.
