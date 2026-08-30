# Studio Content Browser Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a unified, searchable Studio Content Browser that opens authored content in the correct editor and imports dropped project assets through the canonical report pipeline.

**Architecture:** Public editor contracts describe provider-neutral content items. Studio composes imported catalog entries and authored-store sources into one deterministic index, routes open actions through one service, and owns a testable batch import session. WPF binds to that state and delegates Inspector/viewport drops to existing canonical mutation and instantiation commands.

**Tech Stack:** .NET 10, C# 13, WPF, xUnit, Rekall AGE editor/asset/modeling/module/rendering contracts, canonical command/transaction APIs, Vulkan preview infrastructure.

**Spec:** `docs/superpowers/specs/2026-08-30-studio-content-browser-design.md`

## Global Constraints

- Do not create a second asset database or scan arbitrary project directories as the source of truth.
- Imported files must use `rekall.asset.import_report`; authored content must come from canonical stores/commands.
- Content identity is stable ID plus kind/origin; paths are metadata, not identity.
- MP3 is accepted and mapped to `audio/mpeg` because the runtime already decodes MP3.
- A broken content family produces a bounded warning and does not erase healthy families.
- Open routing reuses existing Modeling, Materials, Code, shader, and external-launch surfaces.
- External drops never recurse directories, never accept relative paths, and report partial success per file.
- Internal drags carry stable ID, kind, and allowed operations only—never mutable bytes or genre meaning.
- Inspector assignment uses schema `AssetKind` compatibility and the ordinary component property command.
- World placement uses the generic model-asset instantiation command.
- Cancellation is distinct from failure; summaries never include raw bytes, secrets, or unbounded exception bodies.
- Run only the exact focused tests named in each task during development; reserve the listed final acceptance gate for Task 7.

---

### Task 1: Provider-Neutral Content Contracts and Imported Projection

**Files:**
- Create: `src/Rekall.Age.Editor.Contracts/RekallAgeContentBrowserModel.cs`
- Modify: `src/Rekall.Age.Editor.Contracts/RekallAgeWorkbenchModel.cs`
- Modify: `src/Rekall.Age.Editor/RekallAgeWorkbenchModelBuilder.cs`
- Create: `tests/Rekall.Age.Tests/Editor/ContentBrowserReadModelTests.cs`

**Interfaces:**
- Consumes: `RekallAgeAssetDocument` and existing `RekallAgeWorkbenchModel.Assets` projection.
- Produces: `RekallAgeContentBrowserModel`, `RekallAgeContentBrowserItem`, `RekallAgeContentBrowserWarning`, and `RekallAgeContentCapability`.
- Consumers: Tasks 2–7.

- [ ] **Step 1: Write failing imported-content projection tests**

Create a temporary project with one PNG and one GLB imported through `ImportAssetCommand`, build the workbench model, and assert `model.Content.Items` preserves IDs, kind, origin, path, hash, dimensions, GLB counts, and deterministic ordering. Assert `Content.Warnings` is empty and the legacy `Assets` projection is unchanged.

```csharp
Assert.Collection(model.Content.Items,
    first => Assert.Equal("model", first.Family),
    second => Assert.Equal("texture", second.Family));
Assert.All(model.Content.Items, item => Assert.Equal("Imported", item.Origin));
Assert.NotNull(model.Content.Items.Single(item => item.Family == "texture").Preview.Width);
```

- [ ] **Step 2: Run the focused tests and verify RED**

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --filter "FullyQualifiedName~ContentBrowserReadModelTests"
```

Expected: compile failure because `RekallAgeContentBrowserModel` and `RekallAgeWorkbenchModel.Content` do not exist.

- [ ] **Step 3: Add the content contracts**

Define focused immutable records:

```csharp
public static class RekallAgeContentCapability
{
    public const string Open = "open";
    public const string OpenExternal = "open-external";
    public const string Reveal = "reveal";
    public const string Reimport = "reimport";
    public const string Assign = "assign";
    public const string Place = "place";
}

public sealed record RekallAgeContentBrowserModel(
    IReadOnlyList<RekallAgeContentBrowserItem> Items,
    IReadOnlyList<RekallAgeContentBrowserWarning> Warnings)
{
    public static RekallAgeContentBrowserModel Empty { get; } = new([], []);
}

public sealed record RekallAgeContentBrowserItem(
    string Id,
    string DisplayName,
    string Family,
    string Kind,
    string Origin,
    string? Path,
    string? SourcePath,
    string? Revision,
    string EditorRouteId,
    IReadOnlyList<string> Capabilities,
    string Health,
    string? Diagnostic,
    RekallAgeContentPreviewMetadata Preview);

public sealed record RekallAgeContentPreviewMetadata(
    int? Width = null,
    int? Height = null,
    int? MeshCount = null,
    int? MaterialCount = null,
    int? AnimationCount = null);

public sealed record RekallAgeContentBrowserWarning(string Code, string Family, string Summary);
```

Add `public RekallAgeContentBrowserModel Content { get; init; } = RekallAgeContentBrowserModel.Empty;` to the workbench record so existing constructor call sites remain source-compatible.

- [ ] **Step 4: Project imported catalog entries**

In `RekallAgeWorkbenchModelBuilder`, produce imported content alongside the existing `Assets` model. Use a pure mapper that derives family/route/capabilities from normalized kind and metadata. Sort by family, display name, and ID using ordinal comparers. Do not read directories.

- [ ] **Step 5: Run GREEN and commit**

Run the Step 2 command. Expected: all `ContentBrowserReadModelTests` pass.

```powershell
git add src/Rekall.Age.Editor.Contracts/RekallAgeContentBrowserModel.cs src/Rekall.Age.Editor.Contracts/RekallAgeWorkbenchModel.cs src/Rekall.Age.Editor/RekallAgeWorkbenchModelBuilder.cs tests/Rekall.Age.Tests/Editor/ContentBrowserReadModelTests.cs
git commit -m "feat(editor): expose unified content contracts"
```

---

### Task 2: Studio Authored-Content Index

**Files:**
- Create: `src/Rekall.Age.Studio/RekallAgeStudioContentIndex.cs`
- Create: `tests/Rekall.Age.Studio.Tests/StudioContentIndexTests.cs`
- Modify: `src/Rekall.Age.Studio/RekallAgeStudioViewModel.cs`

**Interfaces:**
- Consumes: Task 1 content records, imported `workbench.Content`, modeling stores/sessions, module-source commands, model asset store, and shader list command.
- Produces: `IRekallAgeStudioContentSource`, `IRekallAgeStudioContentIndex`, `RekallAgeStudioContentIndex`, `ContentItems`, `ContentWarnings`, category/search projections.
- Consumers: Tasks 3–7.

- [ ] **Step 1: Write failing deterministic merge/failure-isolation tests**

Use injected fake sources returning imported, mesh, modeling-graph, material, model, module, shader, curve, and rig items. Assert de-duplication by `(Kind, Id)`, deterministic ordering, category/search filtering, and that a throwing source becomes one `REKALL_CONTENT_SOURCE_FAILED` warning while healthy items remain.

```csharp
var result = await index.RefreshAsync("C:\\project", CancellationToken.None);
Assert.Contains(result.Items, item => item.Kind == "module-source");
Assert.Contains(result.Warnings, warning => warning.Family == "shader");
Assert.DoesNotContain("sentinel-private-path", result.Warnings[0].Summary);
```

- [ ] **Step 2: Run RED**

```powershell
dotnet test tests\Rekall.Age.Studio.Tests\Rekall.Age.Studio.Tests.csproj --filter "FullyQualifiedName~StudioContentIndexTests"
```

Expected: compile failure because the index/source interfaces do not exist.

- [ ] **Step 3: Implement source isolation and projection**

```csharp
internal interface IRekallAgeStudioContentSource
{
    string Family { get; }
    ValueTask<IReadOnlyList<RekallAgeContentBrowserItem>> LoadAsync(
        string projectRoot, CancellationToken cancellationToken);
}

internal interface IRekallAgeStudioContentIndex
{
    ValueTask<RekallAgeContentBrowserModel> RefreshAsync(
        string projectRoot, CancellationToken cancellationToken);
}
```

Implement one composite index and small production source adapters. Catch only bounded source read/format/I/O failures, preserve cancellation, emit a redacted warning, and continue. Do not put store-specific parsing in the composite.

- [ ] **Step 4: Bind Studio state without keeping `AssetLines` as primary UI state**

Add observable `ContentItems`, `FilteredContentItems`, `ContentWarnings`, `ContentCategories`, `SelectedContentCategory`, `ContentSearchText`, and `SelectedContentItem`. Refresh after project load and existing workbench refresh. Keep `AssetLines` temporarily for compatibility, derived from content rather than separately loaded.

- [ ] **Step 5: Run GREEN and commit**

Run Step 2. Expected: all `StudioContentIndexTests` pass.

```powershell
git add src/Rekall.Age.Studio/RekallAgeStudioContentIndex.cs src/Rekall.Age.Studio/RekallAgeStudioViewModel.cs tests/Rekall.Age.Studio.Tests/StudioContentIndexTests.cs
git commit -m "feat(studio): index authored project content"
```

---

### Task 3: Central Content Open Router

**Files:**
- Create: `src/Rekall.Age.Studio/RekallAgeStudioContentOpenRouter.cs`
- Create: `tests/Rekall.Age.Studio.Tests/StudioContentOpenRouterTests.cs`
- Modify: `src/Rekall.Age.Studio/RekallAgeStudioViewModel.cs`

**Interfaces:**
- Consumes: Task 1 route IDs/items and existing Modeling/graph/material/code sessions plus external launcher.
- Produces: `IRekallAgeStudioContentOpenRouter.OpenAsync`, `RekallAgeStudioContentOpenResult`, and `OpenSelectedContentCommand`.
- Consumers: Tasks 5 and 7.

- [ ] **Step 1: Write failing route tests**

Cover mesh/model, modeling graph, material graph/instance, module source, shader/include, image/texture, audio, unknown path, missing path, cancellation, and a route failure whose raw exception contains a sentinel path. Assert workspace/tab/selection calls and a stable result code.

```csharp
var result = await router.OpenAsync(item, CancellationToken.None);
Assert.True(result.Opened);
Assert.Equal("modeling", result.WorkspaceId);
Assert.Equal("mesh-edit", result.SurfaceId);
```

- [ ] **Step 2: Run RED**

```powershell
dotnet test tests\Rekall.Age.Studio.Tests\Rekall.Age.Studio.Tests.csproj --filter "FullyQualifiedName~StudioContentOpenRouterTests"
```

- [ ] **Step 3: Implement the router**

```csharp
internal sealed record RekallAgeStudioContentOpenResult(
    bool Opened,
    string Code,
    string Summary,
    string? WorkspaceId = null,
    string? SurfaceId = null);

internal interface IRekallAgeStudioContentOpenRouter
{
    ValueTask<RekallAgeStudioContentOpenResult> OpenAsync(
        RekallAgeContentBrowserItem item, CancellationToken cancellationToken);
}
```

Use injected focused route targets (`SelectMeshAsync`, `SelectGraphAsync`, `SelectMaterialAsync`, `SelectModuleSourceAsync`, `OpenAssociated`) so tests do not launch applications. Return `REKALL_CONTENT_OPEN_UNAVAILABLE` for missing routes/paths and `REKALL_CONTENT_OPEN_FAILED` with bounded generic text for launch failure.

- [ ] **Step 4: Bind the command and run GREEN**

`OpenSelectedContentCommand` calls the router and projects the structured result into `ContentStatusText`; `CanExecute` requires an open project, selected item, and Open capability.

Run Step 2, then commit:

```powershell
git add src/Rekall.Age.Studio/RekallAgeStudioContentOpenRouter.cs src/Rekall.Age.Studio/RekallAgeStudioViewModel.cs tests/Rekall.Age.Studio.Tests/StudioContentOpenRouterTests.cs
git commit -m "feat(studio): route content to project editors"
```

---

### Task 4: Canonical Batch Import Session and MP3 Mapping

**Files:**
- Create: `src/Rekall.Age.Studio/RekallAgeStudioContentImportSession.cs`
- Create: `tests/Rekall.Age.Studio.Tests/StudioContentImportSessionTests.cs`
- Modify: `src/Rekall.Age.AssetPipeline/RekallAgeAssetPipelineDocuments.cs`
- Modify: `tests/Rekall.Age.Tests/Assets/AssetPipelineImportTests.cs`
- Modify: `src/Rekall.Age.Studio/RekallAgeStudioViewModel.cs`

**Interfaces:**
- Consumes: `ImportAssetWithReportCommand`, Task 2 index refresh, viewport asset invalidation, and absolute dropped paths.
- Produces: `RekallAgeStudioContentImportPolicy`, `IRekallAgeStudioAssetImportCommand`, `RekallAgeStudioContentImportSession.ImportAsync`, observable import jobs, and `ImportContentCommand`.
- Consumers: Tasks 5 and 7.

- [ ] **Step 1: Add failing MP3 media-type test**

Extend `AssetPipelineImportTests` to import a minimal MP3 fixture and assert the pipeline imported record has `MimeType == "audio/mpeg"`.

- [ ] **Step 2: Add failing import policy/session tests**

Cover case-insensitive accepted extensions, a relative path, a directory, duplicate paths, unsupported file, five-file partial success, cancellation, canonical command invocation per accepted file, bounded concurrency, one content refresh, one viewport invalidation, stable per-file job results, and sentinel exception redaction.

```csharp
Assert.Equal(["glb", "texture", "audio"],
    session.Classify(["ship.GLB", "albedo.PNG", "theme.MP3"]).Select(x => x.Kind));
Assert.Equal(4, fakeImporter.Requests.Count);
Assert.Equal(1, fakeRefresh.Count);
```

- [ ] **Step 3: Run both focused classes and verify RED**

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --filter "FullyQualifiedName~AssetPipelineImportTests"
dotnet test tests\Rekall.Age.Studio.Tests\Rekall.Age.Studio.Tests.csproj --filter "FullyQualifiedName~StudioContentImportSessionTests"
```

- [ ] **Step 4: Implement MP3 mapping and import policy**

Add `".mp3" => "audio/mpeg"`. Implement one extension dictionary for `.glb`, `.gltf`, `.png`, `.jpg`, `.jpeg`, `.dds`, `.ktx2`, `.wav`, `.mp3`, `.glsl`, `.vert`, `.frag`, `.comp`, and `.hlsl`. Route `.cs` to explicit module-source import and return `REKALL_CONTENT_IMPORT_MODULE_ROUTE_REQUIRED` from generic file import.

- [ ] **Step 5: Implement the batch session**

```csharp
internal sealed record RekallAgeStudioContentImportJob(
    string SourcePath, string Kind, string Status, string Code, string Summary, string? AssetId = null);

internal interface IRekallAgeStudioAssetImportCommand
{
    ValueTask<ImportAssetWithReportResult> ImportAsync(
        string projectRoot, string sourcePath, string kind, CancellationToken cancellationToken);
}
```

Normalize distinct absolute files, reject directories, run accepted files with a maximum concurrency of two, preserve cancellation, retain partial results, then perform exactly one index refresh and viewport invalidation if any import succeeds.

- [ ] **Step 6: Bind import jobs and run GREEN**

Expose `ImportJobs`, `HasActiveContentImports`, `ContentImportSummary`, and `ImportContentCommand`. The command uses the existing file picker; OS drop calls the same session directly.

Run Step 3, then commit:

```powershell
git add src/Rekall.Age.AssetPipeline/RekallAgeAssetPipelineDocuments.cs tests/Rekall.Age.Tests/Assets/AssetPipelineImportTests.cs src/Rekall.Age.Studio/RekallAgeStudioContentImportSession.cs src/Rekall.Age.Studio/RekallAgeStudioViewModel.cs tests/Rekall.Age.Studio.Tests/StudioContentImportSessionTests.cs
git commit -m "feat(studio): import dropped project content"
```

---

### Task 5: Content Browser WPF Surface and OS Drop Target

**Files:**
- Create: `src/Rekall.Age.Studio/ContentBrowser.xaml`
- Create: `src/Rekall.Age.Studio/ContentBrowser.xaml.cs`
- Modify: `src/Rekall.Age.Studio/MainWindow.xaml`
- Modify: `src/Rekall.Age.Studio/MainWindow.xaml.cs`
- Modify: `src/Rekall.Age.Studio/App.xaml`
- Create: `tests/Rekall.Age.Studio.Tests/ContentBrowserWindowTests.cs`
- Modify: `tests/Rekall.Age.Studio.Tests/StudioLayoutTests.cs`

**Interfaces:**
- Consumes: Tasks 2–4 observable state/commands and import session.
- Produces: docked browser, keyboard/double-click activation, category/search/list/card UI, details/status, import queue, and OS file drop.
- Consumers: Tasks 6 and 7.

- [ ] **Step 1: Write failing source/layout/accessibility tests**

Load WPF resources in the existing non-parallel WPF collection and assert:

- World contains a resizable Content Browser panel with default height at least 190 device-independent pixels;
- search, category, refresh, Import, and view buttons have accessible names/tooltips;
- item list binds `FilteredContentItems`, selection, open command, details, warnings, and jobs;
- empty state mentions Import and drag/drop;
- root browser has `AllowDrop="True"` and handlers for drag enter/over/leave/drop;
- Enter and double-click invoke the same open command.

- [ ] **Step 2: Run RED**

```powershell
dotnet test tests\Rekall.Age.Studio.Tests\Rekall.Age.Studio.Tests.csproj --filter "FullyQualifiedName~ContentBrowserWindowTests|FullyQualifiedName~StudioLayoutTests"
```

- [ ] **Step 3: Build the reusable Content Browser control**

Use a three-row layout: toolbar, content/details splitter, import/status area. Category groups are a narrow left column; the item list uses a `ListView` with card and compact styles selected through existing theme resources. Type icons are geometry/text resources, not emoji. Every clipped label uses tooltip and trimming.

- [ ] **Step 4: Implement safe OS drop handling**

Code-behind only translates WPF events into paths and calls the VM/session:

```csharp
private async void OnFilesDropped(object sender, DragEventArgs e)
{
    if (DataContext is not RekallAgeStudioViewModel vm ||
        !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
    var paths = (string[])e.Data.GetData(DataFormats.FileDrop)!;
    await vm.ImportDroppedContentAsync(paths);
}
```

`DragOver` sets Copy only when at least one absolute supported file exists. It must not read file contents or recurse folders on the UI thread.

- [ ] **Step 5: Replace the flat Assets tab and add View menu restoration**

Embed `ContentBrowser` in the World workspace bottom pane. Remove the flat `AssetLines` ListBox as the primary surface. Add `View > Content Browser` to restore/show/focus the panel. Persist visible state and height through the existing Studio layout mechanism with safe defaults for old layouts.

- [ ] **Step 6: Run GREEN and commit**

Run Step 2, then:

```powershell
git add src/Rekall.Age.Studio/ContentBrowser.xaml src/Rekall.Age.Studio/ContentBrowser.xaml.cs src/Rekall.Age.Studio/MainWindow.xaml src/Rekall.Age.Studio/MainWindow.xaml.cs src/Rekall.Age.Studio/App.xaml tests/Rekall.Age.Studio.Tests/ContentBrowserWindowTests.cs tests/Rekall.Age.Studio.Tests/StudioLayoutTests.cs
git commit -m "feat(studio): add docked content browser"
```

---

### Task 6: Inspector Assignment and Viewport Placement Drags

**Files:**
- Create: `src/Rekall.Age.Studio/RekallAgeStudioContentDragService.cs`
- Create: `tests/Rekall.Age.Studio.Tests/StudioContentDragServiceTests.cs`
- Modify: `src/Rekall.Age.Studio/ContentBrowser.xaml.cs`
- Modify: `src/Rekall.Age.Studio/MainWindow.xaml`
- Modify: `src/Rekall.Age.Studio/MainWindow.xaml.cs`
- Modify: `src/Rekall.Age.Studio/RekallAgeStudioViewModel.cs`

**Interfaces:**
- Consumes: selected content, Inspector property schema `AssetKind`, existing property mutation pipeline, render-derived viewport hit, and model-asset instantiate command.
- Produces: `RekallAgeStudioContentDragPayload`, compatibility checks, Inspector assignment result, and model placement result.
- Consumers: Task 7.

- [ ] **Step 1: Write failing compatibility/mutation tests**

Cover payload serialization containing only ID/kind/operations; texture-to-texture property success; audio-to-texture rejection without mutation; model placement at a world hit; deterministic camera-front fallback when no hit; non-placeable content rejection; locked entity/property rejection; cancellation; and canonical transaction evidence.

```csharp
Assert.DoesNotContain(item.Path!, payload.ToJson(), StringComparison.OrdinalIgnoreCase);
Assert.Equal("asset_texture", mutation.PropertyValue);
Assert.Equal("rekall.model_asset.instantiate", placement.Tool);
```

- [ ] **Step 2: Run RED**

```powershell
dotnet test tests\Rekall.Age.Studio.Tests\Rekall.Age.Studio.Tests.csproj --filter "FullyQualifiedName~StudioContentDragServiceTests"
```

- [ ] **Step 3: Implement the generic drag service**

```csharp
internal sealed record RekallAgeStudioContentDragPayload(
    string ContentId, string ContentKind, IReadOnlyList<string> Operations);

internal sealed record RekallAgeStudioContentDropResult(
    bool Applied, string Code, string Summary, string? TransactionId = null);
```

Normalize compatibility through one mapping between content families and schema asset kinds. Do not special-case gameplay components. Delegate mutation/placement to injected canonical command adapters and return structured results.

- [ ] **Step 4: Wire internal drag sources/targets**

Begin drag only after the normal system drag threshold. Put the payload under a private Studio data format. Inspector rows advertise Copy only for compatible schema asset kinds. Viewport advertises Copy only for Place-capable model content. Show the structured result in the existing status bar and refresh workbench/viewport once.

- [ ] **Step 5: Run GREEN and commit**

Run Step 2, then:

```powershell
git add src/Rekall.Age.Studio/RekallAgeStudioContentDragService.cs src/Rekall.Age.Studio/ContentBrowser.xaml.cs src/Rekall.Age.Studio/MainWindow.xaml src/Rekall.Age.Studio/MainWindow.xaml.cs src/Rekall.Age.Studio/RekallAgeStudioViewModel.cs tests/Rekall.Age.Studio.Tests/StudioContentDragServiceTests.cs
git commit -m "feat(studio): drag content into scenes and properties"
```

---

### Task 7: Preview Health, Documentation, and Disposable-Project Acceptance

**Files:**
- Create: `src/Rekall.Age.Studio/RekallAgeStudioContentPreviewService.cs`
- Create: `tests/Rekall.Age.Studio.Tests/StudioContentPreviewServiceTests.cs`
- Modify: `src/Rekall.Age.Studio/ContentBrowser.xaml`
- Modify: `src/Rekall.Age.Studio/RekallAgeStudioViewModel.cs`
- Modify: `src/Rekall.Age.Studio/Documentation/Rekall-AGE-Documentation.html`
- Modify: `tests/Rekall.Age.Studio.Tests/ContentBrowserWindowTests.cs`
- Create: `scripts/acceptance/Invoke-StudioContentBrowserAcceptance.ps1`

**Interfaces:**
- Consumes: content IDs/revisions, image decoder, existing Vulkan/modeling preview adapter, index health, router, importer, drag service.
- Produces: cancellable cached thumbnails, complete documentation, and repeatable acceptance evidence.

- [ ] **Step 1: Write failing preview cache/fallback tests**

Cover image thumbnail success, model preview adapter success, unsupported-kind icon fallback, decode/render failure fallback with health badge, cancellation, cache key `(Id, Revision)`, revision invalidation, and bounded cache size. Assert preview failure never removes the item.

- [ ] **Step 2: Run RED**

```powershell
dotnet test tests\Rekall.Age.Studio.Tests\Rekall.Age.Studio.Tests.csproj --filter "FullyQualifiedName~StudioContentPreviewServiceTests"
```

- [ ] **Step 3: Implement cancellable previews**

```csharp
internal sealed record RekallAgeStudioContentPreview(
    string ContentId, string Revision, ImageSource? Thumbnail, string IconKey, string Health, string? Summary);

internal interface IRekallAgeStudioContentPreviewService
{
    ValueTask<RekallAgeStudioContentPreview> GetAsync(
        RekallAgeContentBrowserItem item, CancellationToken cancellationToken);
}
```

Decode images off the UI thread with bounded dimensions, freeze WPF images before publication, call the existing preview adapter for model/mesh content, and use a bounded least-recently-used cache. Never block index refresh on preview completion.

- [ ] **Step 4: Complete the UI and documentation**

Bind thumbnails asynchronously with type-icon fallback and health badge. Document categories, search, open routes, import button, OS drops, accepted formats, partial failures, reimport, Inspector assignment, viewport placement, external-editor fallback, and troubleshooting stable codes in the single shipped HTML manual.

- [ ] **Step 5: Write the disposable acceptance script**

The PowerShell script creates a temporary AGE project, generates/copies minimal GLB, PNG, WAV, MP3, and unsupported fixtures, launches Studio with automation hooks, imports the batch, restarts Studio, opens each accepted family, drags the model into the viewport, assigns the texture to a compatible Inspector property, and writes JSON evidence. It must assert:

```powershell
foreach ($requiredKind in @('model', 'texture', 'audio')) {
    if ($requiredKind -notin $evidence.ImportedKinds) { throw "Missing imported kind: $requiredKind" }
}
if ($evidence.UnsupportedCode -ne 'REKALL_CONTENT_IMPORT_UNSUPPORTED') { throw 'Unsupported-file result was not preserved.' }
if ([string]::IsNullOrWhiteSpace($evidence.PersistedModelAssetId)) { throw 'Model placement did not persist.' }
if ([string]::IsNullOrWhiteSpace($evidence.PersistedTextureAssetId)) { throw 'Texture assignment did not persist.' }
```

Use the existing Studio automation protocol rather than mouse-coordinate automation. Always clean only the exact temporary project in `finally`.

- [ ] **Step 6: Run the focused final gate**

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --filter "FullyQualifiedName~ContentBrowserReadModelTests|FullyQualifiedName~AssetPipelineImportTests"
dotnet test tests\Rekall.Age.Studio.Tests\Rekall.Age.Studio.Tests.csproj --filter "FullyQualifiedName~StudioContentIndexTests|FullyQualifiedName~StudioContentOpenRouterTests|FullyQualifiedName~StudioContentImportSessionTests|FullyQualifiedName~ContentBrowserWindowTests|FullyQualifiedName~StudioContentDragServiceTests|FullyQualifiedName~StudioContentPreviewServiceTests|FullyQualifiedName~StudioLayoutTests"
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\acceptance\Invoke-StudioContentBrowserAcceptance.ps1
```

Expected: all named tests pass, the acceptance script exits 0, and evidence proves persisted model placement and texture assignment after restart.

- [ ] **Step 7: Commit acceptance and docs**

```powershell
git add src/Rekall.Age.Studio/RekallAgeStudioContentPreviewService.cs src/Rekall.Age.Studio/ContentBrowser.xaml src/Rekall.Age.Studio/RekallAgeStudioViewModel.cs src/Rekall.Age.Studio/Documentation/Rekall-AGE-Documentation.html tests/Rekall.Age.Studio.Tests/StudioContentPreviewServiceTests.cs tests/Rekall.Age.Studio.Tests/ContentBrowserWindowTests.cs scripts/acceptance/Invoke-StudioContentBrowserAcceptance.ps1
git commit -m "feat(studio): complete content browser previews and acceptance"
```
