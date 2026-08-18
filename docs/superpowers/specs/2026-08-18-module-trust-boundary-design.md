# Agent-Authored Module Trust Boundary Design

## Purpose

Rekall AGE deliberately executes agent- and developer-authored C# so arbitrary
games can own their behavior. That power crosses two distinct trust boundaries:
the module build and the module load. Today a project file is executed by
MSBuild without a Rekall policy check, then any conventionally named DLL under
`Modules/*/bin` is loaded into the engine process without provenance or
integrity verification.

This phase makes that boundary explicit and enforceable. It does not pretend
that in-process .NET code is sandboxed.

## Threat model and honest posture

This phase protects against:

- custom or injected MSBuild targets in a nominal Rekall module project;
- directory-build imports affecting compilation;
- missing, stale, accidentally substituted, or modified compiled module
  artifacts when the build receipt has not also been forged;
- unmanifested dependency substitution;
- reparse-point escapes and unbounded module/source/output inventories;
- SDK compatibility mismatch between a module build and the running engine;
- user-facing failures that currently collapse into a generic execution error.

This phase does not protect the engine process from intentionally malicious C#
that passed the build policy. Loaded modules remain `in-process-full-trust` and
can use ordinary .NET file, network, process, reflection, interop, and native
APIs with the engine process's OS identity. Static forbidden-namespace scans
are not a security boundary and will not be presented as one.

Actual containment requires a later restricted out-of-process module host,
process/job limits, a serialized runtime protocol, capability grants, and
failure recovery. The manifest and load-plan contracts introduced here are
designed to remain the admission layer for that future host.

The build manifest is an unsigned local receipt. A principal with arbitrary
write access can replace both an artifact and its receipt, so cryptographic
publisher authenticity is also outside this phase. Distribution signing and a
user/publisher trust store must supply that root of trust later.

## Selected approach

Implement a canonical build policy, a hashed build-provenance manifest, a
verifying loader, and a read-only trust inspection command.

Alternatives rejected for this phase:

- Forbidden API or namespace scanning is bypassable through reflection,
  dynamic loading, interop, generated IL, or equivalent APIs.
- Authenticode alone proves a signing identity, not that a local build is
  current, compatible, bounded, or safe to execute.
- Moving immediately to an out-of-process host would require a large world
  serialization and lifecycle protocol before the current admission boundary
  is trustworthy.

## Canonical build policy

`rekall.build.modules` accepts only direct module directories beneath
`<project>/Modules`. Each module has exactly one direct `.csproj` whose
normalized contents match `RekallAgeModuleProjectFile.Create(moduleName)`.
The project file is engine-owned infrastructure; agents author `.cs` source,
not build targets.

Before invoking `dotnet`, the build validates:

- module directory, project, source, SDK, and output paths are physically
  inside their expected roots and are not reparse points;
- at most 256 modules exist;
- each module has at most 256 direct `.cs` files, each no larger than 4 MiB,
  with at most 32 MiB total source;
- the canonical project targets `net10.0`, imports only the project-local
  Rekall SDK props, and contains no package/project references, custom targets,
  tasks, analyzers, source generators, or arbitrary imports;
- the installed SDK manifest compatibility equals the engine compatibility
  version and every SDK resource matches its manifest hash.

The build disables `Directory.Build.props` and `Directory.Build.targets`
imports explicitly. A later controlled dependency contract may extend the
engine-generated project; arbitrary project-file mutation is not the extension
point.

Build-policy failures return `REKALL_MODULE_BUILD_POLICY_REJECTED` with the
module/project target and an exact reason. No build process starts after a
policy failure.

## SDK integrity

Upgrade `rekall.sdk.json` to include a bounded file inventory for the four SDK
assemblies and `Rekall.Age.Sdk.props`: normalized relative path, size, and
SHA-256. SDK installation writes the props first, then hashes all resources,
then atomically replaces the manifest. Build preflight compares the props with
the running engine's canonical props and compares SDK assembly bytes with the
running engine resources as well as checking the local inventory. Rewriting a
local manifest therefore cannot authorize a changed SDK input. Missing,
unexpected, changed, reparse-point, incompatible, or oversized SDK resources
fail with `REKALL_MODULE_SDK_INTEGRITY_FAILED`.

## Module build manifest

After a successful canonical build, write
`Modules/<name>/bin/rekall/net10.0/rekall.module.build.json` atomically. Schema
version 1 contains:

- kind `rekall.age.module.build`;
- module name and project-relative module path;
- product version and module SDK compatibility version;
- target framework and main assembly filename;
- execution trust `in-process-full-trust`;
- a deterministic source fingerprint over normalized relative source/project
  paths and bytes;
- a bounded inventory of load-relevant output files with normalized relative
  path, size, and SHA-256; and
- the ordered engine SDK assembly identities referenced at build time.

The manifest does not include itself. Load-relevant files are the module DLL,
its `.deps.json`/`.runtimeconfig.json` if present, and non-Rekall dependency
DLLs. PDBs are diagnostic-only and are neither trusted runtime inputs nor
packaged requirements.

Manifests are unsigned provenance/integrity receipts, not signatures, publisher
authentication, or claims that module behavior is safe.

## Verified loader

Replace filename-convention loading with a two-stage admission plan:

1. `RekallAgeProjectModuleTrustInspector` performs bounded, read-only
   discovery and returns a per-module trust result without loading assemblies.
2. `RekallAgeProjectModuleAssemblyLoader` loads only modules whose trust result
   is ready.

The inspector rejects:

- missing or malformed manifests;
- unsupported schema, product compatibility, or SDK compatibility;
- absolute, parent-traversing, colliding, duplicate, or non-normalized paths;
- missing, extra load-relevant, size-mismatched, or hash-mismatched files;
- reparse points at the module/output/file boundary;
- entry-count, per-file-size, or total-size limit violations; and
- an assembly filename or identity inconsistent with the module directory.

The load context may resolve non-framework dependencies only from verified
inventory paths inside that module's output root. `Rekall.Age.*` references
continue to bind to host assemblies, but only after SDK compatibility passes.

A structured `RekallAgeModuleTrustException` carries the exact trust code and
target. Dynamic command execution and the CLI preserve coded boundary failures
instead of replacing them with `REKALL_COMMAND_EXECUTION_FAILED`.

## Inspection contract

Add `rekall.module.inspect_trust` with a project-root request. It returns:

- overall ready status and explicit `in-process-full-trust` posture;
- module count and bounded per-module results;
- module name, manifest/assembly paths relative to project root;
- product/SDK versions, source fingerprint, assembly hash and size;
- verified dependency inventory;
- named checks and structured issues; and
- next actions such as rebuilding stale/missing manifests.

The command is read-only and never builds, repairs, approves, signs, or authors
content. CLI, MCP, engine status, Studio, package verification, and future
out-of-process hosting consume the same result contract.

## Packaging and compatibility

The existing package filter already copies `Modules/*/bin/rekall/net10.0`.
Therefore the module build manifest travels with the DLL without including
source, project files, the project-local SDK, or PDBs. Package creation must
fail if module trust inspection is not ready before copying. Packaged-game
loading performs the same verification after relocation.

This is an intentional compatibility break for unmanifested preview-era module
outputs: rebuild them with `rekall.build.modules`. Source projects and module
SDK compatibility version 1 remain unchanged.

## Verification

Test-first coverage will prove:

1. a canonical build emits an inspectable, bounded manifest and passes trust;
2. custom target/import/package/project-reference injection is rejected before
   any build-side marker can be created;
3. source, SDK, assembly, dependency, and manifest mutation each fail with a
   specific code before assembly loading;
4. reparse points, traversal/collision paths, and low injected limits fail
   deterministically without escaping the project;
5. project schemas, runtime systems, playable modules, packaging, relocation,
   and installed execution still work through verified admission;
6. CLI/MCP/status expose the full-trust posture and rebuild next action; and
7. installed acceptance tampers with a copied packaged module, observes
   rejection, then proves the untampered package remains playable.

The product gate remains a zero-warning Release build, two independent full
test passes, self-contained Windows distribution assembly, and complete
installed acceptance. The milestone will close integrity/provenance and build
policy gaps only; process isolation remains explicitly open.
