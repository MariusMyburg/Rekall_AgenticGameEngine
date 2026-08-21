# Runtime SDK compiler recovery plan

## Goal

Turn failed agent-authored runtime-module compilation into compact, exact,
immediately actionable SDK repair evidence.

## Contract

1. Detect compiler failures involving Rekall runtime SDK contracts without
   guessing game behavior or rewriting user source.
2. Put correct immutable entity, transform, component-state, and world-update
   patterns before verbose compiler diagnostics so bounded agent previews retain
   them.
3. Suggest a fully populated `rekall.module.inspect_runtime_sdk` query and
   source inspection workflow; never emit an empty-query recovery call.
4. Leave unrelated C# compiler failures and timeout semantics unchanged.

## Verification

- Prove a nonexistent runtime SDK call returns the exact valid replacement
  patterns and populated inspection command.
- Prove ordinary compiler diagnostics remain present.
- Run focused build/module tests, the locked installed-product gate, and the
  unchanged real-Qwen acceptance benchmark.
