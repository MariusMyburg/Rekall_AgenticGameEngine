# Ollama agent authoring benchmark — 2026-08-17

## Objective

Prove that a local provider-neutral Ollama agent can author and verify generic Rekall AGE runtime content through engine tools within a bounded loop. The task required a new project and scene, a styled UI button, an inline transform animation, scene validation, and deterministic inspection after 30 frames. The gauntlet was explicitly excluded so the benchmark exercised ordinary authoring primitives.

## Model and bound

- Provider: Ollama native chat/tool API
- Model: `qwen3.5:35b`
- Maximum model turns: 16
- Authoring surface: progressive Rekall MCP command discovery and execution

## Iteration evidence

| Run | Result | Prompt tokens | Finding |
| --- | --- | ---: | --- |
| V1 | turn limit | 358,674 | Content was mostly authored, but incremental calls exhausted the budget. |
| V2 | turn limit | 316,707 | Raw-history pruning caused loss of authoring progress. |
| V3 | turn limit | 166,057 | The agent remained in broad tool discovery. |
| V4 | turn limit | 144,565 | The model called a discovered native tool directly; the progressive executor rejected it. |
| V5 | turn limit | 171,111 | Component names and animation properties were plausible but not exact runtime contracts. |
| V6 | turn limit | 224,382 | Atomic blueprint creation worked, but the full component catalog was too large and nested animation/UI types were invented. |
| V7 | turn limit | 186,751 | Focused schema search improved outer component types; nested track type and camera-neutral validation still failed the acceptance gate. |
| V8 | completed in 15 turns | 122,878 | Authored exact runtime contracts and completed bounded runtime inspection. |

## Generic engine changes driven by the benchmark

- Progressive tool discovery exposes only a compact gateway plus commands the agent has discovered.
- The agent retains a bounded persistent execution ledger when raw messages are pruned.
- Exact registered native tool calls are accepted after discovery even when a model bypasses the wrapper.
- `rekall.workflow.create_blueprint_project` atomically applies a complete agent-supplied project/scene blueprint.
- `rekall.module.search_component_schemas` returns focused exact runtime component contracts.
- Animation-track schema guidance requires fully qualified runtime types such as `Rekall.Transform3D` and exact properties such as `X`.
- Validation requires a camera only for active camera-rendered world content; UI-only and nonvisual scenes remain valid.
- `rekall.runtime.inspect_scene` returns a bounded post-simulation entity-state summary so an agent can verify effects, not only subsystem counts.

## Independent V8 verification

- Scene validation: `ok`, 0 blocking issues, 0 warnings.
- Runtime frame: 30 at the deterministic 60 Hz step.
- Entities: 3.
- UI elements: 1; styled button text is `AGENT READY` in the authored scene.
- Animation players: 1.
- Animated entity post-simulation X: `3.000`, the expected midpoint of 0→6 at 0.5 seconds.
- Runtime observations: none.

The benchmark passed the functional acceptance gate. The remaining efficiency signal is that V8 still used repeated discovery and blueprint calls; future benchmark work should reduce redundant calls without weakening the generic authoring surface.
