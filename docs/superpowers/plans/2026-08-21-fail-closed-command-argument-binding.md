# Fail-Closed Command Argument Binding

## Goal

Ensure AI-authored tool calls never appear to execute while misspelled or
invented top-level arguments are silently discarded.

## Evidence

Installed Benchmark 46 repeatedly called `rekall.runtime.inspect_scene` with
`inputFrames` and `frameCount` instead of the advertised canonical argument
shape. The checkpoint policy intercepted the malformed call and reported only
missing coverage, causing dozens of ineffective retries rather than one exact
binding repair.

## Implementation

1. Add red dispatcher coverage proving an unknown top-level field fails before
   command execution with a stable code, exact field, allowed names, and command
   contract.
2. Preserve documented type-directed aliases by replacing the alias with its
   canonical field during normalization rather than retaining both names.
3. Add a red language-agent policy regression proving a runtime inspection with
   an unknown top-level field reaches the typed dispatcher instead of being
   hidden by checkpoint coverage policy.
4. Implement bounded case-insensitive validation and runtime-policy pass-through.
5. Run focused tests, the locked installed-product gate, and unchanged clean
   real-Ollama Benchmark 47.

## Acceptance

- an unknown top-level argument cannot execute or mutate a command;
- the error identifies unknown and allowed fields and includes the bounded exact
  command contract;
- canonical casing and supported aliases continue to work;
- malformed runtime inspection calls receive binding evidence before checkpoint
  coverage evidence;
- native and losslessly encoded typed arrays remain compatible.
