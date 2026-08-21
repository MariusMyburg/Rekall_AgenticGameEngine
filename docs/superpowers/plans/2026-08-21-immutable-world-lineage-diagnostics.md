# Immutable World Lineage Diagnostics

## Goal

Prevent a compiled agent-authored runtime module from receiving a trusted build
receipt when its immutable world mutations silently discard earlier state.

## Evidence

Installed Benchmark 45 assigned mutation results but later rebuilt
`updatedWorld` from stale `world`, losing movement. It also assigned outer-world
mutations inside an entity-update callback whose enclosing update subsequently
overwrote those nested results. Both patterns compile and are gameplay no-ops
or partial no-ops despite appearing correct to the authoring model.

## Implementation

1. Add red build-command regressions for stale-base reassignment and an outer
   immutable-world mutation inside an entity-update callback. Preserve valid
   chained mutation and read-only callback use.
2. Introduce a bounded source preflight that recognizes runtime-world mutation
   assignment lineage and callback extent without attempting general C# static
   analysis. Fail closed only on the two proven hazardous shapes, with stable
   codes, source lines, and copyable repairs.
3. Put the same rules in embedded module-authoring guidance so agents can avoid
   the failure before compilation.
4. Run focused red/green tests, the broader module/build selection, the locked
   installed-product gate, and a clean unchanged real-Ollama benchmark.

## Acceptance

- `updatedWorld = world.Update...` after `updatedWorld` already contains a
  mutation fails with an exact `updatedWorld = updatedWorld.Update...` repair.
- assigning an outer world variable from inside an entity-update callback fails
  with guidance to return only the entity from the callback and perform world
  mutations sequentially outside it.
- valid immutable chaining, read-only world queries inside callbacks, and
  ordinary unrelated assignments remain accepted.
- no trusted build receipt is issued for rejected source.

## Verified result

Completed. The two hazardous source shapes fail before compilation with stable
codes, source locations, and exact repair guidance. Valid chained mutations,
read-only callback queries, and comment/string lookalikes remain buildable. The
exact installed Benchmark 45 module is rejected for its stale lineage. Focused
coverage passed 18/18; the zero-warning/error locked gate passed 1,026/1,026
engine and 7/7 Studio tests twice plus the complete installed-product matrix.
