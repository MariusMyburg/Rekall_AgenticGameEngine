# Rekall AGE Product Vision

Rekall AGE means **Rekall Agentic Game Engine**.

## Founding vision

Rekall AGE is a fully capable game engine developed in C#. Above the low-level
graphics APIs, its engine and authoring layers are designed from day one for
efficient, effective use by agentic AI and Model Context Protocol clients.

An LLM must be able to create a fully functional game through two equally
first-class paths:

1. authoring C# game modules against a stable, portable engine SDK; and
2. inspecting, creating, changing, validating, running, and packaging projects
   through typed MCP tools.

These paths share the same engine contracts. MCP is not a macro layer over an
editor, and modules are not an escape hatch around hidden editor state.

## Product principles

- **A complete game engine.** Agent-native authoring does not excuse missing
  rendering, runtime, physics, audio, animation, input, UI, networking, asset,
  editor, profiling, packaging, or deployment capabilities.
- **AI-first above the graphics layer.** World state and engine operations are
  structured, discoverable, bounded, deterministic where practical, and cheap
  for an agent to inspect and change.
- **One contract, many clients.** C# modules, MCP, CLI, Studio, tests, and future
  integrations use the same typed command, runtime, observation, and diagnostic
  contracts.
- **Agents author games; the engine supplies primitives.** The engine provides
  generic, composable facilities and feedback. It does not generate game
  content on an agent's behalf or privilege a genre, controller, or gameplay
  loop.
- **Closed-loop work is the default.** An agent can discover capabilities,
  make a targeted change, validate it, run it, inspect structured observations
  and visual evidence, repair failures, package the result, and audit the
  deliverable without hidden manual steps.
- **Inspectable over magical.** Stable identifiers, schemas, diagnostics,
  provenance, revisions, transactions, dry runs, and next actions make behavior
  legible to both agents and professional developers.
- **Efficient context use.** Queries support summaries, filters, pagination,
  projections, and targeted evidence so useful work does not require dumping an
  entire project into an LLM context window.
- **Portable authored code.** Game modules build against the shipped SDK and
  never depend on the Rekall AGE source tree.
- **Professional ownership.** Rekall AGE is proprietary software. Its product
  identity, distribution, compatibility policy, security posture, and support
  boundaries must be explicit.

## Acceptance question

For every engine feature, ask:

> Can an AI agent discover this capability, use it precisely through MCP or a
> portable C# module, observe the result, diagnose failure, and prove the game
> still works without relying on hidden editor state?

If the answer is no, the capability is not yet agent-native.
