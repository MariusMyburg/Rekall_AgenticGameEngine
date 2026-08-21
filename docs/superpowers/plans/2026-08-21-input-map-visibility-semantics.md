# Input-map visibility semantics plan

## Goal

Separate visual entity visibility from semantic input-map activation.

## Contract

1. Process `Rekall.InputActionMap` on both visible and non-rendered entities.
2. Keep `Rekall.InputActionMap.Active` as the explicit enable/disable switch.
3. Preserve visual culling and unrelated entity-system semantics.
4. Preserve source entity identity in projected actions and observations.

## Verification

- Prove a `visible:false`, `Active:true` input configuration projects semantic
  input.
- Prove `Active:false` remains disabled regardless of visibility.
- Run input/runtime regressions, the locked installed-product gate, and the
  unchanged real-Qwen acceptance benchmark.
