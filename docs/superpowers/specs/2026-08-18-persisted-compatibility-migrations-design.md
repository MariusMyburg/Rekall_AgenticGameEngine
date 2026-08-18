# Persisted Compatibility And Migration Design

Date: 2026-08-18

## Problem

Rekall AGE persists projects and scenes as agent-readable JSON, but the current
compatibility boundary is incomplete. Project manifests serialize schema
version 1 without enforcing it during load. Scene documents have no explicit
document schema. Consequently a newer, malformed, or ambiguous document can
flow into authoring, validation, player, renderer, and Studio consumers before
the engine explains the incompatibility.

An AI-first engine needs the opposite behavior: inspect first, describe exact
facts and actions, migrate only when explicitly requested, and never silently
rewrite an unsupported future format.

## Decision

The first compatibility slice covers the engine-owned project manifest and
scene documents. Package manifests, module SDK manifests, receipts, animation
clips, and failure reports keep their existing independent version contracts;
the project inspection result will identify them as separate boundaries rather
than conflating their version numbers.

Project and scene schema version 1 are the current formats. Existing scene
documents without `schemaVersion` are legacy schema 0 and are mechanically
migratable by adding `schemaVersion: 1`. A project manifest without
`schemaVersion` is likewise legacy schema 0. Explicit future, negative,
non-integral, or malformed versions are blockers and are never guessed.

## Contracts

### Store boundary

The shared project and scene stores inspect raw JSON before typed
deserialization. They accept current schema 1 and legacy schema 0 in memory,
normalizing legacy documents to schema 1. They reject unsupported or malformed
schema facts with a typed compatibility exception carrying a stable code,
document kind, path, detected version when known, and current version.

Saving always writes current schema 1. Loading alone never writes. This keeps
all existing consumers behind one compatibility boundary without duplicating
checks in players, renderers, commands, or Studio.

### Read-only inspection

`rekall.compatibility.inspect_project` examines the manifest and scene files in
ordinal path order without loading modules or executing project code. Every
document receives one status: `current`, `legacy`, `future`, `malformed`, or
`missing`. The bounded result reports detected/current versions, whether an
automatic migration exists, blockers, limitations, and exact next actions.

Inspection has deterministic ceilings for document count and file size. An
exceeded ceiling becomes an explicit blocker rather than an unbounded scan.

### Explicit migration

`rekall.compatibility.migrate_project` consumes the same preflight. Its default
mode is dry-run. Apply mode is allowed only when every non-current document has
a known migration and no blocker exists.

Apply stages all rewritten bytes before replacing any source. It creates a
bounded backup set under `.rekall/migrations`, writes through same-directory
temporary files, and rolls back already-replaced files if any replacement
fails. Output JSON is deterministic and uses the engine's normal camel-case,
indented format. Re-running migration on current content is a successful no-op.

The initial schema-0-to-1 migration only adds the explicit version and
normalizes through the typed document contracts; it does not invent gameplay
content or reinterpret components.

## Agent usability

Both commands return structured facts suitable for MCP and concise CLI output.
The inspection command is recommended and advertised by engine status. A
blocked result names exact paths and codes. A migratable result returns the
exact dry-run/apply command. Migration never asks the engine to author content;
it only transforms engine-owned persisted contracts.

## Security and failure posture

- Future versions fail closed and remain byte-for-byte untouched.
- Malformed JSON and invalid schema tokens are isolated per document during
  inspection and block apply.
- Reparse points are rejected for migration source, backup, staging, and target
  paths.
- Project-root containment is checked before reads or writes.
- File and document-count bounds prevent compatibility tools from becoming an
  unbounded parser or filesystem walker.
- Backups have a bounded retention count and are never treated as live scenes.
- Migration errors preserve the original files or restore replaced files.

## Verification

Tests use committed-style fixture bytes for current, implicit legacy, future,
malformed, oversized, and mixed projects. They prove store enforcement,
deterministic inspection, dry-run immutability, successful atomic migration,
backup content, idempotence, rollback behavior, CLI/MCP discovery, and stable
codes. Full Debug and canonical installed gates follow the focused suite.

## Explicit limitations

This slice does not promise downgrade support, arbitrary future-schema
interpretation, migration of agent-authored component semantics, or a universal
version shared by packages, modules, clips, and diagnostics. Later migrations
must be explicit adjacent transforms with their own fixtures.
