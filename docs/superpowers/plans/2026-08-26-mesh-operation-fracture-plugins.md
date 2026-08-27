# Mesh Operation / Fracture Plugin System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a project's own module code register custom mesh operations and fracture algorithms (auto-discovered exactly like gameplay components/systems already are), instead of requiring engine-source edits.

**Architecture:** Two new interfaces in the leaf `Rekall.Age.Modeling.Contracts` project; two new registration lists/methods on `RekallAgeModuleBuilder` (`Rekall.Age.Modules`); a new `RekallAgeProjectMeshPluginLoader` (`Rekall.Age.Modeling`) that mirrors `RekallAgeProjectRuntimeSystemLoader`'s discovery exactly; `RekallAgeMeshOperationExecutor` gains an optional plugin list and falls back to it for unknown ids; a new `RekallAgeMeshFractureExecutor` wraps the existing static Voronoi algorithm as the default and dispatches to plugins by id. CLI/MCP commands and Studio's modeling session load a project's plugins before dispatching, using the same `ProjectRoot` they already carry.

**Tech Stack:** C#/.NET 10, xUnit, the existing `RekallAgeModule`/`RekallAgeModuleBuilder` project-module convention, reflection-based assembly scanning already used by `RekallAgeProjectRuntimeSystemLoader`.

**Spec:** `docs/superpowers/specs/2026-08-26-mesh-operation-fracture-plugins-design.md`

## Global Constraints

- Plugin operation/algorithm ids MUST contain at least one `.`; bare ids stay reserved for built-ins, enforced at load time by `RekallAgeProjectMeshPluginLoader`.
- Plugins load **in-process** via the existing project-module-assembly loading path — this matches real current practice (gameplay modules aren't sandboxed in production either yet); no IPC/sandbox work in this plan.
- No new project-file configuration syntax — presence of a `Configure(builder)` registration in a built project module is the only signal, exactly like components and runtime systems today.
- New project reference: `Rekall.Age.Modules` → `Rekall.Age.Modeling.Contracts` (acyclic: `Rekall.Age.Modeling.Contracts` has zero project references of its own).
- New project reference: `Rekall.Age.Modeling` → `Rekall.Age.Modules` (acyclic: `Rekall.Age.Modules` never references `Rekall.Age.Modeling`, only `Rekall.Age.Core`, `Rekall.Age.Runtime.Abstractions`, and, after the first reference above, `Rekall.Age.Modeling.Contracts`).
- Every existing built-in mesh operation id and the existing static `RekallAgeMeshFracture.Fracture` algorithm keep working completely unchanged — no built-in `switch` arm changes, `RekallAgeMeshFracture` itself is untouched (only wrapped).
- All CLI/MCP-reachable commands added or changed here are reached the same way every other mesh command already is: the generic `rekall.command.execute`-style registry dispatch (`command execute <name> <jsonArgs>` at the CLI). None of these commands have a dedicated CLI verb in `Program.cs` today, and this plan does not add one — the generic passthrough is sufficient and consistent.

---

### Task 1: Plugin contracts + `RekallAgeModuleBuilder` registration

**Files:**
- Modify: `src/Rekall.Age.Modeling.Contracts/RekallAgeMeshContracts.cs` (add two interfaces at the end of the file)
- Modify: `src/Rekall.Age.Modules/Rekall.Age.Modules.csproj` (add project reference)
- Modify: `src/Rekall.Age.Modules/RekallAgeModuleBuilder.cs`
- Test: `tests/Rekall.Age.Tests/Modules/MeshPluginRegistrationTests.cs` (new)

**Interfaces:**
- Produces: `Rekall.Age.Modeling.Contracts.IRekallAgeMeshOperationPlugin` (`OperationId`, `Descriptor`, `Execute(RekallAgeMeshAsset, RekallAgeMeshOperationRequest) -> RekallAgeMeshOperationResult`), `Rekall.Age.Modeling.Contracts.IRekallAgeFractureAlgorithmPlugin` (`AlgorithmId`, `Fracture(RekallAgeMeshAsset, int, long) -> IReadOnlyList<RekallAgeMeshAsset>`), `RekallAgeModuleBuilder.MeshOperationTypes`/`FractureAlgorithmTypes` (`IReadOnlyList<Type>`), `RekallAgeModuleBuilder.RegisterMeshOperation<T>()`/`RegisterFractureAlgorithm<T>()`.

- [ ] **Step 1: Write the failing test**

Create `tests/Rekall.Age.Tests/Modules/MeshPluginRegistrationTests.cs`:

```csharp
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;
using Rekall.Age.Modules;

namespace Rekall.Age.Tests.Modules;

public sealed class MeshPluginRegistrationTests
{
    [Fact]
    public void RegisterMeshOperationAddsTheTypeOnceEvenIfRegisteredTwice()
    {
        var builder = new RekallAgeModuleBuilder();

        builder.RegisterMeshOperation<FakeMeshOperation>();
        builder.RegisterMeshOperation<FakeMeshOperation>();

        Assert.Single(builder.MeshOperationTypes, type => type == typeof(FakeMeshOperation));
    }

    [Fact]
    public void RegisterFractureAlgorithmAddsTheTypeOnceEvenIfRegisteredTwice()
    {
        var builder = new RekallAgeModuleBuilder();

        builder.RegisterFractureAlgorithm<FakeFractureAlgorithm>();
        builder.RegisterFractureAlgorithm<FakeFractureAlgorithm>();

        Assert.Single(builder.FractureAlgorithmTypes, type => type == typeof(FakeFractureAlgorithm));
    }

    private sealed class FakeMeshOperation : IRekallAgeMeshOperationPlugin
    {
        public string OperationId => "test.fake_operation";
        public RekallAgeMeshOperationDescriptor Descriptor => new(
            OperationId, "A fake test operation.", RekallAgeGeometryDomain.Face,
            RekallAgeMeshChangeKind.None, []);
        public RekallAgeMeshOperationResult Execute(RekallAgeMeshAsset source, RekallAgeMeshOperationRequest request) =>
            throw new NotSupportedException();
    }

    private sealed class FakeFractureAlgorithm : IRekallAgeFractureAlgorithmPlugin
    {
        public string AlgorithmId => "test.fake_algorithm";
        public IReadOnlyList<RekallAgeMeshAsset> Fracture(RekallAgeMeshAsset source, int chunkCount, long seed) =>
            throw new NotSupportedException();
    }
}
```

Check `RekallAgeMeshChangeKind` has a `None` value before using it — if it does not, use the first declared enum member instead (open `src/Rekall.Age.Modeling.Contracts/RekallAgeMeshContracts.cs` and search for `enum RekallAgeMeshChangeKind`).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "FullyQualifiedName~MeshPluginRegistrationTests"`
Expected: FAIL — compile error, `IRekallAgeMeshOperationPlugin`/`RegisterMeshOperation` do not exist yet.

- [ ] **Step 3: Add the two interfaces**

Append to the end of `src/Rekall.Age.Modeling.Contracts/RekallAgeMeshContracts.cs`:

```csharp
public interface IRekallAgeMeshOperationPlugin
{
    string OperationId { get; }
    RekallAgeMeshOperationDescriptor Descriptor { get; }
    RekallAgeMeshOperationResult Execute(RekallAgeMeshAsset source, RekallAgeMeshOperationRequest request);
}

public interface IRekallAgeFractureAlgorithmPlugin
{
    string AlgorithmId { get; }
    IReadOnlyList<RekallAgeMeshAsset> Fracture(RekallAgeMeshAsset source, int chunkCount, long seed);
}
```

- [ ] **Step 4: Add the project reference**

In `src/Rekall.Age.Modules/Rekall.Age.Modules.csproj`, add a line inside the existing `<ItemGroup>` containing the other `<ProjectReference>` entries:

```xml
    <ProjectReference Include="..\Rekall.Age.Modeling.Contracts\Rekall.Age.Modeling.Contracts.csproj" />
```

- [ ] **Step 5: Add registration methods to `RekallAgeModuleBuilder`**

Replace the full content of `src/Rekall.Age.Modules/RekallAgeModuleBuilder.cs`:

```csharp
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modules;

public sealed class RekallAgeModuleBuilder
{
    private readonly List<Type> _componentTypes = [];
    private readonly List<Type> _runtimeSystemTypes = [];
    private readonly List<Type> _meshOperationTypes = [];
    private readonly List<Type> _fractureAlgorithmTypes = [];

    public IReadOnlyList<Type> ComponentTypes => _componentTypes;

    public IReadOnlyList<Type> RuntimeSystemTypes => _runtimeSystemTypes;

    public IReadOnlyList<Type> MeshOperationTypes => _meshOperationTypes;

    public IReadOnlyList<Type> FractureAlgorithmTypes => _fractureAlgorithmTypes;

    public void RegisterComponent<TComponent>()
        where TComponent : RekallAgeComponent
    {
        var type = typeof(TComponent);
        if (!_componentTypes.Contains(type))
        {
            _componentTypes.Add(type);
        }
    }

    public void RegisterRuntimeSystem<TSystem>()
        where TSystem : IRekallAgeRuntimeModuleSystem
    {
        var type = typeof(TSystem);
        if (!_runtimeSystemTypes.Contains(type))
        {
            _runtimeSystemTypes.Add(type);
        }
    }

    public void RegisterMeshOperation<TOperation>()
        where TOperation : IRekallAgeMeshOperationPlugin
    {
        var type = typeof(TOperation);
        if (!_meshOperationTypes.Contains(type))
        {
            _meshOperationTypes.Add(type);
        }
    }

    public void RegisterFractureAlgorithm<TAlgorithm>()
        where TAlgorithm : IRekallAgeFractureAlgorithmPlugin
    {
        var type = typeof(TAlgorithm);
        if (!_fractureAlgorithmTypes.Contains(type))
        {
            _fractureAlgorithmTypes.Add(type);
        }
    }
}
```

- [ ] **Step 6: Run the test suite to verify it passes**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "FullyQualifiedName~MeshPluginRegistrationTests"`
Expected: PASS, 2/2.

Also run: `dotnet build src/Rekall.Age.Modules/Rekall.Age.Modules.csproj -c Debug` to confirm the new project reference resolves cleanly (no circular-reference error).

- [ ] **Step 7: Commit**

```bash
git add src/Rekall.Age.Modeling.Contracts/RekallAgeMeshContracts.cs src/Rekall.Age.Modules/Rekall.Age.Modules.csproj src/Rekall.Age.Modules/RekallAgeModuleBuilder.cs tests/Rekall.Age.Tests/Modules/MeshPluginRegistrationTests.cs
git commit -m "feat: add mesh operation/fracture plugin contracts and registration"
```

---

### Task 2: `RekallAgeProjectMeshPluginLoader` (discovery)

**Files:**
- Create: `src/Rekall.Age.Modeling/RekallAgeProjectMeshPluginLoader.cs`
- Modify: `src/Rekall.Age.Modeling/Rekall.Age.Modeling.csproj` (add project reference)
- Test: `tests/Rekall.Age.Tests/Modeling/ProjectMeshPluginLoaderTests.cs` (new)

**Interfaces:**
- Consumes: `RekallAgeModule`, `RekallAgeModuleBuilder`, `RekallAgeProjectModuleAssemblyLoader.LoadBuiltModuleAssemblies(string) -> IReadOnlyList<Assembly>` (all `Rekall.Age.Modules`); `IRekallAgeMeshOperationPlugin`, `IRekallAgeFractureAlgorithmPlugin` (Task 1).
- Produces: `RekallAgeProjectMeshPluginLoader.Load(string projectRoot) -> RekallAgeProjectMeshPlugins`; `RekallAgeProjectMeshPlugins(IReadOnlyList<IRekallAgeMeshOperationPlugin> Operations, IReadOnlyList<IRekallAgeFractureAlgorithmPlugin> FractureAlgorithms)`.

This task needs a real, built, on-disk project module to load from. The proven, working pattern for this (confirmed by reading `tests/Rekall.Age.Tests/Runtime/ProjectRuntimeSystemTests.cs`, which builds real scratch project modules for `RekallAgeProjectRuntimeSystemLoader` the same way) is three commands in sequence: `ScaffoldModuleCommand` (creates the module's `.csproj` and a starter `.cs` file), `WriteModuleSourceCommand` (overwrites that `.cs` file with real source), `BuildModulesCommand` (compiles it). No separate SDK-install step is needed in this in-process test path.

- [ ] **Step 1: Write the failing test**

Create `tests/Rekall.Age.Tests/Modeling/ProjectMeshPluginLoaderTests.cs`:

```csharp
using Rekall.Age.Build.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modeling;
using Rekall.Age.Modules.Commands;

namespace Rekall.Age.Tests.Modeling;

public sealed class ProjectMeshPluginLoaderTests
{
    [Fact]
    public async Task LoadDiscoversARegisteredMeshOperationAndFractureAlgorithmFromABuiltProjectModule()
    {
        var root = await BuildScratchModuleProjectAsync("TestMeshPlugins", TestModuleSource);

        var plugins = new RekallAgeProjectMeshPluginLoader().Load(root);

        var operation = Assert.Single(plugins.Operations);
        Assert.Equal("testmeshplugins.fake_operation", operation.OperationId);
        var algorithm = Assert.Single(plugins.FractureAlgorithms);
        Assert.Equal("testmeshplugins.fake_algorithm", algorithm.AlgorithmId);
    }

    [Fact]
    public async Task LoadRejectsAPluginWithABareUndottedId()
    {
        var root = await BuildScratchModuleProjectAsync("TestBadMeshPlugin", BadIdModuleSource);

        var error = Assert.Throws<InvalidOperationException>(() => new RekallAgeProjectMeshPluginLoader().Load(root));
        Assert.Contains("bare_operation", error.Message, StringComparison.Ordinal);
    }

    private static async Task<string> BuildScratchModuleProjectAsync(string moduleId, string moduleSource)
    {
        var root = TestPaths.CreateTempDirectory();
        var context = Context("scaffold");
        var scaffold = await new ScaffoldModuleCommand().ExecuteAsync(
            new ScaffoldModuleRequest(root, moduleId, moduleId, moduleId, "PluginState"),
            context);
        Assert.True(scaffold.Ok, scaffold.Summary);
        var write = await new WriteModuleSourceCommand().ExecuteAsync(
            new WriteModuleSourceRequest(root, moduleId, $"{moduleId}Module.cs", moduleSource),
            context);
        Assert.True(write.Ok, write.Summary);
        var build = await new BuildModulesCommand().ExecuteAsync(new BuildModulesRequest(root), context);
        Assert.True(build.Ok, build.Summary);
        return root;
    }

    private static RekallAgeCommandContext Context(string name) =>
        new("mesh-plugin-loader-tests", RekallAgeTransaction.Begin(name), CancellationToken.None);

    private const string TestModuleSource = """
        using Rekall.Age.Modeling;
        using Rekall.Age.Modeling.Contracts;
        using Rekall.Age.Modules;

        namespace Game.Modules.TestMeshPlugins;

        [RekallAgeModule("TestMeshPlugins", "Test Mesh Plugins")]
        public sealed class TestMeshPluginsModule : RekallAgeModule
        {
            public override void Configure(RekallAgeModuleBuilder builder)
            {
                builder.RegisterMeshOperation<FakeOperation>();
                builder.RegisterFractureAlgorithm<FakeAlgorithm>();
            }
        }

        public sealed class FakeOperation : IRekallAgeMeshOperationPlugin
        {
            public string OperationId => "testmeshplugins.fake_operation";
            public RekallAgeMeshOperationDescriptor Descriptor => new(
                OperationId, "A fake test operation.", RekallAgeGeometryDomain.Face,
                RekallAgeMeshChangeKind.None, []);
            public RekallAgeMeshOperationResult Execute(RekallAgeMeshAsset source, RekallAgeMeshOperationRequest request) =>
                throw new System.NotSupportedException();
        }

        public sealed class FakeAlgorithm : IRekallAgeFractureAlgorithmPlugin
        {
            public string AlgorithmId => "testmeshplugins.fake_algorithm";
            public System.Collections.Generic.IReadOnlyList<RekallAgeMeshAsset> Fracture(RekallAgeMeshAsset source, int chunkCount, long seed) =>
                throw new System.NotSupportedException();
        }
        """;

    private const string BadIdModuleSource = """
        using Rekall.Age.Modeling;
        using Rekall.Age.Modeling.Contracts;
        using Rekall.Age.Modules;

        namespace Game.Modules.TestBadMeshPlugin;

        [RekallAgeModule("TestBadMeshPlugin", "Test Bad Mesh Plugin")]
        public sealed class TestBadMeshPluginModule : RekallAgeModule
        {
            public override void Configure(RekallAgeModuleBuilder builder)
            {
                builder.RegisterMeshOperation<BareIdOperation>();
            }
        }

        public sealed class BareIdOperation : IRekallAgeMeshOperationPlugin
        {
            public string OperationId => "bare_operation";
            public RekallAgeMeshOperationDescriptor Descriptor => new(
                OperationId, "A fake test operation with a bare id.", RekallAgeGeometryDomain.Face,
                RekallAgeMeshChangeKind.None, []);
            public RekallAgeMeshOperationResult Execute(RekallAgeMeshAsset source, RekallAgeMeshOperationRequest request) =>
                throw new System.NotSupportedException();
        }
        """;
}
```

If `ScaffoldModuleCommand`/`WriteModuleSourceCommand`/`BuildModulesCommand` reject any detail above (an unexpected required field, a naming-convention check on `ComponentName` even though this module registers no `RekallAgeComponent`, etc.), read the three command implementations directly (`src/Rekall.Age.Modules/Commands/ScaffoldModuleCommand.cs`, `WriteModuleSourceCommand.cs`, `src/Rekall.Age.Build/Commands/BuildModulesCommand.cs`) and adjust — they are real, already-working commands exercised the same way by `ProjectRuntimeSystemTests.cs`, so any failure here is this plan's guess being wrong, not the commands.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "FullyQualifiedName~ProjectMeshPluginLoaderTests"`
Expected: FAIL — compile error, `RekallAgeProjectMeshPluginLoader` does not exist yet.

- [ ] **Step 3: Add the project reference**

In `src/Rekall.Age.Modeling/Rekall.Age.Modeling.csproj`, add inside the existing `<ItemGroup>`:

```xml
    <ProjectReference Include="..\Rekall.Age.Modules\Rekall.Age.Modules.csproj" />
```

- [ ] **Step 4: Implement the loader**

Create `src/Rekall.Age.Modeling/RekallAgeProjectMeshPluginLoader.cs`:

```csharp
using Rekall.Age.Modeling.Contracts;
using Rekall.Age.Modules;

namespace Rekall.Age.Modeling;

public sealed record RekallAgeProjectMeshPlugins(
    IReadOnlyList<IRekallAgeMeshOperationPlugin> Operations,
    IReadOnlyList<IRekallAgeFractureAlgorithmPlugin> FractureAlgorithms)
{
    public static readonly RekallAgeProjectMeshPlugins Empty = new([], []);
}

public sealed class RekallAgeProjectMeshPluginLoader
{
    public RekallAgeProjectMeshPlugins Load(string projectRoot)
    {
        var operations = new List<IRekallAgeMeshOperationPlugin>();
        var algorithms = new List<IRekallAgeFractureAlgorithmPlugin>();

        foreach (var assembly in RekallAgeProjectModuleAssemblyLoader.LoadBuiltModuleAssemblies(projectRoot))
        {
            foreach (var moduleType in assembly.GetTypes()
                .Where(type => !type.IsAbstract && typeof(RekallAgeModule).IsAssignableFrom(type))
                .OrderBy(type => type.FullName, StringComparer.Ordinal))
            {
                var module = (RekallAgeModule?)Activator.CreateInstance(moduleType, nonPublic: true)
                    ?? throw new InvalidOperationException($"Module '{moduleType.FullName}' could not be created.");
                var builder = new RekallAgeModuleBuilder();
                module.Configure(builder);

                foreach (var operationType in builder.MeshOperationTypes
                    .OrderBy(type => type.FullName, StringComparer.Ordinal))
                {
                    var operation = CreatePlugin<IRekallAgeMeshOperationPlugin>(operationType);
                    RequireDottedId(operation.OperationId, operationType);
                    operations.Add(operation);
                }

                foreach (var algorithmType in builder.FractureAlgorithmTypes
                    .OrderBy(type => type.FullName, StringComparer.Ordinal))
                {
                    var algorithm = CreatePlugin<IRekallAgeFractureAlgorithmPlugin>(algorithmType);
                    RequireDottedId(algorithm.AlgorithmId, algorithmType);
                    algorithms.Add(algorithm);
                }
            }
        }

        return new RekallAgeProjectMeshPlugins(operations, algorithms);
    }

    private static TPlugin CreatePlugin<TPlugin>(Type pluginType)
    {
        if (!typeof(TPlugin).IsAssignableFrom(pluginType))
        {
            throw new InvalidOperationException(
                $"Registered type '{pluginType.FullName}' does not implement {typeof(TPlugin).Name}.");
        }

        return (TPlugin?)Activator.CreateInstance(pluginType, nonPublic: true)
            ?? throw new InvalidOperationException($"Plugin '{pluginType.FullName}' could not be created.");
    }

    private static void RequireDottedId(string id, Type pluginType)
    {
        if (string.IsNullOrWhiteSpace(id) || !id.Contains('.', StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Plugin '{pluginType.FullName}' has id '{id}', which must contain '.' " +
                "(bare ids are reserved for built-in operations/algorithms).");
        }
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "FullyQualifiedName~ProjectMeshPluginLoaderTests"`
Expected: PASS, 2/2.

- [ ] **Step 6: Commit**

```bash
git add src/Rekall.Age.Modeling/Rekall.Age.Modeling.csproj src/Rekall.Age.Modeling/RekallAgeProjectMeshPluginLoader.cs tests/Rekall.Age.Tests/Modeling/ProjectMeshPluginLoaderTests.cs
git commit -m "feat: discover project-registered mesh operation/fracture plugins"
```

---

### Task 3: `RekallAgeMeshOperationExecutor` plugin fallback

**Files:**
- Modify: `src/Rekall.Age.Modeling/RekallAgeMeshOperationExecutor.cs`
- Test: `tests/Rekall.Age.Tests/Modeling/MeshOperationExecutorPluginTests.cs` (new)

**Interfaces:**
- Consumes: `IRekallAgeMeshOperationPlugin` (Task 1).
- Produces: `RekallAgeMeshOperationExecutor(IReadOnlyList<IRekallAgeMeshOperationPlugin>? plugins = null)` constructor overload; `Descriptors` now includes plugin descriptors; `Execute` dispatches to a matching plugin before throwing `REKALL_MESH_OPERATION_UNKNOWN`.

- [ ] **Step 1: Write the failing test**

Create `tests/Rekall.Age.Tests/Modeling/MeshOperationExecutorPluginTests.cs`:

```csharp
using System.Text.Json.Nodes;
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Modeling;

public sealed class MeshOperationExecutorPluginTests
{
    [Fact]
    public void ExecuteDispatchesToARegisteredPluginOperation()
    {
        var plugin = new DoublingPlugin();
        var executor = new RekallAgeMeshOperationExecutor([plugin]);
        var source = Box();
        var request = new RekallAgeMeshOperationRequest(
            plugin.OperationId,
            RekallAgeGeometryDomain.Point,
            source.Topology.PointIds,
            new JsonObject());

        var result = executor.Execute(source, request);

        Assert.True(plugin.WasCalled);
        Assert.Equal(source.Topology.Positions.Count, result.Mesh.Topology.Positions.Count);
    }

    [Fact]
    public void DescriptorsIncludesPluginDescriptorsAlongsideBuiltIns()
    {
        var plugin = new DoublingPlugin();
        var executor = new RekallAgeMeshOperationExecutor([plugin]);

        Assert.Contains(executor.Descriptors, item => item.OperationId == plugin.OperationId);
        Assert.Contains(executor.Descriptors, item => item.OperationId == "transform");
    }

    [Fact]
    public void ExecuteStillThrowsForATrulyUnknownOperationId()
    {
        var executor = new RekallAgeMeshOperationExecutor([new DoublingPlugin()]);
        var source = Box();
        var request = new RekallAgeMeshOperationRequest(
            "test.no_such_operation",
            RekallAgeGeometryDomain.Point,
            source.Topology.PointIds,
            new JsonObject());

        var error = Assert.Throws<RekallAgeMeshOperationException>(() => executor.Execute(source, request));
        Assert.Equal("REKALL_MESH_OPERATION_UNKNOWN", error.Code);
    }

    private static RekallAgeMeshAsset Box() => RekallAgeMeshAsset.Create(
        "box",
        "Box",
        new(
            PointIds: [1, 2, 3, 4, 5, 6, 7, 8],
            Positions:
            [
                new(-0.5, -0.5, -0.5), new(0.5, -0.5, -0.5), new(0.5, 0.5, -0.5), new(-0.5, 0.5, -0.5),
                new(-0.5, -0.5, 0.5), new(0.5, -0.5, 0.5), new(0.5, 0.5, 0.5), new(-0.5, 0.5, 0.5)
            ],
            EdgeIds: Enumerable.Range(0, 12).Select(value => (ulong)(11 + value)).ToArray(),
            EdgePointIndices:
            [
                new(0, 1), new(1, 2), new(2, 3), new(3, 0),
                new(4, 5), new(5, 6), new(6, 7), new(7, 4),
                new(0, 4), new(1, 5), new(2, 6), new(3, 7)
            ],
            FaceIds: [31, 32, 33, 34, 35, 36],
            FaceOffsets: [0, 4, 8, 12, 16, 20, 24],
            CornerIds: Enumerable.Range(0, 24).Select(value => (ulong)(41 + value)).ToArray(),
            CornerPointIndices: [0, 3, 2, 1, 4, 5, 6, 7, 0, 1, 5, 4, 1, 2, 6, 5, 2, 3, 7, 6, 3, 0, 4, 7],
            CornerEdgeIndices: [3, 2, 1, 0, 4, 5, 6, 7, 0, 9, 4, 8, 1, 10, 5, 9, 2, 11, 6, 10, 3, 8, 7, 11]));

    private sealed class DoublingPlugin : IRekallAgeMeshOperationPlugin
    {
        public bool WasCalled { get; private set; }

        public string OperationId => "test.double_positions";

        public RekallAgeMeshOperationDescriptor Descriptor => new(
            OperationId, "Doubles selected point positions (test plugin).",
            RekallAgeGeometryDomain.Point, RekallAgeMeshChangeKind.Positions, []);

        public RekallAgeMeshOperationResult Execute(RekallAgeMeshAsset source, RekallAgeMeshOperationRequest request)
        {
            WasCalled = true;
            var doubled = source.Topology.Positions
                .Select(position => new RekallAgeGeometryVector3(position.X * 2, position.Y * 2, position.Z * 2))
                .ToArray();
            var mesh = source with { Topology = source.Topology with { Positions = doubled }, Revision = source.Revision + 1 };
            var zeroBounds = new RekallAgeMeshBounds(new(0, 0, 0), new(0, 0, 0));
            // Execute()'s caller (RekallAgeMeshOperationExecutor.Execute) re-validates the
            // returned Mesh and overwrites Validation with fresh output before returning to the
            // caller, so the placeholder RekallAgeMeshValidationReport/Summary below never needs
            // to reflect the real mesh -- only its shape needs to be correct C#.
            return new RekallAgeMeshOperationResult(
                mesh, source.Revision, mesh.Revision,
                new RekallAgeMeshChangeSet(
                    RekallAgeMeshChangeKind.Positions,
                    [], [], [], [],
                    [], [], [], [],
                    [], [], [], [],
                    [],
                    zeroBounds),
                [],
                new RekallAgeMeshValidationReport(
                    true,
                    new RekallAgeMeshValidationSummary(0, 0, 0, 0, 0, 0, 0, zeroBounds),
                    []));
        }
    }
}
```

The field shapes above (`RekallAgeMeshChangeSet` has 4 created-id lists, 4 deleted-id lists, 4 modified-id lists, a changed-attributes list, then bounds; `RekallAgeMeshValidationReport` is `(bool IsValid, RekallAgeMeshValidationSummary Summary, IReadOnlyList<RekallAgeMeshDiagnostic> Diagnostics)`; `RekallAgeMeshValidationSummary` is `(int PointCount, EdgeCount, FaceCount, CornerCount, LooseEdgeCount, BoundaryEdgeCount, NonManifoldEdgeCount, RekallAgeMeshBounds Bounds)`; `RekallAgeMeshBounds` is `(RekallAgeGeometryVector3 Min, Max)`) were confirmed directly against `src/Rekall.Age.Modeling.Contracts/RekallAgeMeshContracts.cs` while writing this plan — if that file has since changed, re-check it before typing this test.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "FullyQualifiedName~MeshOperationExecutorPluginTests"`
Expected: FAIL — no constructor overload accepts a plugin list yet.

- [ ] **Step 3: Add the plugin-aware constructor and dispatch fallback**

In `src/Rekall.Age.Modeling/RekallAgeMeshOperationExecutor.cs`:

1. Add a field and constructor right after the existing `private readonly RekallAgeMeshValidator _validator = new();` line:

```csharp
    private readonly IReadOnlyList<IRekallAgeMeshOperationPlugin> _plugins;

    public RekallAgeMeshOperationExecutor(IReadOnlyList<IRekallAgeMeshOperationPlugin>? plugins = null)
    {
        _plugins = plugins ?? [];
    }
```

2. Change the `Descriptors` property from:

```csharp
    public IReadOnlyList<RekallAgeMeshOperationDescriptor> Descriptors => OperationDescriptors;
```

to:

```csharp
    public IReadOnlyList<RekallAgeMeshOperationDescriptor> Descriptors =>
        _plugins.Count == 0
            ? OperationDescriptors
            : [.. OperationDescriptors, .. _plugins.Select(plugin => plugin.Descriptor)];
```

3. Change the final `_ => throw Failure(...)` arm of the `switch` inside `Execute` from:

```csharp
            _ => throw Failure("REKALL_MESH_OPERATION_UNKNOWN", $"Unknown mesh operation '{request.OperationId}'.")
```

to:

```csharp
            _ => ExecutePlugin(source, request)
```

4. Add a new private method right after `Execute` (before the first operation method like `Transform`):

```csharp
    private RekallAgeMeshOperationResult ExecutePlugin(RekallAgeMeshAsset source, RekallAgeMeshOperationRequest request)
    {
        var plugin = _plugins.FirstOrDefault(item => item.OperationId == request.OperationId);
        if (plugin is null)
        {
            throw Failure("REKALL_MESH_OPERATION_UNKNOWN", $"Unknown mesh operation '{request.OperationId}'.");
        }

        return plugin.Execute(source, request);
    }
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "FullyQualifiedName~MeshOperationExecutorPluginTests"`
Expected: PASS, 3/3.

- [ ] **Step 5: Run the full modeling regression suite**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "FullyQualifiedName~Modeling"`
Expected: PASS, no regressions — `ModelingProductionContractMatrixTests` and every other existing modeling test must still pass unchanged, proving every built-in operation id still dispatches exactly as before.

- [ ] **Step 6: Commit**

```bash
git add src/Rekall.Age.Modeling/RekallAgeMeshOperationExecutor.cs tests/Rekall.Age.Tests/Modeling/MeshOperationExecutorPluginTests.cs
git commit -m "feat: dispatch unknown mesh operation ids to registered plugins"
```

---

### Task 4: `RekallAgeMeshFractureExecutor`

**Files:**
- Create: `src/Rekall.Age.Modeling/RekallAgeMeshFractureExecutor.cs`
- Test: `tests/Rekall.Age.Tests/Modeling/MeshFractureExecutorPluginTests.cs` (new)

**Interfaces:**
- Consumes: `IRekallAgeFractureAlgorithmPlugin` (Task 1); the existing static `RekallAgeMeshFracture.Fracture(RekallAgeMeshAsset, int, long) -> IReadOnlyList<RekallAgeMeshAsset>` (untouched).
- Produces: `RekallAgeMeshFractureExecutor.BuiltInVoronoiAlgorithmId` (`const string`, value `"rekall.fracture.voronoi"`); `RekallAgeMeshFractureExecutor(IReadOnlyList<IRekallAgeFractureAlgorithmPlugin>? plugins = null)`; `Fracture(RekallAgeMeshAsset source, int chunkCount, long seed, string? algorithmId = null) -> IReadOnlyList<RekallAgeMeshAsset>`.

- [ ] **Step 1: Write the failing test**

Create `tests/Rekall.Age.Tests/Modeling/MeshFractureExecutorPluginTests.cs`. Reuse the `Primitive` helper and `MeshVolume` helper exactly as they appear in `tests/Rekall.Age.Tests/Modeling/MeshFractureTests.cs` (copy them verbatim into this new file rather than making them shared/public — that file's helpers are private and this keeps the two test files independent):

```csharp
using Rekall.Age.Modeling;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Tests.Modeling;

public sealed class MeshFractureExecutorPluginTests
{
    [Fact]
    public async Task DefaultAlgorithmIdCallsTheExistingBuiltInVoronoiFracture()
    {
        var source = await Primitive("rekall.modeling.primitive.box");
        var executor = new RekallAgeMeshFractureExecutor();

        var direct = RekallAgeMeshFracture.Fracture(source, 4, seed: 7);
        var viaExecutor = executor.Fracture(source, 4, seed: 7);
        var viaExplicitId = executor.Fracture(source, 4, seed: 7, RekallAgeMeshFractureExecutor.BuiltInVoronoiAlgorithmId);

        Assert.Equal(direct.Count, viaExecutor.Count);
        for (var i = 0; i < direct.Count; i++)
        {
            Assert.Equal(MeshVolume(direct[i]), MeshVolume(viaExecutor[i]), precision: 6);
            Assert.Equal(MeshVolume(direct[i]), MeshVolume(viaExplicitId[i]), precision: 6);
        }
    }

    [Fact]
    public async Task RegisteredPluginAlgorithmIsDispatchedById()
    {
        var source = await Primitive("rekall.modeling.primitive.box");
        var plugin = new SingleChunkAlgorithm();
        var executor = new RekallAgeMeshFractureExecutor([plugin]);

        var chunks = executor.Fracture(source, 1, seed: 0, plugin.AlgorithmId);

        Assert.True(plugin.WasCalled);
        Assert.Single(chunks);
    }

    [Fact]
    public void UnknownAlgorithmIdThrows()
    {
        var executor = new RekallAgeMeshFractureExecutor();
        var source = SimpleTriangleMesh();

        Assert.Throws<ArgumentException>(() => executor.Fracture(source, 2, seed: 0, "test.no_such_algorithm"));
    }

    private static RekallAgeMeshAsset SimpleTriangleMesh() => RekallAgeMeshAsset.Create(
        "triangle", "Triangle",
        new(
            PointIds: [1, 2, 3],
            Positions: [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)],
            EdgeIds: [11, 12, 13],
            EdgePointIndices: [new(0, 1), new(1, 2), new(2, 0)],
            FaceIds: [21],
            FaceOffsets: [0, 3],
            CornerIds: [31, 32, 33],
            CornerPointIndices: [0, 1, 2],
            CornerEdgeIndices: [0, 1, 2]));

    private sealed class SingleChunkAlgorithm : IRekallAgeFractureAlgorithmPlugin
    {
        public bool WasCalled { get; private set; }
        public string AlgorithmId => "test.single_chunk";
        public IReadOnlyList<RekallAgeMeshAsset> Fracture(RekallAgeMeshAsset source, int chunkCount, long seed)
        {
            WasCalled = true;
            return [source with { AssetId = $"{source.AssetId}-chunk-0" }];
        }
    }

    private static double MeshVolume(RekallAgeMeshAsset mesh)
    {
        var compiled = new RekallAgeMeshCompiler().Compile(mesh);
        double volume = 0;
        for (var triangle = 0; triangle < compiled.Triangles.Count; triangle++)
        {
            var indices = compiled.Indices.Skip(triangle * 3).Take(3).Select(index => checked((int)index)).ToArray();
            var p0 = compiled.Vertices[indices[0]].Position;
            var p1 = compiled.Vertices[indices[1]].Position;
            var p2 = compiled.Vertices[indices[2]].Position;
            volume += (p0.X * (p1.Y * p2.Z - p2.Y * p1.Z)
                     - p0.Y * (p1.X * p2.Z - p2.X * p1.Z)
                     + p0.Z * (p1.X * p2.Y - p2.X * p1.Y)) / 6.0;
        }
        return Math.Abs(volume);
    }

    private static async ValueTask<RekallAgeMeshAsset> Primitive(string typeId)
    {
        var graph = RekallAgeModelingGraphAsset.Create("source", "Source", [new("source", typeId, 1, new())], [], [new("mesh", "source", "geometry")]);
        var result = await new RekallAgeModelingGraphEvaluator().EvaluateAsync(graph, ["mesh"], RekallAgeModelingEvaluationBudget.Default, new(0, 0, "tests", "desktop"), default);
        return result.Outputs["mesh"];
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "FullyQualifiedName~MeshFractureExecutorPluginTests"`
Expected: FAIL — `RekallAgeMeshFractureExecutor` does not exist yet.

- [ ] **Step 3: Implement the executor**

Create `src/Rekall.Age.Modeling/RekallAgeMeshFractureExecutor.cs`:

```csharp
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public sealed class RekallAgeMeshFractureExecutor
{
    public const string BuiltInVoronoiAlgorithmId = "rekall.fracture.voronoi";

    private readonly IReadOnlyList<IRekallAgeFractureAlgorithmPlugin> _plugins;

    public RekallAgeMeshFractureExecutor(IReadOnlyList<IRekallAgeFractureAlgorithmPlugin>? plugins = null)
    {
        _plugins = plugins ?? [];
    }

    public IReadOnlyList<RekallAgeMeshAsset> Fracture(
        RekallAgeMeshAsset source,
        int chunkCount,
        long seed,
        string? algorithmId = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (algorithmId is null || algorithmId == BuiltInVoronoiAlgorithmId)
        {
            return RekallAgeMeshFracture.Fracture(source, chunkCount, seed);
        }

        var plugin = _plugins.FirstOrDefault(item => item.AlgorithmId == algorithmId)
            ?? throw new ArgumentException(
                $"Unknown fracture algorithm '{algorithmId}'.", nameof(algorithmId));
        return plugin.Fracture(source, chunkCount, seed);
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "FullyQualifiedName~MeshFractureExecutorPluginTests"`
Expected: PASS, 3/3.

- [ ] **Step 5: Run the fracture regression suite**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "FullyQualifiedName~MeshFractureTests"`
Expected: PASS, unchanged — `RekallAgeMeshFracture` itself was not modified.

- [ ] **Step 6: Commit**

```bash
git add src/Rekall.Age.Modeling/RekallAgeMeshFractureExecutor.cs tests/Rekall.Age.Tests/Modeling/MeshFractureExecutorPluginTests.cs
git commit -m "feat: add RekallAgeMeshFractureExecutor wrapping built-in and plugin algorithms"
```

---

### Task 5: Wire CLI/MCP-reachable commands to load project plugins

**Files:**
- Modify: `src/Rekall.Age.Modeling/Commands/MeshAuthoringCommands.cs` (Preview/Apply/Batch/Fracture commands)
- Modify: `src/Rekall.Age.Modeling/Commands/MeshOperationDiscoveryCommands.cs` (Search/Inspect commands + new list command)
- Modify: `src/Rekall.Age.Workflows/RekallAgeDefaultCommandRegistry.cs` (register the new command)
- Test: `tests/Rekall.Age.Tests/Modeling/MeshCommandTests.cs` (extend existing file — this is the file with the "reachable through the registry" test pattern established this session for `FractureMeshCommand`; open it first to match its exact style before adding to it)

**Interfaces:**
- Consumes: `RekallAgeProjectMeshPluginLoader` (Task 2), `RekallAgeMeshOperationExecutor(plugins)` (Task 3), `RekallAgeMeshFractureExecutor` (Task 4).
- Produces: `FractureMeshRequest.AlgorithmId` (new optional `string?` field, default `null`); `SearchMeshOperationTypesRequest.ProjectRoot`/`InspectMeshOperationTypeRequest.ProjectRoot` (new required `string` field — breaking change to those two records, acceptable per the plan's Global Constraints); new command `rekall.mesh.fracture_algorithms.list` (`ListFractureAlgorithmsRequest(string ProjectRoot) -> ListFractureAlgorithmsResult(IReadOnlyList<RekallAgeFractureAlgorithmSummary> Algorithms)`, `RekallAgeFractureAlgorithmSummary(string AlgorithmId, string Description)`).

The existing `FractureCommandReachableThroughTheRegistryPersistsChunkAssets` test in this file
(added earlier this session) uses `RekallAgeDefaultCommandRegistry.Create()` (the real, full
production registry, not a hand-picked subset) and `registry.ExecuteJsonAsync(name,
JsonSerializer.Serialize(new {...}), Context(name))` where `Context(string name)` is a private
helper already at the bottom of this file. Reuse both exactly, and reuse the
`BuildScratchModuleProjectAsync`/module-source-string shape from Task 2's
`ProjectMeshPluginLoaderTests.cs` for building the plugin module (copy that private helper and
the two module-source constants into this file too — test files in this codebase keep their
helpers private/local rather than sharing a common test-utilities class, matching this file's and
`ProjectMeshPluginLoaderTests.cs`'s existing style).

- [ ] **Step 1: Write the failing tests**

Add to `tests/Rekall.Age.Tests/Modeling/MeshCommandTests.cs` (exact insertion point: alongside the existing `FractureCommandReachableThroughTheRegistryPersistsChunkAssets` test), and add the necessary `using Rekall.Age.Build.Commands;`/`using Rekall.Age.Modules.Commands;` to this file's existing `using` block if not already present:

```csharp
[Fact]
public async Task SearchMeshOperationTypesIncludesAProjectRegisteredPluginOperation()
{
    var root = await BuildScratchModuleProjectAsync("TestMeshPlugins", TestMeshOperationPluginModuleSource);
    var registry = RekallAgeDefaultCommandRegistry.Create();

    var result = await registry.ExecuteJsonAsync(
        "rekall.mesh.operation_types.search",
        JsonSerializer.Serialize(new { projectRoot = root, query = "testmeshplugins" }),
        Context("search"));

    Assert.True(result.Ok, result.Summary);
    var found = Assert.IsType<SearchMeshOperationTypesResult>(result.Value);
    Assert.Contains(found.OperationTypes, item => item.OperationId == "testmeshplugins.fake_operation");
}

[Fact]
public async Task FractureMeshDispatchesToARegisteredPluginAlgorithmByAlgorithmId()
{
    var root = await BuildScratchModuleProjectAsync("TestMeshPlugins", TestFractureAlgorithmPluginModuleSource);
    var box = await BoxPrimitive();
    await new CreateMeshAssetCommand().ExecuteAsync(new(root, "crate", "Crate", box.Topology, box.Attributes, box.MaterialSlots), Context("create-source"));
    var registry = RekallAgeDefaultCommandRegistry.Create();

    var result = await registry.ExecuteJsonAsync(
        "rekall.mesh.fracture",
        JsonSerializer.Serialize(new { projectRoot = root, sourceAssetId = "crate", chunkAssetIdPrefix = "crate-chunk", chunkCount = 1, algorithmId = "testmeshplugins.fake_algorithm" }),
        Context("fracture-plugin"));

    Assert.True(result.Ok, result.Summary);
    var fractured = Assert.IsType<FractureMeshResult>(result.Value);
    Assert.Single(fractured.Chunks);
}

private static async Task<string> BuildScratchModuleProjectAsync(string moduleId, string moduleSource)
{
    var root = TestPaths.CreateTempDirectory();
    var context = Context("scaffold");
    var scaffold = await new ScaffoldModuleCommand().ExecuteAsync(
        new ScaffoldModuleRequest(root, moduleId, moduleId, moduleId, "PluginState"),
        context);
    Assert.True(scaffold.Ok, scaffold.Summary);
    var write = await new WriteModuleSourceCommand().ExecuteAsync(
        new WriteModuleSourceRequest(root, moduleId, $"{moduleId}Module.cs", moduleSource),
        context);
    Assert.True(write.Ok, write.Summary);
    var build = await new BuildModulesCommand().ExecuteAsync(new BuildModulesRequest(root), context);
    Assert.True(build.Ok, build.Summary);
    return root;
}

// Registers only the mesh operation plugin (fracture algorithm registration line omitted) --
// same module/namespace shape as ProjectMeshPluginLoaderTests.TestModuleSource in Task 2, with
// FakeAlgorithm/its registration line removed.
private const string TestMeshOperationPluginModuleSource = """
    using Rekall.Age.Modeling;
    using Rekall.Age.Modeling.Contracts;
    using Rekall.Age.Modules;

    namespace Game.Modules.TestMeshPlugins;

    [RekallAgeModule("TestMeshPlugins", "Test Mesh Plugins")]
    public sealed class TestMeshPluginsModule : RekallAgeModule
    {
        public override void Configure(RekallAgeModuleBuilder builder)
        {
            builder.RegisterMeshOperation<FakeOperation>();
        }
    }

    public sealed class FakeOperation : IRekallAgeMeshOperationPlugin
    {
        public string OperationId => "testmeshplugins.fake_operation";
        public RekallAgeMeshOperationDescriptor Descriptor => new(
            OperationId, "A fake test operation.", RekallAgeGeometryDomain.Face,
            RekallAgeMeshChangeKind.None, []);
        public RekallAgeMeshOperationResult Execute(RekallAgeMeshAsset source, RekallAgeMeshOperationRequest request) =>
            throw new System.NotSupportedException();
    }
    """;

// Registers only the fracture algorithm plugin. FakeAlgorithm returns exactly one chunk
// (the source mesh itself, renamed) regardless of chunkCount/seed -- enough to prove dispatch
// without needing real CSG fracture math in a test plugin.
private const string TestFractureAlgorithmPluginModuleSource = """
    using Rekall.Age.Modeling;
    using Rekall.Age.Modeling.Contracts;
    using Rekall.Age.Modules;

    namespace Game.Modules.TestMeshPlugins;

    [RekallAgeModule("TestMeshPlugins", "Test Mesh Plugins")]
    public sealed class TestMeshPluginsModule : RekallAgeModule
    {
        public override void Configure(RekallAgeModuleBuilder builder)
        {
            builder.RegisterFractureAlgorithm<FakeAlgorithm>();
        }
    }

    public sealed class FakeAlgorithm : IRekallAgeFractureAlgorithmPlugin
    {
        public string AlgorithmId => "testmeshplugins.fake_algorithm";
        public System.Collections.Generic.IReadOnlyList<RekallAgeMeshAsset> Fracture(RekallAgeMeshAsset source, int chunkCount, long seed) =>
            [source with { AssetId = source.AssetId + "-single-chunk" }];
    }
    """;
```

`BoxPrimitive()` and `Context(string name)` already exist as private helpers at the bottom of this file (used by `FractureCommandReachableThroughTheRegistryPersistsChunkAssets`) — do not redeclare them.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "FullyQualifiedName~MeshCommandTests"`
Expected: FAIL — `ProjectRoot` field missing on the search request, `AlgorithmId` missing on the fracture request.

- [ ] **Step 3: Update `PreviewMeshOperationCommand`/`ApplyMeshOperationCommand`/`BatchMeshOperationsCommand`**

In `src/Rekall.Age.Modeling/Commands/MeshAuthoringCommands.cs`:

For `PreviewMeshOperationCommand`, replace:

```csharp
public sealed class PreviewMeshOperationCommand : IRekallAgeCommand<PreviewMeshOperationRequest, PreviewMeshOperationResult>
{
    private readonly RekallAgeMeshEditService _service = new();
    public string Name => "rekall.mesh.operation.preview";
```

with:

```csharp
public sealed class PreviewMeshOperationCommand : IRekallAgeCommand<PreviewMeshOperationRequest, PreviewMeshOperationResult>
{
    public string Name => "rekall.mesh.operation.preview";
```

and its `ExecuteAsync` body from:

```csharp
    public async ValueTask<RekallAgeCommandResult<PreviewMeshOperationResult>> ExecuteAsync(
        PreviewMeshOperationRequest request,
        RekallAgeCommandContext context) =>
        await MeshOperationCommandRunner.RunSingleAsync(
            request.ProjectRoot,
            request.AssetId,
            request.ExpectedRevision,
            request.Operation,
            persist: false,
            context,
            _service);
```

to:

```csharp
    public async ValueTask<RekallAgeCommandResult<PreviewMeshOperationResult>> ExecuteAsync(
        PreviewMeshOperationRequest request,
        RekallAgeCommandContext context) =>
        await MeshOperationCommandRunner.RunSingleAsync(
            request.ProjectRoot,
            request.AssetId,
            request.ExpectedRevision,
            request.Operation,
            persist: false,
            context,
            BuildEditService(request.ProjectRoot));
```

Apply the identical pattern (remove the `_service` field, keep the same `ExecuteAsync` body but call `BuildEditService(request.ProjectRoot)` in place of `_service`) to `ApplyMeshOperationCommand` and `BatchMeshOperationsCommand`. `BatchMeshOperationsCommand`'s body calls `_service.ApplyBatchAsync`/`_service.PreviewBatchAsync` — replace each `_service.` with a local variable: add `var service = BuildEditService(request.ProjectRoot);` as the first line inside its `try` block, then use `service.ApplyBatchAsync`/`service.PreviewBatchAsync`.

Add this shared private static helper once, at the bottom of the file (outside any of the three command classes — e.g. as a new `internal static class MeshOperationPluginWiring` right before `MeshCommandEvidence` or another existing file-level helper class):

```csharp
internal static class MeshOperationPluginWiring
{
    public static RekallAgeMeshEditService BuildEditService(string projectRoot)
    {
        var plugins = new RekallAgeProjectMeshPluginLoader().Load(projectRoot);
        return new RekallAgeMeshEditService(executor: new RekallAgeMeshOperationExecutor(plugins.Operations));
    }
}
```

Then change each of the three commands' calls from `BuildEditService(...)` to `MeshOperationPluginWiring.BuildEditService(...)`.

- [ ] **Step 4: Update `FractureMeshCommand`**

In the same file, change `FractureMeshRequest` from:

```csharp
public sealed record FractureMeshRequest(
    string ProjectRoot,
    string SourceAssetId,
    string ChunkAssetIdPrefix,
    int ChunkCount,
    long Seed = 0);
```

to:

```csharp
public sealed record FractureMeshRequest(
    string ProjectRoot,
    string SourceAssetId,
    string ChunkAssetIdPrefix,
    int ChunkCount,
    long Seed = 0,
    string? AlgorithmId = null);
```

Change the fracture call site from:

```csharp
        IReadOnlyList<RekallAgeMeshAsset> chunks;
        try
        {
            chunks = RekallAgeMeshFracture.Fracture(source, request.ChunkCount, request.Seed);
        }
```

to:

```csharp
        IReadOnlyList<RekallAgeMeshAsset> chunks;
        try
        {
            var plugins = new RekallAgeProjectMeshPluginLoader().Load(request.ProjectRoot);
            var fractureExecutor = new RekallAgeMeshFractureExecutor(plugins.FractureAlgorithms);
            chunks = fractureExecutor.Fracture(source, request.ChunkCount, request.Seed, request.AlgorithmId);
        }
```

- [ ] **Step 5: Update discovery commands and add the fracture-algorithm list command**

Replace the full content of `src/Rekall.Age.Modeling/Commands/MeshOperationDiscoveryCommands.cs`:

```csharp
using Rekall.Age.Core.Commands;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling.Commands;

public sealed record SearchMeshOperationTypesRequest(string ProjectRoot, string Query = "", int MaximumResults = 32);
public sealed record SearchMeshOperationTypesResult(IReadOnlyList<RekallAgeMeshOperationDescriptor> OperationTypes, bool Truncated);
public sealed class SearchMeshOperationTypesCommand : IRekallAgeCommand<SearchMeshOperationTypesRequest, SearchMeshOperationTypesResult>
{
    public string Name => "rekall.mesh.operation_types.search";
    public RekallAgeCommandSchema Schema => new(Name, "Searches generic semantic mesh operation contracts (built-in and project-registered) by ID or description, returning domains, possible change masks, and typed parameters; maximumResults must be 1-128.", typeof(SearchMeshOperationTypesRequest).FullName!, typeof(SearchMeshOperationTypesResult).FullName!);
    public ValueTask<RekallAgeCommandResult<SearchMeshOperationTypesResult>> ExecuteAsync(SearchMeshOperationTypesRequest request, RekallAgeCommandContext context)
    {
        if (request.MaximumResults is < 1 or > 128) throw new ArgumentException("maximumResults must be 1-128.");
        var plugins = new RekallAgeProjectMeshPluginLoader().Load(request.ProjectRoot);
        var executor = new RekallAgeMeshOperationExecutor(plugins.Operations);
        var q = request.Query?.Trim() ?? "";
        var matches = executor.Descriptors.Where(item => q.Length == 0 || item.OperationId.Contains(q, StringComparison.OrdinalIgnoreCase) || item.Description.Contains(q, StringComparison.OrdinalIgnoreCase)).OrderBy(item => item.OperationId, StringComparer.Ordinal).ToArray();
        return ValueTask.FromResult(RekallAgeCommandResult<SearchMeshOperationTypesResult>.Success(new(matches.Take(request.MaximumResults).ToArray(), matches.Length > request.MaximumResults), $"Returned {Math.Min(matches.Length, request.MaximumResults)} of {matches.Length} mesh operation type(s)."));
    }
}
public sealed record InspectMeshOperationTypeRequest(string ProjectRoot, string OperationId);
public sealed record InspectMeshOperationTypeResult(RekallAgeMeshOperationDescriptor? OperationType);
public sealed class InspectMeshOperationTypeCommand : IRekallAgeCommand<InspectMeshOperationTypeRequest, InspectMeshOperationTypeResult>
{
    public string Name => "rekall.mesh.operation_types.inspect";
    public RekallAgeCommandSchema Schema => new(Name, "Inspects one exact semantic mesh operation contract (built-in or project-registered) before preview or apply.", typeof(InspectMeshOperationTypeRequest).FullName!, typeof(InspectMeshOperationTypeResult).FullName!);
    public ValueTask<RekallAgeCommandResult<InspectMeshOperationTypeResult>> ExecuteAsync(InspectMeshOperationTypeRequest request, RekallAgeCommandContext context)
    {
        var plugins = new RekallAgeProjectMeshPluginLoader().Load(request.ProjectRoot);
        var executor = new RekallAgeMeshOperationExecutor(plugins.Operations);
        var found = executor.Descriptors.SingleOrDefault(item => item.OperationId == request.OperationId);
        return ValueTask.FromResult(found is null ? RekallAgeCommandResult<InspectMeshOperationTypeResult>.Failure(new(null), "Mesh operation type was not found.", [new("REKALL_MESH_OPERATION_TYPE_NOT_FOUND", "Mesh operation type was not found.", request.OperationId)]) : RekallAgeCommandResult<InspectMeshOperationTypeResult>.Success(new(found), $"Inspected mesh operation '{found.OperationId}'."));
    }
}

public sealed record RekallAgeFractureAlgorithmSummary(string AlgorithmId, string Description);
public sealed record ListFractureAlgorithmsRequest(string ProjectRoot);
public sealed record ListFractureAlgorithmsResult(IReadOnlyList<RekallAgeFractureAlgorithmSummary> Algorithms);
public sealed class ListFractureAlgorithmsCommand : IRekallAgeCommand<ListFractureAlgorithmsRequest, ListFractureAlgorithmsResult>
{
    public string Name => "rekall.mesh.fracture_algorithms.list";
    public RekallAgeCommandSchema Schema => new(Name, "Lists the built-in Voronoi-style fracture algorithm and any project-registered fracture algorithm plugins, for use as rekall.mesh.fracture's algorithmId.", typeof(ListFractureAlgorithmsRequest).FullName!, typeof(ListFractureAlgorithmsResult).FullName!);
    public ValueTask<RekallAgeCommandResult<ListFractureAlgorithmsResult>> ExecuteAsync(ListFractureAlgorithmsRequest request, RekallAgeCommandContext context)
    {
        var plugins = new RekallAgeProjectMeshPluginLoader().Load(request.ProjectRoot);
        var algorithms = new List<RekallAgeFractureAlgorithmSummary>
        {
            new(RekallAgeMeshFractureExecutor.BuiltInVoronoiAlgorithmId, "Built-in Voronoi-style CSG fracture (splits a closed manifold mesh into N chunks around random seed points).")
        };
        algorithms.AddRange(plugins.FractureAlgorithms
            .OrderBy(item => item.AlgorithmId, StringComparer.Ordinal)
            .Select(item => new RekallAgeFractureAlgorithmSummary(item.AlgorithmId, item.GetType().FullName ?? item.AlgorithmId)));
        return ValueTask.FromResult(RekallAgeCommandResult<ListFractureAlgorithmsResult>.Success(
            new(algorithms),
            $"Listed {algorithms.Count} fracture algorithm(s)."));
    }
}
```

- [ ] **Step 6: Register the new command**

In `src/Rekall.Age.Workflows/RekallAgeDefaultCommandRegistry.cs`, right after the existing line `registry.Register(new InspectMeshOperationTypeCommand());`, add:

```csharp
        registry.Register(new ListFractureAlgorithmsCommand());
```

- [ ] **Step 7: Update every existing call site of the two changed request records**

Search the whole repo for `new SearchMeshOperationTypesRequest(` and `new InspectMeshOperationTypeRequest(` (both `src/` and `tests/`) and add a `projectRoot` argument as the new first positional argument at every call site found. Do the same for any existing test that constructs `FractureMeshRequest` positionally and would now bind a later positional argument to the new `AlgorithmId` slot by mistake — `AlgorithmId` was added last with a default, so existing positional call sites of `FractureMeshRequest` are unaffected, but double check with a repo-wide search for `new FractureMeshRequest(` to be sure.

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "FullyQualifiedName~MeshCommandTests|FullyQualifiedName~Modeling|FullyQualifiedName~Mcp"`
Expected: PASS — includes the two new tests from Step 2, plus proof that MCP's tool catalog (which reflects the command registry) still builds cleanly with the new command and changed request shapes.

- [ ] **Step 9: Commit**

```bash
git add src/Rekall.Age.Modeling/Commands/MeshAuthoringCommands.cs src/Rekall.Age.Modeling/Commands/MeshOperationDiscoveryCommands.cs src/Rekall.Age.Workflows/RekallAgeDefaultCommandRegistry.cs tests/Rekall.Age.Tests/Modeling/MeshCommandTests.cs
git commit -m "feat: wire mesh operation/fracture commands to project plugins"
```

---

### Task 6: Studio integration

**Files:**
- Modify: `src/Rekall.Age.Studio/RekallAgeStudioModelingSession.cs`
- Test: `tests/Rekall.Age.Studio.Tests/` — find the existing test file that already covers `RekallAgeStudioModelingSession` (search `grep -rl RekallAgeStudioModelingSession tests/Rekall.Age.Studio.Tests`) and add to it; do not create a new file if one already covers this session type.

**Interfaces:**
- Consumes: `RekallAgeProjectMeshPluginLoader` (Task 2).
- Produces: no new public API — `RekallAgeStudioModelingSession.AvailableOperations` now includes plugin operations once `OpenAsync` has been called for a project that has any registered.

The target file is `tests/Rekall.Age.Studio.Tests/StudioModelingSessionTests.cs`. It builds its
own scratch project directories with plain `Path.Combine(Path.GetTempPath(), "rekall-age-studio-modeling-" + Guid.NewGuid().ToString("N"))`
plus manual `try`/`finally` cleanup (not `TestPaths.CreateTempDirectory()` — that is
`Rekall.Age.Tests`'s convention, this project uses its own), and has a private `Quad()` mesh
helper already at the bottom of the file. `Rekall.Age.Studio.Tests` only lists direct project
references to `Rekall.Age.Studio` and `Rekall.Age.World`, but `Rekall.Age.Studio` itself
references `Rekall.Age.Workflows`, which references `Rekall.Age.Modules`/`Rekall.Age.Build` — so
`ScaffoldModuleCommand`/`WriteModuleSourceCommand`/`BuildModulesCommand` are already resolvable
here transitively (the same way this file already uses `Rekall.Age.Modeling`/`.Contracts` types
with no direct reference of its own).

- [ ] **Step 1: Write the failing test**

Add to `tests/Rekall.Age.Studio.Tests/StudioModelingSessionTests.cs`, and add
`using Rekall.Age.Build.Commands;`, `using Rekall.Age.Core.Commands;`, and
`using Rekall.Age.Modules.Commands;` to its existing `using` block if not already present:

```csharp
[Fact]
public async Task OpeningAProjectWithARegisteredMeshOperationPluginAddsItToAvailableOperations()
{
    var root = Path.Combine(Path.GetTempPath(), "rekall-age-studio-modeling-plugin-" + Guid.NewGuid().ToString("N"));
    try
    {
        var context = new RekallAgeCommandContext("studio-tests", RekallAgeTransaction.Begin("scaffold"), CancellationToken.None);
        var scaffold = await new ScaffoldModuleCommand().ExecuteAsync(
            new ScaffoldModuleRequest(root, "TestStudioMeshPlugin", "TestStudioMeshPlugin", "TestStudioMeshPlugin", "PluginState"),
            context);
        Assert.True(scaffold.Ok, scaffold.Summary);
        var write = await new WriteModuleSourceCommand().ExecuteAsync(
            new WriteModuleSourceRequest(root, "TestStudioMeshPlugin", "TestStudioMeshPluginModule.cs", TestModuleSource),
            context);
        Assert.True(write.Ok, write.Summary);
        var build = await new BuildModulesCommand().ExecuteAsync(new BuildModulesRequest(root), context);
        Assert.True(build.Ok, build.Summary);

        var store = new RekallAgeMeshAssetStore();
        await store.SaveAsync(root, Quad(), CancellationToken.None);
        var session = new RekallAgeStudioModelingSession();

        await session.OpenAsync(root, "quad", CancellationToken.None);

        Assert.Contains(session.AvailableOperations, item => item.OperationId == "teststudiomeshplugin.fake_operation");
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

// The fake plugin's Descriptor.Domain must be RekallAgeGeometryDomain.Face to match Quad()'s
// default session Domain (RekallAgeStudioModelingSession.Domain defaults to Face), since
// AvailableOperations filters by the session's current Domain.
private const string TestModuleSource = """
    using Rekall.Age.Modeling;
    using Rekall.Age.Modeling.Contracts;
    using Rekall.Age.Modules;

    namespace Game.Modules.TestStudioMeshPlugin;

    [RekallAgeModule("TestStudioMeshPlugin", "Test Studio Mesh Plugin")]
    public sealed class TestStudioMeshPluginModule : RekallAgeModule
    {
        public override void Configure(RekallAgeModuleBuilder builder)
        {
            builder.RegisterMeshOperation<FakeOperation>();
        }
    }

    public sealed class FakeOperation : IRekallAgeMeshOperationPlugin
    {
        public string OperationId => "teststudiomeshplugin.fake_operation";
        public RekallAgeMeshOperationDescriptor Descriptor => new(
            OperationId, "A fake test operation.", RekallAgeGeometryDomain.Face,
            RekallAgeMeshChangeKind.None, []);
        public RekallAgeMeshOperationResult Execute(RekallAgeMeshAsset source, RekallAgeMeshOperationRequest request) =>
            throw new System.NotSupportedException();
    }
    """;
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Rekall.Age.Studio.Tests/Rekall.Age.Studio.Tests.csproj --filter "FullyQualifiedName~<TestClassName>"`
Expected: FAIL — `AvailableOperations` does not yet include the plugin operation.

- [ ] **Step 3: Wire plugin loading into `OpenAsync`**

In `src/Rekall.Age.Studio/RekallAgeStudioModelingSession.cs`, change the field declarations from:

```csharp
    private readonly RekallAgeMeshAssetStore _store;
    private readonly RekallAgeMeshEditService _edits;
    private readonly RekallAgeMeshOperationExecutor _operations;
    private readonly RekallAgeTransactionLogStore _transactions;
    private readonly List<ulong> _selectionHistory = [];
```

to:

```csharp
    private readonly RekallAgeMeshAssetStore _store;
    private readonly bool _usesDefaultOperations;
    private RekallAgeMeshEditService _edits;
    private RekallAgeMeshOperationExecutor _operations;
    private readonly RekallAgeTransactionLogStore _transactions;
    private readonly List<ulong> _selectionHistory = [];
    private string? _pluginsLoadedForProjectRoot;
```

Change the constructor from:

```csharp
    public RekallAgeStudioModelingSession(
        RekallAgeMeshAssetStore? store = null,
        RekallAgeMeshEditService? edits = null,
        RekallAgeMeshOperationExecutor? operations = null,
        RekallAgeTransactionLogStore? transactions = null)
    {
        _store = store ?? new RekallAgeMeshAssetStore();
        _operations = operations ?? new RekallAgeMeshOperationExecutor();
        _edits = edits ?? new RekallAgeMeshEditService(_store, _operations);
        _transactions = transactions ?? new RekallAgeTransactionLogStore();
    }
```

to:

```csharp
    public RekallAgeStudioModelingSession(
        RekallAgeMeshAssetStore? store = null,
        RekallAgeMeshEditService? edits = null,
        RekallAgeMeshOperationExecutor? operations = null,
        RekallAgeTransactionLogStore? transactions = null)
    {
        _store = store ?? new RekallAgeMeshAssetStore();
        _usesDefaultOperations = operations is null && edits is null;
        _operations = operations ?? new RekallAgeMeshOperationExecutor();
        _edits = edits ?? new RekallAgeMeshEditService(_store, _operations);
        _transactions = transactions ?? new RekallAgeTransactionLogStore();
    }
```

Change `OpenAsync` from:

```csharp
    public async ValueTask OpenAsync(string projectRoot, string assetId, CancellationToken cancellationToken)
    {
        var loaded = await _store.LoadVersionedAsync(projectRoot, assetId, cancellationToken).ConfigureAwait(false);
        ProjectRoot = Path.GetFullPath(projectRoot); AssetId = assetId; FileRevision = loaded.Revision; Mesh = loaded.Value;
        Preview = null; _selectionHistory.Clear();
    }
```

to:

```csharp
    public async ValueTask OpenAsync(string projectRoot, string assetId, CancellationToken cancellationToken)
    {
        var fullRoot = Path.GetFullPath(projectRoot);
        if (_usesDefaultOperations && !string.Equals(_pluginsLoadedForProjectRoot, fullRoot, StringComparison.Ordinal))
        {
            var plugins = new RekallAgeProjectMeshPluginLoader().Load(fullRoot);
            _operations = new RekallAgeMeshOperationExecutor(plugins.Operations);
            _edits = new RekallAgeMeshEditService(_store, _operations);
            _pluginsLoadedForProjectRoot = fullRoot;
        }

        var loaded = await _store.LoadVersionedAsync(projectRoot, assetId, cancellationToken).ConfigureAwait(false);
        ProjectRoot = fullRoot; AssetId = assetId; FileRevision = loaded.Revision; Mesh = loaded.Value;
        Preview = null; _selectionHistory.Clear();
    }
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/Rekall.Age.Studio.Tests/Rekall.Age.Studio.Tests.csproj --filter "FullyQualifiedName~<TestClassName>"`
Expected: PASS.

- [ ] **Step 5: Run the full Studio test suite**

Run: `dotnet test tests/Rekall.Age.Studio.Tests/Rekall.Age.Studio.Tests.csproj -c Debug`
Expected: PASS, no regressions — existing tests that pass an explicit `operations`/`edits` override must be unaffected (`_usesDefaultOperations` stays `false` for them, so `OpenAsync` never reassigns their injected executor).

- [ ] **Step 6: Commit**

```bash
git add src/Rekall.Age.Studio/RekallAgeStudioModelingSession.cs tests/Rekall.Age.Studio.Tests/
git commit -m "feat: load project mesh operation plugins when Studio opens a project"
```

---

### Task 7: Full regression pass and checkpoint

**Files:** none (verification only)

**Interfaces:** none

- [ ] **Step 1: Full solution build**

Run: `dotnet build Rekall.AGE.sln -c Release`
Expected: 0 errors, 0 warnings.

- [ ] **Step 2: Full targeted regression**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "FullyQualifiedName~Modeling|FullyQualifiedName~Modules|FullyQualifiedName~Rekall.Age.Tests.Runtime|FullyQualifiedName~Mcp|FullyQualifiedName~Workflows"`
Expected: PASS, 0 failures.

- [ ] **Step 3: Full Studio regression**

Run: `dotnet test tests/Rekall.Age.Studio.Tests/Rekall.Age.Studio.Tests.csproj -c Release`
Expected: PASS, 0 failures.

- [ ] **Step 4: Manual CLI smoke test proving the whole chain end-to-end**

In a scratch directory outside the repo (or a throwaway `Examples/` folder, cleaned up after), use the CLI exactly as an external client would:
1. `rekall-age project create <root> "Plugin Smoke Test" "world,modules"`
2. `rekall-age module scaffold-runtime-system <root> PluginSmoke "Plugin Smoke" PluginSmoke SmokeState PluginSmokeSystem` (or hand-write a minimal module `.cs` in `<root>/Modules/PluginSmoke/` that registers one `IRekallAgeMeshOperationPlugin` via `builder.RegisterMeshOperation<T>()` in `Configure`)
3. `rekall-age module install-sdk <root>` then `rekall-age module build modules <root>` (or the exact verbs found via `grep -n '"module",' src/Rekall.Age.Cli/Program.cs`)
4. `rekall-age command execute rekall.mesh.operation_types.search '{"projectRoot":"<root>","query":"pluginsmoke"}'` and confirm the plugin operation appears in the output.

This step has no automated pass/fail — it's a final human-observable confirmation that an author following this session's established CLI-authoring workflow can register and discover a custom mesh operation with zero engine-source edits.

- [ ] **Step 5: Update PROGRESS.md** (if this repo's convention, established earlier this session, calls for a checkpoint entry — check `docs/production/PROGRESS.md` for the existing entry style and add one following it)

- [ ] **Step 6: Final commit and push**

```bash
git add -A
git commit -m "docs: checkpoint mesh operation/fracture plugin system"
git push origin codex/procedural-destruction
```
