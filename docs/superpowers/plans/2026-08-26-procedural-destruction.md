# Procedural Destruction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build generic mesh fracture, terrain-crater deformation, and a `Rekall.Destructible` runtime component/system, then prove all three through an original grenade-destruction demo game authored via the CLI/MCP surface.

**Architecture:** Fracture reuses the existing `CSG.Sharp` kernel already wired into the boolean modeling-graph node (`Csg.CSG.Intersect`), clipping the source mesh against one large half-space "slab" per Voronoi neighbor. The crater stamp is one more entry in the existing point-deform operation family. `Rekall.Destructible` is an ordinary built-in runtime component/system pair. The demo is an ordinary agent-authored `Examples/` game.

**Tech Stack:** C#, `Rekall.Age.Modeling` (CSG.Sharp), `Rekall.Age.Runtime` (BEPU physics), xUnit, Rekall AGE CLI/MCP authoring surface

**Spec:** `docs/superpowers/specs/2026-08-26-procedural-destruction-design.md`

## Global Constraints

- The demo game is authored through `rekall-age` CLI commands / MCP tools as an external client would use them — never by hand-editing engine source to fake a result.
- Every real-time calculation uses `context.DeltaTime` with a maximum simulation step of `0.1` seconds (matching every other runtime system in this engine).
- A generic engine deficiency found while building the demo is fixed with a focused failing engine test before the demo works around it (this repo's existing reproduce-test-repair protocol), never special-cased for the demo.
- Fracture and crater-stamp code stays genre-neutral: no grenade/game vocabulary in `Rekall.Age.Modeling`/`Rekall.Age.Runtime`.

---

### Task 1: Extract a shared, reusable CSG⇄mesh conversion kernel

**Files:**
- Create: `src/Rekall.Age.Modeling/RekallAgeMeshCsgKernel.cs`
- Modify: `src/Rekall.Age.Modeling/RekallAgeModelingBoolean.cs`
- Test: `tests/Rekall.Age.Tests/Modeling/HardSurfaceModifierTests.cs` (or wherever the existing boolean-node tests live — locate with `grep -rl "rekall.modeling.boolean" tests/Rekall.Age.Tests`)

**Interfaces:**
- Produces: `internal static class RekallAgeMeshCsgKernel` with `internal static Csg.CSG ToCsg(RekallAgeMeshAsset source, string operand)` (moved verbatim from `RekallAgeModelingBoolean.cs`) and the shared vector helpers (`ToCsgVector`, `Subtract`, `Cross`, `Unit`, `Dot`) also moved verbatim.
- Consumes (by the boolean node, after extraction): the same methods, now via `RekallAgeMeshCsgKernel.ToCsg(...)` instead of the private method on `RekallAgeModelingGraphEvaluator`.

- [ ] **Step 1: Confirm the current boolean-node tests as the regression baseline**

Run: `grep -rl "rekall.modeling.boolean" tests/Rekall.Age.Tests --include=*.cs`

Run the located test file(s) with `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter FullyQualifiedName~<TestClassName>` and record the passing count.

- [ ] **Step 2: Extract `ToCsg` and the vector helpers into `RekallAgeMeshCsgKernel`**

Move `ToCsg`, `ToCsgVector`, `Subtract`, `Cross`, `Unit`, `Dot` from `RekallAgeModelingBoolean.cs` into a new `internal static class RekallAgeMeshCsgKernel` in `RekallAgeMeshCsgKernel.cs`, unchanged in behavior (copy the method bodies verbatim; only the enclosing type changes). Update `RekallAgeModelingBoolean.cs`'s `ToCsg(meshA, "a")` / `ToCsg(meshB, "b")` call sites to `RekallAgeMeshCsgKernel.ToCsg(...)`, and any internal use of the moved vector helpers similarly.

- [ ] **Step 3: Re-run the same boolean-node tests and confirm identical pass count**

Run the same filtered test command from Step 1. Expected: PASS, same count as the baseline — this is a pure extraction, not a behavior change.

- [ ] **Step 4: Full modeling test selection and solution build**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter FullyQualifiedName~Modeling`
Run: `dotnet build Rekall.Age.sln -c Release`
Expected: PASS, 0 warnings/errors.

- [ ] **Step 5: Commit**

```powershell
git add src/Rekall.Age.Modeling/RekallAgeMeshCsgKernel.cs src/Rekall.Age.Modeling/RekallAgeModelingBoolean.cs
git commit -m "refactor: extract a shared CSG-to-mesh conversion kernel"
```

### Task 2: Voronoi mesh fracture

**Files:**
- Create: `src/Rekall.Age.Modeling/RekallAgeMeshFracture.cs`
- Test: `tests/Rekall.Age.Tests/Modeling/MeshFractureTests.cs`

**Interfaces:**
- Consumes: `RekallAgeMeshCsgKernel.ToCsg` from Task 1; `RekallAgeMeshCompiler`, `RekallAgeMeshValidator`, `RekallAgeMeshPrimitiveFactory` (for the half-space slab boxes).
- Produces: `public static class RekallAgeMeshFracture` with `public static IReadOnlyList<RekallAgeMeshAsset> Fracture(RekallAgeMeshAsset source, int chunkCount, long seed)`.

- [ ] **Step 1: Write the failing fracture test**

```csharp
[Theory]
[InlineData(3)]
[InlineData(6)]
public void FractureProducesValidNonOverlappingChunksApproximatingSourceVolume(int chunkCount)
{
    var source = RekallAgeMeshPrimitiveFactory.CreateBoxSync(2, 2, 2); // adjust to whatever the factory's synchronous/test-friendly box constructor is named after inspecting RekallAgeMeshPrimitiveFactory.cs
    var chunks = RekallAgeMeshFracture.Fracture(source, chunkCount, seed: 42);

    Assert.Equal(chunkCount, chunks.Count);
    var validator = new RekallAgeMeshValidator();
    foreach (var chunk in chunks)
    {
        var validation = validator.Validate(chunk);
        Assert.True(validation.IsValid, string.Join(", ", validation.Diagnostics.Select(d => d.Message)));
        Assert.Equal(0, validation.Summary.BoundaryEdgeCount);
    }

    var sourceVolume = MeshVolume(source);
    var chunkVolumeSum = chunks.Sum(MeshVolume);
    Assert.InRange(chunkVolumeSum, sourceVolume * 0.97, sourceVolume * 1.03);
}

private static double MeshVolume(RekallAgeMeshAsset mesh)
{
    var compiled = new RekallAgeMeshCompiler().Compile(mesh);
    double volume = 0;
    for (var triangle = 0; triangle < compiled.Triangles.Count; triangle++)
    {
        var indices = compiled.Indices.Skip(triangle * 3).Take(3).Select(i => checked((int)i)).ToArray();
        var p0 = compiled.Vertices[indices[0]].Position;
        var p1 = compiled.Vertices[indices[1]].Position;
        var p2 = compiled.Vertices[indices[2]].Position;
        volume += (p0.X * (p1.Y * p2.Z - p2.Y * p1.Z)
                 - p0.Y * (p1.X * p2.Z - p2.X * p1.Z)
                 + p0.Z * (p1.X * p2.Y - p2.X * p1.Y)) / 6.0;
    }
    return Math.Abs(volume);
}
```

Before finalizing this test, inspect `RekallAgeMeshPrimitiveFactory.cs` for the exact synchronous/testable box-creation entry point used by existing tests (search `tests/Rekall.Age.Tests/Modeling/ProductionPrimitiveTests.cs` for how a box `RekallAgeMeshAsset` is constructed directly in a test, without the async Studio-facing wrapper) and use that exact call, not a guessed name.

- [ ] **Step 2: Run it and verify it fails**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter FullyQualifiedName~FractureProducesValidNonOverlappingChunksApproximatingSourceVolume`
Expected: FAIL — `RekallAgeMeshFracture` does not exist.

- [ ] **Step 3: Implement fracture**

```csharp
using Csg = CSG.Sharp;
using Rekall.Age.Modeling.Contracts;

namespace Rekall.Age.Modeling;

public static class RekallAgeMeshFracture
{
    public static IReadOnlyList<RekallAgeMeshAsset> Fracture(RekallAgeMeshAsset source, int chunkCount, long seed)
    {
        if (chunkCount < 2 || chunkCount > 64)
            throw new ArgumentOutOfRangeException(nameof(chunkCount), "Fracture chunk count must be 2-64.");
        var validation = new RekallAgeMeshValidator().Validate(source);
        if (!validation.IsValid || validation.Summary.BoundaryEdgeCount != 0)
            throw new ArgumentException("Fracture source must be a closed manifold mesh.", nameof(source));

        var bounds = ComputeBounds(source);
        var random = new Random(unchecked((int)seed));
        var seeds = Enumerable.Range(0, chunkCount)
            .Select(_ => new RekallAgeGeometryVector3(
                Lerp(bounds.Min.X, bounds.Max.X, random.NextDouble()),
                Lerp(bounds.Min.Y, bounds.Max.Y, random.NextDouble()),
                Lerp(bounds.Min.Z, bounds.Max.Z, random.NextDouble())))
            .ToArray();

        var sourceCsg = RekallAgeMeshCsgKernel.ToCsg(source, "source");
        var span = Math.Max(bounds.Max.X - bounds.Min.X, Math.Max(bounds.Max.Y - bounds.Min.Y, bounds.Max.Z - bounds.Min.Z));
        var slabHalfExtent = Math.Max(span * 4, 1);

        var chunks = new List<RekallAgeMeshAsset>(chunkCount);
        for (var i = 0; i < seeds.Length; i++)
        {
            var cell = sourceCsg;
            for (var j = 0; j < seeds.Length; j++)
            {
                if (i == j) continue;
                var slab = HalfSpaceSlab(seeds[i], seeds[j], slabHalfExtent);
                cell = cell.Intersect(slab);
            }
            chunks.Add(FromCsg(cell, $"{source.AssetId}-chunk-{i}", $"{source.Name} Chunk {i}"));
        }
        return chunks;
    }

    /// <summary>
    /// A thin box mesh, in CSG form, whose near face lies on the perpendicular bisector plane
    /// between <paramref name="keep"/> and <paramref name="other"/>, oriented so intersecting
    /// against it keeps the half of space closer to <paramref name="keep"/>.
    /// </summary>
    private static Csg.CSG HalfSpaceSlab(RekallAgeGeometryVector3 keep, RekallAgeGeometryVector3 other, double halfExtent)
    {
        var midpoint = new RekallAgeGeometryVector3((keep.X + other.X) / 2, (keep.Y + other.Y) / 2, (keep.Z + other.Z) / 2);
        var normal = RekallAgeMeshCsgKernel.Unit(RekallAgeMeshCsgKernel.Subtract(keep, other));
        // A box centered `halfExtent` further along -normal from the bisector plane, thick enough
        // (2*halfExtent) to cover the whole source mesh's extent on the "keep" side.
        var boxMesh = RekallAgeMeshPrimitiveFactory.CreateBoxMeshSync(halfExtent * 2, halfExtent * 4, halfExtent * 4); // confirm exact factory signature before use
        var oriented = TransformMeshToPlane(boxMesh, midpoint, normal, halfExtent);
        return RekallAgeMeshCsgKernel.ToCsg(oriented, "slab");
    }

    // TransformMeshToPlane, ComputeBounds, Lerp: straightforward point-transform helpers -
    // rotate the box so its local +X axis aligns with `normal`, then translate its center to
    // `midpoint - normal * halfExtent` (pushing it fully onto the "keep" side). Implement using
    // RekallAgeGeometryVector3 arithmetic only, consistent with the rest of this file.

    private static RekallAgeMeshAsset FromCsg(Csg.CSG csg, string assetId, string name)
    {
        // Reuses the same polygon-to-topology conversion RekallAgeModelingBoolean.FromCsg performs
        // (welding by tolerance, building PointIds/FaceOffsets/CornerPointIndices), but without that
        // method's two-operand attribute-interpolation plan - a fracture chunk has one source, so
        // corner attributes copy directly from the source polygon's stored source-face provenance
        // instead of blending. Extract the topology-building half of RekallAgeModelingBoolean.FromCsg
        // into RekallAgeMeshCsgKernel in this task if it is not already operand-agnostic after Task 1.
        throw new NotImplementedException();
    }
}
```

This step is intentionally left with two explicit implementation gaps (`TransformMeshToPlane`/`ComputeBounds`/`Lerp` bodies, and `FromCsg`'s topology-building) rather than guessed code, because they depend on exact helper signatures in `RekallAgeMeshPrimitiveFactory` and the post-Task-1 shape of the extracted kernel that must be read from the actual source, not assumed. Read `RekallAgeMeshPrimitiveFactory.cs` and the post-extraction `RekallAgeMeshCsgKernel.cs`/`RekallAgeModelingBoolean.cs` before writing these bodies, and prefer moving `FromCsg`'s topology-building loop into `RekallAgeMeshCsgKernel` (parameterized by a per-polygon attribute callback) over duplicating it, per the Global Constraints' "reuse over new primitive" rule.

- [ ] **Step 4: Run the fracture test and iterate until it passes**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter FullyQualifiedName~FractureProducesValidNonOverlappingChunksApproximatingSourceVolume`
Expected: PASS for both chunk counts (3 and 6).

- [ ] **Step 5: Full modeling test selection and solution build**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter FullyQualifiedName~Modeling`
Run: `dotnet build Rekall.Age.sln -c Release`
Expected: PASS, 0 warnings/errors.

- [ ] **Step 6: Commit**

```powershell
git add src/Rekall.Age.Modeling/RekallAgeMeshFracture.cs tests/Rekall.Age.Tests/Modeling/MeshFractureTests.cs
git commit -m "feat: add Voronoi mesh fracture built on the existing CSG kernel"
```

### Task 3: Expose fracture as a modeling graph node

**Files:**
- Modify: `src/Rekall.Age.Modeling/RekallAgeModelingNodeCatalog.cs`
- Modify: `src/Rekall.Age.Modeling/RekallAgeModelingGraphEvaluator.cs` (or a new `RekallAgeModelingGraphEvaluator.Fracture.cs` partial, matching the existing `.HardSurface.cs`/`.Construction.cs` partial-file split)
- Test: extend the modeling graph catalog/evaluator test files used for `rekall.modeling.boolean`

**Interfaces:**
- Consumes: `RekallAgeMeshFracture.Fracture` from Task 2.
- Produces: node type `rekall.modeling.fracture` with an input geometry port, an integer `chunkCount` parameter, an integer `seed` parameter, and **multiple** geometry output ports (`chunk0`..`chunkN-1`, capped at the same 64-chunk ceiling as Task 2) or a single `Selection`-typed output enumerating chunk indices if the graph's port model cannot express a variable output count — read `RekallAgeModelingPortDescriptor`/how any existing node with a data-dependent output shape (if any) solves this before deciding; otherwise cap outputs at a fixed generous maximum (e.g. 16 named `chunk0..chunk15` ports, unused ones simply not wired) rather than inventing new graph-schema capability.

- [ ] **Step 1: Add a failing catalog test** proving `rekall.modeling.fracture` exists with the expected ports/parameters.
- [ ] **Step 2: Run and confirm it fails.**
- [ ] **Step 3: Implement the node descriptor and evaluator case**, following the exact pattern of the existing `rekall.modeling.boolean` node's descriptor/evaluator wiring.
- [ ] **Step 4: Run the catalog/evaluator tests and confirm they pass.**
- [ ] **Step 5: Full modeling test selection and solution build; commit.**

### Task 4: Terrain-crater point deform

**Files:**
- Modify: `src/Rekall.Age.Modeling/RekallAgeMeshDeformOperations.cs`
- Modify: `src/Rekall.Age.Modeling/RekallAgeMeshOperationExecutor.cs` (register `"crater_stamp"` in the operation-descriptor list and dispatch switch, matching `"bend_points"`)
- Test: `tests/Rekall.Age.Tests/Modeling/CraterStampDeformTests.cs`

**Interfaces:**
- Produces: mesh operation `"crater_stamp"` on the Point domain with parameters `centerX`/`centerY`/`centerZ`, `radius`, `depth`, `axis` (the vertical axis to displace along, default `"y"`).

- [ ] **Step 1: Write the failing test** — author a flat grid mesh (reuse whatever grid/plane fixture existing deform tests use), select all points, apply `crater_stamp` centered on the grid with a known radius/depth, and assert: the center point's displaced coordinate along `axis` drops by approximately `depth`; a point exactly at `radius` from center is unchanged; a point beyond `radius` is unchanged; the falloff between center and radius is monotonic (sample 3 points along a radius and assert strictly increasing displaced coordinate as distance from center increases).
- [ ] **Step 2: Run and confirm it fails.**
- [ ] **Step 3: Implement `CraterStamp`** in `RekallAgeMeshDeformOperations.cs` as a smooth (e.g. `depth * (cos(distance/radius * pi) + 1) / 2` for `distance <= radius`, else `0`) radial falloff, following `BendPoints`'s exact structural pattern (`RequireDomain`, `ResolveIndices`, parameter reads via `ReadFiniteDouble`/`ReadBoundedString`, `WithCoordinate`).
- [ ] **Step 4: Run the crater test and confirm it passes.**
- [ ] **Step 5: Full modeling test selection and solution build; commit.**

### Task 5: `Rekall.Destructible` runtime component and system

**Files:**
- Modify: `src/Rekall.Age.World/RekallAgeBuiltInComponentTypeCatalog.cs` (register `Rekall.Destructible`'s schema, matching how every other built-in component is registered)
- Create: `src/Rekall.Age.Runtime/RekallAgeDestructionSystem.cs`
- Test: `tests/Rekall.Age.Tests/Runtime/DestructionSystemTests.cs`

**Interfaces:**
- Consumes: pre-authored chunk model asset references (a scene author supplies N `Rekall.ModelAssetReference`-compatible chunk meshes, produced offline via `RekallAgeMeshFracture`/the CLI, not fractured live at runtime — runtime fracture is out of scope for this slice), `Rekall.Destructible` component properties: `chunkModelAssetIds` (array of strings), `explosionImpulse` (number), `terrainEntityId` (optional string), `craterRadius`/`craterDepth` (numbers, used only when `terrainEntityId` is set).
- Produces: on a semantic `destroy` event targeting the entity (or `health <= 0` if the entity also carries a health-bearing component - read the existing Aetherfall combat pattern for the exact convention this engine uses for "entity died" and match it generically), the system deactivates the source entity, spawns one dynamic-rigid-body entity per `chunkModelAssetIds` entry at the source entity's position with an outward-radial impulse scaled by `explosionImpulse`, and, if `terrainEntityId` is set, applies `crater_stamp` (Task 4) to that entity's mesh at the impact point.

- [ ] **Step 1: Write a failing runtime acceptance test** proving: after the destroy event, the source entity is inactive/removed, exactly `chunkModelAssetIds.Count` new dynamic entities exist with nonzero outward velocity, and (in a second test case with a terrain reference configured) the terrain mesh's points near the impact point have measurably dropped after re-inspecting its compiled geometry.
- [ ] **Step 2: Run and confirm it fails.**
- [ ] **Step 3: Implement `RekallAgeDestructionSystem`**, following the exact structural pattern of an existing built-in runtime system (find one via `ls src/Rekall.Age.Runtime/Rekall.Age*System.cs` and match its shape: constructor, `Priority`, `UpdateAsync`/equivalent, world mutation via the immutable-world SDK helpers).
- [ ] **Step 4: Run the runtime test and confirm it passes.**
- [ ] **Step 5: Full runtime test selection and solution build; commit.**

### Task 6: The grenade destruction demo game (authored via CLI/MCP)

**Files:**
- Create: `Examples/<GameName>/**` (a new original project, name and theme chosen fresh — not a reuse of Aetherfall's assets/story)
- Create: `tests/Rekall.Age.Tests/Examples/<GameName>AcceptanceTests.cs`

**Interfaces:**
- Consumes: `Rekall.Destructible` (Task 5), `rekall.modeling.fracture` (Task 3), `crater_stamp` (Task 4), and the standard `rekall-age` CLI / MCP tool surface used to author every other example in this repository.

This task is intentionally not detailed step-by-step here: per the Global Constraints, it must be authored live through the CLI/MCP authoring surface as an external client would, discovering and fixing any real authoring friction as a generic engine deficiency along the way (this repo's own reproduce-test-repair protocol), exactly as the original Aetherfall plan (`docs/superpowers/plans/2026-08-24-aetherfall-ultimate-3d-testbed.md`) did. Follow that plan's own task structure and acceptance bar (deterministic gameplay checkpoints via `runtime inspect`, native Vulkan capture reviewed for visual quality, a Windows package, and an `ACCEPTANCE.md`) as the template for this task's own sub-plan, scoped to:

- A terrain plane the player can see cratered over time.
- Grenades spawning on a randomized timer, arming, and exploding (via the `Rekall.Destructible` destroy-event path) into visible fractured debris with outward physics impulse.
- At least one other destructible prop type, to prove fracture generalizes beyond one authored shape.
- A concise player-facing win/survive condition (matching this repo's standard of never producing a scoreless tech demo).

If any of capabilities 1-3 fail to produce a good visual/gameplay result here, the fix belongs in Task 1-5's engine code (a new focused failing test there, then the repair), never in this task alone.
