# Ollama embedded-agent smoke

Date: 2026-08-17

## Environment

- Provider: Ollama native API at `http://127.0.0.1:11434`
- Model: `qwen3.5:35b` (local, 23,869,191,742 bytes reported by `/api/tags`)
- Task: call `rekall.context.engine_status`, then report the product name and readiness without making changes
- Turn limit: 2

## Results

| Tool exposure | Completed | Turns | Tool calls | Prompt tokens | Completion tokens |
|---|---:|---:|---:|---:|---:|
| All registered schemas up front | yes | 2 | 1 | 30,905 | 508 |
| Progressive discovery (`engine_status` + `rekall.tools.search`) | yes | 2 | 1 | 2,993 | 245 |

Progressive discovery reduced prompt tokens by 90.3% on the same model and task.
The final answer correctly identified `Rekall AGE` and reported it ready in both runs.

## Acceptance represented

- live Ollama model discovery
- explicit model selection (no silent default)
- native Ollama function-tool request and response mapping
- direct execution against the Rekall AGE command registry
- structured tool results and transaction-aware mutation path
- bounded turns and measured token/duration accounting
- progressive native-tool exposure after schema search

This is a connectivity and efficiency smoke, not yet the multi-task installed-engine
authoring benchmark. The latter must measure complete create/repair/package tasks and
quality gates across 2D, 3D, UI, audio, animation, and physics.
