# Shader Libraries and Bounded Preprocessing Design

## Goal

Allow agents to author reusable GLSL libraries and compose production shader
pipelines with deterministic, safe, inspectable preprocessing.

## Contract

- Include files live under project `Shaders/` and use `.glslinc`.
- Vertex/fragment sources use one full-line directive:
  `#include "relative/path.glslinc"`.
- Paths resolve relative to the including file, remain under `Shaders/`, and
  cannot be absolute, traverse, escape through links, or exceed bounded sizes.
- Nested includes are supported to depth 16, at most 64 unique files, and at
  most 1 MiB expanded UTF-8 source.
- Include cycles, missing files, malformed directives, traversal, oversize
  graphs, and duplicate ambiguous paths produce stable diagnostics.
- `#pragma once` suppresses repeated expansion of the same canonical include.
- Preprocessing emits source boundary comments and dependency paths so agents,
  hot reload, cache keys, and diagnostics can explain the compiled result.
- Pipeline hashes include expanded source; changing an include invalidates the
  relevant pipeline automatically.
- CLI/MCP can write/read/list includes and preprocess a shader without assigning
  it to an entity.

## Non-goals

This milestone does not introduce a new shader language, copy Godot shader
code, relax AGE's material ABI, or claim WebGPU/compute support.

