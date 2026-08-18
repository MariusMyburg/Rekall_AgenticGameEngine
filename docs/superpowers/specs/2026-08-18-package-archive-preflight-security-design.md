# Package Archive Preflight Security Design

Date: 2026-08-18

## Problem

Playable ZIP packages already receive integrity, traversal, size, count, and
safe-extraction checks, but those checks are not one coherent boundary. The
inspector opens and deserializes `rekall.package.json` and materializes an
archive file list before archive bounds and path collisions are enforced.
Extraction performs a separate validation after creating its destination.

This ordering permits resource use before advertised limits apply and creates
inconsistent outcomes. In particular, duplicate manifest entries can be read
one way during inspection and rejected later during extraction. A hostile or
corrupt archive must be rejected before manifest deserialization, file-list
allocation, destination creation, hashing, or execution.

## Decision

Add one bounded archive preflight contract in `Rekall.Age.Workflows` and use it
as the first operation for archive inspection and extraction. The preflight
examines central-directory metadata only and returns an immutable,
deterministically ordered entry plan.

Preflight validates:

- total entry count, per-entry uncompressed bytes, and total uncompressed bytes;
- exactly one regular `rekall.package.json` within a dedicated manifest-size
  ceiling;
- normalized relative paths with no traversal, backslashes, rooted paths,
  colons, empty segments, control characters, or platform-ambiguous terminal
  spaces/dots;
- case-insensitive duplicate/collision detection matching the supported
  Windows host;
- file/directory and ancestor conflicts such as file `Game` plus
  `Game/scene.json`;
- ZIP entries marked as Unix symbolic links or unsupported special files;
- non-negative, internally consistent compressed/uncompressed metadata.

The existing absolute entry/package ceilings remain the product limits for
this slice. Compression ratios are reported as facts but are not used as a
heuristic rejection rule because valid game assets can be highly compressible.
Actual copied bytes remain bounded and must equal declared entry sizes.

## Inspection flow

Archive inspection opens one read handle, runs preflight, then deserializes the
bounded unique manifest and enumerates the preflight plan. Integrity hashing
uses only planned regular files. Stable errors identify unsafe paths,
collisions, special files, missing/duplicate manifests, and exceeded limits.

No archive-supplied content is deserialized before preflight succeeds.

## Extraction flow

Safe extraction opens the archive, runs the same preflight, and only then
creates the destination. It rejects an existing or reparse-backed destination
boundary. It creates planned directories and files from the validated plan,
never recomputes target names from untrusted strings, streams with an explicit
remaining-byte ceiling, verifies exact copied length, and leaves failure
cleanup to the caller's isolated staging lifecycle.

Run, capture, audit, and relocation continue to inspect before extraction, but
extraction independently repeats preflight so a changed archive still fails
closed.

## Agent and operator behavior

Existing package commands keep their names and result shapes. Failures become
more precise and consistent. The engine does not repair hostile packages; next
actions direct agents to recreate/revalidate the package from trusted project
content.

## Verification

Adversarial tests cover oversized manifests before deserialization, duplicate
and case-colliding manifests/files, traversal, empty/control/ambiguous names,
ancestor conflicts, symlink/special-file metadata, count/size overflow,
truncated data, destination reparse points, no-destination-on-preflight-failure,
and changed-after-inspection archives. Valid generated archives must still
inspect, relocate, run, capture, and audit.

## Limitations

ZIP preflight does not authenticate publishers, scan executable intent, or make
in-process modules safe. Package SHA-256 inventory proves consistency with the
manifest, not trust in the package author. Those remain explicit product
boundaries.
