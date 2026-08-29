# Kimi and local GGUF provider integration

Research checked on 2026-08-30 against provider-owned documentation.

## Kimi

Rekall AGE uses Kimi's OpenAI-compatible Chat Completions surface at
`https://api.moonshot.ai/v1/chat/completions`, sends the API key as a Bearer
credential, and discovers the account's available models from `GET /v1/models`.
The current documented default is `kimi-k3`. Multi-turn requests replay prior
assistant messages and matching `tool_call_id` tool results. Kimi K3 preserved
reasoning is replayed through `reasoning_content`; K3 reasoning effort is
normalized to the supported `low`, `high`, or `max` values.

The Studio credential can come from `KIMI_API_KEY`, the official
`MOONSHOT_API_KEY`, or a session-only password field. `REKALL_AGE_KIMI_URL`
overrides the API base for a compatible gateway. Credentials and provider
response bodies are never included in diagnostics.

Official references:

- https://platform.kimi.ai/docs/api/overview
- https://platform.kimi.ai/docs/api/chat
- https://platform.kimi.ai/docs/api/list-models
- https://platform.kimi.ai/docs/api/tool-use

## Local GGUF through Ollama

Ollama's supported GGUF import flow is a generated `Modelfile` containing an
absolute `FROM` path followed by `ollama create <model> -f <Modelfile>`. Studio
implements that flow behind a `.gguf` file picker. It verifies the file exists,
has the `.gguf` extension and `GGUF` magic header, uses a deterministic safe
local model name, deletes the transient Modelfile, refreshes Ollama discovery,
and selects the imported model only when Ollama advertises both completion and
tool capability. AGE never displays the local source path in status or error
facts.

Official references:

- https://docs.ollama.com/import
- https://docs.ollama.com/modelfile
- https://docs.ollama.com/cli

The Local Ollama default is `qwen3.8:27b`, the current official tool-capable
Ollama tag: https://ollama.com/library/qwen3.8:27b
