# Remote Asset Acquisition Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let Studio and MCP agents securely import an exact public HTTPS asset with durable integrity and license provenance.

**Architecture:** A focused acquisition service owns URI/network/resource policy and produces a staged file receipt. A command composes that service with the existing asset importer and catalog store, then replaces the ephemeral local source with durable remote provenance. Production networking pins each connection to a prevalidated public address; tests inject deterministic HTTP and DNS seams.

**Tech Stack:** C# 14, .NET 10, `HttpClient`/`SocketsHttpHandler`, xUnit, existing Rekall AGE command and asset-catalog contracts.

**Spec:** `docs/superpowers/specs/2026-08-21-remote-asset-acquisition-design.md`

## Global Constraints

- HTTPS only; reject credentials, fragments, non-default ports, and non-public addresses.
- Follow at most five redirects and validate every hop.
- Enforce a 30-second total deadline and a 32 MiB maximum body.
- Stage only inside a project-confined non-reparse temporary directory and always clean it up.
- Preserve existing local asset-catalog compatibility.
- Never search for, choose, generate, or license content for the agent.

---

### Task 1: Remote acquisition policy and receipt

**Files:**
- Create: `src/Rekall.Age.Assets/Remote/RekallAgeRemoteAssetAcquisition.cs`
- Test: `tests/Rekall.Age.Tests/Assets/RemoteAssetAcquisitionTests.cs`

**Interfaces:**
- Produces: `IRekallAgeHostAddressResolver.ResolveAsync(string, CancellationToken)` and `RekallAgeRemoteAssetAcquisition.AcquireAsync(string projectRoot, Uri source, CancellationToken)` returning `RekallAgeRemoteAssetReceipt` (`OriginalUrl`, `FinalUrl`, `StagedPath`, `MediaType`, `ByteCount`, `Sha256`).
- Produces: `RekallAgeRemoteAssetException.Code` with stable `REKALL_ASSET_REMOTE_*` values.

- [ ] Write failing tests using a scripted `HttpMessageHandler` and resolver for HTTPS success, HTTP rejection, credentials/fragment/port rejection, private/loopback address rejection, redirect-hop revalidation, declared and streamed oversize bodies, HTTP failure, digest-ready receipt data, and staging cleanup ownership.
- [ ] Run `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --no-restore --filter FullyQualifiedName~RemoteAssetAcquisitionTests` and confirm the new types are missing.
- [ ] Implement URL validation, public IP classification, manual redirects, bounded streaming SHA-256, project-confined staging, and typed errors. The default constructor must use a `SocketsHttpHandler.ConnectCallback` that resolves, validates, and connects to one validated address while `HttpClient` retains TLS hostname checks.
- [ ] Rerun the focused tests and confirm all pass.

### Task 2: Provenance-aware remote import command

**Files:**
- Modify: `src/Rekall.Age.Assets/RekallAgeAssetDocument.cs`
- Create: `src/Rekall.Age.Assets/Commands/ImportRemoteAssetCommand.cs`
- Modify: `src/Rekall.Age.Workflows/RekallAgeDefaultCommandRegistry.cs`
- Modify: `tests/Rekall.Age.Tests/Assets/AssetCommandTests.cs`
- Modify: `tests/Rekall.Age.Tests/VerticalSlice/WorkbenchFoundationTests.cs`

**Interfaces:**
- Consumes: `RekallAgeRemoteAssetAcquisition.AcquireAsync` and existing `RekallAgeAssetImporter.ImportAsync`/`RekallAgeAssetCatalogStore`.
- Produces: `ImportRemoteAssetRequest(ProjectRoot, SourceUrl, Kind, DisplayName, ExpectedSha256, Attribution, License, LicenseUrl)` and `ImportRemoteAssetResult(Asset, FinalUrl, MediaType, ByteCount, Sha256)` under command name `rekall.asset.import_remote`.
- Produces: optional `RekallAgeAssetDocument.Provenance` of type `RekallAgeAssetProvenance`.

- [ ] Write failing command tests proving a successful imported file, URL-valued `SourcePath`, all provenance fields, expected-digest verification, durable catalog JSON, transaction resources, staging cleanup on success/failure, typed failures, and default-registry discovery.
- [ ] Run the focused `AssetCommandTests` and `WorkbenchFoundationTests` filters and confirm failure for the absent command.
- [ ] Implement the additive provenance record/property and command composition. Map acquisition exceptions to command errors; perform expected digest comparison before catalog mutation; delete staging in `finally`; describe the command with remote/HTTPS/URL/download/attribution/license/provenance terms; register it beside local import.
- [ ] Rerun both focused filters and confirm pass, then run all asset and MCP catalog tests.

### Task 3: Agent discoverability and production ledger

**Files:**
- Modify: `tests/Rekall.Age.Tests/Agent/AgentToolCatalogTests.cs` (or the existing progressive-discovery test file located by `rg`)
- Modify: `docs/production/PROGRESS.md`

**Interfaces:**
- Consumes: registered `rekall.asset.import_remote` schema.
- Produces: a discovery assertion for a prompt containing “download remote image URL with license provenance”.

- [ ] Add a failing progressive-discovery test requiring `rekall.asset.import_remote` for the remote/provenance query and run that exact test.
- [ ] If schema description alone is insufficient, adjust only generic search metadata/tokenization; do not add game-specific prompt rules.
- [ ] Rerun agent discovery tests, record the initial Studio `turn_limit` evidence and completed repair in `docs/production/PROGRESS.md`, and confirm the ledger names the same Studio rerun as next acceptance.

### Task 4: Verification, distribution, and repeated Studio acceptance

**Files:**
- Modify only if evidence exposes another generic defect; record each repair in `docs/production/PROGRESS.md`.

**Interfaces:**
- Consumes: locked solution build, distribution assembly workflow, Windows Studio UI, Qwen Ollama provider, and ordinary AGE authoring tools.
- Produces: installed-distribution evidence for the same `Rain Glass Reverie` task.

- [ ] Run the affected tests, locked Release build, then complete engine and Studio suites twice; record exact totals.
- [ ] Assemble the Windows distribution, calculate archive SHA-256/file count, and launch its Studio executable.
- [ ] Through Studio UI only, open the existing `Artifacts/StudioRaindrops` project and rerun the original prompt with the real CC0 Wikimedia URL and provenance.
- [ ] Verify the agent created the asset/scene/shader/module without manual game-file edits, then use Studio to validate and play/capture two distinct runtime frames.
- [ ] Inspect and audit the packaged game, verify catalog provenance and package relocation, and update the ledger with hashes and evidence paths.
- [ ] If acceptance exposes a new generic engine deficiency, preserve the failed evidence, repair that contract test-first, and repeat this task rather than substituting a hand-authored demo.

## Self-review

- Spec coverage: command contract, SSRF/DNS-rebinding policy, resource bounds, provenance, compatibility, discovery, Studio rerun, runtime evidence, and package audit each map to a task.
- Placeholder scan: no deferred implementation markers or unspecified error-handling steps remain.
- Type consistency: the acquisition receipt and command/provenance names are identical across producing and consuming tasks.

