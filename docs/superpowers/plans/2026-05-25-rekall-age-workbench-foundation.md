# Rekall AGE Workbench Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build Workbench Foundation 1: command-backed editor read models, level-design workflows, asset-pipeline reports, runtime/viewport contracts, and a first WPF Rekall Studio shell.

**Architecture:** Keep the master design intact: Studio is a client over command/read-model services, not a second source of truth. The first implementation favors stable contracts, tests, and editor-visible projections over deep renderer/physics/audio implementations.

**Tech Stack:** C# 13, .NET 10, xUnit, WPF for the first Windows desktop shell, `System.Text.Json`, existing Rekall AGE command bus and stores.

---

## Scope

This plan implements the first production workbench slice from `docs/superpowers/specs/2026-05-25-rekall-age-production-workbench-design.md`.

It delivers:

- editor contracts and read models
- command-backed workbench model builder
- asset pipeline source/imported/cooked records and import reports
- level-design commands for duplicate, parent, prefab create, prefab instantiate, and grid snap
- runtime and render viewport contracts
- WPF Rekall Studio shell with hierarchy, inspector, assets, validation, transactions, and toolbar actions wired to read models
- CLI/MCP parity for new workbench and workflow commands
- end-to-end tests for create, import, place, edit, validate, play, capture, and inspect workbench state

It does not deliver:

- advanced realtime renderer
- full physics solver
- full audio mixer
- animation graph editor
- complete prefab override UI
- cross-platform editor shell

## File Structure

Create these projects:

- `src/Rekall.Age.Editor.Contracts/Rekall.Age.Editor.Contracts.csproj`: immutable DTOs for editor read models.
- `src/Rekall.Age.Editor/Rekall.Age.Editor.csproj`: read-model builders and editor-facing command adapters.
- `src/Rekall.Age.AssetPipeline/Rekall.Age.AssetPipeline.csproj`: source/imported/cooked asset records, dependency graph, import reports, and import-with-report command.
- `src/Rekall.Age.Runtime.Abstractions/Rekall.Age.Runtime.Abstractions.csproj`: runtime frame, system, subsystem, and fixed-step contracts.
- `src/Rekall.Age.Rendering.Abstractions/Rekall.Age.Rendering.Abstractions.csproj`: render world, viewport, camera, sprite, mesh, and capture contracts.
- `src/Rekall.Age.LevelDesign/Rekall.Age.LevelDesign.csproj`: prefab documents, prefab store, and command-backed level-design workflows.
- `src/Rekall.Age.Studio/Rekall.Age.Studio.csproj`: Windows WPF editor shell.

Modify these existing files:

- `Rekall.AGE.sln`: add new projects.
- `src/Rekall.Age.Cli/Program.cs`: register new commands and add `studio open`, `asset import-report`, and level-design routes.
- `src/Rekall.Age.Mcp/RekallAgeMcpCatalog.cs`: no code change expected; tests should prove new command schemas appear once registered.
- `src/Rekall.Age.World/RekallAgeEntityDocument.cs`: add optional `ParentId`, `PrefabSourceId`, and editor state properties without breaking existing constructors.
- `src/Rekall.Age.World/RekallAgeSceneDocument.cs`: add entity replacement and lookup helpers used by level-design commands.
- `src/Rekall.Age.Assets/RekallAgeAssetDocument.cs`: add optional `ImportedAtUtc` and `ArtifactPaths` init properties.
- `README.md`: document the Workbench Foundation commands.
- `tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj`: reference new non-WPF libraries.

Do not reference the WPF `Rekall.Age.Studio` project from tests. It targets `net10.0-windows` and is verified by solution build on Windows.

---

### Task 1: Add Workbench Projects

**Files:**
- Create: `src/Rekall.Age.Editor.Contracts/Rekall.Age.Editor.Contracts.csproj`
- Create: `src/Rekall.Age.Editor/Rekall.Age.Editor.csproj`
- Create: `src/Rekall.Age.AssetPipeline/Rekall.Age.AssetPipeline.csproj`
- Create: `src/Rekall.Age.Runtime.Abstractions/Rekall.Age.Runtime.Abstractions.csproj`
- Create: `src/Rekall.Age.Rendering.Abstractions/Rekall.Age.Rendering.Abstractions.csproj`
- Create: `src/Rekall.Age.LevelDesign/Rekall.Age.LevelDesign.csproj`
- Create: `src/Rekall.Age.Studio/Rekall.Age.Studio.csproj`
- Modify: `Rekall.AGE.sln`
- Modify: `tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj`

- [ ] **Step 1: Create project folders and project files**

Run:

```powershell
dotnet new classlib -n Rekall.Age.Editor.Contracts -o src/Rekall.Age.Editor.Contracts
dotnet new classlib -n Rekall.Age.Editor -o src/Rekall.Age.Editor
dotnet new classlib -n Rekall.Age.AssetPipeline -o src/Rekall.Age.AssetPipeline
dotnet new classlib -n Rekall.Age.Runtime.Abstractions -o src/Rekall.Age.Runtime.Abstractions
dotnet new classlib -n Rekall.Age.Rendering.Abstractions -o src/Rekall.Age.Rendering.Abstractions
dotnet new classlib -n Rekall.Age.LevelDesign -o src/Rekall.Age.LevelDesign
dotnet new wpf -n Rekall.Age.Studio -o src/Rekall.Age.Studio
```

Expected: seven projects are created.

- [ ] **Step 2: Replace the Studio project file**

Replace `src/Rekall.Age.Studio/Rekall.Age.Studio.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <LangVersion>13.0</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Rekall.Age.Editor\Rekall.Age.Editor.csproj" />
    <ProjectReference Include="..\Rekall.Age.Editor.Contracts\Rekall.Age.Editor.Contracts.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Add project references**

Run:

```powershell
dotnet add src/Rekall.Age.Editor/Rekall.Age.Editor.csproj reference src/Rekall.Age.Editor.Contracts/Rekall.Age.Editor.Contracts.csproj src/Rekall.Age.Project/Rekall.Age.Project.csproj src/Rekall.Age.World/Rekall.Age.World.csproj src/Rekall.Age.Assets/Rekall.Age.Assets.csproj src/Rekall.Age.Validation/Rekall.Age.Validation.csproj src/Rekall.Age.Modules/Rekall.Age.Modules.csproj src/Rekall.Age.Core/Rekall.Age.Core.csproj
dotnet add src/Rekall.Age.AssetPipeline/Rekall.Age.AssetPipeline.csproj reference src/Rekall.Age.Core/Rekall.Age.Core.csproj src/Rekall.Age.Assets/Rekall.Age.Assets.csproj
dotnet add src/Rekall.Age.Rendering.Abstractions/Rekall.Age.Rendering.Abstractions.csproj reference src/Rekall.Age.World/Rekall.Age.World.csproj
dotnet add src/Rekall.Age.Runtime.Abstractions/Rekall.Age.Runtime.Abstractions.csproj reference src/Rekall.Age.World/Rekall.Age.World.csproj
dotnet add src/Rekall.Age.LevelDesign/Rekall.Age.LevelDesign.csproj reference src/Rekall.Age.Core/Rekall.Age.Core.csproj src/Rekall.Age.World/Rekall.Age.World.csproj
dotnet add src/Rekall.Age.Cli/Rekall.Age.Cli.csproj reference src/Rekall.Age.Editor/Rekall.Age.Editor.csproj src/Rekall.Age.AssetPipeline/Rekall.Age.AssetPipeline.csproj src/Rekall.Age.LevelDesign/Rekall.Age.LevelDesign.csproj
dotnet add tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj reference src/Rekall.Age.Editor.Contracts/Rekall.Age.Editor.Contracts.csproj src/Rekall.Age.Editor/Rekall.Age.Editor.csproj src/Rekall.Age.AssetPipeline/Rekall.Age.AssetPipeline.csproj src/Rekall.Age.Runtime.Abstractions/Rekall.Age.Runtime.Abstractions.csproj src/Rekall.Age.Rendering.Abstractions/Rekall.Age.Rendering.Abstractions.csproj src/Rekall.Age.LevelDesign/Rekall.Age.LevelDesign.csproj
```

Expected: each command reports references were added.

- [ ] **Step 4: Add projects to solution**

Run:

```powershell
dotnet sln Rekall.AGE.sln add src/Rekall.Age.Editor.Contracts/Rekall.Age.Editor.Contracts.csproj
dotnet sln Rekall.AGE.sln add src/Rekall.Age.Editor/Rekall.Age.Editor.csproj
dotnet sln Rekall.AGE.sln add src/Rekall.Age.AssetPipeline/Rekall.Age.AssetPipeline.csproj
dotnet sln Rekall.AGE.sln add src/Rekall.Age.Runtime.Abstractions/Rekall.Age.Runtime.Abstractions.csproj
dotnet sln Rekall.AGE.sln add src/Rekall.Age.Rendering.Abstractions/Rekall.Age.Rendering.Abstractions.csproj
dotnet sln Rekall.AGE.sln add src/Rekall.Age.LevelDesign/Rekall.Age.LevelDesign.csproj
dotnet sln Rekall.AGE.sln add src/Rekall.Age.Studio/Rekall.Age.Studio.csproj
```

- [ ] **Step 5: Remove generated template classes**

Run:

```powershell
Remove-Item src/Rekall.Age.Editor.Contracts/Class1.cs
Remove-Item src/Rekall.Age.Editor/Class1.cs
Remove-Item src/Rekall.Age.AssetPipeline/Class1.cs
Remove-Item src/Rekall.Age.Runtime.Abstractions/Class1.cs
Remove-Item src/Rekall.Age.Rendering.Abstractions/Class1.cs
Remove-Item src/Rekall.Age.LevelDesign/Class1.cs
```

- [ ] **Step 6: Verify build**

Run:

```powershell
dotnet build Rekall.AGE.sln
```

Expected: build succeeds on Windows with zero warnings.

- [ ] **Step 7: Commit**

Run:

```powershell
git add Rekall.AGE.sln src/Rekall.Age.Editor.Contracts src/Rekall.Age.Editor src/Rekall.Age.AssetPipeline src/Rekall.Age.Runtime.Abstractions src/Rekall.Age.Rendering.Abstractions src/Rekall.Age.LevelDesign src/Rekall.Age.Studio tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj
git commit -m "chore: add workbench foundation projects"
```

Expected: commit succeeds.

---

### Task 2: Editor Read Models

**Files:**
- Create: `src/Rekall.Age.Editor.Contracts/RekallAgeWorkbenchModel.cs`
- Create: `src/Rekall.Age.Editor.Contracts/RekallAgeProjectTreeModel.cs`
- Create: `src/Rekall.Age.Editor.Contracts/RekallAgeSceneGraphModel.cs`
- Create: `src/Rekall.Age.Editor.Contracts/RekallAgeInspectorModel.cs`
- Create: `src/Rekall.Age.Editor.Contracts/RekallAgeAssetBrowserModel.cs`
- Create: `src/Rekall.Age.Editor.Contracts/RekallAgeDiagnosticsModels.cs`
- Create: `src/Rekall.Age.Editor/RekallAgeWorkbenchModelBuilder.cs`
- Test: `tests/Rekall.Age.Tests/Editor/WorkbenchReadModelTests.cs`

- [ ] **Step 1: Write failing read-model test**

Create `tests/Rekall.Age.Tests/Editor/WorkbenchReadModelTests.cs`:

```csharp
using System.Text.Json.Nodes;
using Rekall.Age.Assets;
using Rekall.Age.Editor;
using Rekall.Age.Project;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Editor;

public sealed class WorkbenchReadModelTests
{
    [Fact]
    public async Task WorkbenchModelUsesStableIdsAndInspectorProperties()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeProjectStore().SaveAsync(
            root,
            RekallAgeProjectManifest.Create("Crystal Mines", ["world", "rendering2d"]),
            CancellationToken.None);

        var sceneStore = new RekallAgeSceneStore();
        var player = RekallAgeEntityDocument.Create("Player", ["player"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Transform2D",
                new JsonObject
                {
                    ["x"] = 4,
                    ["y"] = 8
                }));
        await sceneStore.SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world", "rendering2d"]).AddEntity(player),
            CancellationToken.None);

        var assetStore = new RekallAgeAssetCatalogStore();
        await assetStore.SaveAsync(
            root,
            RekallAgeAssetCatalogDocument.Empty.AddOrReplace(new RekallAgeAssetDocument(
                "asset_player_12345678",
                "player",
                "Player",
                "sprite",
                "source.png",
                "Assets/sprite/asset_player_12345678.png",
                "1234567890abcdef")),
            CancellationToken.None);

        var model = await new RekallAgeWorkbenchModelBuilder().BuildAsync(root, "Main", CancellationToken.None);

        Assert.Equal("Crystal Mines", model.Project.Name);
        Assert.Equal("Main", model.Scene.Name);
        var node = Assert.Single(model.Scene.RootEntities);
        Assert.Equal(player.Id, node.EntityId);
        Assert.Equal("Player", node.Name);
        Assert.Equal("Rekall.Transform2D", Assert.Single(model.Inspector.Components).Type);
        Assert.Contains(model.Inspector.Components[0].Properties, property => property.Name == "x" && property.Value == "4");
        Assert.Equal("asset_player_12345678", Assert.Single(model.Assets.Assets).AssetId);
        Assert.Contains(model.Diagnostics.Issues, issue => issue.Code == "REKALL_CAMERA_MISSING");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter FullyQualifiedName~WorkbenchReadModelTests
```

Expected: FAIL because editor contracts do not exist.

- [ ] **Step 3: Add editor contract records**

Create `src/Rekall.Age.Editor.Contracts/RekallAgeWorkbenchModel.cs`:

```csharp
namespace Rekall.Age.Editor.Contracts;

public sealed record RekallAgeWorkbenchModel(
    RekallAgeProjectTreeModel Project,
    RekallAgeSceneGraphModel Scene,
    RekallAgeInspectorModel Inspector,
    RekallAgeAssetBrowserModel Assets,
    RekallAgeValidationPanelModel Diagnostics,
    RekallAgeTransactionPanelModel Transactions,
    RekallAgeImportQueueModel ImportQueue);
```

Create `src/Rekall.Age.Editor.Contracts/RekallAgeProjectTreeModel.cs`:

```csharp
namespace Rekall.Age.Editor.Contracts;

public sealed record RekallAgeProjectTreeModel(
    string Name,
    string RootPath,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<RekallAgeProjectSceneItem> Scenes);

public sealed record RekallAgeProjectSceneItem(
    string Name,
    string Path,
    bool Active);
```

Create `src/Rekall.Age.Editor.Contracts/RekallAgeSceneGraphModel.cs`:

```csharp
namespace Rekall.Age.Editor.Contracts;

public sealed record RekallAgeSceneGraphModel(
    string SceneId,
    string Name,
    IReadOnlyList<RekallAgeSceneEntityNode> RootEntities);

public sealed record RekallAgeSceneEntityNode(
    string EntityId,
    string Name,
    IReadOnlyList<string> Tags,
    string? ParentId,
    bool Visible,
    bool Locked,
    IReadOnlyList<RekallAgeSceneEntityNode> Children);
```

Create `src/Rekall.Age.Editor.Contracts/RekallAgeInspectorModel.cs`:

```csharp
namespace Rekall.Age.Editor.Contracts;

public sealed record RekallAgeInspectorModel(
    string? SelectedEntityId,
    string? SelectedEntityName,
    IReadOnlyList<RekallAgeInspectorComponentModel> Components);

public sealed record RekallAgeInspectorComponentModel(
    string Type,
    IReadOnlyList<RekallAgeInspectorPropertyModel> Properties);

public sealed record RekallAgeInspectorPropertyModel(
    string Name,
    string Value,
    string ValueKind);
```

Create `src/Rekall.Age.Editor.Contracts/RekallAgeAssetBrowserModel.cs`:

```csharp
namespace Rekall.Age.Editor.Contracts;

public sealed record RekallAgeAssetBrowserModel(
    IReadOnlyList<RekallAgeAssetBrowserItem> Assets);

public sealed record RekallAgeAssetBrowserItem(
    string AssetId,
    string DisplayName,
    string Kind,
    string ImportedPath,
    string ContentHash);
```

Create `src/Rekall.Age.Editor.Contracts/RekallAgeDiagnosticsModels.cs`:

```csharp
namespace Rekall.Age.Editor.Contracts;

public sealed record RekallAgeValidationPanelModel(
    IReadOnlyList<RekallAgeValidationPanelIssue> Issues);

public sealed record RekallAgeValidationPanelIssue(
    string Code,
    string Severity,
    string Message,
    string Target,
    IReadOnlyList<string> SuggestedTools);

public sealed record RekallAgeTransactionPanelModel(
    IReadOnlyList<RekallAgeTransactionPanelItem> Transactions);

public sealed record RekallAgeTransactionPanelItem(
    string Id,
    string Name,
    IReadOnlyList<string> ChangedResources);

public sealed record RekallAgeImportQueueModel(
    IReadOnlyList<RekallAgeImportQueueItem> Jobs);

public sealed record RekallAgeImportQueueItem(
    string SourcePath,
    string Kind,
    string Status,
    string Summary);
```

- [ ] **Step 4: Add workbench model builder**

Create `src/Rekall.Age.Editor/RekallAgeWorkbenchModelBuilder.cs`:

```csharp
using System.Text.Json.Nodes;
using Rekall.Age.Assets;
using Rekall.Age.Editor.Contracts;
using Rekall.Age.Project;
using Rekall.Age.Validation;
using Rekall.Age.World;

namespace Rekall.Age.Editor;

public sealed class RekallAgeWorkbenchModelBuilder
{
    private readonly RekallAgeProjectStore _projectStore;
    private readonly RekallAgeSceneStore _sceneStore;
    private readonly RekallAgeAssetCatalogStore _assetStore;

    public RekallAgeWorkbenchModelBuilder()
        : this(new RekallAgeProjectStore(), new RekallAgeSceneStore(), new RekallAgeAssetCatalogStore())
    {
    }

    public RekallAgeWorkbenchModelBuilder(
        RekallAgeProjectStore projectStore,
        RekallAgeSceneStore sceneStore,
        RekallAgeAssetCatalogStore assetStore)
    {
        _projectStore = projectStore;
        _sceneStore = sceneStore;
        _assetStore = assetStore;
    }

    public async ValueTask<RekallAgeWorkbenchModel> BuildAsync(
        string projectRoot,
        string activeSceneName,
        CancellationToken cancellationToken)
    {
        var manifest = await _projectStore.LoadAsync(projectRoot, cancellationToken);
        var scene = await _sceneStore.LoadAsync(projectRoot, activeSceneName, cancellationToken);
        var assets = await _assetStore.LoadAsync(projectRoot, cancellationToken);
        var validation = await new RekallAgeProjectValidator(_sceneStore)
            .ValidateSceneAsync(projectRoot, activeSceneName, cancellationToken);

        return new RekallAgeWorkbenchModel(
            new RekallAgeProjectTreeModel(
                manifest.Name,
                projectRoot,
                manifest.Capabilities,
                _sceneStore.ListSceneNames(projectRoot)
                    .Select(name => new RekallAgeProjectSceneItem(
                        name,
                        _sceneStore.GetScenePath(projectRoot, name),
                        name.Equals(activeSceneName, StringComparison.Ordinal)))
                    .ToArray()),
            BuildSceneGraph(scene),
            BuildInspector(scene),
            new RekallAgeAssetBrowserModel(
                assets.Assets
                    .OrderBy(asset => asset.Kind, StringComparer.Ordinal)
                    .ThenBy(asset => asset.DisplayName, StringComparer.Ordinal)
                    .Select(asset => new RekallAgeAssetBrowserItem(
                        asset.Id,
                        asset.DisplayName,
                        asset.Kind,
                        asset.ImportedPath,
                        asset.ContentHash))
                    .ToArray()),
            new RekallAgeValidationPanelModel(
                validation.Issues
                    .Select(issue => new RekallAgeValidationPanelIssue(
                        issue.Code,
                        issue.Severity,
                        issue.Message,
                        issue.Target,
                        issue.SuggestedCommands.Select(command => command.Tool).ToArray()))
                    .ToArray()),
            new RekallAgeTransactionPanelModel(Array.Empty<RekallAgeTransactionPanelItem>()),
            new RekallAgeImportQueueModel(Array.Empty<RekallAgeImportQueueItem>()));
    }

    private static RekallAgeSceneGraphModel BuildSceneGraph(RekallAgeSceneDocument scene)
    {
        var childrenByParent = scene.Entities
            .Where(entity => !string.IsNullOrWhiteSpace(entity.ParentId))
            .GroupBy(entity => entity.ParentId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(entity => entity.Name, StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        var roots = scene.Entities
            .Where(entity => string.IsNullOrWhiteSpace(entity.ParentId))
            .OrderBy(entity => entity.Name, StringComparer.Ordinal)
            .Select(entity => ToNode(entity, childrenByParent))
            .ToArray();
        return new RekallAgeSceneGraphModel(scene.Id, scene.Name, roots);
    }

    private static RekallAgeSceneEntityNode ToNode(
        RekallAgeEntityDocument entity,
        IReadOnlyDictionary<string, RekallAgeEntityDocument[]> childrenByParent)
    {
        var children = childrenByParent.TryGetValue(entity.Id, out var items)
            ? items.Select(child => ToNode(child, childrenByParent)).ToArray()
            : Array.Empty<RekallAgeSceneEntityNode>();
        return new RekallAgeSceneEntityNode(
            entity.Id,
            entity.Name,
            entity.Tags,
            entity.ParentId,
            entity.Visible,
            entity.Locked,
            children);
    }

    private static RekallAgeInspectorModel BuildInspector(RekallAgeSceneDocument scene)
    {
        var selected = scene.Entities.OrderBy(entity => entity.Name, StringComparer.Ordinal).FirstOrDefault();
        if (selected is null)
        {
            return new RekallAgeInspectorModel(null, null, Array.Empty<RekallAgeInspectorComponentModel>());
        }

        return new RekallAgeInspectorModel(
            selected.Id,
            selected.Name,
            selected.Components
                .Select(component => new RekallAgeInspectorComponentModel(
                    component.Type,
                    component.Properties
                        .OrderBy(property => property.Key, StringComparer.Ordinal)
                        .Select(property => new RekallAgeInspectorPropertyModel(
                            property.Key,
                            ToDisplayValue(property.Value),
                            property.Value?.GetValueKind().ToString() ?? "Null"))
                        .ToArray()))
                .ToArray());
    }

    private static string ToDisplayValue(JsonNode? value)
    {
        if (value is null)
        {
            return "null";
        }

        return value is JsonValue jsonValue
            ? jsonValue.ToJsonString().Trim('"')
            : value.ToJsonString();
    }
}
```

- [ ] **Step 5: Run read-model tests**

Run:

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter FullyQualifiedName~WorkbenchReadModelTests
```

Expected: PASS.

- [ ] **Step 6: Commit**

Run:

```powershell
git add src/Rekall.Age.Editor.Contracts src/Rekall.Age.Editor tests/Rekall.Age.Tests/Editor
git commit -m "feat: add editor workbench read models"
```

Expected: commit succeeds.

---

### Task 3: Entity Metadata For Editor And Level Design

**Files:**
- Modify: `src/Rekall.Age.World/RekallAgeEntityDocument.cs`
- Modify: `src/Rekall.Age.World/RekallAgeSceneDocument.cs`
- Test: `tests/Rekall.Age.Tests/World/SceneHierarchyMetadataTests.cs`

- [ ] **Step 1: Write failing metadata test**

Create `tests/Rekall.Age.Tests/World/SceneHierarchyMetadataTests.cs`:

```csharp
using Rekall.Age.World;

namespace Rekall.Age.Tests.World;

public sealed class SceneHierarchyMetadataTests
{
    [Fact]
    public void EntitySupportsParentPrefabAndEditorFlags()
    {
        var parent = RekallAgeEntityDocument.Create("Root", ["level"]);
        var child = RekallAgeEntityDocument.Create("Child", ["prop"]) with
        {
            ParentId = parent.Id,
            PrefabSourceId = "prefab_crate",
            Visible = false,
            Locked = true
        };

        var scene = RekallAgeSceneDocument.Create("Main", ["world"])
            .AddEntity(child)
            .AddEntity(parent);

        Assert.Same(child, scene.GetRequiredEntity(child.Id));
        Assert.Equal(parent.Id, scene.GetRequiredEntity(child.Id).ParentId);
        Assert.Equal("prefab_crate", scene.GetRequiredEntity(child.Id).PrefabSourceId);
        Assert.False(scene.GetRequiredEntity(child.Id).Visible);
        Assert.True(scene.GetRequiredEntity(child.Id).Locked);
    }

    [Fact]
    public void SceneCanReplaceEntityByStableId()
    {
        var entity = RekallAgeEntityDocument.Create("Player", ["player"]);
        var scene = RekallAgeSceneDocument.Create("Main", ["world"]).AddEntity(entity);

        var updated = scene.ReplaceEntity(entity with { Name = "Hero" });

        Assert.Equal("Hero", updated.GetRequiredEntity(entity.Id).Name);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter FullyQualifiedName~SceneHierarchyMetadataTests
```

Expected: FAIL because the metadata and helper methods do not exist.

- [ ] **Step 3: Update entity document**

Replace `src/Rekall.Age.World/RekallAgeEntityDocument.cs` with:

```csharp
namespace Rekall.Age.World;

public sealed record RekallAgeEntityDocument(
    string Id,
    string Name,
    IReadOnlyList<string> Tags,
    IReadOnlyList<RekallAgeComponentDocument> Components)
{
    public string? ParentId { get; init; }

    public string? PrefabSourceId { get; init; }

    public bool Visible { get; init; } = true;

    public bool Locked { get; init; }

    public static RekallAgeEntityDocument Create(string name, IEnumerable<string> tags)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Entity name is required.", nameof(name));
        }

        return new RekallAgeEntityDocument(
            $"ent_{Guid.NewGuid():N}",
            name.Trim(),
            NormalizeTags(tags),
            Array.Empty<RekallAgeComponentDocument>());
    }

    public RekallAgeEntityDocument AddComponent(RekallAgeComponentDocument component)
    {
        var components = Components
            .Where(existing => !existing.Type.Equals(component.Type, StringComparison.Ordinal))
            .Append(component)
            .OrderBy(item => item.Type, StringComparer.Ordinal)
            .ToArray();
        return this with { Components = components };
    }

    public RekallAgeEntityDocument UpdateComponent(
        string componentType,
        Func<RekallAgeComponentDocument, RekallAgeComponentDocument> update)
    {
        var found = false;
        var components = Components.Select(component =>
        {
            if (!component.Type.Equals(componentType, StringComparison.Ordinal))
            {
                return component;
            }

            found = true;
            return update(component);
        }).ToArray();

        if (!found)
        {
            throw new InvalidOperationException($"Component '{componentType}' was not found on entity '{Name}'.");
        }

        return this with
        {
            Components = components.OrderBy(component => component.Type, StringComparer.Ordinal).ToArray()
        };
    }

    private static IReadOnlyList<string> NormalizeTags(IEnumerable<string> tags)
    {
        return tags
            .Select(tag => tag.Trim().ToLowerInvariant())
            .Where(tag => tag.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(tag => tag, StringComparer.Ordinal)
            .ToArray();
    }
}
```

- [ ] **Step 4: Update scene document helpers**

Replace `src/Rekall.Age.World/RekallAgeSceneDocument.cs` with:

```csharp
namespace Rekall.Age.World;

public sealed record RekallAgeSceneDocument(
    string Id,
    string Name,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<RekallAgeEntityDocument> Entities)
{
    public static RekallAgeSceneDocument Create(string name, IEnumerable<string> capabilities)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Scene name is required.", nameof(name));
        }

        return new RekallAgeSceneDocument(
            $"scene_{Guid.NewGuid():N}",
            name.Trim(),
            NormalizeCapabilities(capabilities),
            Array.Empty<RekallAgeEntityDocument>());
    }

    public RekallAgeSceneDocument AddEntity(RekallAgeEntityDocument entity)
    {
        return this with { Entities = SortEntities(Entities.Append(entity)) };
    }

    public RekallAgeSceneDocument ReplaceEntity(RekallAgeEntityDocument replacement)
    {
        var found = false;
        var entities = Entities.Select(entity =>
        {
            if (!entity.Id.Equals(replacement.Id, StringComparison.Ordinal))
            {
                return entity;
            }

            found = true;
            return replacement;
        }).ToArray();

        if (!found)
        {
            throw new InvalidOperationException($"Entity '{replacement.Id}' was not found in scene '{Name}'.");
        }

        return this with { Entities = SortEntities(entities) };
    }

    public RekallAgeEntityDocument GetRequiredEntity(string entityId)
    {
        return Entities.FirstOrDefault(entity => entity.Id.Equals(entityId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Entity '{entityId}' was not found in scene '{Name}'.");
    }

    public RekallAgeSceneDocument UpdateEntity(string entityId, Func<RekallAgeEntityDocument, RekallAgeEntityDocument> update)
    {
        return ReplaceEntity(update(GetRequiredEntity(entityId)));
    }

    private static IReadOnlyList<RekallAgeEntityDocument> SortEntities(IEnumerable<RekallAgeEntityDocument> entities)
    {
        return entities
            .OrderBy(item => item.Name, StringComparer.Ordinal)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<string> NormalizeCapabilities(IEnumerable<string> capabilities)
    {
        return capabilities
            .Select(capability => capability.Trim().ToLowerInvariant())
            .Where(capability => capability.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(capability => capability, StringComparer.Ordinal)
            .ToArray();
    }
}
```

- [ ] **Step 5: Run world tests**

Run:

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter FullyQualifiedName~SceneHierarchyMetadataTests
```

Expected: PASS.

- [ ] **Step 6: Run existing world command tests**

Run:

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "FullyQualifiedName~WorldMutationCommandTests|FullyQualifiedName~SceneStoreTests"
```

Expected: PASS.

- [ ] **Step 7: Commit**

Run:

```powershell
git add src/Rekall.Age.World tests/Rekall.Age.Tests/World/SceneHierarchyMetadataTests.cs
git commit -m "feat: add editor-ready scene hierarchy metadata"
```

Expected: commit succeeds.

---

### Task 4: Asset Pipeline Import Reports

**Files:**
- Create: `src/Rekall.Age.AssetPipeline/RekallAgeAssetPipelineDocuments.cs`
- Create: `src/Rekall.Age.AssetPipeline/RekallAgeAssetPipelineStore.cs`
- Create: `src/Rekall.Age.AssetPipeline/Commands/ImportAssetWithReportCommand.cs`
- Test: `tests/Rekall.Age.Tests/Assets/AssetPipelineImportTests.cs`

- [ ] **Step 1: Write failing asset pipeline test**

Create `tests/Rekall.Age.Tests/Assets/AssetPipelineImportTests.cs`:

```csharp
using Rekall.Age.AssetPipeline;
using Rekall.Age.AssetPipeline.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;

namespace Rekall.Age.Tests.Assets;

public sealed class AssetPipelineImportTests
{
    [Fact]
    public async Task ImportWithReportWritesSourceImportedAndCookedRecords()
    {
        var root = TestPaths.CreateTempDirectory();
        var source = Path.Combine(root, "player.png");
        await File.WriteAllBytesAsync(source, [1, 2, 3, 4, 5], CancellationToken.None);
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new ImportAssetWithReportCommand());
        var context = new RekallAgeCommandContext("test", RekallAgeTransaction.Begin("import"), CancellationToken.None);

        var result = await registry.ExecuteAsync<ImportAssetWithReportRequest, ImportAssetWithReportResult>(
            "rekall.asset.import_report",
            new ImportAssetWithReportRequest(root, source, "sprite", "Player"),
            context);

        Assert.True(result.Ok);
        Assert.True(result.Value.Report.Imported);
        Assert.Equal("sprite", result.Value.Report.Kind);
        Assert.Single(result.Value.Pipeline.Sources);
        Assert.Single(result.Value.Pipeline.Imported);
        Assert.Single(result.Value.Pipeline.CookedArtifacts);
        Assert.Contains("asset-pipeline.age.json", context.Transaction.ChangedResources.Single(path => path.EndsWith("asset-pipeline.age.json", StringComparison.Ordinal)));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter FullyQualifiedName~AssetPipelineImportTests
```

Expected: FAIL because asset pipeline types do not exist.

- [ ] **Step 3: Add asset pipeline documents**

Create `src/Rekall.Age.AssetPipeline/RekallAgeAssetPipelineDocuments.cs`:

```csharp
using Rekall.Age.Assets;

namespace Rekall.Age.AssetPipeline;

public sealed record RekallAgeAssetPipelineDocument(
    IReadOnlyList<RekallAgeAssetSourceRecord> Sources,
    IReadOnlyList<RekallAgeImportedAssetRecord> Imported,
    IReadOnlyList<RekallAgeCookedAssetRecord> CookedArtifacts,
    IReadOnlyList<RekallAgeAssetDependencyRecord> Dependencies)
{
    public static RekallAgeAssetPipelineDocument Empty { get; } = new(
        Array.Empty<RekallAgeAssetSourceRecord>(),
        Array.Empty<RekallAgeImportedAssetRecord>(),
        Array.Empty<RekallAgeCookedAssetRecord>(),
        Array.Empty<RekallAgeAssetDependencyRecord>());

    public RekallAgeAssetPipelineDocument AddImport(RekallAgeAssetDocument asset, string sourcePath, string kind)
    {
        var source = new RekallAgeAssetSourceRecord(asset.Id, sourcePath, kind, asset.ContentHash);
        var imported = new RekallAgeImportedAssetRecord(asset.Id, asset.ImportedPath, kind, asset.ContentHash);
        var cooked = new RekallAgeCookedAssetRecord(asset.Id, asset.ImportedPath, "raw-copy", asset.ContentHash);
        return this with
        {
            Sources = Replace(Sources, source, item => item.AssetId),
            Imported = Replace(Imported, imported, item => item.AssetId),
            CookedArtifacts = Replace(CookedArtifacts, cooked, item => item.AssetId)
        };
    }

    private static IReadOnlyList<T> Replace<T>(IEnumerable<T> existing, T value, Func<T, string> key)
    {
        var valueKey = key(value);
        return existing
            .Where(item => !key(item).Equals(valueKey, StringComparison.Ordinal))
            .Append(value)
            .OrderBy(key, StringComparer.Ordinal)
            .ToArray();
    }
}

public sealed record RekallAgeAssetSourceRecord(
    string AssetId,
    string SourcePath,
    string Kind,
    string ContentHash);

public sealed record RekallAgeImportedAssetRecord(
    string AssetId,
    string ImportedPath,
    string Kind,
    string ContentHash);

public sealed record RekallAgeCookedAssetRecord(
    string AssetId,
    string ArtifactPath,
    string ArtifactKind,
    string ContentHash);

public sealed record RekallAgeAssetDependencyRecord(
    string AssetId,
    string DependsOnAssetId,
    string Reason);

public sealed record RekallAgeAssetImportReport(
    bool Imported,
    string AssetId,
    string Kind,
    string SourcePath,
    string ImportedPath,
    IReadOnlyList<string> Diagnostics);
```

- [ ] **Step 4: Add pipeline store**

Create `src/Rekall.Age.AssetPipeline/RekallAgeAssetPipelineStore.cs`:

```csharp
using System.Text.Json;

namespace Rekall.Age.AssetPipeline;

public sealed class RekallAgeAssetPipelineStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string GetPath(string projectRoot)
    {
        return Path.Combine(projectRoot, "Assets", "asset-pipeline.age.json");
    }

    public async ValueTask<RekallAgeAssetPipelineDocument> LoadAsync(
        string projectRoot,
        CancellationToken cancellationToken)
    {
        var path = GetPath(projectRoot);
        if (!File.Exists(path))
        {
            return RekallAgeAssetPipelineDocument.Empty;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<RekallAgeAssetPipelineDocument>(
            stream,
            JsonOptions,
            cancellationToken) ?? RekallAgeAssetPipelineDocument.Empty;
    }

    public async ValueTask SaveAsync(
        string projectRoot,
        RekallAgeAssetPipelineDocument document,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.Combine(projectRoot, "Assets"));
        var json = JsonSerializer.Serialize(document, JsonOptions);
        await File.WriteAllTextAsync(GetPath(projectRoot), json + Environment.NewLine, cancellationToken);
    }
}
```

- [ ] **Step 5: Add import-with-report command**

Create `src/Rekall.Age.AssetPipeline/Commands/ImportAssetWithReportCommand.cs`:

```csharp
using Rekall.Age.Assets;
using Rekall.Age.Core.Commands;

namespace Rekall.Age.AssetPipeline.Commands;

public sealed record ImportAssetWithReportRequest(
    string ProjectRoot,
    string SourcePath,
    string Kind,
    string? DisplayName = null);

public sealed record ImportAssetWithReportResult(
    RekallAgeAssetImportReport Report,
    RekallAgeAssetPipelineDocument Pipeline);

public sealed class ImportAssetWithReportCommand
    : IRekallAgeCommand<ImportAssetWithReportRequest, ImportAssetWithReportResult>
{
    private readonly RekallAgeAssetCatalogStore _assetStore = new();
    private readonly RekallAgeAssetPipelineStore _pipelineStore = new();

    public string Name => "rekall.asset.import_report";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Imports an asset and writes editor-facing source/imported/cooked pipeline records.",
        typeof(ImportAssetWithReportRequest).FullName!,
        typeof(ImportAssetWithReportResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<ImportAssetWithReportResult>> ExecuteAsync(
        ImportAssetWithReportRequest request,
        RekallAgeCommandContext context)
    {
        var asset = await RekallAgeAssetImporter.ImportAsync(
            request.ProjectRoot,
            request.SourcePath,
            request.Kind,
            request.DisplayName,
            context.CancellationToken);
        var catalog = await _assetStore.LoadAsync(request.ProjectRoot, context.CancellationToken);
        await _assetStore.SaveAsync(request.ProjectRoot, catalog.AddOrReplace(asset), context.CancellationToken);

        var pipeline = await _pipelineStore.LoadAsync(request.ProjectRoot, context.CancellationToken);
        var updatedPipeline = pipeline.AddImport(asset, request.SourcePath, request.Kind);
        await _pipelineStore.SaveAsync(request.ProjectRoot, updatedPipeline, context.CancellationToken);

        context.Transaction.RecordChangedResource(asset.ImportedPath);
        context.Transaction.RecordChangedResource(_assetStore.GetCatalogPath(request.ProjectRoot));
        context.Transaction.RecordChangedResource(_pipelineStore.GetPath(request.ProjectRoot));

        var report = new RekallAgeAssetImportReport(
            true,
            asset.Id,
            asset.Kind,
            asset.SourcePath,
            asset.ImportedPath,
            Array.Empty<string>());
        return RekallAgeCommandResult<ImportAssetWithReportResult>.Success(
            new ImportAssetWithReportResult(report, updatedPipeline),
            $"Imported asset '{asset.Id}' with pipeline report.");
    }
}
```

- [ ] **Step 6: Run asset pipeline tests**

Run:

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter FullyQualifiedName~AssetPipelineImportTests
```

Expected: PASS.

- [ ] **Step 7: Commit**

Run:

```powershell
git add src/Rekall.Age.AssetPipeline tests/Rekall.Age.Tests/Assets/AssetPipelineImportTests.cs
git commit -m "feat: add asset pipeline import reports"
```

Expected: commit succeeds.

---

### Task 5: Level-Design Commands And Prefabs

**Files:**
- Create: `src/Rekall.Age.LevelDesign/RekallAgePrefabDocument.cs`
- Create: `src/Rekall.Age.LevelDesign/RekallAgePrefabStore.cs`
- Create: `src/Rekall.Age.LevelDesign/Commands/DuplicateEntityCommand.cs`
- Create: `src/Rekall.Age.LevelDesign/Commands/ParentEntityCommand.cs`
- Create: `src/Rekall.Age.LevelDesign/Commands/CreatePrefabFromEntityCommand.cs`
- Create: `src/Rekall.Age.LevelDesign/Commands/InstantiatePrefabCommand.cs`
- Create: `src/Rekall.Age.LevelDesign/Commands/SnapEntityToGridCommand.cs`
- Test: `tests/Rekall.Age.Tests/LevelDesign/LevelDesignCommandTests.cs`

- [ ] **Step 1: Write failing level-design workflow test**

Create `tests/Rekall.Age.Tests/LevelDesign/LevelDesignCommandTests.cs`:

```csharp
using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.LevelDesign.Commands;
using Rekall.Age.World;

namespace Rekall.Age.Tests.LevelDesign;

public sealed class LevelDesignCommandTests
{
    [Fact]
    public async Task LevelDesignCommandsDuplicateParentPrefabInstantiateAndSnap()
    {
        var root = TestPaths.CreateTempDirectory();
        var sceneStore = new RekallAgeSceneStore();
        var entity = RekallAgeEntityDocument.Create("Crate", ["prop"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Transform2D",
                new JsonObject { ["x"] = 2.2, ["y"] = 5.7 }));
        await sceneStore.SaveAsync(root, RekallAgeSceneDocument.Create("Main", ["world"]).AddEntity(entity), CancellationToken.None);

        var registry = new RekallAgeCommandRegistry();
        registry.Register(new DuplicateEntityCommand());
        registry.Register(new ParentEntityCommand());
        registry.Register(new CreatePrefabFromEntityCommand());
        registry.Register(new InstantiatePrefabCommand());
        registry.Register(new SnapEntityToGridCommand());
        var context = new RekallAgeCommandContext("test", RekallAgeTransaction.Begin("level design"), CancellationToken.None);

        var duplicate = await registry.ExecuteAsync<DuplicateEntityRequest, DuplicateEntityResult>(
            "rekall.level.entity.duplicate",
            new DuplicateEntityRequest(root, "Main", entity.Id, "Crate Copy"),
            context);
        Assert.True(duplicate.Ok);

        var parent = await registry.ExecuteAsync<ParentEntityRequest, ParentEntityResult>(
            "rekall.level.entity.parent",
            new ParentEntityRequest(root, "Main", duplicate.Value.EntityId, entity.Id),
            context);
        Assert.True(parent.Ok);

        var prefab = await registry.ExecuteAsync<CreatePrefabFromEntityRequest, CreatePrefabFromEntityResult>(
            "rekall.level.prefab.create_from_entity",
            new CreatePrefabFromEntityRequest(root, "Main", entity.Id, "CratePrefab"),
            context);
        Assert.True(prefab.Ok);

        var instance = await registry.ExecuteAsync<InstantiatePrefabRequest, InstantiatePrefabResult>(
            "rekall.level.prefab.instantiate",
            new InstantiatePrefabRequest(root, "Main", prefab.Value.PrefabId, "Crate Instance"),
            context);
        Assert.True(instance.Ok);

        var snapped = await registry.ExecuteAsync<SnapEntityToGridRequest, SnapEntityToGridResult>(
            "rekall.level.entity.snap_to_grid",
            new SnapEntityToGridRequest(root, "Main", entity.Id, 1.0),
            context);
        Assert.True(snapped.Ok);

        var scene = await sceneStore.LoadAsync(root, "Main", CancellationToken.None);
        Assert.Equal(entity.Id, scene.GetRequiredEntity(duplicate.Value.EntityId).ParentId);
        Assert.Equal(prefab.Value.PrefabId, scene.GetRequiredEntity(instance.Value.EntityId).PrefabSourceId);
        var transform = scene.GetRequiredEntity(entity.Id).Components.Single(component => component.Type == "Rekall.Transform2D");
        Assert.Equal(2, transform.Properties["x"]!.GetValue<double>());
        Assert.Equal(6, transform.Properties["y"]!.GetValue<double>());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter FullyQualifiedName~LevelDesignCommandTests
```

Expected: FAIL because level-design commands do not exist.

- [ ] **Step 3: Add prefab document and store**

Create `src/Rekall.Age.LevelDesign/RekallAgePrefabDocument.cs`:

```csharp
using Rekall.Age.World;

namespace Rekall.Age.LevelDesign;

public sealed record RekallAgePrefabDocument(
    string Id,
    string Name,
    RekallAgeEntityDocument RootEntity)
{
    public static RekallAgePrefabDocument Create(string name, RekallAgeEntityDocument entity)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Prefab name is required.", nameof(name));
        }

        return new RekallAgePrefabDocument($"prefab_{Guid.NewGuid():N}", name.Trim(), entity with { ParentId = null });
    }
}
```

Create `src/Rekall.Age.LevelDesign/RekallAgePrefabStore.cs`:

```csharp
using System.Text.Json;

namespace Rekall.Age.LevelDesign;

public sealed class RekallAgePrefabStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string GetPath(string projectRoot, string prefabId)
    {
        return Path.Combine(projectRoot, "Prefabs", $"{prefabId}.age.prefab.json");
    }

    public async ValueTask SaveAsync(
        string projectRoot,
        RekallAgePrefabDocument prefab,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.Combine(projectRoot, "Prefabs"));
        var json = JsonSerializer.Serialize(prefab, JsonOptions);
        await File.WriteAllTextAsync(GetPath(projectRoot, prefab.Id), json + Environment.NewLine, cancellationToken);
    }

    public async ValueTask<RekallAgePrefabDocument> LoadAsync(
        string projectRoot,
        string prefabId,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(GetPath(projectRoot, prefabId));
        return await JsonSerializer.DeserializeAsync<RekallAgePrefabDocument>(
            stream,
            JsonOptions,
            cancellationToken) ?? throw new InvalidOperationException($"Prefab '{prefabId}' could not be read.");
    }
}
```

- [ ] **Step 4: Add duplicate and parent commands**

Create `src/Rekall.Age.LevelDesign/Commands/DuplicateEntityCommand.cs`:

```csharp
using Rekall.Age.Core.Commands;
using Rekall.Age.World;

namespace Rekall.Age.LevelDesign.Commands;

public sealed record DuplicateEntityRequest(string ProjectRoot, string SceneName, string EntityId, string? Name = null);

public sealed record DuplicateEntityResult(string EntityId, RekallAgeSceneDocument Scene);

public sealed class DuplicateEntityCommand : IRekallAgeCommand<DuplicateEntityRequest, DuplicateEntityResult>
{
    private readonly RekallAgeSceneStore _store = new();

    public string Name => "rekall.level.entity.duplicate";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Duplicates an entity using a new stable id while preserving components and editor metadata.",
        typeof(DuplicateEntityRequest).FullName!,
        typeof(DuplicateEntityResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<DuplicateEntityResult>> ExecuteAsync(
        DuplicateEntityRequest request,
        RekallAgeCommandContext context)
    {
        var scene = await _store.LoadAsync(request.ProjectRoot, request.SceneName, context.CancellationToken);
        var source = scene.GetRequiredEntity(request.EntityId);
        var duplicate = new RekallAgeEntityDocument(
            $"ent_{Guid.NewGuid():N}",
            string.IsNullOrWhiteSpace(request.Name) ? $"{source.Name} Copy" : request.Name.Trim(),
            source.Tags,
            source.Components)
        {
            ParentId = source.ParentId,
            PrefabSourceId = source.PrefabSourceId,
            Visible = source.Visible,
            Locked = source.Locked
        };
        var updated = scene.AddEntity(duplicate);
        await _store.SaveAsync(request.ProjectRoot, updated, context.CancellationToken);
        context.Transaction.RecordChangedResource(_store.GetScenePath(request.ProjectRoot, request.SceneName));
        return RekallAgeCommandResult<DuplicateEntityResult>.Success(
            new DuplicateEntityResult(duplicate.Id, updated),
            $"Duplicated entity '{source.Name}'.");
    }
}
```

Create `src/Rekall.Age.LevelDesign/Commands/ParentEntityCommand.cs`:

```csharp
using Rekall.Age.Core.Commands;
using Rekall.Age.World;

namespace Rekall.Age.LevelDesign.Commands;

public sealed record ParentEntityRequest(string ProjectRoot, string SceneName, string EntityId, string? ParentId);

public sealed record ParentEntityResult(RekallAgeSceneDocument Scene);

public sealed class ParentEntityCommand : IRekallAgeCommand<ParentEntityRequest, ParentEntityResult>
{
    private readonly RekallAgeSceneStore _store = new();

    public string Name => "rekall.level.entity.parent";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Parents or unparents an entity by stable id.",
        typeof(ParentEntityRequest).FullName!,
        typeof(ParentEntityResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<ParentEntityResult>> ExecuteAsync(
        ParentEntityRequest request,
        RekallAgeCommandContext context)
    {
        if (request.ParentId is not null && request.ParentId.Equals(request.EntityId, StringComparison.Ordinal))
        {
            var error = new RekallAgeCommandError("REKALL_PARENT_SELF", "An entity cannot be parented to itself.", request.EntityId);
            return RekallAgeCommandResult<ParentEntityResult>.Failure(default!, error.Message, [error]);
        }

        var scene = await _store.LoadAsync(request.ProjectRoot, request.SceneName, context.CancellationToken);
        if (!string.IsNullOrWhiteSpace(request.ParentId))
        {
            scene.GetRequiredEntity(request.ParentId);
        }

        var updated = scene.UpdateEntity(request.EntityId, entity => entity with { ParentId = request.ParentId });
        await _store.SaveAsync(request.ProjectRoot, updated, context.CancellationToken);
        context.Transaction.RecordChangedResource(_store.GetScenePath(request.ProjectRoot, request.SceneName));
        return RekallAgeCommandResult<ParentEntityResult>.Success(
            new ParentEntityResult(updated),
            $"Updated parent for entity '{request.EntityId}'.");
    }
}
```

- [ ] **Step 5: Add prefab and snap commands**

Create `src/Rekall.Age.LevelDesign/Commands/CreatePrefabFromEntityCommand.cs`:

```csharp
using Rekall.Age.Core.Commands;
using Rekall.Age.World;

namespace Rekall.Age.LevelDesign.Commands;

public sealed record CreatePrefabFromEntityRequest(string ProjectRoot, string SceneName, string EntityId, string PrefabName);

public sealed record CreatePrefabFromEntityResult(string PrefabId, string PrefabPath);

public sealed class CreatePrefabFromEntityCommand
    : IRekallAgeCommand<CreatePrefabFromEntityRequest, CreatePrefabFromEntityResult>
{
    private readonly RekallAgeSceneStore _sceneStore = new();
    private readonly RekallAgePrefabStore _prefabStore = new();

    public string Name => "rekall.level.prefab.create_from_entity";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Creates a prefab asset from an entity.",
        typeof(CreatePrefabFromEntityRequest).FullName!,
        typeof(CreatePrefabFromEntityResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<CreatePrefabFromEntityResult>> ExecuteAsync(
        CreatePrefabFromEntityRequest request,
        RekallAgeCommandContext context)
    {
        var scene = await _sceneStore.LoadAsync(request.ProjectRoot, request.SceneName, context.CancellationToken);
        var prefab = RekallAgePrefabDocument.Create(request.PrefabName, scene.GetRequiredEntity(request.EntityId));
        await _prefabStore.SaveAsync(request.ProjectRoot, prefab, context.CancellationToken);
        var path = _prefabStore.GetPath(request.ProjectRoot, prefab.Id);
        context.Transaction.RecordChangedResource(path);
        return RekallAgeCommandResult<CreatePrefabFromEntityResult>.Success(
            new CreatePrefabFromEntityResult(prefab.Id, path),
            $"Created prefab '{prefab.Name}'.");
    }
}
```

Create `src/Rekall.Age.LevelDesign/Commands/InstantiatePrefabCommand.cs`:

```csharp
using Rekall.Age.Core.Commands;
using Rekall.Age.World;

namespace Rekall.Age.LevelDesign.Commands;

public sealed record InstantiatePrefabRequest(string ProjectRoot, string SceneName, string PrefabId, string? Name = null);

public sealed record InstantiatePrefabResult(string EntityId, RekallAgeSceneDocument Scene);

public sealed class InstantiatePrefabCommand : IRekallAgeCommand<InstantiatePrefabRequest, InstantiatePrefabResult>
{
    private readonly RekallAgeSceneStore _sceneStore = new();
    private readonly RekallAgePrefabStore _prefabStore = new();

    public string Name => "rekall.level.prefab.instantiate";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Instantiates a prefab into a scene using a new stable entity id.",
        typeof(InstantiatePrefabRequest).FullName!,
        typeof(InstantiatePrefabResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<InstantiatePrefabResult>> ExecuteAsync(
        InstantiatePrefabRequest request,
        RekallAgeCommandContext context)
    {
        var scene = await _sceneStore.LoadAsync(request.ProjectRoot, request.SceneName, context.CancellationToken);
        var prefab = await _prefabStore.LoadAsync(request.ProjectRoot, request.PrefabId, context.CancellationToken);
        var entity = new RekallAgeEntityDocument(
            $"ent_{Guid.NewGuid():N}",
            string.IsNullOrWhiteSpace(request.Name) ? prefab.Name : request.Name.Trim(),
            prefab.RootEntity.Tags,
            prefab.RootEntity.Components)
        {
            PrefabSourceId = prefab.Id,
            Visible = prefab.RootEntity.Visible,
            Locked = prefab.RootEntity.Locked
        };
        var updated = scene.AddEntity(entity);
        await _sceneStore.SaveAsync(request.ProjectRoot, updated, context.CancellationToken);
        context.Transaction.RecordChangedResource(_sceneStore.GetScenePath(request.ProjectRoot, request.SceneName));
        return RekallAgeCommandResult<InstantiatePrefabResult>.Success(
            new InstantiatePrefabResult(entity.Id, updated),
            $"Instantiated prefab '{prefab.Name}'.");
    }
}
```

Create `src/Rekall.Age.LevelDesign/Commands/SnapEntityToGridCommand.cs`:

```csharp
using System.Text.Json.Nodes;
using Rekall.Age.Core.Commands;
using Rekall.Age.World;

namespace Rekall.Age.LevelDesign.Commands;

public sealed record SnapEntityToGridRequest(string ProjectRoot, string SceneName, string EntityId, double GridSize);

public sealed record SnapEntityToGridResult(RekallAgeSceneDocument Scene);

public sealed class SnapEntityToGridCommand : IRekallAgeCommand<SnapEntityToGridRequest, SnapEntityToGridResult>
{
    private readonly RekallAgeSceneStore _store = new();

    public string Name => "rekall.level.entity.snap_to_grid";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Snaps Rekall.Transform2D x and y properties to the requested grid size.",
        typeof(SnapEntityToGridRequest).FullName!,
        typeof(SnapEntityToGridResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<SnapEntityToGridResult>> ExecuteAsync(
        SnapEntityToGridRequest request,
        RekallAgeCommandContext context)
    {
        if (request.GridSize <= 0)
        {
            var error = new RekallAgeCommandError("REKALL_GRID_SIZE_INVALID", "Grid size must be greater than zero.", request.EntityId);
            return RekallAgeCommandResult<SnapEntityToGridResult>.Failure(default!, error.Message, [error]);
        }

        var scene = await _store.LoadAsync(request.ProjectRoot, request.SceneName, context.CancellationToken);
        var updated = scene.UpdateEntity(
            request.EntityId,
            entity => entity.UpdateComponent(
                "Rekall.Transform2D",
                component => component
                    .SetProperty("x", JsonValue.Create(Snap(ReadDouble(component.Properties["x"]), request.GridSize)))
                    .SetProperty("y", JsonValue.Create(Snap(ReadDouble(component.Properties["y"]), request.GridSize)))));
        await _store.SaveAsync(request.ProjectRoot, updated, context.CancellationToken);
        context.Transaction.RecordChangedResource(_store.GetScenePath(request.ProjectRoot, request.SceneName));
        return RekallAgeCommandResult<SnapEntityToGridResult>.Success(
            new SnapEntityToGridResult(updated),
            $"Snapped entity '{request.EntityId}' to grid.");
    }

    private static double ReadDouble(JsonNode? value)
    {
        return value is null ? 0 : value.GetValue<double>();
    }

    private static double Snap(double value, double grid)
    {
        return Math.Round(value / grid, MidpointRounding.AwayFromZero) * grid;
    }
}
```

- [ ] **Step 6: Run level-design tests**

Run:

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter FullyQualifiedName~LevelDesignCommandTests
```

Expected: PASS.

- [ ] **Step 7: Commit**

Run:

```powershell
git add src/Rekall.Age.LevelDesign tests/Rekall.Age.Tests/LevelDesign/LevelDesignCommandTests.cs
git commit -m "feat: add command-backed level design workflows"
```

Expected: commit succeeds.

---

### Task 6: Runtime And Viewport Contracts

**Files:**
- Create: `src/Rekall.Age.Runtime.Abstractions/RekallAgeRuntimeContracts.cs`
- Create: `src/Rekall.Age.Rendering.Abstractions/RekallAgeRenderWorldContracts.cs`
- Create: `src/Rekall.Age.Editor/RekallAgeViewportModelBuilder.cs`
- Test: `tests/Rekall.Age.Tests/Rendering/ViewportContractTests.cs`

- [ ] **Step 1: Write failing viewport contract test**

Create `tests/Rekall.Age.Tests/Rendering/ViewportContractTests.cs`:

```csharp
using System.Text.Json.Nodes;
using Rekall.Age.Editor;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Rendering;

public sealed class ViewportContractTests
{
    [Fact]
    public async Task ViewportModelExtractsCameraAndRenderableSprites()
    {
        var root = TestPaths.CreateTempDirectory();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering2d"])
            .AddEntity(RekallAgeEntityDocument.Create("Camera", ["camera"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.Camera2D", new JsonObject { ["active"] = true })))
            .AddEntity(RekallAgeEntityDocument.Create("Player", ["player"])
                .AddComponent(RekallAgeComponentDocument.Create("Rekall.SpriteRenderer", new JsonObject { ["sprite"] = "asset_player" })));
        await new RekallAgeSceneStore().SaveAsync(root, scene, CancellationToken.None);

        var viewport = await new RekallAgeViewportModelBuilder().BuildAsync(root, "Main", CancellationToken.None);

        Assert.Equal("Main", viewport.SceneName);
        Assert.Equal("Camera", viewport.ActiveCameraName);
        Assert.Single(viewport.RenderWorld.Sprites);
        Assert.Equal("Player", viewport.RenderWorld.Sprites[0].EntityName);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter FullyQualifiedName~ViewportContractTests
```

Expected: FAIL because viewport contracts do not exist.

- [ ] **Step 3: Add runtime contracts**

Create `src/Rekall.Age.Runtime.Abstractions/RekallAgeRuntimeContracts.cs`:

```csharp
using Rekall.Age.World;

namespace Rekall.Age.Runtime.Abstractions;

public sealed record RekallAgeFrameContext(
    int FrameIndex,
    TimeSpan DeltaTime,
    TimeSpan ElapsedTime,
    CancellationToken CancellationToken);

public interface IRekallAgeRuntimeSystem
{
    string Id { get; }

    ValueTask UpdateAsync(
        RekallAgeSceneDocument scene,
        RekallAgeFrameContext context);
}

public sealed record RekallAgeSubsystemDescriptor(
    string Id,
    string Kind,
    string Status,
    IReadOnlyList<string> Capabilities);

public sealed class RekallAgeSubsystemRegistry
{
    private readonly List<RekallAgeSubsystemDescriptor> _subsystems = [];

    public IReadOnlyList<RekallAgeSubsystemDescriptor> Subsystems => _subsystems;

    public void Register(RekallAgeSubsystemDescriptor descriptor)
    {
        if (_subsystems.Any(item => item.Id.Equals(descriptor.Id, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Subsystem '{descriptor.Id}' is already registered.");
        }

        _subsystems.Add(descriptor);
    }
}
```

- [ ] **Step 4: Add render world contracts**

Create `src/Rekall.Age.Rendering.Abstractions/RekallAgeRenderWorldContracts.cs`:

```csharp
namespace Rekall.Age.Rendering.Abstractions;

public sealed record RekallAgeViewportModel(
    string SceneName,
    string? ActiveCameraName,
    RekallAgeRenderWorld RenderWorld);

public sealed record RekallAgeRenderWorld(
    IReadOnlyList<RekallAgeRenderCamera> Cameras,
    IReadOnlyList<RekallAgeRenderSprite> Sprites,
    IReadOnlyList<RekallAgeRenderMesh> Meshes,
    IReadOnlyList<RekallAgeRenderLight> Lights);

public sealed record RekallAgeRenderCamera(
    string EntityId,
    string EntityName,
    string Kind,
    bool Active);

public sealed record RekallAgeRenderSprite(
    string EntityId,
    string EntityName,
    string? AssetId);

public sealed record RekallAgeRenderMesh(
    string EntityId,
    string EntityName,
    string? AssetId);

public sealed record RekallAgeRenderLight(
    string EntityId,
    string EntityName,
    string Kind);
```

- [ ] **Step 5: Add viewport model builder**

Create `src/Rekall.Age.Editor/RekallAgeViewportModelBuilder.cs`:

```csharp
using Rekall.Age.Rendering.Abstractions;
using Rekall.Age.World;

namespace Rekall.Age.Editor;

public sealed class RekallAgeViewportModelBuilder
{
    private readonly RekallAgeSceneStore _sceneStore;

    public RekallAgeViewportModelBuilder()
        : this(new RekallAgeSceneStore())
    {
    }

    public RekallAgeViewportModelBuilder(RekallAgeSceneStore sceneStore)
    {
        _sceneStore = sceneStore;
    }

    public async ValueTask<RekallAgeViewportModel> BuildAsync(
        string projectRoot,
        string sceneName,
        CancellationToken cancellationToken)
    {
        var scene = await _sceneStore.LoadAsync(projectRoot, sceneName, cancellationToken);
        var cameras = scene.Entities
            .SelectMany(entity => entity.Components
                .Where(component => component.Type is "Rekall.Camera2D" or "Rekall.Camera3D")
                .Select(component => new RekallAgeRenderCamera(
                    entity.Id,
                    entity.Name,
                    component.Type,
                    component.Properties.TryGetPropertyValue("active", out var activeNode)
                        && activeNode is not null
                        && activeNode.GetValue<bool>())))
            .ToArray();
        var sprites = scene.Entities
            .Where(entity => entity.Components.Any(component => component.Type == "Rekall.SpriteRenderer"))
            .Select(entity =>
            {
                var component = entity.Components.First(item => item.Type == "Rekall.SpriteRenderer");
                var assetId = component.Properties.TryGetPropertyValue("sprite", out var spriteNode)
                    ? spriteNode?.GetValue<string>()
                    : null;
                return new RekallAgeRenderSprite(entity.Id, entity.Name, assetId);
            })
            .ToArray();
        var meshes = scene.Entities
            .Where(entity => entity.Components.Any(component => component.Type is "Rekall.MeshRenderer" or "Rekall.MeshSet"))
            .Select(entity => new RekallAgeRenderMesh(entity.Id, entity.Name, null))
            .ToArray();
        var lights = scene.Entities
            .Where(entity => entity.Components.Any(component => component.Type.Contains("Light", StringComparison.Ordinal)))
            .Select(entity => new RekallAgeRenderLight(entity.Id, entity.Name, "light"))
            .ToArray();
        var activeCamera = cameras.FirstOrDefault(camera => camera.Active)?.EntityName ?? cameras.FirstOrDefault()?.EntityName;
        return new RekallAgeViewportModel(scene.Name, activeCamera, new RekallAgeRenderWorld(cameras, sprites, meshes, lights));
    }
}
```

- [ ] **Step 6: Add editor reference to rendering abstractions**

Run:

```powershell
dotnet add src/Rekall.Age.Editor/Rekall.Age.Editor.csproj reference src/Rekall.Age.Rendering.Abstractions/Rekall.Age.Rendering.Abstractions.csproj
```

Expected: reference is added if not already present.

- [ ] **Step 7: Run viewport contract tests**

Run:

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter FullyQualifiedName~ViewportContractTests
```

Expected: PASS.

- [ ] **Step 8: Commit**

Run:

```powershell
git add src/Rekall.Age.Runtime.Abstractions src/Rekall.Age.Rendering.Abstractions src/Rekall.Age.Editor tests/Rekall.Age.Tests/Rendering/ViewportContractTests.cs
git commit -m "feat: add runtime and viewport contracts"
```

Expected: commit succeeds.

---

### Task 7: WPF Rekall Studio Shell

**Files:**
- Modify: `src/Rekall.Age.Studio/App.xaml`
- Modify: `src/Rekall.Age.Studio/App.xaml.cs`
- Modify: `src/Rekall.Age.Studio/MainWindow.xaml`
- Modify: `src/Rekall.Age.Studio/MainWindow.xaml.cs`
- Create: `src/Rekall.Age.Studio/RekallAgeStudioViewModel.cs`

- [ ] **Step 1: Replace Studio app XAML**

Replace `src/Rekall.Age.Studio/App.xaml` with:

```xml
<Application x:Class="Rekall.Age.Studio.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             StartupUri="MainWindow.xaml">
    <Application.Resources>
        <SolidColorBrush x:Key="WorkbenchBackground" Color="#20242A" />
        <SolidColorBrush x:Key="PanelBackground" Color="#2B3038" />
        <SolidColorBrush x:Key="PanelBorder" Color="#414852" />
        <SolidColorBrush x:Key="WorkbenchText" Color="#F3F5F7" />
    </Application.Resources>
</Application>
```

- [ ] **Step 2: Replace main window layout**

Replace `src/Rekall.Age.Studio/MainWindow.xaml` with:

```xml
<Window x:Class="Rekall.Age.Studio.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Rekall Studio" Width="1440" Height="900"
        Background="{StaticResource WorkbenchBackground}"
        Foreground="{StaticResource WorkbenchText}">
    <DockPanel>
        <ToolBar DockPanel.Dock="Top" Background="{StaticResource PanelBackground}">
            <Button Content="Open" Padding="12,4" />
            <Separator />
            <Button Content="Undo" Padding="12,4" />
            <Button Content="Redo" Padding="12,4" />
            <Separator />
            <Button Content="Play" Padding="12,4" />
            <Button Content="Pause" Padding="12,4" />
            <Button Content="Stop" Padding="12,4" />
            <Separator />
            <Button Content="Capture" Padding="12,4" />
            <Button Content="Validate" Padding="12,4" />
        </ToolBar>

        <Grid>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="280" />
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="360" />
            </Grid.ColumnDefinitions>
            <Grid.RowDefinitions>
                <RowDefinition Height="*" />
                <RowDefinition Height="260" />
            </Grid.RowDefinitions>

            <Border Grid.Column="0" Grid.Row="0" Grid.RowSpan="2" Margin="8" Padding="8"
                    Background="{StaticResource PanelBackground}" BorderBrush="{StaticResource PanelBorder}" BorderThickness="1">
                <DockPanel>
                    <TextBlock DockPanel.Dock="Top" Text="Scene Hierarchy" FontWeight="SemiBold" Margin="0,0,0,8" />
                    <TreeView ItemsSource="{Binding EntityNodes}" />
                </DockPanel>
            </Border>

            <Border Grid.Column="1" Grid.Row="0" Margin="0,8,0,8" Padding="12"
                    Background="#15181D" BorderBrush="{StaticResource PanelBorder}" BorderThickness="1">
                <Grid>
                    <TextBlock Text="{Binding ViewportTitle}" FontSize="18" FontWeight="SemiBold" VerticalAlignment="Top" />
                    <TextBlock Text="{Binding ViewportSummary}" FontSize="14" VerticalAlignment="Center" HorizontalAlignment="Center" />
                </Grid>
            </Border>

            <Border Grid.Column="2" Grid.Row="0" Margin="8" Padding="8"
                    Background="{StaticResource PanelBackground}" BorderBrush="{StaticResource PanelBorder}" BorderThickness="1">
                <DockPanel>
                    <TextBlock DockPanel.Dock="Top" Text="Inspector" FontWeight="SemiBold" Margin="0,0,0,8" />
                    <ListBox ItemsSource="{Binding InspectorLines}" />
                </DockPanel>
            </Border>

            <TabControl Grid.Column="1" Grid.ColumnSpan="2" Grid.Row="1" Margin="0,0,8,8">
                <TabItem Header="Assets">
                    <ListBox ItemsSource="{Binding AssetLines}" />
                </TabItem>
                <TabItem Header="Validation">
                    <ListBox ItemsSource="{Binding ValidationLines}" />
                </TabItem>
                <TabItem Header="Transactions">
                    <ListBox ItemsSource="{Binding TransactionLines}" />
                </TabItem>
                <TabItem Header="Imports">
                    <ListBox ItemsSource="{Binding ImportLines}" />
                </TabItem>
            </TabControl>
        </Grid>
    </DockPanel>
</Window>
```

- [ ] **Step 3: Add Studio view model**

Create `src/Rekall.Age.Studio/RekallAgeStudioViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using Rekall.Age.Editor.Contracts;

namespace Rekall.Age.Studio;

public sealed class RekallAgeStudioViewModel
{
    public RekallAgeStudioViewModel(RekallAgeWorkbenchModel? model)
    {
        EntityNodes = new ObservableCollection<string>(
            model?.Scene.RootEntities.Select(FormatEntity) ?? ["No project loaded"]);
        InspectorLines = new ObservableCollection<string>(
            model?.Inspector.Components.SelectMany(component =>
                new[] { component.Type }.Concat(component.Properties.Select(property => $"  {property.Name}: {property.Value}")))
            ?? ["Select an entity"]);
        AssetLines = new ObservableCollection<string>(
            model?.Assets.Assets.Select(asset => $"{asset.Kind}: {asset.DisplayName} ({asset.AssetId})") ?? []);
        ValidationLines = new ObservableCollection<string>(
            model?.Diagnostics.Issues.Select(issue => $"{issue.Severity}: {issue.Code} - {issue.Message}") ?? []);
        TransactionLines = new ObservableCollection<string>(
            model?.Transactions.Transactions.Select(transaction => $"{transaction.Name}: {transaction.ChangedResources.Count} changes") ?? []);
        ImportLines = new ObservableCollection<string>(
            model?.ImportQueue.Jobs.Select(job => $"{job.Status}: {job.SourcePath}") ?? []);
        ViewportTitle = model is null ? "Viewport" : $"{model.Scene.Name} Viewport";
        ViewportSummary = model is null
            ? "Open a Rekall AGE project to begin."
            : $"{model.Scene.RootEntities.Count} root entities, {model.Assets.Assets.Count} assets, {model.Diagnostics.Issues.Count} diagnostics";
    }

    public ObservableCollection<string> EntityNodes { get; }

    public ObservableCollection<string> InspectorLines { get; }

    public ObservableCollection<string> AssetLines { get; }

    public ObservableCollection<string> ValidationLines { get; }

    public ObservableCollection<string> TransactionLines { get; }

    public ObservableCollection<string> ImportLines { get; }

    public string ViewportTitle { get; }

    public string ViewportSummary { get; }

    private static string FormatEntity(RekallAgeSceneEntityNode node)
    {
        return node.Children.Count == 0
            ? node.Name
            : $"{node.Name} ({node.Children.Count})";
    }
}
```

- [ ] **Step 4: Replace main window code-behind**

Replace `src/Rekall.Age.Studio/MainWindow.xaml.cs` with:

```csharp
using System.Windows;
using Rekall.Age.Editor;
using Rekall.Age.Editor.Contracts;

namespace Rekall.Age.Studio;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = LoadInitialViewModel();
    }

    private static RekallAgeStudioViewModel LoadInitialViewModel()
    {
        var args = Environment.GetCommandLineArgs();
        var projectIndex = Array.IndexOf(args, "--project");
        var sceneIndex = Array.IndexOf(args, "--scene");
        if (projectIndex < 0 || projectIndex + 1 >= args.Length)
        {
            return new RekallAgeStudioViewModel(null);
        }

        var projectRoot = args[projectIndex + 1];
        var sceneName = sceneIndex >= 0 && sceneIndex + 1 < args.Length ? args[sceneIndex + 1] : "Main";
        RekallAgeWorkbenchModel? model = new RekallAgeWorkbenchModelBuilder()
            .BuildAsync(projectRoot, sceneName, CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();
        return new RekallAgeStudioViewModel(model);
    }
}
```

- [ ] **Step 5: Verify Studio project builds**

Run:

```powershell
dotnet build src/Rekall.Age.Studio/Rekall.Age.Studio.csproj
```

Expected: build succeeds with zero warnings.

- [ ] **Step 6: Commit**

Run:

```powershell
git add src/Rekall.Age.Studio
git commit -m "feat: add Rekall Studio workbench shell"
```

Expected: commit succeeds.

---

### Task 8: CLI And MCP Parity

**Files:**
- Modify: `src/Rekall.Age.Cli/Program.cs`
- Test: `tests/Rekall.Age.Tests/Cli/StudioCliTests.cs`
- Test: `tests/Rekall.Age.Tests/Mcp/WorkbenchMcpCatalogTests.cs`

- [ ] **Step 1: Write failing CLI and MCP tests**

Create `tests/Rekall.Age.Tests/Cli/StudioCliTests.cs`:

```csharp
using Rekall.Age.Cli.Tests;
using Rekall.Age.Project;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Cli;

public sealed class StudioCliTests
{
    [Fact]
    public async Task StudioOpenPrintsWorkbenchSummary()
    {
        var root = TestPaths.CreateTempDirectory();
        await new RekallAgeProjectStore().SaveAsync(
            root,
            RekallAgeProjectManifest.Create("Studio Test", ["world"]),
            CancellationToken.None);
        await new RekallAgeSceneStore().SaveAsync(
            root,
            RekallAgeSceneDocument.Create("Main", ["world"]),
            CancellationToken.None);

        var result = await CliRunner.RunAsync(["studio", "open", root, "Main"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Studio Test", result.Output);
        Assert.Contains("Main", result.Output);
    }
}
```

Create `tests/Rekall.Age.Tests/Mcp/WorkbenchMcpCatalogTests.cs`:

```csharp
using Rekall.Age.AssetPipeline.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.LevelDesign.Commands;
using Rekall.Age.Mcp;

namespace Rekall.Age.Tests.Mcp;

public sealed class WorkbenchMcpCatalogTests
{
    [Fact]
    public void CatalogExposesWorkbenchWorkflowCommands()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new ImportAssetWithReportCommand());
        registry.Register(new DuplicateEntityCommand());
        registry.Register(new ParentEntityCommand());
        registry.Register(new CreatePrefabFromEntityCommand());
        registry.Register(new InstantiatePrefabCommand());
        registry.Register(new SnapEntityToGridCommand());

        var names = RekallAgeMcpCatalog.FromRegistry(registry).Tools.Select(tool => tool.Name).ToArray();

        Assert.Contains("rekall.asset.import_report", names);
        Assert.Contains("rekall.level.entity.duplicate", names);
        Assert.Contains("rekall.level.prefab.instantiate", names);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "FullyQualifiedName~StudioCliTests|FullyQualifiedName~WorkbenchMcpCatalogTests"
```

Expected: FAIL because CLI routes and registry imports are missing.

- [ ] **Step 3: Add CLI usings and registry entries**

In `src/Rekall.Age.Cli/Program.cs`, add:

```csharp
using Rekall.Age.AssetPipeline.Commands;
using Rekall.Age.Editor;
using Rekall.Age.LevelDesign.Commands;
```

In `BuildRegistry()`, after the existing asset commands, add:

```csharp
        registry.Register(new ImportAssetWithReportCommand());
        registry.Register(new DuplicateEntityCommand());
        registry.Register(new ParentEntityCommand());
        registry.Register(new CreatePrefabFromEntityCommand());
        registry.Register(new InstantiatePrefabCommand());
        registry.Register(new SnapEntityToGridCommand());
```

- [ ] **Step 4: Add CLI routes**

In the main `args switch`, add these cases before the `_ => PrintUnknown(args)` arm:

```csharp
                ["studio", "open", var root, var scene] => await OpenStudioModelAsync(root, scene),
                ["asset", "import-report", var root, var source, var kind, var displayName] =>
                    await ImportAssetReportAsync(registry, context, root, source, kind, displayName),
                ["level", "entity", "duplicate", var root, var scene, var entityId, var name] =>
                    await DuplicateEntityAsync(registry, context, root, scene, entityId, name),
                ["level", "entity", "parent", var root, var scene, var entityId, var parentId] =>
                    await ParentEntityAsync(registry, context, root, scene, entityId, parentId),
                ["level", "prefab", "create", var root, var scene, var entityId, var prefabName] =>
                    await CreatePrefabAsync(registry, context, root, scene, entityId, prefabName),
                ["level", "prefab", "instantiate", var root, var scene, var prefabId, var name] =>
                    await InstantiatePrefabAsync(registry, context, root, scene, prefabId, name),
                ["level", "entity", "snap", var root, var scene, var entityId, var gridSize] =>
                    await SnapEntityAsync(registry, context, root, scene, entityId, gridSize),
```

- [ ] **Step 5: Add CLI handler methods**

Add these methods near the other private CLI handlers:

```csharp
    private static async Task<int> OpenStudioModelAsync(string root, string scene)
    {
        var model = await new RekallAgeWorkbenchModelBuilder().BuildAsync(root, scene, CancellationToken.None);
        Console.WriteLine($"{model.Project.Name}: {model.Scene.Name}");
        Console.WriteLine($"Root entities: {model.Scene.RootEntities.Count}");
        Console.WriteLine($"Assets: {model.Assets.Assets.Count}");
        Console.WriteLine($"Diagnostics: {model.Diagnostics.Issues.Count}");
        return 0;
    }

    private static async Task<int> ImportAssetReportAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string source,
        string kind,
        string displayName)
    {
        var result = await registry.ExecuteAsync<ImportAssetWithReportRequest, ImportAssetWithReportResult>(
            "rekall.asset.import_report",
            new ImportAssetWithReportRequest(root, source, kind, displayName),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine($"Imported: {result.Value.Report.Imported}");
        Console.WriteLine($"Asset: {result.Value.Report.AssetId}");
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> DuplicateEntityAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string entityId,
        string name)
    {
        var result = await registry.ExecuteAsync<DuplicateEntityRequest, DuplicateEntityResult>(
            "rekall.level.entity.duplicate",
            new DuplicateEntityRequest(root, scene, entityId, name),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine(result.Value.EntityId);
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> ParentEntityAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string entityId,
        string parentId)
    {
        var normalizedParent = parentId.Equals("null", StringComparison.OrdinalIgnoreCase) ? null : parentId;
        var result = await registry.ExecuteAsync<ParentEntityRequest, ParentEntityResult>(
            "rekall.level.entity.parent",
            new ParentEntityRequest(root, scene, entityId, normalizedParent),
            context);
        Console.WriteLine(result.Summary);
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> CreatePrefabAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string entityId,
        string prefabName)
    {
        var result = await registry.ExecuteAsync<CreatePrefabFromEntityRequest, CreatePrefabFromEntityResult>(
            "rekall.level.prefab.create_from_entity",
            new CreatePrefabFromEntityRequest(root, scene, entityId, prefabName),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine(result.Value.PrefabId);
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> InstantiatePrefabAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string prefabId,
        string name)
    {
        var result = await registry.ExecuteAsync<InstantiatePrefabRequest, InstantiatePrefabResult>(
            "rekall.level.prefab.instantiate",
            new InstantiatePrefabRequest(root, scene, prefabId, name),
            context);
        Console.WriteLine(result.Summary);
        Console.WriteLine(result.Value.EntityId);
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> SnapEntityAsync(
        RekallAgeCommandRegistry registry,
        RekallAgeCommandContext context,
        string root,
        string scene,
        string entityId,
        string gridSize)
    {
        var result = await registry.ExecuteAsync<SnapEntityToGridRequest, SnapEntityToGridResult>(
            "rekall.level.entity.snap_to_grid",
            new SnapEntityToGridRequest(
                root,
                scene,
                entityId,
                double.Parse(gridSize, System.Globalization.CultureInfo.InvariantCulture)),
            context);
        Console.WriteLine(result.Summary);
        return result.Ok ? 0 : 1;
    }
```

- [ ] **Step 6: Run CLI and MCP parity tests**

Run:

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "FullyQualifiedName~StudioCliTests|FullyQualifiedName~WorkbenchMcpCatalogTests"
```

Expected: PASS.

- [ ] **Step 7: Commit**

Run:

```powershell
git add src/Rekall.Age.Cli tests/Rekall.Age.Tests/Cli/StudioCliTests.cs tests/Rekall.Age.Tests/Mcp/WorkbenchMcpCatalogTests.cs
git commit -m "feat: expose workbench workflows through CLI and MCP"
```

Expected: commit succeeds.

---

### Task 9: End-To-End Workbench Authoring Loop

**Files:**
- Create: `tests/Rekall.Age.Tests/VerticalSlice/WorkbenchFoundationTests.cs`
- Modify: `README.md`

- [ ] **Step 1: Write end-to-end workbench test**

Create `tests/Rekall.Age.Tests/VerticalSlice/WorkbenchFoundationTests.cs`:

```csharp
using System.Text.Json.Nodes;
using Rekall.Age.AssetPipeline.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Editor;
using Rekall.Age.LevelDesign.Commands;
using Rekall.Age.Playback.Commands;
using Rekall.Age.Project.Commands;
using Rekall.Age.Rendering.Commands;
using Rekall.Age.World;
using Rekall.Age.World.Commands;

namespace Rekall.Age.Tests.VerticalSlice;

public sealed class WorkbenchFoundationTests
{
    [Fact]
    public async Task AgentAndStudioCanShareAuthoringLoop()
    {
        var root = TestPaths.CreateTempDirectory();
        var source = Path.Combine(root, "player.png");
        await File.WriteAllBytesAsync(source, [10, 20, 30, 40], CancellationToken.None);
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new CreateProjectCommand());
        registry.Register(new CreateSceneCommand());
        registry.Register(new CreateEntityCommand());
        registry.Register(new AddComponentCommand());
        registry.Register(new ImportAssetWithReportCommand());
        registry.Register(new DuplicateEntityCommand());
        registry.Register(new CreatePrefabFromEntityCommand());
        registry.Register(new InstantiatePrefabCommand());
        registry.Register(new PlaySceneCommand());
        registry.Register(new CaptureScreenshotCommand());
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("workbench loop"), CancellationToken.None);

        Assert.True((await registry.ExecuteAsync<CreateProjectRequest, CreateProjectResult>(
            "rekall.project.create",
            new CreateProjectRequest(root, "Workbench Game", ["world", "rendering2d", "input"]),
            context)).Ok);
        Assert.True((await registry.ExecuteAsync<CreateSceneRequest, CreateSceneResult>(
            "rekall.scene.create",
            new CreateSceneRequest(root, "Main", ["world", "rendering2d"]),
            context)).Ok);
        var camera = await registry.ExecuteAsync<CreateEntityRequest, CreateEntityResult>(
            "rekall.entity.create",
            new CreateEntityRequest(root, "Main", "MainCamera", ["camera"]),
            context);
        Assert.True(camera.Ok);
        Assert.True((await registry.ExecuteAsync<AddComponentRequest, AddComponentResult>(
            "rekall.component.add",
            new AddComponentRequest(root, "Main", camera.Value.EntityId, "Rekall.Camera2D", new JsonObject { ["active"] = true }),
            context)).Ok);
        var player = await registry.ExecuteAsync<CreateEntityRequest, CreateEntityResult>(
            "rekall.entity.create",
            new CreateEntityRequest(root, "Main", "Player", ["player"]),
            context);
        Assert.True(player.Ok);
        Assert.True((await registry.ExecuteAsync<AddComponentRequest, AddComponentResult>(
            "rekall.component.add",
            new AddComponentRequest(root, "Main", player.Value.EntityId, "Rekall.Transform2D", new JsonObject { ["x"] = 0, ["y"] = 0 }),
            context)).Ok);
        Assert.True((await registry.ExecuteAsync<ImportAssetWithReportRequest, ImportAssetWithReportResult>(
            "rekall.asset.import_report",
            new ImportAssetWithReportRequest(root, source, "sprite", "Player"),
            context)).Ok);
        var duplicate = await registry.ExecuteAsync<DuplicateEntityRequest, DuplicateEntityResult>(
            "rekall.level.entity.duplicate",
            new DuplicateEntityRequest(root, "Main", player.Value.EntityId, "Player Copy"),
            context);
        Assert.True(duplicate.Ok);
        var prefab = await registry.ExecuteAsync<CreatePrefabFromEntityRequest, CreatePrefabFromEntityResult>(
            "rekall.level.prefab.create_from_entity",
            new CreatePrefabFromEntityRequest(root, "Main", player.Value.EntityId, "PlayerPrefab"),
            context);
        Assert.True(prefab.Ok);
        Assert.True((await registry.ExecuteAsync<InstantiatePrefabRequest, InstantiatePrefabResult>(
            "rekall.level.prefab.instantiate",
            new InstantiatePrefabRequest(root, "Main", prefab.Value.PrefabId, "Prefab Player"),
            context)).Ok);

        var model = await new RekallAgeWorkbenchModelBuilder().BuildAsync(root, "Main", CancellationToken.None);
        Assert.Equal("Workbench Game", model.Project.Name);
        Assert.True(model.Scene.RootEntities.Count >= 3);
        Assert.Single(model.Assets.Assets);
        Assert.Empty(model.Diagnostics.Issues.Where(issue => issue.Severity == "blocking"));

        var capture = await registry.ExecuteAsync<CaptureScreenshotRequest, CaptureScreenshotResult>(
            "rekall.capture.screenshot",
            new CaptureScreenshotRequest(root, "Main", Path.Combine(root, "Artifacts", "Screenshots")),
            context);
        Assert.True(capture.Ok);
        Assert.True(File.Exists(capture.Value.ScreenshotPath));
        Assert.NotEmpty(context.Transaction.ChangedResources);
    }
}
```

- [ ] **Step 2: Run end-to-end test**

Run:

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter FullyQualifiedName~WorkbenchFoundationTests
```

Expected: PASS.

- [ ] **Step 3: Update README**

Add this section to `README.md` after the existing CLI examples:

```markdown
## Workbench Foundation

The production workbench slice adds editor-facing read models, level-design workflows, asset pipeline reports, and a first Windows desktop Studio shell.

```powershell
dotnet run --project src/Rekall.Age.Cli -- studio open .age-sandbox Main
dotnet run --project src/Rekall.Age.Cli -- asset import-report .age-sandbox .\player.png sprite "Player"
dotnet run --project src/Rekall.Age.Cli -- level entity duplicate .age-sandbox Main <entity-id> "Player Copy"
dotnet run --project src/Rekall.Age.Cli -- level prefab create .age-sandbox Main <entity-id> PlayerPrefab
dotnet run --project src/Rekall.Age.Cli -- level prefab instantiate .age-sandbox Main <prefab-id> "Prefab Player"
dotnet run --project src/Rekall.Age.Cli -- level entity snap .age-sandbox Main <entity-id> 1
dotnet run --project src/Rekall.Age.Studio -- --project .age-sandbox --scene Main
```
```

- [ ] **Step 4: Run full verification**

Run:

```powershell
dotnet build Rekall.AGE.sln
dotnet test Rekall.AGE.sln
```

Expected: build succeeds with zero warnings and all tests pass.

- [ ] **Step 5: Commit**

Run:

```powershell
git add README.md tests/Rekall.Age.Tests/VerticalSlice/WorkbenchFoundationTests.cs
git commit -m "test: prove workbench foundation authoring loop"
```

Expected: commit succeeds.

---

## Plan Self-Review

**Spec coverage:** The plan maps the production workbench spec to executable tasks: editor read models, asset pipeline reports, level-design commands, runtime/viewport contracts, Studio shell, CLI/MCP parity, and an end-to-end workbench authoring loop. Advanced rendering, physics, audio, animation, and graph tooling remain follow-up specs as stated in the design.

**Placeholder scan:** The plan avoids unfinished markers and supplies concrete file paths, commands, code blocks, expected outcomes, and commit points.

**Type consistency:** The commands registered in CLI/MCP tests match the command names and request/result types defined in Tasks 4 and 5. Read model types used by Studio match the contracts from Task 2. Viewport builder types match Task 6 contracts.
