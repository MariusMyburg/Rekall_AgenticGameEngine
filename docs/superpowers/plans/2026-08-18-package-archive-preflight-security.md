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

- [ ] Route archive inspection and inventory hashing through one preflighted
  archive handle.
- [ ] Return exact archive security codes instead of generic path failures.
- [ ] Preserve valid directory/manifest/archive behavior and MCP contracts.
- [ ] Run package inspection, run, capture, audit, and relocation regressions;
  commit.

## Task 3: Extraction integration

- [ ] Add failing extraction tests for no destination on invalid preflight,
  destination reparse rejection, exact streamed length, and changed archives.
- [ ] Extract only the immutable preflight plan with bounded copying.
- [ ] Keep relocation/run/capture staging cleanup and source-changed diagnostics
  deterministic.
- [ ] Run focused tests and commit.

## Task 4: Product gate

- [ ] Document package trust and archive limits.
- [ ] Extend installed acceptance with a safe negative archive fixture.
- [ ] Run complete Debug and canonical two-pass Release installed gate.
- [ ] Record exact evidence, limitations, archive hash, and next priority in
  `docs/production/PROGRESS.md`; review and commit.
