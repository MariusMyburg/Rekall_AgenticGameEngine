# Early runtime authoring checkpoint plan

## Goal

Ensure tasks that explicitly require executable gameplay establish their
agent-owned runtime module before consuming the authoring budget on scene polish.

## Contract

1. Add a bounded request option controlling successful scene mutations allowed
   before runtime-module authoring; default four, zero disables the policy.
2. Count generic scene/entity/component mutations, not genre-specific content.
3. Once the bound is reached without a successful runtime-system scaffold or
   source write, defer further world mutation, validation, packaging, capture,
   and delivery operations.
4. Keep context, schema/SDK inspection, module source operations, scaffold, and
   build available so the agent can establish the executable slice.
5. Return a structured recovery payload and persistent prompt. The existing
   build/runtime checkpoint and freshness policies remain authoritative after
   module authoring begins.
6. Do not affect tasks where runtime behavior assertions are not required.

## Verification

- Prove the threshold, deferred non-execution, recovery, and non-runtime bypass
  red-first.
- Run the full language-agent policy suite and locked installed-product gate.
- Repeat the unchanged real-Qwen benchmark and record the next blocker.
