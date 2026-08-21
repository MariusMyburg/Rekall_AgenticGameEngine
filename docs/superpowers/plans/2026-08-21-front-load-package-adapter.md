# Front-load Package Adapter After Runtime Proof

## Goal

Prevent a packaging task from discovering the required generic playable adapter
only after its first successful gameplay checkpoint, while preserving the rule
that game behavior remains in the agent-authored runtime-system module.

## Evidence

Installed Benchmark 34 passed a semantic-input runtime checkpoint, then rebuilt,
attempted packaging, discovered the missing `IRekallAgePlayableModule`, scaffolded
it, rebuilt, and exhausted its protected reserve when that mutation correctly
made the earlier runtime proof stale.

## Implementation

1. Add a regression assertion that the bounded post-checkpoint delivery message
   gives the exact `rekall.module.scaffold_playable` ordering for packaged tasks.
2. Update the just-in-time message to scaffold the generic package-proof adapter
   before the final build, keep gameplay in the runtime-system module, and refresh
   runtime proof once after that final build.
3. Run focused agent tests, then the locked production validation and a clean
   installed Ollama benchmark before recording the result.

