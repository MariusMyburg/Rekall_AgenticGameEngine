# Package Archive Preflight Security Implementation Plan

Date: 2026-08-18

Design: `docs/superpowers/specs/2026-08-18-package-archive-preflight-security-design.md`

## Task 1: Central bounded preflight

- [x] Add failing metadata-only tests for valid archives, count/size bounds,
  unique bounded manifest, unsafe/ambiguous names, case collisions, ancestor
  conflicts, and symlink/special-file modes.
- [x] Implement stable preflight contracts and errors with deterministic entry
  plans.
- [x] Prove no manifest stream is opened before preflight succeeds.
- [x] Run focused tests and commit.

Verified 2026-08-18: 15/15 metadata-only preflight tests pass. The preflight
returns a manifest-first immutable entry plan and rejects count/size overflow,
missing/duplicate/oversized manifests, traversal and Windows-ambiguous paths,
case collisions, file/ancestor conflicts, and link/special-file metadata using
stable codes. No entry content stream is opened by preflight; an oversized
manifest with invalid JSON content is rejected solely from central-directory
metadata.

## Task 2: Inspection integration

- [x] Route archive inspection and inventory hashing through one preflighted
  archive handle.
- [x] Return exact archive security codes instead of generic path failures.
- [x] Preserve valid directory/manifest/archive behavior and MCP contracts.
- [x] Run package inspection, run, capture, audit, and relocation regressions;
  commit.

Verified 2026-08-18: 18/18 focused preflight/inspection tests and 5/5 broad
package integrity tests pass. Archive inspection now preflights before manifest
deserialization or file-list allocation, reads the bounded unique manifest from
the preflight plan, and hashes only planned regular files. Oversized invalid
JSON and duplicate manifests return exact archive codes, archive-source reparse
points fail closed, and generated package inspect/run/capture/audit/relocation
behavior remains green.

## Task 3: Extraction integration

- [x] Add failing extraction tests for no destination on invalid preflight,
  destination reparse rejection, exact streamed length, and changed archives.
- [x] Extract only the immutable preflight plan with bounded copying.
- [x] Keep relocation/run/capture staging cleanup and source-changed diagnostics
  deterministic.
- [x] Run focused tests and commit.

Verified 2026-08-18: 23/23 focused archive security tests and 5/5 broad
package-integrity tests pass. Extraction now preflights before destination
creation, rejects existing or reparse-backed destination boundaries, streams
each immutable planned entry to its exact declared length, and publishes only
through an atomic sibling-directory move. Invalid, truncated, or changed
archives cannot publish partial package destinations; relocation preserves its
stable source-changed diagnostic and cleanup behavior.

## Task 4: Product gate

- [x] Document package trust and archive limits.
- [x] Extend installed acceptance with a safe negative archive fixture.
- [x] Run complete Debug and canonical two-pass Release installed gate.
- [x] Record exact evidence, limitations, archive hash, and next priority in
  `docs/production/PROGRESS.md`; review and commit.

Verified 2026-08-18: the complete Debug suite passed 706/706 in 2m18s. The
canonical locked Release gate built with zero warnings/errors and passed two
independent 706/706 runs in 2m18s and 2m17s. Shipped binaries rejected a
duplicate-root-manifest ZIP through inspect and audit with exact code
`REKALL_PACKAGE_ARCHIVE_MANIFEST_DUPLICATE`; rejected audit created no output.
The unchanged installed product matrix passed, including module tamper,
authoring gauntlet, relocation, informative UI, audio, compatibility, recovery,
and a 600-frame/10-second soak at 4,449.2 FPS with 693,680 retained bytes. The
1,149-payload-file archive is 195,083,188 bytes with SHA-256
`5744CCEEE831BC9C80ABE7F8A2668AA1BE4C570E70106097EE26052368E88B60`.
