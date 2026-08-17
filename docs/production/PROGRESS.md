# Rekall AGE Production Progress

This is the durable execution ledger for Rekall AGE. Update it only from
verified repository or acceptance evidence. Conversational recency does not
change the priority order.

Last verified: 2026-08-17 21:53 Africa/Johannesburg  
Branch: `codex/production-foundation`  
Current commit: `e181160` (`feat: skin imported GLB meshes from runtime poses`)

## Product objective

Build Rekall AGE into a proprietary, production-quality, AI-first C# game
engine. It must let professional developers and AI agents author arbitrary
games through generic, inspectable runtime primitives, SDK helpers, structured
diagnostics, and portable MCP/tool contracts. The engine supplies capability;
agents author the game.

## Stable priority order

1. Generic runtime and rendering primitives.
2. Agent SDK, MCP/tool contracts, diagnostics, and bounded repair loops.
3. Closed-loop authoring proof using packaged, installed binaries.
4. Reliability, performance, security, packaging, and release hardening.
5. Rekall.Age.Studio as a professional consumer of the proven contracts.

Studio is important, but it does not define or reorder the engine foundation.

## Verified status

- Windows distribution: fresh 194,618,167-byte win-x64 archive assembled.
- Canonical verification: 558/558 Release tests passed twice independently.
- Installed acceptance: project/module workflows, packaging and relocation,
  runtime UI, animation, and audible audio paths have installed-binary proof.
- Local agent: Ollama currently uses `qwen3.5:35b` through its native API.
- Agent authoring: both source and installed multi-subsystem benchmarks created
  and repaired UI, animation, and audio content using tool calls.
- Runtime animation: generic clip playback, weighted layers, crossfades,
  deterministic resume, GLB skeletal channels, runtime joint poses, imported
  JOINTS_0/WEIGHTS_0, and CPU skinning before Vulkan submission are covered.
- Diagnostics: runtime inspection exposes UI/audio/animation state; viewport
  analysis reports severe clipping and invisible text without irrelevant
  camera advice for UI-only scenes.

## Current gaps

- Capture installed, visibly rendered skeletal animation evidence rather than
  relying only on state and vertex assertions.
- Expand the installed Ollama benchmark across generic 2D, 3D, physics,
  package relocation, deliberate faults, diagnosis, and repair.
- Add broader performance budgets, soak/device-loss recovery, security threat
  tests, compatibility fixtures, and release-operability evidence.
- Complete advanced animation coverage such as cubic interpolation, morph
  targets, complex transform fixtures, and generic state-graph primitives.
- Replace the current Studio facade with a professional workbench only after
  its runtime/authoring contracts are stable and independently proven.

## In progress

Produce an installed-distribution skeletal-animation capture that demonstrates
the shipped runtime reading a skinned GLB, advancing its pose, deforming its
mesh, and presenting an informative frame with structured inspection evidence.

## Next after the current item

Run the expanded installed Ollama gauntlet, classify failures by generic engine
contract, fix those contracts with regression tests, then begin quantified
production hardening. Studio work follows as a consumer of those results.

## Evidence index

- `docs/production/2026-08-17-engine-maturity-audit.md`
- `docs/production/2026-08-17-ollama-authoring-benchmark.md`
- `docs/superpowers/plans/2026-08-17-runtime-subsystems.md`
- `Artifacts/TestResults/release-pass-1.trx`
- `Artifacts/TestResults/release-pass-2.trx`
- `Artifacts/Distribution/Rekall-AGE-0.1.0-preview.1-win-x64.zip`

## Update rule

At every verified milestone, update the timestamp, verified status, current
gaps, in-progress item, and next item in this file in the same commit as the
milestone documentation or immediately after the verification completes.
