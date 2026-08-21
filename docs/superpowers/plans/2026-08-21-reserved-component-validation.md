# Reserved Component Validation Plan

Date: 2026-08-21

Status: in progress

## Objective

Make validation fail closed for every unknown `Rekall.*` component type. Offer
automatic add/remove repair only when the intended built-in is unambiguous by
exact final type segment or close edit distance; otherwise report a blocking
diagnostic without guessing.

## Measured failure

Clean real-Qwen benchmark 22 ended with seven entities using
`Rekall.Components.Transform3D`. Project validation silently ignored all seven
because its nearest built-in edit distance exceeded three, then misleadingly
reported only the closer `Rekall.UICanvas` typo. Runtime had zero renderables
and the agent did not receive the true failure set.

## Tasks

- [ ] Add failing tests for a distant exact-suffix alias and a wholly invented
  reserved type.
- [ ] Always emit a blocking reserved-type issue; attach repair only for a safe
  unique suggestion.
- [ ] Prove project repair canonicalizes suffix aliases without guessing the
  wholly invented type.
- [ ] Run full/installed verification and the unchanged real-Qwen benchmark.
- [ ] Record, commit, push, and continue to the next measured blocker.
