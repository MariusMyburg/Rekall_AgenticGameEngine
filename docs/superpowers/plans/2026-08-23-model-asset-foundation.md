# Model Asset Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Publish an editable AGE mesh as a stable, revisioned Model Asset, inspect its dependency health, and instantiate it into a scene through canonical CLI/MCP commands.

**Architecture:** `Rekall.Age.Assets` owns the dependency-neutral Model Asset document and store. `Rekall.Age.AssetPipeline` depends on Modeling to validate, compile, hash, and atomically publish derived mesh snapshots. `Rekall.Age.LevelDesign` consumes only the published Model Asset contract to create ordinary scene entities with stable asset references.

**Tech Stack:** C# 14, .NET 10, System.Text.Json, existing AGE atomic persistence and transaction contracts, xUnit.

**Spec:** `docs/superpowers/specs/2026-08-23-model-publishing-and-placement-design.md`

## Global Constraints

- Live linking is the default; source and published asset IDs remain stable across rebuilds.
- Publishing and placement use canonical revision-checked AGE transactions and are exposed to MCP through the default command registry.
- Derived output is replaced only after the complete staged result validates; a failed rebuild retains the last successful output.
- Gameplay behavior remains agent-authored entity components; Model Assets do not embed genre-specific behavior.
- Identical normalized inputs and compiler version must produce identical content hashes.
- No scene document copies compiled geometry; scenes store `Rekall.ModelAssetReference`.
- This first slice supports editable mesh sources. Graph outputs, modifiers, compound hierarchies, Studio drag/drop, Prefab v2, and hot reload remain subsequent vertical slices defined by the design specification.

---

### Task 1: Versioned Model Asset contract and store

**Files:**
- Create: `src/Rekall.Age.Assets/RekallAgeModelAssetDocument.cs`
- Create: `src/Rekall.Age.Assets/RekallAgeModelAssetStore.cs`
- Test: `tests/Rekall.Age.Tests/Assets/ModelAssetPersistenceTests.cs`

**Interfaces:**
- Consumes: `RekallAgeAtomicFile`, `RekallAgeDocumentSchemaProbe`, `RekallAgeDocumentRevision`, and `RekallAgeVersionedDocument<T>` from Core.
- Produces: `RekallAgeModelAssetDocument`, `RekallAgeModelSourceReference`, `RekallAgeModelBuildManifest`, `RekallAgeModelBuildDiagnostic`, `RekallAgeModelBuildState`, and `RekallAgeModelAssetStore`.

- [ ] **Step 1: Write persistence and validation tests**

Add tests proving a valid document round-trips under `Assets/Models`, new documents begin at logical revision 1, IDs cannot escape the directory, source IDs and output hashes are required, and `SaveIfRevisionAsync` rejects a stale expected file revision.

```csharp
[Fact]
public async Task ModelAssetRoundTripsAsVersionedProjectAsset()
{
    var document = RekallAgeModelAssetDocument.Create(
        "hero-model", "Hero Model",
        new(RekallAgeModelSourceKind.Mesh, "hero-mesh"),
        RekallAgeModelBuildManifest.Success(
            sourceFileRevision: "source-revision",
            sourceLogicalRevision: 1,
            compiledMeshPath: "Assets/Models/Compiled/hero-model.age.compiled-mesh.json",
            compiledContentHash: new string('a', 64),
            compilerVersion: RekallAgeModelBuildManifest.CurrentCompilerVersion));
    var store = new RekallAgeModelAssetStore();
    var revision = await store.SaveIfRevisionAsync(root, document, RekallAgeDocumentRevision.Missing, default);
    var loaded = await store.LoadVersionedAsync(root, "hero-model", default);
    Assert.Equal(revision, loaded.Revision);
    Assert.Equal(document, loaded.Value);
}
```

- [ ] **Step 2: Run the focused tests and verify they fail**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --no-restore --filter FullyQualifiedName~ModelAssetPersistenceTests`

Expected: FAIL because the Model Asset types and store do not exist.

- [ ] **Step 3: Implement immutable contracts and strict persistence**

Define these stable shapes:

```csharp
public enum RekallAgeModelSourceKind { Mesh }
public enum RekallAgeModelBuildState { Current, Stale, Failed, Frozen }

public sealed record RekallAgeModelSourceReference(
    RekallAgeModelSourceKind Kind,
    string AssetId,
    string? OutputName = null);

public sealed record RekallAgeModelBuildDiagnostic(
    string Code,
    string Severity,
    string Message,
    string? Target = null);

public sealed record RekallAgeModelBuildManifest(
    string SourceFileRevision,
    long SourceLogicalRevision,
    string CompiledMeshPath,
    string CompiledContentHash,
    string CompilerVersion,
    DateTimeOffset BuiltAtUtc,
    IReadOnlyList<RekallAgeModelBuildDiagnostic> Diagnostics);

public sealed record RekallAgeModelAssetDocument(
    int SchemaVersion,
    string AssetId,
    string DisplayName,
    long Revision,
    RekallAgeModelSourceReference Source,
    RekallAgeModelBuildState BuildState,
    RekallAgeModelBuildManifest? LastSuccessfulBuild,
    bool Frozen);
```

The store path is `Assets/Models/<asset-id>.age.model.json`. Follow the mesh-store pattern for atomic writes, recovery snapshots, safe IDs, exact document-ID matching, schema probing, logical-revision advancement, and optimistic file revisions. Reject absolute or escaping compiled paths, non-64-character lowercase hexadecimal hashes, empty dependency fields, nonpositive logical revisions, and a Current/Frozen asset without a successful manifest.

- [ ] **Step 4: Run persistence tests**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --no-restore --filter FullyQualifiedName~ModelAssetPersistenceTests`

Expected: PASS.

- [ ] **Step 5: Commit the contract and store**

```powershell
git add src/Rekall.Age.Assets/RekallAgeModelAssetDocument.cs src/Rekall.Age.Assets/RekallAgeModelAssetStore.cs tests/Rekall.Age.Tests/Assets/ModelAssetPersistenceTests.cs
git commit -m "feat(assets): add versioned model asset contracts"
```

### Task 2: Deterministic compiled-output persistence

**Files:**
- Modify: `src/Rekall.Age.AssetPipeline/Rekall.Age.AssetPipeline.csproj`
- Create: `src/Rekall.Age.AssetPipeline/RekallAgePublishedModelOutputStore.cs`
- Test: `tests/Rekall.Age.Tests/Assets/PublishedModelOutputStoreTests.cs`

**Interfaces:**
- Consumes: `RekallAgeCompiledMeshSnapshot` from Modeling.Contracts and Core atomic persistence.
- Produces: `RekallAgePublishedModelOutputStore.WriteStagedAsync`, `CommitStagedAsync`, `LoadAsync`, `HashAsync`, and `DeleteStagedAsync`.

- [ ] **Step 1: Write failing deterministic-output tests**

Create a compiled box snapshot and prove two serializations are byte-equivalent, the SHA-256 is lowercase hexadecimal, the final relative path is `Assets/Models/Compiled/<asset-id>.age.compiled-mesh.json`, and staging does not replace the current output until `CommitStagedAsync` succeeds.

```csharp
var first = await store.WriteStagedAsync(root, "hero-model", snapshot, default);
var second = await store.WriteStagedAsync(root, "hero-model", snapshot, default);
Assert.Equal(first.ContentHash, second.ContentHash);
Assert.Equal(await File.ReadAllBytesAsync(first.Path), await File.ReadAllBytesAsync(second.Path));
Assert.False(File.Exists(store.GetFinalPath(root, "hero-model")));
```

- [ ] **Step 2: Run tests and verify the missing implementation failure**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --no-restore --filter FullyQualifiedName~PublishedModelOutputStoreTests`

Expected: FAIL because the output store does not exist.

- [ ] **Step 3: Implement canonical JSON output and staging**

Add the Modeling.Contracts project reference to AssetPipeline. Serialize with camelCase, stable property order from records, indentation, maximum persisted-document depth, and a trailing newline. Stage under `Assets/Models/.staging/<transaction-id>/`; validate by deserializing before commit. Commit with atomic replacement into `Assets/Models/Compiled`. Return:

```csharp
public sealed record RekallAgeStagedModelOutput(
    string Path,
    string RelativeFinalPath,
    string ContentHash,
    RekallAgeCompiledMeshSnapshot Snapshot);
```

Reject unsafe asset IDs, oversized output, nonfinite compiled vertex data, invalid indices, and paths outside the project Model Asset root.

- [ ] **Step 4: Run output-store tests**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --no-restore --filter FullyQualifiedName~PublishedModelOutputStoreTests`

Expected: PASS.

- [ ] **Step 5: Commit deterministic output persistence**

```powershell
git add src/Rekall.Age.AssetPipeline/Rekall.Age.AssetPipeline.csproj src/Rekall.Age.AssetPipeline/RekallAgePublishedModelOutputStore.cs tests/Rekall.Age.Tests/Assets/PublishedModelOutputStoreTests.cs
git commit -m "feat(assets): persist deterministic model outputs"
```

### Task 3: Publish and rebuild editable meshes

**Files:**
- Modify: `src/Rekall.Age.AssetPipeline/Rekall.Age.AssetPipeline.csproj`
- Create: `src/Rekall.Age.AssetPipeline/RekallAgeModelPublishingService.cs`
- Create: `src/Rekall.Age.AssetPipeline/Commands/ModelAssetCommands.cs`
- Test: `tests/Rekall.Age.Tests/Assets/ModelAssetPublishingTests.cs`

**Interfaces:**
- Consumes: `RekallAgeMeshAssetStore.LoadVersionedAsync`, `RekallAgeMeshCompiler.Compile`, Task 1 Model Asset store, and Task 2 output store.
- Produces: `RekallAgeModelPublishingService.PublishAsync`, `RebuildAsync`, `InspectAsync`; `PublishModelAssetCommand`, `RebuildModelAssetCommand`, and `InspectModelAssetCommand`.

- [ ] **Step 1: Write failing publish/rebuild tests**

Prove first publish creates a Current logical revision-1 Model Asset and compiled output; identical rebuild preserves the same output hash but advances the asset revision; a changed source produces a different dependency revision and correct manifest; stale expected Model Asset revisions fail; invalid source compilation retains the prior output and Model Asset revision; frozen assets reject rebuild.

```csharp
var publish = await service.PublishAsync(
    root,
    new("hero-model", "Hero Model", new(RekallAgeModelSourceKind.Mesh, "hero-mesh"), RekallAgeDocumentRevision.Missing),
    transaction,
    default);
Assert.Equal(RekallAgeModelBuildState.Current, publish.Asset.BuildState);
Assert.True(File.Exists(Path.Combine(root, publish.Asset.LastSuccessfulBuild!.CompiledMeshPath)));
```

- [ ] **Step 2: Run publishing tests and verify failure**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --no-restore --filter FullyQualifiedName~ModelAssetPublishingTests`

Expected: FAIL because the publishing service and commands do not exist.

- [ ] **Step 3: Implement the service with stage-validate-commit ordering**

Use request/result shapes:

```csharp
public sealed record RekallAgePublishModelRequest(
    string AssetId,
    string DisplayName,
    RekallAgeModelSourceReference Source,
    string ExpectedModelFileRevision);

public sealed record RekallAgePublishModelResult(
    RekallAgeModelAssetDocument Asset,
    string ModelFileRevision,
    string CompiledOutputPath,
    string CompiledContentHash);
```

Load and compile the source before writing. Stage and validate the output. Capture preimages for an existing Model Asset and output. Commit the staged output, then save the new Model Asset with exact expected revision, update the asset catalog with kind `model`, and record the output, Model Asset, and catalog as changed resources. If Model Asset persistence fails after output commit, restore the output preimage through the transaction before returning failure. Always remove staging data in `finally`.

Compute health by comparing the current mesh file revision and logical revision with the manifest. `InspectAsync` returns Current, Stale, Frozen, Failed, or missing-source diagnostics without mutating state.

- [ ] **Step 4: Implement bounded canonical commands**

Expose:

```csharp
rekall.asset.model.publish
rekall.asset.model.rebuild
rekall.asset.model.inspect
```

Command schemas must explain live linking, required expected revisions, retained last-successful output on failure, and exact source shape. Convert known validation, revision, compile, I/O, and JSON exceptions into bounded command errors with stable `REKALL_MODEL_*` codes.

- [ ] **Step 5: Run publishing tests**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --no-restore --filter FullyQualifiedName~ModelAssetPublishingTests`

Expected: PASS.

- [ ] **Step 6: Commit publishing and inspection**

```powershell
git add src/Rekall.Age.AssetPipeline/Rekall.Age.AssetPipeline.csproj src/Rekall.Age.AssetPipeline/RekallAgeModelPublishingService.cs src/Rekall.Age.AssetPipeline/Commands/ModelAssetCommands.cs tests/Rekall.Age.Tests/Assets/ModelAssetPublishingTests.cs
git commit -m "feat(assets): publish live-linked model assets"
```

### Task 4: Catalog listing and dependency health

**Files:**
- Modify: `src/Rekall.Age.Assets/RekallAgeAssetDocument.cs`
- Modify: `src/Rekall.Age.Assets/Commands/ListAssetsCommand.cs`
- Modify: `src/Rekall.Age.AssetPipeline/Commands/ModelAssetCommands.cs`
- Test: `tests/Rekall.Age.Tests/Assets/ModelAssetListingTests.cs`

**Interfaces:**
- Consumes: Model Asset store and publishing-service health inspection.
- Produces: `ListModelAssetsCommand` and model-specific optional metadata on `RekallAgeAssetDocument`.

- [ ] **Step 1: Write failing listing tests**

Publish two assets, mutate one source, and assert `rekall.asset.model.list` returns deterministic asset-ID order with one Current and one Stale entry. Assert the generic asset catalog exposes the stable Model Asset ID, document path, compiled output path/hash, and source relationship without embedding the full model document.

- [ ] **Step 2: Run listing tests and verify failure**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --no-restore --filter FullyQualifiedName~ModelAssetListingTests`

Expected: FAIL because model health listing is absent.

- [ ] **Step 3: Add catalog metadata and list command**

Add:

```csharp
public sealed record RekallAgeModelAssetCatalogMetadata(
    string ModelDocumentPath,
    string SourceKind,
    string SourceAssetId,
    string CompiledOutputPath,
    string CompiledContentHash);
```

as an optional property on `RekallAgeAssetDocument`. `ListModelAssetsCommand` enumerates Model Asset documents, inspects each boundedly, sorts by asset ID, limits output to the engine command-output budget, and reports per-item build state and diagnostics.

- [ ] **Step 4: Run listing and existing asset tests**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~ModelAssetListingTests|FullyQualifiedName~Asset"`

Expected: PASS.

- [ ] **Step 5: Commit catalog health listing**

```powershell
git add src/Rekall.Age.Assets/RekallAgeAssetDocument.cs src/Rekall.Age.Assets/Commands/ListAssetsCommand.cs src/Rekall.Age.AssetPipeline/Commands/ModelAssetCommands.cs tests/Rekall.Age.Tests/Assets/ModelAssetListingTests.cs
git commit -m "feat(assets): expose model asset dependency health"
```

### Task 5: Canonical scene placement

**Files:**
- Modify: `src/Rekall.Age.World/RekallAgeBuiltInComponentTypeCatalog.cs`
- Create: `src/Rekall.Age.LevelDesign/Commands/InstantiateModelAssetCommand.cs`
- Test: `tests/Rekall.Age.Tests/LevelDesign/ModelAssetPlacementTests.cs`

**Interfaces:**
- Consumes: `RekallAgeModelAssetStore`, `RekallAgeSceneStore`, ordinary entity/component documents, and transaction preimages.
- Produces: `InstantiateModelAssetCommand` named `rekall.scene.instantiate_asset` for Model Assets.

- [ ] **Step 1: Write failing placement tests**

Prove a published Current Model Asset creates one visible entity containing `Rekall.Transform3D`, `Rekall.ModelAssetReference`, and `Rekall.MeshRenderer`; exact position/rotation/scale and optional parent are retained; the scene transaction is undoable; unbuilt or missing assets fail without scene mutation; a Stale asset is allowed with a warning and its last successful output; an invalid parent fails.

```csharp
var result = await registry.ExecuteAsync<InstantiateModelAssetRequest, InstantiateModelAssetResult>(
    "rekall.scene.instantiate_asset",
    new(root, "Main", "hero-model", "Hero", new(1, 2, 3), new(0, 45, 0), new(1, 1, 1)),
    context);
var entity = result.Value.Scene.GetRequiredEntity(result.Value.EntityId);
Assert.Equal("hero-model", entity.Components.Single(c => c.Type == "Rekall.ModelAssetReference").Properties["assetId"]!.GetValue<string>());
```

- [ ] **Step 2: Run placement tests and verify failure**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --no-restore --filter FullyQualifiedName~ModelAssetPlacementTests`

Expected: FAIL because the placement command and component type do not exist.

- [ ] **Step 3: Implement generic placement**

Define finite three-component vector request records and:

```csharp
public sealed record InstantiateModelAssetRequest(
    string ProjectRoot,
    string SceneName,
    string ModelAssetId,
    string? Name,
    RekallAgePlacementVector3 Position,
    RekallAgePlacementVector3 RotationDegrees,
    RekallAgePlacementVector3 Scale,
    string? ParentEntityId = null,
    string? ExpectedSceneRevision = null);
```

Validate finite transforms, nonzero bounded scale, parent existence, Model Asset health, and last-successful output existence. Create the three components with stable lower-camel property names and no copied geometry. Save via exact scene revision, record the scene preimage/change, and return warnings separately from errors.

- [ ] **Step 4: Run placement and runtime mesh-reference tests**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~ModelAssetPlacementTests|FullyQualifiedName~MeshAsset"`

Expected: PASS.

- [ ] **Step 5: Commit scene placement**

```powershell
git add src/Rekall.Age.World/RekallAgeBuiltInComponentTypeCatalog.cs src/Rekall.Age.LevelDesign/Commands/InstantiateModelAssetCommand.cs tests/Rekall.Age.Tests/LevelDesign/ModelAssetPlacementTests.cs
git commit -m "feat(level): instantiate published model assets"
```

### Task 6: Registry, MCP discovery, and end-to-end contract

**Files:**
- Modify: `src/Rekall.Age.Workflows/RekallAgeDefaultCommandRegistry.cs`
- Test: `tests/Rekall.Age.Tests/Assets/ModelAssetCommandContractTests.cs`
- Modify: `docs/production/PROGRESS.md`

**Interfaces:**
- Consumes: all commands from Tasks 3–5 and `RekallAgeMcpCatalog.FromRegistry`.
- Produces: default CLI/MCP discoverability and a durable verified progress record.

- [ ] **Step 1: Write the failing end-to-end contract test**

Assert the default registry and MCP catalog expose publish, rebuild, inspect,
list, and instantiate commands. Execute the complete flow using only registry
commands: create mesh, publish, list, create scene, instantiate, inspect entity,
mutate the source mesh, inspect Stale, rebuild, and prove the entity retains its
Model Asset ID and attached agent-owned component data.

- [ ] **Step 2: Run the contract test and verify registration failure**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --no-restore --filter FullyQualifiedName~ModelAssetCommandContractTests`

Expected: FAIL because the commands are not registered.

- [ ] **Step 3: Register commands in dependency order**

Register exactly:

```csharp
registry.Register(new PublishModelAssetCommand());
registry.Register(new RebuildModelAssetCommand());
registry.Register(new InspectModelAssetCommand());
registry.Register(new ListModelAssetsCommand());
registry.Register(new InstantiateModelAssetCommand());
```

Do not add bespoke MCP code; MCP discovery must derive from the registry.

- [ ] **Step 4: Run the end-to-end and related test suites**

Run:

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~ModelAsset|FullyQualifiedName~PublishedModelOutput"
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --no-restore --filter FullyQualifiedName~Modeling
dotnet test tests/Rekall.Age.Studio.Tests/Rekall.Age.Studio.Tests.csproj --no-restore
dotnet build Rekall.Age.slnx -c Release --no-restore
```

Expected: all tests pass; Release build has zero errors and no new warnings.

- [ ] **Step 5: Update the production ledger with exact evidence**

Add the stable Model Asset lifecycle, command names, end-to-end proof, test totals,
and explicit next Studio slice to the top of `docs/production/PROGRESS.md`.

- [ ] **Step 6: Commit and push the verified foundation**

```powershell
git add src/Rekall.Age.Workflows/RekallAgeDefaultCommandRegistry.cs tests/Rekall.Age.Tests/Assets/ModelAssetCommandContractTests.cs docs/production/PROGRESS.md
git commit -m "feat(assets): complete model publishing foundation"
git push origin HEAD
```

