# Shader Libraries Implementation Plan

**Goal:** Add bounded reusable shader includes across authoring, compilation,
hashing, CLI, MCP, and diagnostics.

- [x] Write failing preprocessing, traversal, missing, cycle, pragma-once,
  bounds, compilation, cache-key, command, CLI, and MCP tests.
- [x] Implement the C# preprocessor and stable diagnostics.
- [x] Integrate expanded source into validate/write/resolve/hot-reload paths.
- [x] Add include write/read/list/preprocess commands plus CLI/MCP exposure.
- [x] Run focused tests, full rendering/input/Studio suites, and zero-warning
  Debug/Release builds; document and commit.
