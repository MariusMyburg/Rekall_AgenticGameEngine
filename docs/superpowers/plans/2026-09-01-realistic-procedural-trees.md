# Realistic Procedural Trees Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build deterministic, realistic, performant procedural deciduous trees as a generic AGE modeling capability.

**Architecture:** Public settings/results live in Modeling.Contracts; a focused generator in Modeling produces separate bark and foliage meshes for near, mid, and far LODs. Midnight Rider consumes the generic result through existing GeometryMesh, Material, LOD, and wind contracts.

**Tech Stack:** C# 14/.NET 10, AGE mesh contracts, xUnit, Vulkan runtime rendering.

**Spec:** `docs/superpowers/specs/2026-09-01-realistic-procedural-trees-design.md`

## Global Constraints

- Deterministic for a seed and settings.
- Generic engine authoring primitive; no genre-specific or tree-specific renderer branch.
- Separate bark and foliage surfaces with bounded LOD complexity.
- Focused tests only during implementation.

---

### Task 1: Tree authoring contracts and deterministic generator

**Files:**
- Create: `src/Rekall.Age.Modeling.Contracts/RekallAgeProceduralTreeContracts.cs`
- Create: `src/Rekall.Age.Modeling/RekallAgeProceduralTreeGenerator.cs`
- Test: `tests/Rekall.Age.Tests/Modeling/ProceduralTreeGeneratorTests.cs`

**Interfaces:**
- Produces: `RekallAgeProceduralTreeSettings`, `RekallAgeGeneratedTree`, `RekallAgeProceduralTreeGenerator.Generate`.

- [ ] Write focused failing tests for deterministic output, crown shape, taper, UVs, leaf cards, and monotonic LOD budgets.
- [ ] Run the exact test class and confirm failures are caused by missing contracts.
- [ ] Implement validation, skeleton growth, bark tube meshing, foliage-card meshing, attributes, and LOD presets.
- [ ] Run the exact test class and make it pass.

### Task 2: Example integration

**Files:**
- Modify: `Examples/MidnightRider/Modules/MidnightRiderRules/MidnightRiderRules.csproj`
- Modify: `Examples/MidnightRider/Modules/MidnightRiderRules/MidnightRiderRulesModule.cs`
- Delete: `Examples/MidnightRider/Modules/MidnightRiderRules/ProceduralTreeGenerator.cs`
- Test: `tests/Rekall.Age.Tests/Modeling/ProceduralTreeGeneratorTests.cs`

**Interfaces:**
- Consumes: `RekallAgeProceduralTreeGenerator.Generate` and existing component JSON.
- Produces: Midnight Rider roadside trees with separate bark/foliage entities and ordinary LOD/material metadata.

- [ ] Add an integration/source assertion that the example consumes the generic generator.
- [ ] Run it and confirm it fails against the private generator.
- [ ] Replace the example generator call and preserve deterministic placement.
- [ ] Build the example module and rerun the focused tests.

### Task 3: Verification and delivery

**Files:**
- Modify: `docs/production/PROGRESS.md`

**Interfaces:**
- Consumes: the completed generator and example integration.
- Produces: focused test/build evidence and a source-control checkpoint.

- [ ] Run focused modeling tests and the Midnight Rider module build.
- [ ] Run `git diff --check` and inspect the complete diff.
- [ ] Record evidence in PROGRESS, commit, and push `master`.
