# Front-loaded runtime assertion evidence plan

## Goal

Ensure failed runtime assertions expose exact bounded repair evidence before
large runtime inspection payloads can consume an agent's tool-result budget.

## Contract

1. Put failed entity, subject, component/property, operator, expected value,
   actual value, and comparison explanation into the command summary.
2. Bound individual values, detail count, and total summary size.
3. Retain the full structured runtime result and assertion result collection.
4. Do not weaken assertion evaluation or convert failures into success.

## Verification

- Prove missing-component and numeric-transition failures are self-contained in
  the front-loaded summary.
- Prove summaries remain bounded with many/large assertions.
- Run runtime/CLI/agent regressions, the locked installed-product gate, and the
  unchanged real-Qwen benchmark.
