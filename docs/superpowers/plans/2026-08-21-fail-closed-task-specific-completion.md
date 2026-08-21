# Fail-Closed Task-Specific Completion

## Goal

Prevent task-specific Studio runs from terminating on a narrative self-audit
without successful configured audit-tool evidence.

## Evidence

Installed Benchmark 37 had zero renderables, no package, and no package audit,
but the language-agent loop accepted a second completion narrative after an
engine-status call because the same flag represented both a requested narrative
audit and a successful audit tool.

## Implementation

1. Add a red regression proving narrative claims cannot prime strict completion.
2. Add an explicit strict-audit-evidence request flag and require a successful
   configured priming tool before final narrative completion.
3. Enable strict evidence for task-specific project sessions while preserving
   gauntlet and existing non-strict callers.
4. Run policy/session tests, the locked installed-product gate, and a clean real
   Ollama benchmark.

## Verified result

Completed. The strict tool-evidence contract passed 40 focused language/session
tests, 7 Studio tests, both 1,013-test locked Release passes, and the complete
installed-product matrix. Clean installed Benchmark 38 returned a real failure
instead of accepting unsupported completion: no package audit occurred, so the
run exhausted its turn limit with `Succeeded=False`. That benchmark exposed the
next independent bottleneck—exact boolean and scalar-input SDK compiler repair—
which is tracked in the production ledger.
