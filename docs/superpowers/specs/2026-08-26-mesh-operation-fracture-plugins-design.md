# Mesh Operation / Fracture Plugin System Design

## Purpose

Rekall AGE currently has real extensibility for gameplay: a project module's
`Configure(RekallAgeModuleBuilder builder)` can register custom
`RekallAgeComponent` types and `IRekallAgeRuntimeModuleSystem` runtime systems,
auto-discovered from the project's built `Modules` folder with no engine-source
changes. Nothing analogous exists for mesh authoring. `RekallAgeMeshOperationExecutor`
is a single hardcoded `switch` over ~30 built-in operation ids, and
`RekallAgeMeshFracture` is one static Voronoi-style algorithm. A project that
wants a new mesh operation ("shatter into glass shards", a custom bevel
variant, a game-specific fracture pattern) currently has no path but editing
engine source directly — which is exactly what this session's procedural
destruction work did three separate times (fracture, crater_stamp, the wall
winding/rotation fixes).

This spec adds the same kind of extensibility the Module SDK already gives
gameplay code, to mesh operations and fracture algorithms specifically. It
deliberately excludes per-frame rendering extension points (a plugin
`Rekall.GrassRenderer`-style renderer): those run in-process inside
`RekallAgeRuntimeRenderFrameBuilder` every frame, and neither the performance
budget nor the trust model of an authoring-time mesh edit applies there. That
is a separate, harder design left for a future pass.

## Global Constraints

- Plugin operation/algorithm ids MUST contain at least one `.` (e.g.
  `craterfieldrules.shatter_glass`); bare/undotted ids remain permanently
  reserved for built-ins, so a project plugin can never collide with a current
  or future built-in id, in either direction.
- Plugin mesh operations and fracture algorithms load **in-process** via the
  existing project-module-assembly loading path (`RekallAgeProjectModuleAssemblyLoader`),
  the same way every gameplay module actually runs in production today.
  `RekallAgeRestrictedModuleHostClient` (the AppContainer-sandboxed worker
  described in `docs/superpowers/specs/2026-08-20-restricted-module-host-design.md`)
  exists and is unit-tested, but is **not wired into any real execution path**
  yet — `RekallAgeProjectRuntimeSystemLoader` still loads project assemblies
  directly, and no production CLI/Player/Studio code references the sandboxed
  client at all. This spec follows that real current practice rather than the
  sandbox's aspirational end state. Wiring the sandbox into gameplay module
  execution and into this plugin mechanism is future work, tracked as one
  follow-up, not duplicated per extension point.
- Every new command/session integration point that already carries a
  `ProjectRoot` (or knows one, like Studio's modeling session once a project is
  open) loads that project's plugins before dispatching. No new project-file
  configuration syntax: presence of a registration in a built project module is
  the only signal, exactly like components and runtime systems today.

---

## Architecture

Three additions, layered directly on existing code:

1. **Contracts** (`Rekall.Age.Modeling`): `IRekallAgeMeshOperationPlugin` and
   `IRekallAgeFractureAlgorithmPlugin`, shaped identically to the methods the
   engine's own built-in operations and fracture algorithm already use.
2. **Registration** (`Rekall.Age.Modules`): two new lists and register methods
   on `RekallAgeModuleBuilder`, mirroring `RegisterComponent`/`RegisterRuntimeSystem`
   exactly. This adds a new `Rekall.Age.Modules` → `Rekall.Age.Modeling`
   project reference (verified acyclic: `Rekall.Age.Modeling` depends only on
   `Rekall.Age.Core` and `Rekall.Age.Modeling.Contracts`, neither of which
   depends on `Rekall.Age.Modules`).
3. **Discovery + dispatch** (`Rekall.Age.Modeling`): a new
   `RekallAgeProjectMeshPluginLoader`, mirroring `RekallAgeProjectRuntimeSystemLoader`'s
   discovery exactly (same `LoadBuiltModuleAssemblies` call, same
   reflection-over-`Configure` pattern), plus extending
   `RekallAgeMeshOperationExecutor` and a new `RekallAgeMeshFractureExecutor`
   to accept plugins and fall back to them.

### Contracts

```csharp
namespace Rekall.Age.Modeling;

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

`RekallAgeMeshOperationDescriptor` and `RekallAgeMeshOperationResult` are the
existing types the built-in operations already return — a plugin author
writes exactly the kind of code already in the engine (this session wrote two
built-in operations and a fracture algorithm this way).

A registration-time guard (in `RekallAgeModuleBuilder.RegisterMeshOperation<T>`/
`RegisterFractureAlgorithm<T>`) throws `ArgumentException` immediately if
`OperationId`/`AlgorithmId` doesn't contain a `.` — construction-time
instantiation isn't required to check this (the id is a property, not
constructor-computed), so the check happens once, in
`RekallAgeProjectMeshPluginLoader`, after instantiating each discovered type.

### Registration

```csharp
public sealed class RekallAgeModuleBuilder
{
    // ...existing _componentTypes / _runtimeSystemTypes...
    private readonly List<Type> _meshOperationTypes = [];
    private readonly List<Type> _fractureAlgorithmTypes = [];

    public IReadOnlyList<Type> MeshOperationTypes => _meshOperationTypes;
    public IReadOnlyList<Type> FractureAlgorithmTypes => _fractureAlgorithmTypes;

    public void RegisterMeshOperation<TOperation>()
        where TOperation : IRekallAgeMeshOperationPlugin { /* same dedupe pattern */ }

    public void RegisterFractureAlgorithm<TAlgorithm>()
        where TAlgorithm : IRekallAgeFractureAlgorithmPlugin { /* same dedupe pattern */ }
}
```

A project module declares these in `Configure`, exactly like components:

```csharp
public override void Configure(RekallAgeModuleBuilder builder)
{
    builder.RegisterMeshOperation<ShatterGlassOperation>();
    builder.RegisterFractureAlgorithm<VoronoiCellsAlgorithm>();
}
```

### Discovery + dispatch

`RekallAgeProjectMeshPluginLoader` (new, in `Rekall.Age.Modeling`):

```csharp
public sealed class RekallAgeProjectMeshPluginLoader
{
    public RekallAgeProjectMeshPlugins Load(string projectRoot);
}

public sealed record RekallAgeProjectMeshPlugins(
    IReadOnlyList<IRekallAgeMeshOperationPlugin> Operations,
    IReadOnlyList<IRekallAgeFractureAlgorithmPlugin> FractureAlgorithms);
```

Implementation mirrors `RekallAgeProjectRuntimeSystemLoader.Load(string)`
exactly: call `RekallAgeProjectModuleAssemblyLoader.LoadBuiltModuleAssemblies(projectRoot)`,
reflect over each assembly's module types for a `Configure` method, invoke it
against a fresh `RekallAgeModuleBuilder`, instantiate every registered type via
`Activator.CreateInstance`, and validate each instance's id contains a `.`
before including it (throwing a clear, actionable exception naming the
offending type and id if not — this is an authoring mistake, not a runtime
condition to recover from).

`RekallAgeMeshOperationExecutor` gains an optional constructor parameter:

```csharp
public RekallAgeMeshOperationExecutor(IReadOnlyList<IRekallAgeMeshOperationPlugin>? plugins = null)
```

`Descriptors` becomes the built-in list concatenated with `plugins.Select(p => p.Descriptor)`.
`Execute`'s `switch` keeps every existing built-in arm unchanged; its `_ =>` arm
changes from an immediate throw to: look up a matching plugin by `OperationId`
first, call its `Execute`, and only throw `REKALL_MESH_OPERATION_UNKNOWN` if no
plugin matches either. Existing behavior for every current built-in id is
byte-for-byte unchanged.

`RekallAgeMeshFractureExecutor` (new instance class, `Rekall.Age.Modeling`):

```csharp
public sealed class RekallAgeMeshFractureExecutor
{
    public const string BuiltInVoronoiAlgorithmId = "rekall.fracture.voronoi";

    public RekallAgeMeshFractureExecutor(IReadOnlyList<IRekallAgeFractureAlgorithmPlugin>? plugins = null);

    public IReadOnlyList<RekallAgeMeshAsset> Fracture(
        RekallAgeMeshAsset source, int chunkCount, long seed, string? algorithmId = null);
}
```

`algorithmId: null` or `BuiltInVoronoiAlgorithmId` calls the existing static
`RekallAgeMeshFracture.Fracture` unchanged; any other id looks up a matching
plugin or throws `REKALL_MESH_FRACTURE_ALGORITHM_UNKNOWN`. The existing static
`RekallAgeMeshFracture` class is untouched — it remains the built-in
implementation the new executor wraps, and existing direct callers/tests of it
keep working exactly as today.

### Command and Studio wiring

- `PreviewMeshOperationCommand`/`ApplyMeshOperationCommand`/`FractureMeshCommand`
  each already carry `ProjectRoot`. Each constructs its executor via
  `new RekallAgeMeshOperationExecutor(new RekallAgeProjectMeshPluginLoader().Load(request.ProjectRoot).Operations)`
  (and the fracture equivalent) instead of the parameterless constructor.
- `FractureMeshRequest` gains an optional `string? AlgorithmId = null` field,
  passed through to `RekallAgeMeshFractureExecutor.Fracture`.
- `SearchMeshOperationTypesRequest`/`InspectMeshOperationTypeRequest` gain a
  required `string ProjectRoot` field so their commands can load that
  project's plugins before searching/inspecting `Descriptors`. This is a
  breaking change to those two request shapes — both are recent additions
  with no external consumers outside this repo's own CLI/MCP/tests, so this is
  a plain signature update, not a versioned migration.
- New `rekall.mesh.fracture_algorithms.list` command (`Rekall.Age.Modeling.Commands`):
  takes `ProjectRoot`, returns built-in + project algorithm ids with a short
  description each (no parameter schema needed — fracture's own parameters,
  `chunkCount`/`seed`, are already fixed and don't vary per algorithm).
- `RekallAgeStudioModelingSession`: when `ProjectRoot` is set (opening a
  project/mesh), reconstruct `_operations` (and hold a
  `RekallAgeMeshFractureExecutor` the same way) with that project's plugins
  loaded via `RekallAgeProjectMeshPluginLoader`. `AvailableOperations` already
  reads dynamically from `_operations.Descriptors`, so the existing operation
  palette/node-graph canvas shows project-registered operations alongside
  built-ins with no further UI change.

## Error handling

- Registering a plugin whose id has no `.`: `RekallAgeProjectMeshPluginLoader`
  throws at load time (an authoring-time mistake — the same posture as an
  invalid module registration today), naming the offending type and id.
- Two plugins (or a plugin and another plugin) registering the same dotted id:
  same treatment as the existing `RegisterComponent`/`RegisterRuntimeSystem`
  dedupe — last registration for the exact same `Type` is a no-op (idempotent
  re-registration), but two *different* types claiming the same `OperationId`/
  `AlgorithmId` string is a load-time `ArgumentException` naming both types.
- Unknown operation/algorithm id at dispatch time (neither built-in nor any
  loaded plugin matches): existing `REKALL_MESH_OPERATION_UNKNOWN` /  new
  `REKALL_MESH_FRACTURE_ALGORITHM_UNKNOWN`, unchanged shape from today's error
  contract.
- A plugin's `Execute`/`Fracture` throwing: propagates as today's operations
  already do (caught and wrapped by the existing command-level try/catch in
  `ApplyMeshOperationCommand`/`FractureMeshCommand`, no new handling needed).

## Testing

- New `tests/Rekall.Age.Tests/Modules/MeshPluginRegistrationTests.cs`:
  `RegisterMeshOperation`/`RegisterFractureAlgorithm` dedupe by type, list is
  populated (mirroring the existing coverage style for
  `RegisterComponent`/`RegisterRuntimeSystem` in `ModuleMetadataTests.cs`).
- New `MeshOperationPluginTests`: a fake plugin operation registered on a
  temp-project module, loaded via `RekallAgeProjectMeshPluginLoader`, dispatched
  through `RekallAgeMeshOperationExecutor.Execute`, alongside proof that every
  existing built-in operation id still dispatches unchanged (a full sweep,
  mirroring `ModelingProductionContractMatrixTests`'s existing lock-step
  coverage).
- New `MeshFractureAlgorithmPluginTests`: a fake plugin fracture algorithm
  dispatched through `RekallAgeMeshFractureExecutor` by id; default (no id)
  still calls the existing static Voronoi algorithm unchanged, proven by
  volume-conservation assertions reused from `MeshFractureTests`.
- Registration-time validation: a fake plugin with a bare (undotted) id throws
  from `RekallAgeProjectMeshPluginLoader.Load`.
- End-to-end CLI test: register a plugin operation in a scratch project's
  module, call `rekall.mesh.operation.apply` through the real command
  registry (matching this session's established "reachable through the
  registry, as CLI/MCP would call it" pattern), assert the plugin ran and its
  output persisted.
- Studio: extend existing `RekallAgeStudioModelingSession` tests to prove
  `AvailableOperations` includes a plugin operation once a project with one is
  opened.

## Out of scope (explicitly deferred)

- Per-frame rendering extension points (custom `Rekall.*Renderer`-style
  components). Different trust/performance envelope; needs its own design.
- Wiring `RekallAgeRestrictedModuleHostClient` into any real execution path
  (gameplay modules or this plugin mechanism). Tracked as a separate future
  integration project.
- A project-file opt-in/allow-list for which plugins are active. Presence of a
  registration in a built module is the only signal, matching every other
  extension point today.
