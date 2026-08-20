# Restricted Agent-Authored Module Host Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use
> superpowers:executing-plans and superpowers:test-driven-development. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** Execute every project-authored C# module behind a bounded Windows
AppContainer and job-object boundary without changing the generic module SDK.

**Architecture:** Keep verified module receipts as admission, stage only
verified worker/runtime and module artifacts, and launch one persistent
no-capability AppContainer worker per game/runtime session. Exchange immutable
world, input, playable, and schema data through a versioned length-prefixed JSON
protocol; production consumers use broker proxies and have no in-process
project-assembly load path.

**Tech Stack:** C# / .NET 10, System.Text.Json source generation, Windows
AppContainer and job-object APIs through P/Invoke, anonymous pipes, xUnit,
PowerShell installed acceptance.

**Spec:** `docs/superpowers/specs/2026-08-20-restricted-module-host-design.md`

## Global constraints

- Supported production host: Windows 10/11 x64; authored modules fail closed on
  unsupported platforms.
- Required posture: `windows-appcontainer-restricted`; never silently fall back
  to `in-process-full-trust`.
- AppContainer receives no explicit capabilities and no project-root ACL.
- Inherit only the protocol pipe handles via
  `PROC_THREAD_ATTRIBUTE_HANDLE_LIST`.
- Job limits: kill on close, active process count 1, 512 MiB process/job memory.
- Bounds: 10-second startup, 250-millisecond request, 64 MiB message, JSON depth
  128, stderr 64 KiB, 256 modules, one pending request.
- Preserve authored system ID/priority ordering and existing C# SDK source
  compatibility.
- Use stable `REKALL_MODULE_HOST_*` codes; no module stack/environment dump.
- No production caller may load a project-authored assembly in-process.
- All behavior changes follow a witnessed red-green TDD cycle.

---

### Task 1: Receipt posture and bounded host protocol

**Files:**

- Modify: `src/Rekall.Age.Modules/Security/RekallAgeModuleTrustContracts.cs`
- Modify: `src/Rekall.Age.Modules/Security/RekallAgeModuleBuildReceiptService.cs`
- Modify: `src/Rekall.Age.Modules/Security/RekallAgeProjectModuleTrustInspector.cs`
- Create: `src/Rekall.Age.Modules/Hosting/RekallAgeModuleHostContracts.cs`
- Create: `src/Rekall.Age.Modules/Hosting/RekallAgeModuleHostFrameCodec.cs`
- Create: `src/Rekall.Age.Modules/Hosting/RekallAgeModuleHostException.cs`
- Test: `tests/Rekall.Age.Tests/Modules/ModuleHostProtocolTests.cs`
- Modify: `tests/Rekall.Age.Tests/Modules/ModuleTrustInspectionTests.cs`
- Modify: `tests/Rekall.Age.Tests/Build/BuildModulesCommandTests.cs`

**Interfaces:**

- Produce `RekallAgeModuleHostProtocol.Version = 1` and limits constants.
- Produce request/response envelopes with `ProtocolVersion`, `Sequence`,
  `Operation`, payload/error, plus typed initialize/runtime/playable DTOs.
- Produce `ReadAsync(Stream, CancellationToken)` and
  `WriteAsync(Stream, envelope, CancellationToken)` with exact-length framing.
- Change new receipts to required posture `windows-appcontainer-restricted`.
- Reject legacy full-trust receipts with
  `REKALL_MODULE_RECEIPT_HOST_POSTURE_MISMATCH`.

**Steps:**

- [x] Write framing tests for partial reads/writes, exact UTF-8 round trip,
  little-endian length, 0/oversized/truncated/trailing data, depth, cancellation,
  unknown protocol/operation, and monotonically increasing sequence checks.
- [x] Run the focused tests and retain the expected compile/behavior failures.
- [x] Implement minimal contracts, coded exception, and frame codec; keep JSON
  options deterministic and bounded.
- [x] Run the focused protocol tests until green.
- [x] Write failing receipt tests for the new posture, legacy mismatch, rebuild
  next action, malformed/unknown posture, and unchanged artifact hash bounds.
- [x] Implement receipt schema/posture migration and inspector diagnostics.
- [x] Run module trust/build/protocol tests and commit the independently usable
  admission/protocol layer.

---

### Task 2: Deterministic module-host server

**Files:**

- Create: `src/Rekall.Age.ModuleHost/Rekall.Age.ModuleHost.csproj`
- Create: `src/Rekall.Age.ModuleHost/Program.cs`
- Create: `src/Rekall.Age.ModuleHost/RekallAgeModuleHostServer.cs`
- Create: `src/Rekall.Age.ModuleHost/RekallAgeModuleHostSession.cs`
- Create: `src/Rekall.Age.Modules/Hosting/RekallAgeModuleHostLoadPlan.cs`
- Create: `src/Rekall.Age.ModuleHost/RekallAgeModuleHostJsonContext.cs`
- Modify: `Rekall.AGE.sln`
- Modify: `tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj`
- Create: `tests/Rekall.Age.Tests/Modules/ModuleHostServerTests.cs`

**Interfaces:**

- Consume Task 1 framing/contracts and the existing verified assembly loader,
  module indexer, runtime-system interfaces, and playable interfaces.
- Define the serialized confined-load-plan contract here so the server can
  independently validate it; Task 3's broker stager produces this exact format.
- Produce `RekallAgeModuleHostServer.RunAsync(input, output, error,
  cancellationToken)`.
- Initialize returns ordered system descriptors, component schemas, playable
  kind, protocol version, and exact restricted posture.
- Runtime update selects exactly one declared system ID; playable state remains
  worker-owned across create/tick/render calls.

**Steps:**

- [x] Write server tests using memory/anonymous streams and a verified fixture
  module for initialize, system priority/ID discovery, component schemas,
  runtime mutation, playable state/tick/render, shutdown, and state persistence.
- [x] Add adversarial tests for operation-before-initialize, duplicate init,
  unknown system, malformed module output, module throw, response bounds,
  bounded error projection, and sequence violations; run red.
- [x] Add the worker project and implement the single-request session loop with
  source-generated serialization metadata.
- [x] Ensure stdout is protocol-only and bounded diagnostics go to stderr.
- [x] Run server/protocol/module tests, inspect process lifetime manually with a
  finite fixture, and commit the deterministic worker.

---

### Task 3: Verified staging and Windows containment launcher

**Files:**

- Modify: `src/Rekall.Age.Modules/Hosting/RekallAgeModuleHostLoadPlan.cs`
- Create: `src/Rekall.Age.Modules/Hosting/RekallAgeModuleHostStager.cs`
- Create: `src/Rekall.Age.Modules/Hosting/RekallAgeModuleHostResolver.cs`
- Create: `src/Rekall.Age.Modules/Hosting/RekallAgeRestrictedModuleHostClient.cs`
- Create: `src/Rekall.Age.Modules/Hosting/Windows/RekallAgeAppContainerProfile.cs`
- Create: `src/Rekall.Age.Modules/Hosting/Windows/RekallAgeAppContainerProcess.cs`
- Create: `src/Rekall.Age.Modules/Hosting/Windows/RekallAgeModuleHostJob.cs`
- Create: `src/Rekall.Age.Modules/Hosting/Windows/RekallAgeWindowsNative.cs`
- Test: `tests/Rekall.Age.Tests/Modules/ModuleHostStagingTests.cs`
- Test: `tests/Rekall.Age.Tests/Modules/ModuleHostWindowsIsolationTests.cs`

**Interfaces:**

- `RekallAgeModuleHostStager.StageAsync(projectRoot, hostRoot, ct)` returns an
  immutable confined load plan and disposable staging root.
- `RekallAgeRestrictedModuleHostClient.StartAsync(projectRoot, ct)` starts one
  persistent verified restricted worker and exposes typed operations.
- The native launcher uses `PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES` and
  `PROC_THREAD_ATTRIBUTE_HANDLE_LIST`; the job owns worker lifetime.

**Steps:**

- [ ] Write staging red tests for exact inventory copies, hash recheck, changed
  source/artifact, reparse/path/case escapes, host manifest mismatch, no source
  or project copy, read-only AppContainer ACL, failure cleanup, and success
  cleanup.
- [ ] Implement resolver/load plan/stager with immutable snapshot checks and run
  focused tests green.
- [ ] Write Windows-only red integration fixtures that try to read a sentinel,
  write the project, connect to loopback, start/retain a child, allocate beyond
  the job limit, hang, crash, and emit excessive stderr.
- [ ] Implement AppContainer profile/SID/ACL creation, extended CreateProcess,
  explicit handle allow-list, job assignment, deadlines, termination, bounded
  stderr, cleanup, and SafeHandle ownership.
- [ ] Require exact stable codes for every failed boundary and prove the broker
  plus project survive each fixture.
- [ ] Run isolation tests repeatedly (minimum 10 passes for timing fixtures),
  check zero orphan host processes/staging trees, and commit containment.

---

### Task 4: Runtime, playable, and schema consumers

**Files:**

- Modify: `src/Rekall.Age.Runtime/RekallAgeProjectRuntimeSystemLoader.cs`
- Modify: `src/Rekall.Age.Runtime/RekallAgeRuntimeExecutionLoop.cs`
- Modify: runtime callers returned by
  `rg -n "CreateDefault\(" src --glob "*.cs"`
- Modify: `src/Rekall.Age.Playback/RekallAgeModulePlayableGame.cs`
- Modify: `src/Rekall.Age.Playback/RekallAgePlayableGameFactory.cs`
- Modify: `src/Rekall.Age.Playback/IRekallAgePlayableGame.cs`
- Modify: player/rendering callers returned by
  `rg -n "CreateWithRuntime\(|PlayableGameFactory.Create" src --glob "*.cs"`
- Modify: `src/Rekall.Age.Modules/Commands/ListComponentSchemasCommand.cs`
- Restrict: `src/Rekall.Age.Modules/RekallAgeProjectModuleAssemblyLoader.cs`
- Test: `tests/Rekall.Age.Tests/Runtime/RestrictedModuleRuntimeTests.cs`
- Modify: `tests/Rekall.Age.Tests/Playback/ModulePlayableRuntimeTests.cs`
- Modify: `tests/Rekall.Age.Tests/Modules/ComponentSchemaCommandTests.cs`

**Interfaces:**

- Runtime loader produces one proxy per host-discovered system and preserves
  exact priority/ID interleaving.
- Execution loops and playable games own/dispose the shared host client.
- Project schema discovery consumes initialization descriptors; only built-in
  assemblies use in-process reflection.

**Steps:**

- [ ] Write red end-to-end tests proving project system semantics, time/input,
  custom events/observations/render meshes, priority interleaving, playable
  state/rendering, and project component schemas through the broker.
- [ ] Add a process-identity fixture and assert authored code never shares the
  engine PID across runtime, playable, and schema paths.
- [ ] Implement shared host proxies and explicit async disposal through every
  runtime/player/rendering/Studio caller; do not rely on finalizers for normal
  cleanup.
- [ ] Make the old project assembly loader worker-internal/test-only and add a
  source audit test rejecting production references.
- [ ] Run all runtime, playback, modules, rendering, player, editor, CLI, and MCP
  selections; verify zero orphan workers; commit consumer cutover.

---

### Task 5: Agent inspection, packaging, and distribution

**Files:**

- Create: `src/Rekall.Age.Modules/Commands/InspectModuleHostCommand.cs`
- Modify: `src/Rekall.Age.Agent/Commands/GetEngineStatusCommand.cs`
- Modify: `src/Rekall.Age.Mcp/RekallAgeMcpCatalog.cs`
- Modify: `src/Rekall.Age.Cli/Program.cs`
- Modify: `src/Rekall.Age.Build/Distribution/RekallAgeDistributionAssembler.cs`
- Modify: `src/Rekall.Age.Build/Commands/AssembleDistributionCommand.cs`
- Modify: `src/Rekall.Age.Build/Commands/BuildPlayerCommand.cs`
- Modify: `eng/build.ps1`
- Modify: package manifest/audit files identified by
  `rg -n "PlayerPublishRoot|distribution|rekall.package" src/Rekall.Age.Build src/Rekall.Age.Workflows --glob "*.cs"`
- Test: `tests/Rekall.Age.Tests/Modules/ModuleHostInspectionTests.cs`
- Modify: `tests/Rekall.Age.Tests/Mcp/WorkbenchMcpCatalogTests.cs`
- Modify: `tests/Rekall.Age.Tests/Cli/CliSmokeTests.cs`
- Modify: distribution/package tests under `tests/Rekall.Age.Tests/Build` and
  `tests/Rekall.Age.Tests/Workflows`

**Interfaces:**

- Add `rekall.module.inspect_host` with read-only platform, host/version,
  limits, required/active posture, capabilities, issues, and next actions.
- Add CLI `module host <projectRoot>` and generated MCP schema/category/priority.
- Distribution/package layout includes a relative verified
  `module-host/windows` payload.

**Steps:**

- [ ] Write red direct/CLI/MCP tests for supported, missing, version mismatch,
  unavailable AppContainer/job, legacy receipt, and ready restricted posture.
- [ ] Implement inspection without executing project code and add concise engine
  status guidance without crossing the 12,000-character status bound.
- [ ] Write red package/distribution tests proving host payload inclusion,
  relative resolution after relocation, hash/audit coverage, and refusal when
  worker files are absent/tampered.
- [ ] Publish and assemble the worker through the normal build pipeline; update
  package copying and audit checks.
- [ ] Run agent/MCP/CLI/build/workflow/package selections and commit the shipped
  contract.

---

### Task 6: Installed hostile-module proof and complete gate

**Files:**

- Create: `eng/accept-installed-restricted-module-host.ps1`
- Modify: `eng/accept-distribution.ps1`
- Modify: `docs/production/package-trust-and-archive-security.md`
- Modify: `README.md`
- Modify: `docs/production/2026-08-17-engine-maturity-audit.md`
- Modify: `docs/production/PROGRESS.md`
- Modify: this plan's checkboxes

**Steps:**

- [ ] Use only shipped binaries to scaffold/build a generic module; require
  ready `windows-appcontainer-restricted` facts through CLI and MCP.
- [ ] Run, inspect, package, relocate, audit, and capture ordinary authored
  module behavior; require a nonblank informative frame and exact state change.
- [ ] Run installed file-read, project-write, loopback/network, child-process,
  hang, crash, stderr, and memory hostile fixtures; require exact codes, intact
  sentinel/project, no orphan process, and bounded evidence.
- [ ] Run a 600-frame authored-module soak at least 30 FPS and record p50/p95
  request latency, retained memory, worker peak memory, and zero protocol drift.
- [ ] Run the complete Debug suite, clean locked Release build, two independent
  Release suites, four-app plus worker publishing, archive assembly, and the
  unchanged installed acceptance matrix.
- [ ] Record exact test counts, durations, security codes, isolation denials,
  performance, archive bytes/hash/file counts, and remaining unsigned
  publisher/macOS/Linux boundaries in the audit and progress ledger.
- [ ] Use verification-before-completion, check a clean worktree, and commit the
  final evidence before selecting the next audit-driven tranche.
