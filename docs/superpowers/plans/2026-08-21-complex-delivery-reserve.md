# Complex Post-Runtime Delivery Reserve

## Goal

Give complex packaged game tasks enough bounded turns after their first passing
gameplay checkpoint to finish required visuals, final proof, packaging, and audit
without increasing the general authoring budget or allowing an unbounded loop.

## Evidence

Installed Benchmark 35 passed its first executable checkpoint at turn 69. The
existing eight-turn reserve ended at turn 77 after the required playable adapter,
final module build, visual schema discovery, and refreshed runtime proof. Package
creation and audit had no remaining turn.

## Implementation

1. Add a red regression that requires the default reserve to provide exactly 16
   bounded post-checkpoint turns.
2. Increase only `MaxPostRuntimeDeliveryTurns` from 8 to 16; keep the existing
   single-activation and 256-turn hard bounds.
3. Run focused policy tests, the locked installed-product gate, and a clean real
   Ollama benchmark before recording the next measured blocker.

