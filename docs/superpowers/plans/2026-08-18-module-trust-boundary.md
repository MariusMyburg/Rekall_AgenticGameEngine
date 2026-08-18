# Agent-Authored Module Trust Boundary Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enforce a canonical C# module build policy, emit bounded hashed build receipts, verify every module before in-process loading, and expose the honest full-trust posture through CLI, MCP, packaging, and installed acceptance.

**Architecture:** Keep C# game behavior agent-authored and generic. Treat build admission, artifact provenance, and assembly loading as separate services under `Rekall.Age.Modules`; let the build command produce receipts and all runtime/schema/playback consumers share one verifying loader. Preserve exact trust codes through command adapters. Do not claim sandboxing or publisher authentication.

**Tech Stack:** C# 13, .NET 10, xUnit, `System.Text.Json`, SHA-256, `AssemblyLoadContext`, existing command registry, CLI/MCP projection, PowerShell installed acceptance.

---

## Task 1: Enforce the canonical module build policy

**Files:**

- Create: `src/Rekall.Age.Modules/Security/RekallAgeModuleBuildPolicy.cs`
- Modify: `src/Rekall.Age.Modules/Sdk/RekallAgeModuleProjectFile.cs`
- Modify: `src/Rekall.Age.Build/Commands/BuildModulesCommand.cs`
- Create: `tests/Rekall.Age.Tests/Modules/ModuleBuildPolicyTests.cs`

- [x] **Step 1: Add failing policy tests**

Scaffold a module, mutate its project in separate tests with a custom target/write marker, `UsingTask`, arbitrary import, `PackageReference`, and `ProjectReference`, then execute `BuildModulesCommand`. Assert each returns `REKALL_MODULE_BUILD_POLICY_REJECTED`, does not start a build, and never creates the marker. Add cases for nested module projects, reparse-point source, more than injected source/module limits, oversized source, and canonical success.

- [x] **Step 2: Run focused tests RED**

```powershell
$env:TEMP='F:\Dev\Rekall_AGE\.worktrees\production-foundation\Artifacts\TestTemp'
$env:TMP=$env:TEMP
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --filter FullyQualifiedName~ModuleBuildPolicyTests -p:UseSharedCompilation=false
```

Expected: policy types/codes are missing or malicious projects still execute.

- [x] **Step 3: Implement bounded canonical discovery and validation**

Add injectable production defaults: 256 direct module directories, one canonical direct project, 256 direct `.cs` files/module, 4 MiB/file, and 32 MiB total. Validate normalized physical containment and reject reparse points before reading. Compare normalized project text with `RekallAgeModuleProjectFile.Create(moduleName)`; expose the canonical text from one source of truth.

- [x] **Step 4: Harden the build process invocation**

Pass `-p:ImportDirectoryBuildProps=false` and `-p:ImportDirectoryBuildTargets=false`. Only after policy success, safely reset the verified module output root and invoke the canonical project. Return structured policy errors without starting `dotnet`.

- [x] **Step 5: Run focused tests GREEN and commit**

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --filter "FullyQualifiedName~ModuleBuildPolicyTests|FullyQualifiedName~ScaffoldModuleCommandTests|FullyQualifiedName~ScaffoldPlayableModuleCommandTests|FullyQualifiedName~ScaffoldRuntimeSystemModuleCommandTests" -p:UseSharedCompilation=false
git add src/Rekall.Age.Modules/Security/RekallAgeModuleBuildPolicy.cs src/Rekall.Age.Modules/Sdk/RekallAgeModuleProjectFile.cs src/Rekall.Age.Build/Commands/BuildModulesCommand.cs tests/Rekall.Age.Tests/Modules/ModuleBuildPolicyTests.cs
git commit -m "fix: enforce canonical module builds"
```

## Task 2: Anchor project-local SDK integrity to the running engine

**Files:**

- Create: `src/Rekall.Age.Modules/Security/RekallAgeModuleSdkIntegrityVerifier.cs`
- Modify: `src/Rekall.Age.Modules/Sdk/RekallAgeModuleSdkInstaller.cs`
- Modify: `src/Rekall.Age.Build/Commands/BuildModulesCommand.cs`
- Modify: `tests/Rekall.Age.Tests/Workflows/EngineDoctorTests.cs`
- Create: `tests/Rekall.Age.Tests/Modules/ModuleSdkIntegrityTests.cs`

- [x] **Step 1: Add failing SDK mutation tests**

After scaffold/SDK installation, independently mutate props, an SDK assembly, inventory hash, compatibility version, add an unexpected SDK file, and simulate a reparse point/low bound. Assert `REKALL_MODULE_SDK_INTEGRITY_FAILED` before build. Prove changing both local resource and local inventory still fails against host canonical bytes.

- [x] **Step 2: Run SDK tests RED**

Run the two SDK-related test classes; expect missing inventory and mutation acceptance.

- [x] **Step 3: Add SDK inventory and verifier**

Extend `RekallAgeModuleSdkManifest` compatibly with schema version and file integrity records. Install props/assemblies first, compute normalized size/SHA-256 inventory, and atomically replace the JSON manifest. Verify exact expected resources, bounds, local inventory, engine compatibility, canonical props, and host assembly hashes.

- [x] **Step 4: Require SDK verification before every build**

Build policy success is followed by SDK verification; failure returns the exact SDK integrity code and starts no compiler process.

- [x] **Step 5: Run SDK/build tests GREEN and commit**

```powershell
git add src/Rekall.Age.Modules/Security/RekallAgeModuleSdkIntegrityVerifier.cs src/Rekall.Age.Modules/Sdk/RekallAgeModuleSdkInstaller.cs src/Rekall.Age.Build/Commands/BuildModulesCommand.cs tests/Rekall.Age.Tests/Modules/ModuleSdkIntegrityTests.cs tests/Rekall.Age.Tests/Workflows/EngineDoctorTests.cs
git commit -m "fix: verify project module sdk integrity"
```

## Task 3: Emit and inspect bounded module build receipts

**Files:**

- Create: `src/Rekall.Age.Modules/Security/RekallAgeModuleTrustContracts.cs`
- Create: `src/Rekall.Age.Modules/Security/RekallAgeModuleBuildReceiptService.cs`
- Create: `src/Rekall.Age.Modules/Security/RekallAgeProjectModuleTrustInspector.cs`
- Modify: `src/Rekall.Age.Build/Commands/BuildModulesCommand.cs`
- Create: `tests/Rekall.Age.Tests/Modules/ModuleTrustInspectionTests.cs`

- [x] **Step 1: Add failing receipt and inspection tests**

Prove a canonical build writes `rekall.module.build.json`, trust inspection reports `in-process-full-trust`, relative normalized paths, exact SDK/product data, source fingerprint, and output inventory. Add missing/malformed/schema/compatibility/traversal/case-collision/duplicate/extra/missing/size/hash/assembly-identity/reparse/bounds cases with specific codes. Mutating source after an authoring build must return `REKALL_MODULE_SOURCE_STALE`; packaged output without source remains verifiable.

- [x] **Step 2: Run receipt tests RED**

Expected: no receipt or inspector exists.

- [x] **Step 3: Implement atomic receipt writing**

After successful build, compute a deterministic source fingerprint from canonical relative paths and bytes. Inventory only load-relevant files after the output root was reset. Write schema 1 atomically and add receipt path/trust posture to `BuildModuleResult` as backward-compatible init properties.

- [x] **Step 4: Implement bounded read-only inspection**

Validate without loading assemblies. Use lazy bounded enumeration, normalized ordinal path keys plus OS collision keys, SHA-256 streaming, injected low limits/attributes for deterministic tests, and compact named checks/issues.

- [x] **Step 5: Run receipt tests GREEN and commit**

```powershell
git add src/Rekall.Age.Modules/Security src/Rekall.Age.Build/Commands/BuildModulesCommand.cs tests/Rekall.Age.Tests/Modules/ModuleTrustInspectionTests.cs
git commit -m "feat: add module build trust receipts"
```

## Task 4: Make verified admission the only module load path

**Files:**

- Modify: `src/Rekall.Age.Modules/RekallAgeProjectModuleAssemblyLoader.cs`
- Create: `src/Rekall.Age.Modules/Security/RekallAgeModuleTrustException.cs`
- Modify: `src/Rekall.Age.Core/Commands/RekallAgeCommandRegistry.cs`
- Modify: `src/Rekall.Age.Cli/Program.cs`
- Modify: `tests/Rekall.Age.Tests/Modules/ScaffoldPlayableModuleCommandTests.cs`
- Modify: `tests/Rekall.Age.Tests/Runtime/ProjectRuntimeSystemTests.cs`
- Modify: `tests/Rekall.Age.Tests/Playback/ModulePlayableRuntimeTests.cs`
- Modify: `tests/Rekall.Age.Tests/Modules/ProjectModuleSchemaTests.cs`

- [ ] **Step 1: Add failing load-admission tests**

Build then mutate the main DLL/deps receipt and prove schema discovery, runtime-system loading, and playable loading reject before `AssemblyLoadContext` sees the artifact. Prove the load resolver refuses unverified/out-of-root dependencies and same-named modules in different projects still load independently after verification.

- [ ] **Step 2: Preserve exact trust errors through dynamic and CLI execution**

Add failing tests showing MCP/dynamic execution and CLI print the module trust code instead of generic `REKALL_COMMAND_EXECUTION_FAILED` or message-only failure.

- [ ] **Step 3: Verify first, then load only inventory paths**

The loader consumes ready inspection plans, opens verified main assemblies with read/delete sharing, and constrains resolver paths to the verified output root and inventory. A coded trust exception is thrown before load for non-ready modules.

- [ ] **Step 4: Add generic coded-boundary propagation**

Introduce a narrow structured exception contract in Core or equivalent mapping so boundary exceptions retain code/target in dynamic commands and CLI without exposing unexpected exception details.

- [ ] **Step 5: Run all module/runtime/playback admission tests GREEN and commit**

```powershell
git add src/Rekall.Age.Modules src/Rekall.Age.Core/Commands/RekallAgeCommandRegistry.cs src/Rekall.Age.Cli/Program.cs tests/Rekall.Age.Tests/Modules tests/Rekall.Age.Tests/Runtime/ProjectRuntimeSystemTests.cs tests/Rekall.Age.Tests/Playback/ModulePlayableRuntimeTests.cs
git commit -m "fix: verify modules before loading"
```

## Task 5: Expose trust inspection and gate packaging

**Files:**

- Create: `src/Rekall.Age.Modules/Commands/InspectModuleTrustCommand.cs`
- Modify: `src/Rekall.Age.Cli/Program.cs`
- Modify: `src/Rekall.Age.Agent/Commands/GetEngineStatusCommand.cs`
- Modify: `src/Rekall.Age.Workflows/Commands/VerifyPlayableGameCommand.cs`
- Modify: `src/Rekall.Age.Workflows/Commands/PackagePlayableGameCommand.cs`
- Modify: `tests/Rekall.Age.Tests/Mcp/WorkbenchMcpCatalogTests.cs`
- Modify: `tests/Rekall.Age.Tests/Agent/AgentContextCommandTests.cs`
- Modify: `tests/Rekall.Age.Tests/Cli/RuntimeInspectCliTests.cs`
- Modify: `tests/Rekall.Age.Tests/Workflows/PlayablePackageIntegrityTests.cs`
- Modify: `README.md`

- [ ] **Step 1: Add failing command/catalog/package tests**

Assert `rekall.module.inspect_trust` is read-only, recommended/discoverable, states `in-process-full-trust`, and gives rebuild next actions. Package verification must add a module-trust check; package creation must refuse stale/tampered outputs before copying.

- [ ] **Step 2: Implement command, adapters, and workflow preflight**

Register one typed command for CLI/MCP. Add concise engine-status and README guidance that receipts are not a sandbox/signature. Reuse the inspector in verify/package workflows and preserve exact issues.

- [ ] **Step 3: Prove packaged and relocated verification**

Update package tests to require the receipt beside the module DLL, verify loading after source/project/SDK removal, reject a copied DLL mutation, and retain package relocation/run/audit success.

- [ ] **Step 4: Run focused integration tests GREEN and commit**

```powershell
git add src/Rekall.Age.Modules/Commands/InspectModuleTrustCommand.cs src/Rekall.Age.Cli/Program.cs src/Rekall.Age.Agent/Commands/GetEngineStatusCommand.cs src/Rekall.Age.Workflows tests/Rekall.Age.Tests README.md
git commit -m "feat: expose module trust inspection"
```

## Task 6: Installed adversarial acceptance and full product gate

**Files:**

- Modify: `eng/accept-distribution.ps1`
- Modify: `docs/production/PROGRESS.md`
- Modify: `docs/superpowers/plans/2026-08-18-module-trust-boundary.md`

- [ ] **Step 1: Extend installed acceptance**

Use the shipped CLI to scaffold/build/inspect a module and require ready/full-trust/receipt evidence. Copy its project or package to a separate proof root, mutate one byte in the copied module DLL, and require exact trust rejection before load. Then prove the untouched original/relocated package still runs and audits.

- [ ] **Step 2: Run full Debug verification**

```powershell
$env:TEMP='F:\Dev\Rekall_AGE\.worktrees\production-foundation\Artifacts\TestTemp'
$env:TMP=$env:TEMP
dotnet test Rekall.AGE.sln -c Debug -p:UseSharedCompilation=false --verbosity minimal
```

- [ ] **Step 3: Run the canonical installed product gate**

```powershell
$env:TEMP='F:\Dev\Rekall_AGE\.worktrees\production-foundation\Artifacts\GateTemp'
$env:TMP=$env:TEMP
pwsh -NoProfile -File eng\build.ps1
```

Require zero build warnings/errors, two independent full Release passes, a self-contained Windows distribution, installed adversarial module rejection, and the unchanged SDK/gauntlet/package/UI/audio/soak matrix.

- [ ] **Step 4: Review and record exact evidence**

Use requesting-code-review (subject to current delegation rules) and verification-before-completion. Record test count, exact trust codes, installed positive/negative results, archive size/hash, remaining unsigned-receipt/full-trust limitations, and next device-loss/crash priority in `docs/production/PROGRESS.md`.

- [ ] **Step 5: Commit and preserve the production branch**

```powershell
git add eng/accept-distribution.ps1 docs/production/PROGRESS.md docs/superpowers/plans/2026-08-18-module-trust-boundary.md
git commit -m "test: gate installed module trust boundary"
```
