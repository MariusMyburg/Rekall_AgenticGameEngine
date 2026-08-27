# Collision Layers & Masks Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let authored entities selectively ignore each other's collisions/overlaps via a new generic `Rekall.CollisionFilter` component, respected uniformly by physical response, `collision.*` events, and `trigger.*` events, for both 2D and 3D.

**Architecture:** A new declarative built-in component (`RekallAgeCollisionFilterComponent`) carries a `Layer` string and an optional `CollidesWith` string array. A new pure static helper (`RekallAgeCollisionFilter`) reads that component off any two entities and applies one symmetric-AND matching rule. Three existing, independent runtime systems (`RekallAgeBepuPhysicsSystem`, `RekallAgeCollisionEventSystem`, `RekallAgeTriggerEventSystem`) each call that one helper at their own existing pair-detection point.

**Tech Stack:** C# 13, .NET 10, BEPU Physics v2 (`BepuPhysics.CollisionDetection.INarrowPhaseCallbacks`), AGE runtime world/entity contracts, xUnit.

**Spec:** `docs/superpowers/specs/2026-08-27-collision-layers-design.md`

## Global Constraints

- No behavior change for any entity without a `Rekall.CollisionFilter` component, or with one whose `CollidesWith` is null/absent — it must keep colliding with everything, exactly as today.
- One matching rule, defined once, consumed by all three integration points — never duplicated inline.
- Applies uniformly to 2D and 3D colliders/triggers.
- `layer`/`collidesWith` entries are free-form authored strings; no enum, no validation rejection for an unrecognized name.
- Per the repo's testing policy (`AGENTS.md`), every step below runs only its own narrowly targeted test filter — never the full solution suite.

---

## File Structure

- `src/Rekall.Age.Modules/BuiltIns/RekallAgeBuiltInModule.cs` — add the new `RekallAgeCollisionFilterComponent` class + its `RegisterComponent<T>()` call, following the exact pattern of the adjacent `RekallAgeTriggerComponent`.
- `src/Rekall.Age.World/RekallAgeBuiltInComponentTypeCatalog.cs` — add `"Rekall.CollisionFilter"` to the hand-maintained `Types` set (a separate gate from the reflection-based schema above; both must be updated).
- `src/Rekall.Age.Runtime/RekallAgeCollisionFilter.cs` (new) — the one shared, pure, unit-tested matching rule.
- `src/Rekall.Age.Runtime/RekallAgeBepuPhysicsSystem.cs` — add a `CollidableProperty<RekallAgeCollisionFilter.Rule> _filters`, allocated in `AddDynamic`/`AddStatic` next to the existing `_materials.Allocate(...)` lines, and consumed in `AllowContactGeneration`.
- `src/Rekall.Age.Runtime/RekallAgeCollisionEventSystem.cs` — guard its existing pairwise `Overlaps` check with the shared rule.
- `src/Rekall.Age.Runtime/RekallAgeTriggerEventSystem.cs` — guard its existing pairwise overlap check with the shared rule.
- `tests/Rekall.Age.Tests/Runtime/CollisionFilterTests.cs` (new) — unit tests for the pure matching rule.
- `tests/Rekall.Age.Tests/Runtime/SceneRuntimeFoundationTests.cs` — add a BEPU physical-response regression.
- `tests/Rekall.Age.Tests/Runtime/RuntimeCollisionEventSystemTests.cs` — add a `collision.begin` suppression regression.
- `tests/Rekall.Age.Tests/Runtime/RuntimeTriggerEventSystemTests.cs` — add a `trigger.enter` suppression regression.
- `tests/Rekall.Age.Tests/Modules/ModuleMetadataTests.cs` — add a discoverability regression (schema search surfaces the new component).

---

### Task 1: `Rekall.CollisionFilter` component and the shared matching rule

**Files:**
- Modify: `src/Rekall.Age.Modules/BuiltIns/RekallAgeBuiltInModule.cs`
- Modify: `src/Rekall.Age.World/RekallAgeBuiltInComponentTypeCatalog.cs`
- Create: `src/Rekall.Age.Runtime/RekallAgeCollisionFilter.cs`
- Test: `tests/Rekall.Age.Tests/Runtime/CollisionFilterTests.cs`
- Test: `tests/Rekall.Age.Tests/Modules/ModuleMetadataTests.cs`

**Interfaces:**
- Consumes: `RekallAgeRuntimeEntity`, `RekallAgeRuntimeComponent(string Type, JsonObject Properties)` (`src/Rekall.Age.Runtime.Abstractions/RekallAgeRuntimeContracts.cs`); the `entity.FindComponent(string)` extension (`Rekall.Age.Modules`, in `src/Rekall.Age.Modules/RekallAgeRuntimeModuleSdk.cs`).
- Produces: `RekallAgeCollisionFilter.Allows(RekallAgeRuntimeEntity a, RekallAgeRuntimeEntity b) -> bool` and `RekallAgeCollisionFilter.Rule.From(RekallAgeRuntimeEntity entity) -> RekallAgeCollisionFilter.Rule` — both are what Tasks 2-4 call.

- [ ] **Step 1: Write the failing unit tests for the matching rule**

Create `tests/Rekall.Age.Tests/Runtime/CollisionFilterTests.cs`:

```csharp
using System.Text.Json.Nodes;
using Rekall.Age.Runtime;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Tests.Runtime;

public sealed class CollisionFilterTests
{
    [Fact]
    public void EntitiesWithNoFilterComponentAlwaysCollide()
    {
        Assert.True(RekallAgeCollisionFilter.Allows(Entity("a", null), Entity("b", null)));
    }

    [Fact]
    public void AnAbsentCollidesWithListMeansCollidesWithEverything()
    {
        var withFilter = Entity("a", new JsonObject { ["layer"] = "player" });
        var noFilter = Entity("b", null);
        Assert.True(RekallAgeCollisionFilter.Allows(withFilter, noFilter));
    }

    [Fact]
    public void BothSidesMustAcceptEachOthersLayerSymmetrically()
    {
        var accepts = Entity("a", new JsonObject
        {
            ["layer"] = "player",
            ["collidesWith"] = new JsonArray("enemy")
        });
        var rejects = Entity("b", new JsonObject
        {
            ["layer"] = "enemy",
            ["collidesWith"] = new JsonArray("terrain")
        });

        Assert.False(RekallAgeCollisionFilter.Allows(accepts, rejects));
    }

    [Fact]
    public void MatchingLayersOnBothSidesCollide()
    {
        var a = Entity("a", new JsonObject
        {
            ["layer"] = "player",
            ["collidesWith"] = new JsonArray("enemy")
        });
        var b = Entity("b", new JsonObject
        {
            ["layer"] = "enemy",
            ["collidesWith"] = new JsonArray("player")
        });

        Assert.True(RekallAgeCollisionFilter.Allows(a, b));
    }

    [Fact]
    public void EmptyCollidesWithArrayMeansCollidesWithEverything()
    {
        var a = Entity("a", new JsonObject
        {
            ["layer"] = "player",
            ["collidesWith"] = new JsonArray()
        });
        var b = Entity("b", new JsonObject { ["layer"] = "terrain" });

        Assert.True(RekallAgeCollisionFilter.Allows(a, b));
    }

    private static RekallAgeRuntimeEntity Entity(string id, JsonObject? filterProperties)
    {
        var components = filterProperties is null
            ? Array.Empty<RekallAgeRuntimeComponent>()
            : [new RekallAgeRuntimeComponent("Rekall.CollisionFilter", filterProperties)];
        return new RekallAgeRuntimeEntity(
            id,
            id,
            [],
            null,
            null,
            true,
            false,
            RekallAgeRuntimeTransform.Identity,
            components);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "FullyQualifiedName~CollisionFilterTests"`
Expected: FAIL — compile error, `RekallAgeCollisionFilter` does not exist.

- [ ] **Step 3: Implement the shared matching rule**

Create `src/Rekall.Age.Runtime/RekallAgeCollisionFilter.cs`:

```csharp
using System.Globalization;
using System.Text.Json.Nodes;
using Rekall.Age.Modules;
using Rekall.Age.Runtime.Abstractions;

namespace Rekall.Age.Runtime;

/// <summary>
/// The one shared collision-layer matching rule, consumed identically by
/// <see cref="RekallAgeBepuPhysicsSystem"/>, <see cref="RekallAgeCollisionEventSystem"/>,
/// and <see cref="RekallAgeTriggerEventSystem"/> so physical response and event facts can
/// never drift from each other.
/// </summary>
public static class RekallAgeCollisionFilter
{
    private const string ComponentType = "Rekall.CollisionFilter";
    private const string DefaultLayer = "default";

    public static bool Allows(RekallAgeRuntimeEntity a, RekallAgeRuntimeEntity b)
    {
        var left = Rule.From(a);
        var right = Rule.From(b);
        return left.Accepts(right.Layer) && right.Accepts(left.Layer);
    }

    public readonly record struct Rule(string Layer, IReadOnlySet<string>? CollidesWith)
    {
        public static Rule From(RekallAgeRuntimeEntity entity)
        {
            var component = entity.FindComponent(ComponentType);
            if (component is null)
            {
                return new Rule(DefaultLayer, null);
            }

            var layer = ReadString(component.Properties, "layer") is { Length: > 0 } value
                ? value
                : DefaultLayer;
            var collidesWith = ReadStringArray(component.Properties, "collidesWith");
            return new Rule(layer, collidesWith is { Count: > 0 } ? collidesWith : null);
        }

        public bool Accepts(string otherLayer) =>
            CollidesWith is null || CollidesWith.Contains(otherLayer);
    }

    private static string? ReadString(JsonObject properties, string name)
    {
        return TryGetPropertyValue(properties, name, out var node)
            && node is JsonValue value
            && value.TryGetValue<string>(out var text)
            ? text
            : null;
    }

    private static IReadOnlySet<string>? ReadStringArray(JsonObject properties, string name)
    {
        if (!TryGetPropertyValue(properties, name, out var node) || node is not JsonArray array)
        {
            return null;
        }

        return array
            .OfType<JsonValue>()
            .Select(value => value.TryGetValue<string>(out var text) ? text : null)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text!)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool TryGetPropertyValue(JsonObject properties, string name, out JsonNode? node)
    {
        if (properties.TryGetPropertyValue(name, out node))
        {
            return true;
        }

        var pascalName = char.ToUpperInvariant(name[0]) + name[1..];
        if (properties.TryGetPropertyValue(pascalName, out node))
        {
            return true;
        }

        var match = properties.FirstOrDefault(property =>
            property.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
        node = match.Value;
        return !string.IsNullOrEmpty(match.Key);
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "FullyQualifiedName~CollisionFilterTests"`
Expected: PASS, 5/5.

- [ ] **Step 5: Register the built-in component**

In `src/Rekall.Age.Modules/BuiltIns/RekallAgeBuiltInModule.cs`, add the registration call in `Configure` immediately after the existing `builder.RegisterComponent<RekallAgeTriggerComponent>();` line, and add the class immediately after the existing `RekallAgeTriggerComponent` class definition:

```csharp
[RekallAgeComponent("Collision Filter", Description = "Restricts which collidables this entity's collider/trigger physically interacts with and generates collision/trigger events against. An entity with no Rekall.CollisionFilter, or an empty/absent collidesWith, interacts with every layer (default, zero-authoring-change behavior).")]
public sealed class RekallAgeCollisionFilterComponent : RekallAgeComponent
{
    [RekallAgeProperty(Description = "The layer name this entity's collidable belongs to.")]
    public string Layer { get; init; } = "default";

    [RekallAgeProperty(Description = "Native JSON array of layer names this entity's collidable is allowed to interact with. Pass a native array, never an encoded string. Absent/empty means it interacts with every layer.")]
    public string[]? CollidesWith { get; init; }
}
```

- [ ] **Step 6: Add the component to the reserved-type catalog**

In `src/Rekall.Age.World/RekallAgeBuiltInComponentTypeCatalog.cs`, add `"Rekall.CollisionFilter",` to the `Types` set, immediately after the existing `"Rekall.Trigger",` line.

- [ ] **Step 7: Write the failing discoverability test**

In `tests/Rekall.Age.Tests/Modules/ModuleMetadataTests.cs`, add this test immediately after the existing `PhysicsConceptSearchReturnsComposable2DAnd3DContractFamilies` test:

```csharp
[Fact]
public async Task CollisionFilterIsDiscoverableAsAPhysicsConcept()
{
    var command = new SearchComponentSchemasCommand(typeof(RekallAgeBuiltInModule).Assembly);
    var context = new RekallAgeCommandContext(
        "agent",
        RekallAgeTransaction.Begin("search collision filter"),
        CancellationToken.None);

    var result = await command.ExecuteAsync(
        new SearchComponentSchemasRequest("collision layer filter mask", Limit: 24),
        context);

    Assert.True(result.Ok, result.Summary);
    var types = result.Value.Components.Select(component => component.TypeName).ToHashSet(StringComparer.Ordinal);
    Assert.Contains("Rekall.CollisionFilter", types);
}
```

- [ ] **Step 8: Run all Task 1 tests**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "FullyQualifiedName~CollisionFilterTests|FullyQualifiedName~CollisionFilterIsDiscoverableAsAPhysicsConcept"`
Expected: PASS, 6/6.

- [ ] **Step 9: Commit**

```bash
git add src/Rekall.Age.Modules/BuiltIns/RekallAgeBuiltInModule.cs src/Rekall.Age.World/RekallAgeBuiltInComponentTypeCatalog.cs src/Rekall.Age.Runtime/RekallAgeCollisionFilter.cs tests/Rekall.Age.Tests/Runtime/CollisionFilterTests.cs tests/Rekall.Age.Tests/Modules/ModuleMetadataTests.cs
git commit -m "feat: add the Rekall.CollisionFilter component and shared matching rule"
```

---

### Task 2: Filter physical collision response (BEPU)

**Files:**
- Modify: `src/Rekall.Age.Runtime/RekallAgeBepuPhysicsSystem.cs`
- Test: `tests/Rekall.Age.Tests/Runtime/SceneRuntimeFoundationTests.cs`

**Interfaces:**
- Consumes: `RekallAgeCollisionFilter.Rule.From(RekallAgeRuntimeEntity) -> RekallAgeCollisionFilter.Rule` and `Rule.Accepts(string) -> bool` from Task 1.
- Produces: no new public API — `AllowContactGeneration` now returns `false` for a non-accepting pair instead of always `true`.

- [ ] **Step 1: Write the failing physical-response regression**

In `tests/Rekall.Age.Tests/Runtime/SceneRuntimeFoundationTests.cs`, add this test immediately after `BepuPhysicsBodiesCollideWithStaticBoxColliders`:

```csharp
[Fact]
public async Task BepuPhysicsIgnoresCollisionsBetweenNonAcceptingLayers()
{
    var scene = RekallAgeSceneDocument.Create("Main", ["world", "physics3d"])
        .AddEntity(RekallAgeEntityDocument.Create("Ground", ["level"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Transform3D",
                new JsonObject { ["x"] = 0, ["y"] = -0.5, ["z"] = 0 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.BoxCollider3D",
                new JsonObject { ["width"] = 20, ["height"] = 1, ["depth"] = 20 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.CollisionFilter",
                new JsonObject { ["layer"] = "terrain" })))
        .AddEntity(RekallAgeEntityDocument.Create("Falling Box", ["actor"])
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Transform3D",
                new JsonObject { ["x"] = 0, ["y"] = 3, ["z"] = 0 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.Rigidbody3D",
                new JsonObject { ["mass"] = 1 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.BoxCollider3D",
                new JsonObject { ["width"] = 1, ["height"] = 1, ["depth"] = 1 }))
            .AddComponent(RekallAgeComponentDocument.Create(
                "Rekall.CollisionFilter",
                new JsonObject
                {
                    ["layer"] = "ghost",
                    ["collidesWith"] = new JsonArray("nothing")
                })));
    var initial = new RekallAgeRuntimeWorldBuilder().Build(scene);

    var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
        .RunAsync(initial, frames: 180, CancellationToken.None);

    var body = result.World.Entities.Single(entity => entity.Name == "Falling Box");
    Assert.True(body.Transform.Position3D.Y < -5, $"Expected the box to fall through the non-accepting ground, actual Y={body.Transform.Position3D.Y}.");
}
```

Note: `collidesWith` must be a non-empty array containing a genuinely non-matching layer name (`"nothing"`), not an empty array — an empty `JsonArray` parses to zero elements, and Task 1's `Rule.From` treats zero elements the same as absent (`null`, meaning "collides with everything"), so an empty array would not exercise exclusion at all.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "FullyQualifiedName~BepuPhysicsIgnoresCollisionsBetweenNonAcceptingLayers"`
Expected: FAIL — the box currently still lands on the ground (`Position3D.Y` around 0.5, not below -5), because no filtering exists yet.

- [ ] **Step 3: Add the filter property and allocate it per body**

In `src/Rekall.Age.Runtime/RekallAgeBepuPhysicsSystem.cs`, add a new field next to the existing `private readonly CollidableProperty<PhysicsMaterial> _materials;` (around line 826):

```csharp
private readonly CollidableProperty<RekallAgeCollisionFilter.Rule> _filters;
```

Initialize it next to the existing `_materials = new CollidableProperty<PhysicsMaterial>(_pool);` (around line 835):

```csharp
_filters = new CollidableProperty<RekallAgeCollisionFilter.Rule>(_pool);
```

Pass it into the narrow-phase callbacks alongside `_materials` where `RekallAgeBepuNarrowPhaseCallbacks` is constructed (around line 838, currently `new RekallAgeBepuNarrowPhaseCallbacks(_materials),`):

```csharp
new RekallAgeBepuNarrowPhaseCallbacks(_materials, _filters),
```

Also call `_filters.Initialize(simulation);` next to the existing `materials.Initialize(simulation);` inside `RekallAgeBepuNarrowPhaseCallbacks.Initialize` (see Step 4 below — the constructor signature changes there too).

In `AddDynamic` (around line 957), add a line immediately after the existing `_materials.Allocate(handle) = item.Material;`:

```csharp
_filters.Allocate(handle) = RekallAgeCollisionFilter.Rule.From(item.Entity);
```

In `AddStatic` (around line 976), add the same line immediately after its own existing `_materials.Allocate(handle) = item.Material;`.

- [ ] **Step 4: Filter contact generation**

Change the `RekallAgeBepuNarrowPhaseCallbacks` struct declaration from:

```csharp
private struct RekallAgeBepuNarrowPhaseCallbacks(
    CollidableProperty<PhysicsMaterial> materials) : INarrowPhaseCallbacks
{
    private Simulation? _simulation;

    public void Initialize(Simulation simulation)
    {
        _simulation = simulation;
        materials.Initialize(simulation);
    }

    public bool AllowContactGeneration(
        int workerIndex,
        CollidableReference a,
        CollidableReference b,
        ref float speculativeMargin)
    {
        return true;
    }
```

to:

```csharp
private struct RekallAgeBepuNarrowPhaseCallbacks(
    CollidableProperty<PhysicsMaterial> materials,
    CollidableProperty<RekallAgeCollisionFilter.Rule> filters) : INarrowPhaseCallbacks
{
    private Simulation? _simulation;

    public void Initialize(Simulation simulation)
    {
        _simulation = simulation;
        materials.Initialize(simulation);
        filters.Initialize(simulation);
    }

    public bool AllowContactGeneration(
        int workerIndex,
        CollidableReference a,
        CollidableReference b,
        ref float speculativeMargin)
    {
        var left = filters[a];
        var right = filters[b];
        return left.Accepts(right.Layer) && right.Accepts(left.Layer);
    }
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "FullyQualifiedName~BepuPhysicsIgnoresCollisionsBetweenNonAcceptingLayers"`
Expected: PASS.

- [ ] **Step 6: Run the existing physics regression to confirm zero behavior change for unfiltered entities**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "FullyQualifiedName~SceneRuntimeFoundationTests"`
Expected: PASS, same count as before this task (no regressions — every existing physics test authored zero `Rekall.CollisionFilter` components, so `Rule.From` returns the default "collides with everything" rule for all of them).

- [ ] **Step 7: Commit**

```bash
git add src/Rekall.Age.Runtime/RekallAgeBepuPhysicsSystem.cs tests/Rekall.Age.Tests/Runtime/SceneRuntimeFoundationTests.cs
git commit -m "feat: filter BEPU contact generation by collision layer"
```

---

### Task 3: Filter `collision.*` events

**Files:**
- Modify: `src/Rekall.Age.Runtime/RekallAgeCollisionEventSystem.cs`
- Test: `tests/Rekall.Age.Tests/Runtime/RuntimeCollisionEventSystemTests.cs`

**Interfaces:**
- Consumes: `RekallAgeCollisionFilter.Allows(RekallAgeRuntimeEntity, RekallAgeRuntimeEntity) -> bool` from Task 1.
- Produces: no new public API — `collision.begin/stay/end` are no longer emitted for a non-accepting pair.

- [ ] **Step 1: Write the failing event-suppression regression**

In `tests/Rekall.Age.Tests/Runtime/RuntimeCollisionEventSystemTests.cs`, add this test immediately after `CollisionSystemEmitsBeginForNewOverlaps`:

```csharp
[Fact]
public async Task CollisionSystemDoesNotEmitBeginForNonAcceptingLayers()
{
    var world = CreateWorld(
        CreateSphereWithFilter(
            "actor-a",
            "Actor A",
            x: 0,
            layer: "player",
            collidesWith: ["terrain"],
            [
                new JsonObject { ["event"] = "collision.begin", ["handler"] = "touchStarted" }
            ]),
        CreateSphereWithFilter(
            "actor-b",
            "Actor B",
            x: 0.75,
            layer: "enemy",
            collidesWith: null,
            []));

    var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
        .RunAsync(world, 1, CancellationToken.None);

    Assert.DoesNotContain(result.World.Subsystems.Events.Events, runtimeEvent =>
        runtimeEvent.Type == "collision.begin");
}
```

Add the helper `CreateSphereWithFilter` immediately after the existing `CreateSphere` helper (around line 189, right before `CreateCircle2D`):

```csharp
private static RekallAgeRuntimeEntity CreateSphereWithFilter(
    string id,
    string name,
    double x,
    string layer,
    IReadOnlyList<string>? collidesWith,
    JsonArray events)
{
    var filterProperties = new JsonObject { ["layer"] = layer };
    if (collidesWith is not null)
    {
        var array = new JsonArray();
        foreach (var item in collidesWith)
        {
            array.Add(item);
        }

        filterProperties["collidesWith"] = array;
    }

    return new RekallAgeRuntimeEntity(
        id,
        name,
        [],
        null,
        null,
        true,
        false,
        RekallAgeRuntimeTransform.Identity with
        {
            Position3D = new RekallAgeRuntimeVector3(x, 0, 0)
        },
        [
            new RekallAgeRuntimeComponent(
                "Rekall.SphereCollider3D",
                new JsonObject { ["radius"] = 0.5 }),
            new RekallAgeRuntimeComponent(
                "Rekall.CollisionFilter",
                filterProperties),
            new RekallAgeRuntimeComponent(
                "Rekall.EventBindings",
                new JsonObject { ["events"] = events })
        ]);
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "FullyQualifiedName~CollisionSystemDoesNotEmitBeginForNonAcceptingLayers"`
Expected: FAIL — `collision.begin` is currently emitted regardless of layers.

- [ ] **Step 3: Guard the overlap loop**

In `src/Rekall.Age.Runtime/RekallAgeCollisionEventSystem.cs`, find the existing pairwise loop (the one containing `if (!Overlaps(left, right))`) and change:

```csharp
                var left = bodies[leftIndex];
                var right = bodies[rightIndex];
                if (!Overlaps(left, right))
                {
                    continue;
                }
```

to:

```csharp
                var left = bodies[leftIndex];
                var right = bodies[rightIndex];
                if (!Overlaps(left, right) || !RekallAgeCollisionFilter.Allows(left.Entity, right.Entity))
                {
                    continue;
                }
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "FullyQualifiedName~CollisionSystemDoesNotEmitBeginForNonAcceptingLayers"`
Expected: PASS.

- [ ] **Step 5: Run the existing collision-event regression**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "FullyQualifiedName~RuntimeCollisionEventSystemTests"`
Expected: PASS, no regressions.

- [ ] **Step 6: Commit**

```bash
git add src/Rekall.Age.Runtime/RekallAgeCollisionEventSystem.cs tests/Rekall.Age.Tests/Runtime/RuntimeCollisionEventSystemTests.cs
git commit -m "feat: filter collision.* events by collision layer"
```

---

### Task 4: Filter `trigger.*` events

**Files:**
- Modify: `src/Rekall.Age.Runtime/RekallAgeTriggerEventSystem.cs`
- Test: `tests/Rekall.Age.Tests/Runtime/RuntimeTriggerEventSystemTests.cs`

**Interfaces:**
- Consumes: `RekallAgeCollisionFilter.Allows(RekallAgeRuntimeEntity, RekallAgeRuntimeEntity) -> bool` from Task 1.
- Produces: no new public API — `trigger.enter/stay/exit` are no longer emitted for a non-accepting pair.

- [ ] **Step 1: Write the failing event-suppression regression**

In `tests/Rekall.Age.Tests/Runtime/RuntimeTriggerEventSystemTests.cs`, first read the existing `CreateTrigger`/`CreateActor` helper signatures at the bottom of the file to confirm their exact parameters, then add this test immediately after `TriggerSystemEmitsEnterForNewOccupants`:

```csharp
[Fact]
public async Task TriggerSystemDoesNotEmitEnterForNonAcceptingLayers()
{
    var zone = CreateTrigger(
        "zone",
        "Zone",
        x: 0,
        [
            new JsonObject { ["event"] = "trigger.enter", ["handler"] = "enteredZone" }
        ]);
    zone = zone with
    {
        Components = [.. zone.Components, new RekallAgeRuntimeComponent(
            "Rekall.CollisionFilter",
            new JsonObject { ["layer"] = "zoneOnly", ["collidesWith"] = new JsonArray("nothing") })]
    };
    var world = CreateWorld(zone, CreateActor("actor", "Actor", x: 0.5));

    var result = await RekallAgeRuntimeExecutionLoop.CreateDefault()
        .RunAsync(world, 1, CancellationToken.None);

    Assert.DoesNotContain(result.World.Subsystems.Events.Events, runtimeEvent =>
        runtimeEvent.Type == "trigger.enter");
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "FullyQualifiedName~TriggerSystemDoesNotEmitEnterForNonAcceptingLayers"`
Expected: FAIL — `trigger.enter` is currently emitted regardless of layers. If the `with` expression or `zone.Components` shape does not compile, inspect `RekallAgeRuntimeEntity`'s real definition in `src/Rekall.Age.Runtime.Abstractions/RekallAgeRuntimeContracts.cs` (it is a `sealed record` with an init `Components` list, so `with { Components = [...] }` is valid) and the existing `CreateTrigger` helper's return type before adjusting.

- [ ] **Step 3: Guard the overlap loop**

In `src/Rekall.Age.Runtime/RekallAgeTriggerEventSystem.cs`'s `UpdateAsync`, the pairwise check is a LINQ `.Where(...)` chain, not an `if`. Change:

```csharp
            var current = colliders
                .Where(body => !body.Entity.Id.Equals(entity.Id, StringComparison.Ordinal)
                               && MatchesFilters(body.Entity, trigger)
                               && Overlaps(triggerBody, body))
                .Select(body => body.Entity.Id)
                .ToHashSet(StringComparer.Ordinal);
```

to:

```csharp
            var current = colliders
                .Where(body => !body.Entity.Id.Equals(entity.Id, StringComparison.Ordinal)
                               && MatchesFilters(body.Entity, trigger)
                               && Overlaps(triggerBody, body)
                               && RekallAgeCollisionFilter.Allows(entity, body.Entity))
                .Select(body => body.Entity.Id)
                .ToHashSet(StringComparer.Ordinal);
```

(`entity` is the trigger's own `RekallAgeRuntimeEntity`, already in scope as the outer `Select` lambda parameter in `UpdateAsync`; `body.Entity` is the candidate collider's entity.)

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "FullyQualifiedName~TriggerSystemDoesNotEmitEnterForNonAcceptingLayers"`
Expected: PASS.

- [ ] **Step 5: Run the existing trigger-event regression**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "FullyQualifiedName~RuntimeTriggerEventSystemTests"`
Expected: PASS, no regressions.

- [ ] **Step 6: Commit**

```bash
git add src/Rekall.Age.Runtime/RekallAgeTriggerEventSystem.cs tests/Rekall.Age.Tests/Runtime/RuntimeTriggerEventSystemTests.cs
git commit -m "feat: filter trigger.* events by collision layer"
```

---

### Task 5: Validation pass and checkpoint

**Files:**
- Test: `tests/Rekall.Age.Tests/Validation/ProjectValidatorTests.cs`
- Modify: `docs/production/PROGRESS.md`

**Interfaces:**
- Consumes: nothing new — proves the whole feature is validation-clean end to end.

- [ ] **Step 1: Write a failing validation regression**

In `tests/Rekall.Age.Tests/Validation/ProjectValidatorTests.cs`, find an existing test that builds a scene with a `Rekall.BoxCollider3D`/`Rekall.Trigger` and asserts zero validation issues (search the file for `Rekall.Trigger` for a model to copy exactly), and add an equivalent test adding a `Rekall.CollisionFilter` component with `{"layer":"player","collidesWith":["enemy"]}` to an entity, asserting the validation result reports zero issues.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "FullyQualifiedName~<NewTestName>"`
Expected: FAIL if `Rekall.CollisionFilter` is somehow still rejected as unknown (this would indicate Task 1 Step 6's catalog registration was missed or reverted); otherwise this test should already pass, confirming Task 1 wired the catalog correctly — in that case this step's "RED" is the compile-time absence of the test method itself, not a runtime failure. Either way, run it once before declaring the feature done.

- [ ] **Step 3: Fix forward only if it fails**

If the validator rejects `Rekall.CollisionFilter`, re-check `src/Rekall.Age.World/RekallAgeBuiltInComponentTypeCatalog.cs` for the Task 1 Step 6 addition and re-add it if missing.

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter "FullyQualifiedName~<NewTestName>"`
Expected: PASS.

- [ ] **Step 5: Update PROGRESS.md**

Add a dated checkpoint entry (matching the style of the existing dated `## YYYY-MM-DD ...` entries at the end of `docs/production/PROGRESS.md`) recording: the new `Rekall.CollisionFilter` component and matching rule, the three integration points, and that this closes the "collision layers/masks" item of the physics current-gaps bullet (update that bullet's wording — it currently says "Remaining breadth includes generic joints/constraints, a dedicated 2D world/material contract, authored angular control, collision layers/masks, ..." and should drop "collision layers/masks" from that list now that it exists).

- [ ] **Step 6: Final commit and push**

```bash
git add tests/Rekall.Age.Tests/Validation/ProjectValidatorTests.cs docs/production/PROGRESS.md
git commit -m "test: accept collision layers and masks"
git push origin master
```

---

## Plan Self-Review

- **Spec coverage:** component contract (Task 1), symmetric-AND matching rule (Task 1), BEPU physical response (Task 2), `collision.*` events (Task 3), `trigger.*` events (Task 4), catalog/schema registration (Task 1), zero-behavior-change default (regression steps in Tasks 2-4), validation discoverability (Task 5). All spec sections are covered.
- **Placeholder scan:** every step has real, concrete code, verified against the actual current source of every file this plan touches (including `RekallAgeTriggerEventSystem`'s exact LINQ `.Where(...)` chain, which differs in shape from `RekallAgeCollisionEventSystem`'s `if (!Overlaps) continue`).
- **Type consistency:** `RekallAgeCollisionFilter.Allows(RekallAgeRuntimeEntity, RekallAgeRuntimeEntity)` and `RekallAgeCollisionFilter.Rule.From/Accepts` (Task 1) are used with identical signatures in Tasks 2, 3, and 4.

## Execution Notes (2026-08-27)

- **Task 2 deviation:** `RekallAgeCollisionFilter.Rule` holds reference-type fields (`string Layer`, `IReadOnlySet<string>? CollidesWith`), so it cannot be stored in BEPU's `CollidableProperty<T>` — the constructor threw `CS8377` requiring an unmanaged `T`. Fixed by using plain `Dictionary<BodyHandle, Rule>`/`Dictionary<StaticHandle, Rule>` fields on `PersistentPhysicsWorld` instead, populated in `AddDynamic`/`AddStatic`, cleared in `RemoveDynamic`/`RemoveStatic` (so a recycled BEPU handle never resolves a stale rule), and read via a new `LookupFilter(CollidableReference)` method passed into `RekallAgeBepuNarrowPhaseCallbacks` as a `Func<CollidableReference, RekallAgeCollisionFilter.Rule>` instead of a second `CollidableProperty<T>` parameter.
- **Task 5 side-fix:** running the full `ProjectValidatorTests` regression surfaced a pre-existing, unrelated failure — `Rekall.Destructible` was in `RekallAgeBuiltInComponentTypeCatalog.Types` but had no matching `[RekallAgeComponent]` schema class. Fixed by adding the real `RekallAgeDestructibleComponent` class with the exact property names `RekallAgeDestructionSystem` already reads at runtime (`Triggered`, `ChunkMeshAssetIds`, `ExplosionImpulse`, `TerrainEntityId`, `CraterRadius`, `CraterDepth`) — schema-only, no runtime behavior change.
