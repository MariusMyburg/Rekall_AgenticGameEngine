# Task-Anchored Package Audit Recovery

## Goal

When package proof is visually weak, redirect the agent to repair the original
requested game instead of adding unrelated diagnostic or showcase filler.

## Evidence

Installed Benchmark 36 received an uninformative-frame audit failure, then added
`Cube` and `CubeFaulted`, introduced an unresolved `default` shader, and displaced
the requested Lumen Vault content while leaving package proof stale.

## Implementation

1. Add a red agent-policy regression for failed package-audit recovery.
2. Inject a bounded recovery message containing the audit reason, an original-task
   anchor, a prohibition on generic filler, and the required validation/runtime/
   package/audit refresh sequence.
3. Run the language-agent suite, locked installed-product gate, and a clean real
   Ollama benchmark before selecting the next measured blocker.

