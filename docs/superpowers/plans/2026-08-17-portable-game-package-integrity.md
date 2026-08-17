# Portable Game Package Integrity Implementation Plan

**Goal:** Make every Rekall AGE playable package minimal, tamper-evident,
source-independent, and runnable after extraction at a different path.

**Architecture:** Replace absolute package manifest fields with normalized
relative paths, add a deterministic SHA-256 file inventory, copy an explicit
runtime project payload instead of the entire authoring tree, and make the audit
workflow extract the ZIP into a new temporary root before running and capturing
it. Retain authored runtime module DLLs while excluding source, SDK, caches,
intermediates, logs, secrets, and development metadata.

**Constraints:** Generic engine contracts only; no game-specific packaging.
Existing package readers accept the current manifest during the preview
migration, but all newly written manifests use schema version 2.

## Task 1: Relocatable manifest contract

- [x] Add failing tests requiring `gameRoot`, `launchPath`, and arguments to be relative `/` paths.
- [x] Add manifest schema/product versions and deterministic file records with size and lowercase SHA-256.
- [x] Reject rooted, traversal, duplicate, and case-colliding manifest paths.
- [x] Resolve all launch paths beneath the inspected package root.

## Task 2: Minimal runtime payload

- [x] Add a failing package test with `.rekall`, `obj`, source, logs, secrets, and unrelated build outputs.
- [x] Copy the generic runtime payload while filtering authoring-only trees and files.
- [x] Copy only compiled project module runtime outputs under `Modules/*/bin/rekall`.
- [x] Exclude project files, C# source, SDK, caches, logs, tests, and secret-bearing files.

## Task 3: Integrity verification

- [x] Recompute every manifest hash during inspect/audit.
- [x] Return structured errors for missing, changed, unexpected, unsafe, or case-colliding files.
- [x] Verify archives without unsafe extraction and cap unreasonable entry counts/sizes.

## Task 4: Relocation acceptance

- [x] Extract the archive into a new unique temporary directory using traversal-safe extraction.
- [x] Run, capture, and audit from the extracted root only.
- [x] Require a nonblank proof frame and zero absolute build/source paths.
- [x] Add the relocation proof to the installed distribution acceptance script.

## Task 5: Verification and release

- [x] Run focused package/workflow/MCP/CLI tests.
- [x] Run the complete suite twice.
- [x] Run the canonical installed distribution build and relocated game gauntlet.
- [x] Record package sizes, manifest counts, hash evidence, and proof-frame evidence.
- [x] Commit as `feat: make playable packages relocatable and verified`.

## Acceptance evidence

- Release build: zero warnings and zero errors.
- Independent full-suite passes: 507/507 and 507/507.
- Installed distribution acceptance: passed from the assembled self-contained Windows product.
- Relocation: renamed ZIP audited from a fresh extraction root; player exit code 0 and proof frame nonblank.
- Example schema-v2 package: 207 total files, 206 inventoried payload files, 37,111,182-byte ZIP.
- Example ZIP SHA-256: `63c1152fd202e93c077a20eb437bf5f345e39fb146071eb2a69fc59555062403`.
- Example proof PNG: 711 bytes; packaged C# source files: 0; absolute manifest paths: 0.
