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

- [ ] Add failing tests requiring `gameRoot`, `launchPath`, and arguments to be relative `/` paths.
- [ ] Add manifest schema/product versions and deterministic file records with size and lowercase SHA-256.
- [ ] Reject rooted, traversal, duplicate, and case-colliding manifest paths.
- [ ] Resolve all launch paths beneath the inspected package root.

## Task 2: Minimal runtime payload

- [ ] Add a failing package test with `.rekall`, `obj`, source, logs, secrets, and unrelated build outputs.
- [ ] Copy manifests, scenes, assets, catalogs, recipes, configuration explicitly.
- [ ] Copy only compiled project module runtime outputs under `Modules/*/bin/rekall`.
- [ ] Exclude project files, C# source, SDK, caches, logs, tests, and secret-bearing files.

## Task 3: Integrity verification

- [ ] Recompute every manifest hash during inspect/audit.
- [ ] Return structured errors for missing, changed, unexpected, unsafe, or case-colliding files.
- [ ] Verify archives without unsafe extraction and cap unreasonable entry counts/sizes.

## Task 4: Relocation acceptance

- [ ] Extract the archive into a new unique temporary directory using traversal-safe extraction.
- [ ] Run, capture, and audit from the extracted root only.
- [ ] Require a nonblank proof frame and zero absolute build/source paths.
- [ ] Add the relocation proof to the installed distribution acceptance script.

## Task 5: Verification and release

- [ ] Run focused package/workflow/MCP/CLI tests.
- [ ] Run the complete suite twice.
- [ ] Run the canonical installed distribution build and relocated game gauntlet.
- [ ] Record package sizes, manifest counts, hash evidence, and proof-frame evidence.
- [ ] Commit as `feat: make playable packages relocatable and verified`.
