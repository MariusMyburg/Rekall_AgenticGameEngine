# Generic Animation State Graph Design

Date: 2026-08-18

## Purpose

Rekall AGE already provides versioned clips, generic property tracks, bounded
markers, deterministic players, weighted mixer layers, and cross-fades. Agents
can build a state machine in C#, but there is no inspectable declarative
primitive for selecting and transitioning reusable clips. The engine should
provide that orchestration primitive without deciding game-specific states,
parameters, or transition policy.

## Decision

Add a versioned `Rekall.AnimationStateGraph` component. An agent authors its
states, ordered transitions, and primitive parameter values. Agent-authored
modules update those parameters from arbitrary game facts. A new runtime system
evaluates at most one transition per entity per frame and projects the active
state or cross-fade into a runtime-only graph mixer consumed by the existing
animation sampler. The graph never interprets genre concepts, input keys,
movement, combat, or character roles.

This is preferred over an event-only graph because durable parameters are easy
to inspect, serialize, replicate, and resume. It is preferred over embedding a
second sampler because clips, markers, blending, limits, and diagnostics must
retain one implementation. Direct module control of `Rekall.AnimationMixer`
remains available when a graph is unnecessary.

## Authored contract

`Rekall.AnimationStateGraph` version 1 contains:

- `version`: exactly `1`;
- `playing`: whether graph time and transition evaluation advance;
- `initialState`: required state name;
- `parameters`: a JSON object whose values are finite numbers, booleans, or
  bounded strings;
- `states`: an ordered array of `{name, clip, speed, loopMode,
  startTimeSeconds}`; and
- `transitions`: an ordered array of `{from, to, durationSeconds,
  resetTime, conditions}`.

State and parameter names are nonblank ordinal identifiers of at most 128
characters. `clip` is an animation-catalog id. `loopMode` uses the existing
`clamp`, `loop`, or `pingpong` contract. `from` names a state or uses `*` for an
any-state transition. Each condition is `{parameter, operator, value}` where
operator is `equals`, `notEquals`, `greater`, `greaterOrEqual`, `less`, or
`lessOrEqual`. Numeric ordering operators require finite numeric operands;
equality supports matching primitive types. All conditions must pass. Empty
conditions are valid explicit unconditional transitions.

Transitions are evaluated in authored order. Exact-current-state transitions
are considered before any-state transitions while preserving authored order
inside each group. A self-transition is ignored unless `resetTime` is true.
At most one transition begins per frame. A transition already in progress is
not interrupted in version 1. On completion, evaluation may begin another
transition on a later frame.

## Bounds and failure behavior

The runtime accepts at most 64 states, 256 transitions, 128 parameters, and 16
conditions per transition. Strings are at most 1,024 characters. Duplicate
state names, missing targets, invalid initial state, unsupported versions,
invalid primitive values/operators, non-finite numbers, and exceeded bounds
fail closed for that entity. They emit bounded `runtime.animation.graph_*`
observations and do not mutate animated properties. Missing clip assets remain
reported through the existing animation clip diagnostics.

Validation is performed before evaluation. Runtime-authored state is never
trusted as a replacement for the authored graph definition.

## Runtime data flow

`RekallAgeAnimationStateGraphSystem` runs before `runtime.animation`:

1. Parse and validate the authored graph into a bounded deterministic model.
2. Read `Rekall.AnimationGraphState`, or initialize from `initialState`.
3. If no transition is active, evaluate ordered conditions against the current
   authored `parameters` and begin at most one transition.
4. Advance state and transition clocks using `context.DeltaTime` only.
5. Upsert runtime-only `Rekall.AnimationGraphMixer` layers. A stable state has
   one full-weight layer. A transition has source and target layers weighted
   linearly by normalized transition progress. The existing animation system
   samples this mixer using its normal clip, track, marker, and blend limits.
6. Upsert `Rekall.AnimationGraphState` with version, active/previous state,
   state raw time, transition elapsed/duration/progress, and playing status.

`resetTime=true` starts the target at its authored `startTimeSeconds`.
`resetTime=false` resumes the target time previously retained in a bounded
per-state clock map. State clocks contain only graph-declared states and are
therefore capped at 64 entries. Splitting a run and resuming from the resulting
runtime world must produce the same state, weights, clocks, and animated values
as one continuous run.

When a transition begins, `activeState` becomes the target immediately and
`previousState` retains the source until the fade completes. Both clocks advance
while their clips contribute. `animation.state.exit`,
`animation.transition.begin`, and `animation.state.enter` are emitted once on
that begin frame; `animation.transition.end` is emitted once on completion.
Transition conditions are not evaluated again until the following frame.

The runtime-only graph mixer uses layer names derived from state names and is a
separate component type, so an authored `Rekall.AnimationMixer` is never
overwritten. An entity may author either a state graph or a mixer/player. If a
graph coexists with another animation driver, the graph wins and a conflict
observation is emitted; this prevents component-order-dependent mutation.

## Facts and inspection

The graph system emits generic facts on the owning entity:

- `animation.state.exit` when a transition begins;
- `animation.transition.begin` when a transition begins;
- `animation.state.enter` when the target becomes active; and
- `animation.transition.end` when the cross-fade completes.

Payloads contain source state, target state, duration, and progress facts where
applicable. They do not execute gameplay behavior. Agent-authored modules may
consume them and emit their own custom events.

Runtime projection and `runtime inspect` expose kind
`AnimationStateGraph`, active state, previous state, transition progress,
playing status, active clip, and the projected layers. The built-in component
schema describes exact JSON shapes, limits, parameter ownership, and the
full-trust/module-authored responsibility.

## Verification

Focused tests must prove initialization, ordered typed conditions, any-state
precedence, self-transition rules, linear cross-fade through existing generic
tracks, exact transition facts, pause behavior, split-run determinism, bounded
state-clock resume, malformed and excessive graphs, coexistence diagnostics,
schema discovery, and runtime/CLI inspection. The complete Debug suite and
canonical two-pass installed Release gate remain required before this tranche
is considered production-verified.

An installed proof should author two generic clips and a graph, mutate only a
generic parameter through an agent-authored module or scene update, inspect the
active/transition state, and capture visually distinct pre/post frames. The
proof must not introduce a built-in locomotion, controller, or character
concept.
