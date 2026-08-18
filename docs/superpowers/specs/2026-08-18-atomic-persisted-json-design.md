# Atomic Persisted JSON Design

## Goal

Make engine-owned JSON documents fail closed under oversized, deeply nested,
malformed, concurrently replaced, interrupted, or cancelled I/O. A successful
load must validate and deserialize one immutable byte snapshot. A successful
save must publish one complete durable document without exposing a partial live
file.

## Scope

This tranche covers the project manifest, scenes, asset catalog, asset pipeline,
prefabs, render plans, and transaction log. It does not change agent-authored C#
trust, package/archive preflight, external provider responses, shader/module
source files, or multi-user merge/conflict semantics.

## Contracts

- Core provides a bounded file snapshot read from one open handle with a fixed
  byte limit and cancellation.
- Schema inspection and typed deserialization consume the same snapshot; stores
  do not preflight one path state and reopen another.
- JSON parsing and typed deserialization use the same maximum depth.
- Core provides a same-directory atomic text publisher. It opens an unpredictable
  temporary sibling with `CreateNew`, writes UTF-8 without BOM, flushes through
  the file handle, atomically replaces/moves to the destination, and removes its
  own temporary file on every failure.
- Existing public serialized shapes, camel-case naming, trailing newline, legacy
  in-memory normalization, and stable compatibility error codes remain intact.
- Engine-owned temporary files are never enumerated as scenes/prefabs and never
  survive successful or cancelled operations.
- Size/depth/malformed failures occur before a partially deserialized document is
  returned. An existing destination remains byte-identical when staging fails.

## Failure boundaries

The snapshot reader rejects negative/changed/excessive lengths and short reads.
The JSON layer rejects malformed or over-depth input with stable typed errors
where a compatibility contract already exists. Atomic publication reports I/O
failure without deleting or truncating the previous destination.

Atomic replacement prevents torn files, not lost updates between two valid
writers. Optimistic revisions and collaborative merge semantics are a separate
professional-workflow capability. Filesystem durability ultimately follows the
host filesystem and storage device guarantees.

## Evidence

Adversarial tests will cover over-limit and over-depth project/scene input,
schema/payload snapshot identity, cancellation/failure cleanup, exact preservation
of an existing destination, concurrent reader observations during repeated
saves, and regression coverage for every migrated store. Installed acceptance
will repeatedly mutate and inspect a shipped project and require no temporary or
malformed engine documents.
