# Runtime checkpoint component identity plan

## Goal

Align protected runtime evidence with the component identities produced and
consumed by generic agent-authored modules.

## Contract

1. Accept both namespace-qualified `Game.*` component identities and exact
   unqualified CLR component identities used by authored runtime systems.
2. Continue rejecting canonical engine-owned `Rekall.*` components as the
   required agent-owned state proof.
3. Apply the same identity rule to existence, changed-property, delta-property,
   and copyable recovery guidance.
4. Keep actual attachment and assertion truth authoritative in
   `rekall.runtime.inspect_scene`; policy admission validates evidence shape,
   not scene contents.

## Verification

- Prove a scaffold-style unqualified component reaches the runtime tool.
- Prove a built-in-only component does not satisfy agent-owned coverage.
- Run the complete language-agent suite and locked installed-product gate.
- Repeat the unchanged real-Qwen acceptance benchmark.
