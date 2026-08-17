# Product Contract and Portable SDK Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Rekall AGE identify itself as a versioned proprietary product and make every scaffolded game module build from a portable project-local SDK rather than an absolute repository reference.

**Architecture:** Add immutable product/stability metadata in Core, expose it through engine status, and add a Modules-owned SDK installer that copies the minimum reference-assembly closure into each authored project. All three scaffold commands consume one shared project-file writer, while module builds use isolated intermediates and return complete diagnostics. A read-only doctor command verifies the resulting product and SDK environment through the same command bus used by CLI and MCP.

**Tech Stack:** C# 13, .NET 10, xUnit, MSBuild/dotnet CLI, existing Rekall AGE command bus

**Spec:** `docs/superpowers/specs/2026-08-17-production-foundation-design.md`

## Global Constraints

- Rekall AGE is proprietary and Windows-first.
- Product version is `0.1.0-preview.1`; module SDK compatibility version is `1`.
- Supported capability status values are exactly `supported`, `experimental`, and `unavailable`.
- Generated module projects contain no absolute path and no `ProjectReference` to engine source.
- Game behavior remains in agent-authored modules; no genre behavior is added.
- New behavior follows red-green-refactor and preserves stable `REKALL_` error codes.

---

### Task 1: Product metadata and stability contract

**Files:**
- Modify: `Directory.Build.props`
- Create: `src/Rekall.Age.Core/Product/RekallAgeProductInfo.cs`
- Create: `tests/Rekall.Age.Tests/Core/ProductInfoTests.cs`

**Interfaces:**
- Produces: `RekallAgeProductInfo.Current : RekallAgeProductMetadata`
- Produces: `RekallAgeProductInfo.Capabilities : IReadOnlyList<RekallAgeCapabilityStatus>`
- Produces: `RekallAgeCapabilityStability` constants

- [x] **Step 1: Write the failing metadata test**

```csharp
[Fact]
public void ProductMetadataDefinesPreviewCompatibilityAndCapabilityStability()
{
    var product = RekallAgeProductInfo.Current;

    Assert.Equal("Rekall AGE", product.Name);
    Assert.Equal("0.1.0-preview.1", product.Version);
    Assert.Equal("preview", product.Channel);
    Assert.Equal(1, product.ProjectSchemaVersion);
    Assert.Equal(1, product.ModuleSdkCompatibilityVersion);
    Assert.True(product.Proprietary);
    Assert.Equal("supported", RekallAgeProductInfo.Capability("authoring.core").Stability);
    Assert.Equal("experimental", RekallAgeProductInfo.Capability("runtime.openxr").Stability);
    Assert.Equal("experimental", RekallAgeProductInfo.Capability("runtime.multiplayer").Stability);
}
```

- [x] **Step 2: Run the test and verify RED**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter FullyQualifiedName~ProductInfoTests --no-restore`

Expected: compilation fails because `RekallAgeProductInfo` does not exist.

- [x] **Step 3: Add centralized build metadata**

Add to `Directory.Build.props`:

```xml
<VersionPrefix>0.1.0</VersionPrefix>
<VersionSuffix>preview.1</VersionSuffix>
<Product>Rekall AGE</Product>
<Company>Rekall</Company>
<Copyright>Copyright © 2026 Rekall. All rights reserved.</Copyright>
```

- [x] **Step 4: Implement immutable product metadata**

Create records and lookup logic with this public API:

```csharp
public static class RekallAgeCapabilityStability
{
    public const string Supported = "supported";
    public const string Experimental = "experimental";
    public const string Unavailable = "unavailable";
}

public sealed record RekallAgeProductMetadata(
    string Name,
    string Version,
    string Channel,
    int ProjectSchemaVersion,
    int ModuleSdkCompatibilityVersion,
    bool Proprietary,
    string SupportedHost);

public sealed record RekallAgeCapabilityStatus(string Id, string Stability, string Summary);

public static class RekallAgeProductInfo
{
    public static RekallAgeProductMetadata Current { get; } = new(
        "Rekall AGE", "0.1.0-preview.1", "preview", 1, 1, true, "windows-x64");

    public static IReadOnlyList<RekallAgeCapabilityStatus> Capabilities { get; } =
    [
        new("authoring.core", RekallAgeCapabilityStability.Supported, "Project, scene, entity, component, transaction, and module authoring."),
        new("runtime.desktop", RekallAgeCapabilityStability.Supported, "Windows desktop runtime and player."),
        new("rendering.vulkan", RekallAgeCapabilityStability.Supported, "Vulkan-first desktop rendering."),
        new("runtime.openxr", RekallAgeCapabilityStability.Experimental, "Windowed OpenXR play and diagnostics."),
        new("runtime.multiplayer", RekallAgeCapabilityStability.Experimental, "Authoritative sessions, snapshots, deltas, and reconciliation."),
        new("assets.tripo", RekallAgeCapabilityStability.Experimental, "External text-to-model provider bridge."),
        new("rendering.virtual_geometry", RekallAgeCapabilityStability.Experimental, "CPU clustered mesh LOD."),
    ];

    public static RekallAgeCapabilityStatus Capability(string id) =>
        Capabilities.Single(item => item.Id.Equals(id, StringComparison.Ordinal));
}
```

- [x] **Step 5: Verify GREEN**

Run the filtered test again. Expected: PASS with zero warnings.

- [x] **Step 6: Commit**

```powershell
git add Directory.Build.props src/Rekall.Age.Core/Product/RekallAgeProductInfo.cs tests/Rekall.Age.Tests/Core/ProductInfoTests.cs
git commit -m "feat: add product stability metadata"
```

### Task 2: Expose product compatibility through engine status

**Files:**
- Modify: `src/Rekall.Age.Agent/Commands/GetEngineStatusCommand.cs`
- Modify: `tests/Rekall.Age.Tests/Agent/AgentContextCommandTests.cs`
- Modify: `tests/Rekall.Age.Tests/Cli/CliSmokeTests.cs`

**Interfaces:**
- Consumes: `RekallAgeProductInfo.Current`, `RekallAgeProductInfo.Capabilities`
- Produces: additional `Product` and `Capabilities` fields on `GetEngineStatusResult`

- [x] **Step 1: Add failing command assertions**

Extend the engine-status test:

```csharp
Assert.Equal("0.1.0-preview.1", result.Value.Product.Version);
Assert.Equal(1, result.Value.Product.ModuleSdkCompatibilityVersion);
Assert.True(result.Value.Product.Proprietary);
Assert.Contains(result.Value.Capabilities, item =>
    item.Id == "authoring.core" && item.Stability == "supported");
Assert.Contains(result.Value.Capabilities, item =>
    item.Id == "runtime.openxr" && item.Stability == "experimental");
```

Extend the CLI smoke assertion:

```csharp
Assert.Contains("Version: 0.1.0-preview.1", engine.Output);
Assert.Contains("Channel: preview", engine.Output);
Assert.Contains("runtime.openxr [experimental]", engine.Output);
```

- [x] **Step 2: Run both tests and verify RED**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "FullyQualifiedName~EngineStatus|FullyQualifiedName~CliCreatesGenericProject" --no-restore`

Expected: compilation fails because status lacks `Product` and `Capabilities`.

- [x] **Step 3: Extend the status result and CLI rendering**

Change the result signature to:

```csharp
public sealed record GetEngineStatusResult(
    string EngineName,
    bool AgentFirst,
    string RenderingPosture,
    RekallAgeProductMetadata Product,
    IReadOnlyList<RekallAgeCapabilityStatus> Capabilities,
    IReadOnlyList<RekallAgeAgentWorkflowTool> WorkflowTools,
    IReadOnlyList<RekallAgeAgentAuthoringContract> AuthoringContracts);
```

Populate the new fields from `RekallAgeProductInfo`, then print version, channel, proprietary status, supported host, SDK compatibility, and one line per capability in `PrintEngineStatusAsync`.

- [x] **Step 4: Verify GREEN**

Run the filtered tests. Expected: PASS.

- [x] **Step 5: Commit**

```powershell
git add src/Rekall.Age.Agent/Commands/GetEngineStatusCommand.cs tests/Rekall.Age.Tests/Agent/AgentContextCommandTests.cs tests/Rekall.Age.Tests/Cli/CliSmokeTests.cs
git commit -m "feat: expose product compatibility status"
```

### Task 3: Install a portable project-local module SDK

**Files:**
- Create: `src/Rekall.Age.Modules/Sdk/RekallAgeModuleSdkInstaller.cs`
- Create: `src/Rekall.Age.Modules/Sdk/RekallAgeModuleProjectFile.cs`
- Create: `tests/Rekall.Age.Tests/Modules/ModuleSdkInstallerTests.cs`

**Interfaces:**
- Produces: `ValueTask<RekallAgeModuleSdkInstallation> RekallAgeModuleSdkInstaller.InstallAsync(string projectRoot, CancellationToken cancellationToken)`
- Produces: `string RekallAgeModuleProjectFile.Create(string moduleName)`
- SDK location: `<project>/.rekall/sdk/1/`
- SDK files: `Rekall.Age.Core.dll`, `Rekall.Age.World.dll`, `Rekall.Age.Runtime.Abstractions.dll`, `Rekall.Age.Modules.dll`, `Rekall.Age.Sdk.props`, `rekall.sdk.json`

- [x] **Step 1: Write failing SDK installation tests**

```csharp
[Fact]
public async Task InstallerCreatesVersionedProjectLocalSdkWithRelativeProps()
{
    var root = TestPaths.CreateTempDirectory();
    var result = await new RekallAgeModuleSdkInstaller().InstallAsync(root, CancellationToken.None);

    Assert.Equal(1, result.CompatibilityVersion);
    Assert.Equal(Path.Combine(root, ".rekall", "sdk", "1"), result.SdkRoot);
    Assert.All(result.Assemblies, path => Assert.True(File.Exists(path), path));
    Assert.True(File.Exists(result.PropsPath));
    Assert.True(File.Exists(result.ManifestPath));
    var props = await File.ReadAllTextAsync(result.PropsPath);
    Assert.Contains("Rekall.Age.Modules.dll", props);
    Assert.DoesNotContain(Path.GetFullPath("."), props, StringComparison.OrdinalIgnoreCase);
}

[Fact]
public void ProjectFileImportsProjectLocalSdkWithoutAbsolutePaths()
{
    var project = RekallAgeModuleProjectFile.Create("AgentModule");
    Assert.Contains("..\\..\\.rekall\\sdk\\1\\Rekall.Age.Sdk.props", project);
    Assert.DoesNotContain("ProjectReference", project);
    Assert.DoesNotContain(Path.GetPathRoot(Environment.CurrentDirectory)!, project);
}
```

- [x] **Step 2: Run tests and verify RED**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter FullyQualifiedName~ModuleSdkInstallerTests --no-restore`

Expected: compilation fails because the installer and project writer do not exist.

- [x] **Step 3: Implement SDK assembly discovery and installation**

Use `typeof(RekallAgeModule).Assembly.Location` as the anchor and resolve the four named assemblies from loaded assembly locations or the anchor directory. Copy each file only when bytes differ. Write a JSON manifest containing product version, compatibility version, and relative assembly names.

The generated props file must use `$(MSBuildThisFileDirectory)`:

```xml
<Project>
  <ItemGroup>
    <Reference Include="Rekall.Age.Core" HintPath="$(MSBuildThisFileDirectory)Rekall.Age.Core.dll" Private="false" />
    <Reference Include="Rekall.Age.World" HintPath="$(MSBuildThisFileDirectory)Rekall.Age.World.dll" Private="false" />
    <Reference Include="Rekall.Age.Runtime.Abstractions" HintPath="$(MSBuildThisFileDirectory)Rekall.Age.Runtime.Abstractions.dll" Private="false" />
    <Reference Include="Rekall.Age.Modules" HintPath="$(MSBuildThisFileDirectory)Rekall.Age.Modules.dll" Private="false" />
  </ItemGroup>
</Project>
```

- [x] **Step 4: Implement the shared project-file writer**

Return a `net10.0` project containing nullable/implicit-usings settings and:

```xml
<Import Project="..\..\.rekall\sdk\1\Rekall.Age.Sdk.props"
        Condition="Exists('..\..\.rekall\sdk\1\Rekall.Age.Sdk.props')" />
<Target Name="ValidateRekallAgeSdk" BeforeTargets="ResolveReferences"
        Condition="!Exists('..\..\.rekall\sdk\1\Rekall.Age.Sdk.props')">
  <Error Code="REKALL_SDK_MISSING"
         Text="Rekall AGE module SDK compatibility version 1 is missing. Re-run a Rekall AGE module scaffold or SDK repair command." />
</Target>
```

- [x] **Step 5: Verify GREEN**

Run the filtered tests. Expected: PASS.

- [x] **Step 6: Commit**

```powershell
git add src/Rekall.Age.Modules/Sdk tests/Rekall.Age.Tests/Modules/ModuleSdkInstallerTests.cs
git commit -m "feat: add portable module sdk"
```

### Task 4: Migrate all scaffold commands to the portable SDK

**Files:**
- Modify: `src/Rekall.Age.Modules/Commands/ScaffoldModuleCommand.cs`
- Modify: `src/Rekall.Age.Modules/Commands/ScaffoldPlayableModuleCommand.cs`
- Modify: `src/Rekall.Age.Modules/Commands/ScaffoldRuntimeSystemModuleCommand.cs`
- Modify: `tests/Rekall.Age.Tests/Modules/ScaffoldModuleCommandTests.cs`
- Modify: `tests/Rekall.Age.Tests/Modules/ScaffoldPlayableModuleCommandTests.cs`
- Modify: `tests/Rekall.Age.Tests/Modules/ScaffoldRuntimeSystemModuleCommandTests.cs`

**Interfaces:**
- Consumes: `RekallAgeModuleSdkInstaller.InstallAsync`, `RekallAgeModuleProjectFile.Create`
- Produces: portable scaffold projects with SDK resources recorded in the active transaction

- [x] **Step 1: Add failing portability assertions to each scaffold test**

For each scaffold result, assert:

```csharp
var projectFile = await File.ReadAllTextAsync(scaffold.Value.ProjectPath);
Assert.DoesNotContain("ProjectReference", projectFile);
Assert.DoesNotContain(Path.GetFullPath("."), projectFile, StringComparison.OrdinalIgnoreCase);
Assert.Contains(".rekall\\sdk\\1\\Rekall.Age.Sdk.props", projectFile);
Assert.True(File.Exists(Path.Combine(root, ".rekall", "sdk", "1", "rekall.sdk.json")));
```

- [x] **Step 2: Run the three scaffold test classes and verify RED**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "FullyQualifiedName~ScaffoldModuleCommandTests|FullyQualifiedName~ScaffoldPlayableModuleCommandTests|FullyQualifiedName~ScaffoldRuntimeSystemModuleCommandTests" --no-restore`

Expected: assertions fail because projects still contain absolute source references.

- [x] **Step 3: Replace duplicated project discovery/writing**

In each command, install the project-local SDK before writing the module project:

```csharp
var sdk = await new RekallAgeModuleSdkInstaller().InstallAsync(request.ProjectRoot, context.CancellationToken);
await File.WriteAllTextAsync(
    projectPath,
    RekallAgeModuleProjectFile.Create(moduleName),
    context.CancellationToken);
foreach (var resource in sdk.Resources)
{
    context.Transaction.RecordChangedResource(resource);
}
```

Delete all three `FindModulesProjectPath` methods and all three private `CreateProjectFile` copies.

- [x] **Step 4: Verify GREEN and buildability**

Run the filtered tests. Expected: PASS, including the existing build assertions.

- [x] **Step 5: Commit**

```powershell
git add src/Rekall.Age.Modules/Commands tests/Rekall.Age.Tests/Modules
git commit -m "refactor: scaffold modules against portable sdk"
```

### Task 5: Make module builds isolated and diagnostically complete

**Files:**
- Modify: `src/Rekall.Age.Build/Commands/BuildModulesCommand.cs`
- Modify: `tests/Rekall.Age.Tests/Build/BuildModulesCommandTests.cs`

**Interfaces:**
- Extends: `BuildModuleResult` with `int ExitCode` and `string SdkVersion`
- Build isolation: `<module>/obj/rekall/` and `<module>/bin/rekall/`
- Preserves final assembly discovery through MSBuild property `TargetPath`

- [x] **Step 1: Write a failing concurrency regression test**

```csharp
[Fact]
public async Task PortableModulesBuildConcurrentlyWithoutSharingEngineOutputs()
{
    var roots = Enumerable.Range(0, 6).Select(_ => TestPaths.CreateTempDirectory()).ToArray();
    var tasks = roots.Select(async (root, index) =>
    {
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("parallel module"), CancellationToken.None);
        await new ScaffoldPlayableModuleCommand().ExecuteAsync(
            new ScaffoldPlayableModuleRequest(root, $"parallel.{index}", $"Parallel {index}", $"Parallel{index}"), context);
        return await new BuildModulesCommand().ExecuteAsync(new BuildModulesRequest(root), context);
    });

    var results = await Task.WhenAll(tasks);
    Assert.All(results, result => Assert.True(result.Ok,
        string.Join(Environment.NewLine, result.Value.Modules.Select(module => module.Output))));
    Assert.All(results.SelectMany(result => result.Value.Modules), module =>
    {
        Assert.Equal(0, module.ExitCode);
        Assert.Equal("1", module.SdkVersion);
        Assert.True(File.Exists(module.AssemblyPath));
    });
}
```

- [x] **Step 2: Run the regression repeatedly and verify RED**

Run three times:

`dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter FullyQualifiedName~PortableModulesBuildConcurrently --no-restore`

Expected before implementation: compile failure due to absent result fields; after adding only assertions, at least one run exposes the old shared-reference behavior if retained.

- [x] **Step 3: Add isolated MSBuild arguments and structured diagnostics**

Invoke `dotnet build` with:

```csharp
startInfo.ArgumentList.Add("-p:BaseIntermediateOutputPath=obj/rekall/");
startInfo.ArgumentList.Add("-p:OutputPath=bin/rekall/");
startInfo.ArgumentList.Add("-p:AppendTargetFrameworkToOutputPath=true");
startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
```

Derive the module assembly as `bin/rekall/net10.0/<ModuleName>.dll`. Read `.rekall/sdk/1/rekall.sdk.json` and record compatibility version `1`. Return the real process exit code even when the output file is missing. Preserve combined stdout/stderr in `Output` and include it unchanged in `REKALL_MODULE_BUILD_FAILED`.

- [x] **Step 4: Verify GREEN and repeatability**

Run the concurrency test three times, then all build/module/player tests:

`dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "FullyQualifiedName~Build|FullyQualifiedName~Module|FullyQualifiedName~PlayerSmoke" --no-restore`

Expected: all runs PASS.

- [x] **Step 5: Commit**

```powershell
git add src/Rekall.Age.Build/Commands/BuildModulesCommand.cs tests/Rekall.Age.Tests/Build/BuildModulesCommandTests.cs
git commit -m "fix: isolate authored module builds"
```

### Task 6: Add agent-readable product doctor diagnostics

**Files:**
- Create: `src/Rekall.Age.Workflows/Commands/InspectEngineDoctorCommand.cs`
- Create: `tests/Rekall.Age.Tests/Workflows/EngineDoctorTests.cs`
- Modify: `src/Rekall.Age.Cli/Program.cs`
- Modify: `tests/Rekall.Age.Tests/Cli/CliSmokeTests.cs`
- Modify: `tests/Rekall.Age.Tests/Mcp/McpCatalogTests.cs`

**Interfaces:**
- Produces command: `rekall.context.doctor`
- Produces records: `RekallAgeDoctorCheck`, `InspectEngineDoctorRequest`, `InspectEngineDoctorResult`
- CLI route: `context doctor [projectRoot]`

- [x] **Step 1: Write failing doctor command tests**

```csharp
[Fact]
public async Task DoctorReportsProductHostAndPortableSdkEvidence()
{
    var root = TestPaths.CreateTempDirectory();
    await new RekallAgeModuleSdkInstaller().InstallAsync(root, CancellationToken.None);
    var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("doctor"), CancellationToken.None);

    var result = await new InspectEngineDoctorCommand().ExecuteAsync(new(root), context);

    Assert.True(result.Ok, result.Summary);
    Assert.Equal("0.1.0-preview.1", result.Value.Product.Version);
    Assert.Contains(result.Value.Checks, check => check.Id == "host.os" && check.Severity == "info");
    Assert.Contains(result.Value.Checks, check => check.Id == "sdk.module" && check.Status == "ready");
    Assert.DoesNotContain(result.Value.Checks.SelectMany(check => check.Evidence), item =>
        item.Contains(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), StringComparison.OrdinalIgnoreCase));
}
```

Also test a missing SDK returns `Ok == false`, check status `blocked`, code `REKALL_SDK_MISSING`, and a suggested `rekall.module.scaffold_runtime_system` command.

- [x] **Step 2: Run doctor tests and verify RED**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter FullyQualifiedName~EngineDoctorTests --no-restore`

Expected: compilation fails because doctor types do not exist.

- [x] **Step 3: Implement read-only checks**

Use these records:

```csharp
public sealed record RekallAgeDoctorCheck(
    string Id,
    string Status,
    string Severity,
    string Summary,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<RekallAgeSuggestedCommand> NextActions);

public sealed record InspectEngineDoctorRequest(string? ProjectRoot = null);

public sealed record InspectEngineDoctorResult(
    RekallAgeProductMetadata Product,
    string OperatingSystem,
    string Architecture,
    IReadOnlyList<RekallAgeDoctorCheck> Checks);
```

Implement checks for Windows host, x64 process, product metadata, and optional project SDK manifest/assemblies. Paths in evidence are project-relative labels (`.rekall/sdk/1`) rather than absolute user paths. Return failure only for blocking supported-core checks.

- [x] **Step 4: Register CLI/MCP command and add CLI output**

Register `InspectEngineDoctorCommand` in `BuildRegistry`, add routes for `context doctor` and `context doctor <root>`, and print:

```text
Rekall AGE doctor 0.1.0-preview.1 [preview]
host.os: ready [info]
host.architecture: ready [info]
sdk.module: ready [info]
```

CLI returns `1` when any blocking check exists and `0` otherwise.

Register the command in the MCP catalog test registry and assert `tools/list` contains `rekall.context.doctor`; this proves the shared registry exposes the same structured command to agents.

- [x] **Step 5: Verify GREEN**

Run doctor and CLI smoke tests. Expected: PASS.

- [x] **Step 6: Commit**

```powershell
git add src/Rekall.Age.Workflows/Commands/InspectEngineDoctorCommand.cs src/Rekall.Age.Cli/Program.cs tests/Rekall.Age.Tests/Workflows/EngineDoctorTests.cs tests/Rekall.Age.Tests/Cli/CliSmokeTests.cs tests/Rekall.Age.Tests/Mcp/McpCatalogTests.cs
git commit -m "feat: add engine doctor diagnostics"
```

### Task 7: Prove the product/SDK foundation

**Files:**
- Modify: `README.md`
- Modify: `.gitignore`
- Modify: `docs/superpowers/plans/2026-08-17-product-sdk-foundation.md`

**Interfaces:**
- Validates all interfaces introduced by Tasks 1–6.

- [x] **Step 1: Document product status and portable SDK behavior**

Change README wording to identify Rekall AGE as proprietary Developer Preview software. Add product version, supported/experimental table, `context doctor` command, and explain that `.rekall/sdk/1` is engine-managed project state. Do not claim an open-source or MIT license.

- [x] **Step 2: Ignore generated SDK state**

Add this exact entry to `.gitignore`:

```gitignore
# Project-local Rekall AGE SDK installed by the engine
.rekall/
```

- [x] **Step 3: Run repository policy scans**

Run:

```powershell
rg -n -i "open[ -]source|MIT License" README.md src tests docs
rg -n "ProjectReference Include=.*Rekall.Age.Modules" src/Rekall.Age.Modules/Commands
git diff --check
```

Expected: no product/open-source claim, no scaffold source reference, and no whitespace errors. Historical design documents may describe rejected alternatives but must not claim current licensing.

- [x] **Step 4: Run the complete suite twice**

Run twice without disabling test parallelism:

`dotnet test Rekall.AGE.sln --no-restore -c Release --verbosity minimal`

Expected each time: all tests pass, zero skipped, zero warnings.

- [x] **Step 5: Run a CLI proof outside repository project data**

Create a temporary project through CLI, then execute `project create`, `scene create`, `module scaffold-runtime-system`, `build modules`, `context doctor`, `validation scene`, and `game gauntlet`. Verify its generated `.csproj` contains only the relative SDK import and its built module loads.

- [x] **Step 6: Mark plan tasks complete and commit**

```powershell
git add README.md .gitignore docs/superpowers/plans/2026-08-17-product-sdk-foundation.md
git commit -m "docs: establish proprietary developer preview"
```

- [x] **Step 7: Record verification evidence**

Capture the two test totals, CLI proof result, current commit, and remaining untracked user files in the completion handoff. Do not add or modify `Hero.png`; it requires a separate branding edit because its current license text is incorrect.

## Execution Notes

- The portable MSBuild output path is explicitly `bin/rekall/net10.0` because setting `OutputPath` suppresses target-framework appending.
- `RekallAgeProjectModuleAssemblyLoader` was updated to load portable SDK output while retaining the legacy Debug path for existing repository examples.
- Portable output isolation is applied only to SDK-importing projects so legacy source-reference examples are not broken by MSBuild property propagation.
- Release verification passed twice consecutively with 497 tests, zero failures, zero skips, and no warnings.
- The external CLI proof created and built a runtime-system module with no repository reference; doctor reported ready and the agent-authoring gauntlet packaged, audited, ran, and captured a non-blank proof frame.
