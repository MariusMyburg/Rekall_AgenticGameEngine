# Rekall AGE Production Foundation Design

**Date:** 2026-08-17  
**Status:** Approved  
**Product posture:** Proprietary, Windows-first, AI-first game engine  
**Release target:** Developer Preview 1

## Purpose

Rekall AGE already contains a broad technical vertical slice, but it still behaves like a source repository rather than an installable product. Developer Preview 1 turns the existing engine into a dependable Windows product that professional developers and AI coding agents can install, inspect, use, diagnose, and update without knowing the engine repository layout.

The release is successful when a user or agent can start from a clean Windows environment, unpack the Rekall AGE distribution, create a project, author scene data and C# modules, build those modules without source-tree references, validate and run the project, capture evidence, package the game, and audit the package through stable CLI and MCP contracts.

## Product Principles

- Rekall AGE is proprietary. Repository and product materials must not claim an open-source license or imply community ownership.
- AI agents are first-class authors, not assistants attached to a human-only editor. Every supported workflow must expose structured inputs, structured results, stable error codes, diagnostics, and next actions.
- The engine supplies generic, inspectable, composable primitives. Game behavior remains in authored project data and project modules.
- Release reliability takes priority over adding another rendering or gameplay feature.
- Supported and experimental capabilities must be machine-readable. Experimental systems may ship, but cannot silently weaken supported workflows.
- Local workflows are deterministic and telemetry-free by default. A future network service may add opt-in telemetry under a separate design.

## Scope

### Supported in Developer Preview 1

- Windows 11 x64 development host
- Rekall AGE CLI and MCP stdio server
- WPF Studio workbench
- Windows Vulkan player
- deterministic project, scene, entity, component, transaction, and module authoring
- local asset import and generated geometry
- runtime inspection, observations, validation, and viewport capture
- game verification, packaging, execution, proof capture, and audit
- self-contained Windows distribution with a bundled project-module SDK

### Experimental in Developer Preview 1

- OpenXR and windowed playable VR
- multiplayer sessions, transport, prediction, and reconciliation
- Tripo3D provider integration
- CPU virtual geometry and advanced planet rendering
- custom shaders beyond the validated Vulkan authoring contract

Experimental systems remain accessible and tested, but their status must appear in engine status, diagnostics, documentation, and manifests. They do not block a release unless they break a supported capability or corrupt authored data.

### Explicitly Deferred

- macOS and Linux editor/player support
- cloud accounts, hosted collaboration, marketplace, billing, and telemetry
- matchmaking and production internet multiplayer services
- GPU mesh-shader virtual geometry, disk-page streaming, and hierarchical occlusion
- installer UI, automatic updater, and code signing; the first release is a versioned self-contained ZIP
- genre templates or engine-authored game behavior

## Alternatives Considered

### 1. Harden the existing vertical slice, then expand — selected

Preserve the existing command bus, project model, runtime contracts, renderer, Studio shell, and gauntlet. Replace repository-coupled seams, add product metadata and release automation, and use an installed-distribution acceptance test as the main gate.

This approach delivers a usable product fastest while retaining the engine's strongest differentiator: a broad, inspectable agent-authoring loop.

### 2. Rewrite around a new editor and runtime

A rewrite could produce cleaner abstractions but would discard working authoring, rendering, runtime, VR, multiplayer, and packaging contracts. It postpones user evidence and creates a long period where architectural quality cannot be tested against real games.

### 3. Continue feature expansion before productization

Adding more renderer, VR, or multiplayer features would increase surface area without fixing installation, SDK portability, reliability, or release evidence. It would make the prototype more impressive but less deliverable.

## Architecture

Developer Preview 1 introduces a product layer around the existing engine rather than a second engine architecture.

```text
Versioned Windows distribution
  rekall.exe                 CLI + MCP entry point
  Rekall.Age.Studio.exe      graphical workbench
  Rekall.Age.Player.Windows  graphical player
  sdk/                       project-module reference assemblies and metadata
  docs/                      installed quick start and support matrix
  THIRD-PARTY-NOTICES.txt    dependency attribution

Authored project
  rekall.project.json
  Scenes/
  Assets/
  Modules/                   references installed/bundled SDK, never repository source
  Transactions/
  Artifacts/
  Builds/
```

The existing typed command registry remains the source of truth. CLI, MCP, Studio, diagnostics, and release acceptance use those same command contracts.

## Product Metadata and Stability

A single product metadata contract will define:

- product name
- semantic product version
- distribution channel (`development`, `preview`, `stable`)
- project schema version
- module SDK compatibility version
- host operating-system support
- capability stability (`supported`, `experimental`, `unavailable`)

`rekall.context.engine_status` will return this metadata alongside its existing workflow and authoring contracts. Human CLI output will remain concise; MCP receives the structured record.

Unknown or incompatible project schema and SDK versions must fail with stable error codes and an actionable next step. Experimental capabilities must never be inferred from prose alone.

## Portable Project-Module SDK

The current scaffolded module project contains an absolute `ProjectReference` into `src/Rekall.Age.Modules`. This is acceptable only inside the repository and is the principal blocker to distributing Rekall AGE as a product.

The production contract is:

- The distribution contains versioned reference/runtime assemblies under `sdk/`.
- The CLI resolves its distribution root from the executable location, with an explicit development override for repository tests.
- Scaffolded module projects import one generated `Rekall.Age.Sdk.props` file through a stable path recorded relative to the project or via a `REKALL_AGE_SDK_ROOT` build property.
- Generated projects contain no absolute repository path.
- Module build output records the SDK version used.
- A module with an incompatible SDK version fails before compilation with a stable compatibility error.
- Concurrent module builds use isolated intermediate/output directories and cannot rebuild shared engine projects.

Repository development may generate the same SDK layout into an ignored artifacts directory. Tests must exercise the distribution-style reference path, not retain a separate repository-only scaffold behavior.

## Diagnostics and Doctor

A new `rekall.context.doctor` command will provide a read-only environment assessment through CLI and MCP. Its structured result includes:

- product and SDK versions
- operating system and architecture
- distribution-root validity
- writable cache/log/artifact locations
- .NET runtime presence when required by development mode
- Vulkan backend probe summary
- optional OpenXR readiness summary marked experimental
- discovered project-module SDK
- blocking issues, warnings, and exact next actions

Every check has a stable identifier, severity, summary, evidence, and remediation. Secrets, access tokens, home-directory contents, and unrelated environment variables are never emitted.

## Build and Test Reliability

The production build has one canonical entry point implemented as a checked-in PowerShell script usable locally and in CI. It performs restore, Release build, tests, distribution publish, and installed-distribution acceptance in a deterministic order.

The current parallel generated-module failures must be resolved at their architectural source. Generated module builds will consume immutable SDK artifacts and use unique `obj`/`bin` paths, removing contention on engine project outputs. Build failures must include the invoked project, exit code, captured compiler output, and stable error code.

Release gates are:

1. clean restore with locked dependency resolution
2. Release build with warnings treated as errors
3. complete unit/integration suite passing under normal parallel execution
4. a second consecutive test pass to detect leaked state and nondeterminism
5. self-contained Windows distribution creation
6. acceptance workflow executed only through distribution binaries in a new temporary directory
7. archive inventory, manifest, hashes, and third-party notices verification

Tests may use temporary directories, but must clean up successful runs and preserve failed-run evidence under `Artifacts/TestFailures` with bounded retention.

## Distribution

The distribution builder publishes self-contained `win-x64` binaries and assembles a versioned ZIP. It creates `rekall.distribution.json` containing:

- product version and channel
- target runtime identifier
- build commit when available
- build timestamp in UTC
- included tools and relative launch paths
- SDK compatibility version
- capability stability map
- SHA-256 hashes for shipped files

The manifest uses only paths relative to the distribution root. Rebuilding the same commit with the same inputs should produce identical file content; the outer ZIP timestamp is not a compatibility contract for Developer Preview 1.

The ZIP includes a proprietary notice and third-party attribution. It does not include repository source, tests, local logs, credentials, provider keys, transaction snapshots from development projects, or sample build artifacts.

## End-to-End Acceptance Proof

The installed-distribution acceptance test creates a fresh project outside the repository and uses only shipped executables and SDK files. It must:

1. run doctor and confirm no supported-capability blockers
2. create a project and scene
3. scaffold an agent-authored runtime module
4. build the module against the bundled SDK
5. author generic entities/components and semantic input actions
6. validate the scene
7. inspect runtime frames and structured observations
8. capture a Vulkan proof frame when graphics are available; CI may use the deterministic software viewport
9. package the game as a self-contained Windows artifact
10. audit and run the package
11. verify that manifests contain no repository paths

The existing agent-authoring gauntlet will be extended to consume an explicit engine environment so the same workflow validates repository builds and installed distributions. No parallel bespoke product workflow will duplicate it.

## Studio

Studio remains a workbench over generic engine commands. Developer Preview 1 does not attempt a full visual-editor rewrite.

Studio will add:

- visible product version and release channel
- supported/experimental capability badges
- doctor summary and blocking remediation
- distribution-aware SDK status
- a release-proof action that invokes the generic gauntlet and opens its artifacts

Studio must not hide command errors. It displays the same stable code, summary, evidence, and next actions returned to CLI/MCP consumers.

## Security and Data Handling

- Rekall AGE sends no telemetry by default.
- Provider credentials are accepted through documented environment variables or process-local inputs and are never written into projects, transactions, packages, logs, manifests, or diagnostics.
- Packaging rejects paths that escape the project or distribution root and excludes caches, builds, logs, credentials, and intermediate files.
- Archive extraction and asset import validate path traversal before writing files.
- Release manifests hash every shipped file so corruption can be detected locally.
- Dependency versions are centrally managed or locked, reviewed in CI, and represented in third-party notices.

Formal license agreements, privacy terms, export controls, and code-signing policy require owner/legal review before a commercial stable release. Developer Preview 1 will carry a clear proprietary copyright notice without claiming terms that have not been approved.

## Documentation and Branding

The README becomes a product landing page and concise source-developer guide rather than the only technical manual. Detailed contracts remain in versioned documentation.

Required documentation:

- product positioning and proprietary status
- installed quick start
- AI-agent authoring loop
- supported versus experimental matrix
- project/module SDK compatibility policy
- troubleshooting through doctor and diagnostics
- distribution verification and SHA-256 instructions
- omit the security-contact section until a real monitored address exists

The existing `Hero.png` cannot ship unchanged because it says “Open Source” and “MIT License.” It will be edited or replaced before inclusion.

## CI

Windows CI is authoritative for Developer Preview 1. The workflow will:

- use a pinned .NET 10 SDK feature band
- restore with dependency locking
- build and test in Release mode
- run the parallel suite twice
- assemble the self-contained distribution
- run installed-distribution acceptance
- upload test evidence and the versioned ZIP on tagged/release builds

Linux compilation may be added as a non-blocking portability signal for platform-neutral libraries, but it is not a release gate.

## Error Contract

New productization commands follow the existing `RekallAgeCommandResult<T>` model. Errors include:

- stable uppercase code beginning with `REKALL_`
- concise user-facing message
- affected resource or check identifier
- evidence safe to log
- one or more concrete next actions when remediation is possible

Expected failures return command results and nonzero CLI exit codes; they do not surface raw stack traces by default. Unexpected failures receive a correlation identifier and preserve a local diagnostic record.

## Delivery Sequence

1. Product metadata, stability model, and engine-status compatibility contract.
2. Portable bundled module SDK and concurrency-safe module builds.
3. Doctor command and actionable diagnostics.
4. Deterministic release script, dependency locking, and Windows CI.
5. Self-contained distribution manifest, hashes, notices, and ZIP.
6. Installed-distribution gauntlet acceptance.
7. Studio production status surfaces.
8. Proprietary branding and documentation correction.

Each step must preserve the generic agent-authoring architecture and land with tests before the next begins.

## Exit Criteria

Developer Preview 1 is complete only when all of the following are true:

- the repository has no known consistently reproducible supported-core test failure
- the full test suite passes twice consecutively under default parallel settings
- all first-party projects build in Release with zero warnings
- a versioned self-contained `win-x64` distribution is produced by one command
- a fresh temporary project completes the installed-distribution gauntlet without referencing repository source
- CLI and MCP expose product versions, compatibility, stability, diagnostics, and actionable errors
- Studio shows the same product health and release-proof results
- package and distribution manifests use relative paths and verified SHA-256 hashes
- no shipped material claims Rekall AGE is open source or MIT licensed
- supported and experimental capabilities are explicit in documentation and machine-readable status
- the distribution contains no credentials, local logs, repository source, or unrelated development artifacts

This is a developer preview quality bar, not the final commercial-stable bar. Stable release planning begins only after at least one substantial game has been authored entirely through the shipped agent contracts and its failures have been converted into generic engine improvements.
