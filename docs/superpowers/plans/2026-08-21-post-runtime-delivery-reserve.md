# Post-runtime delivery reserve plan

## Goal

Guarantee a small, bounded delivery window when an agent proves executable
gameplay at the end of its normal or repair turn budget, without making the
authoring loop unbounded or weakening runtime evidence requirements.

## Contract

1. Add an explicitly bounded request option for post-runtime delivery turns.
2. Activate it at most once, only after a successful runtime inspection with
   effective input, an attached `Game.*` component assertion, and a meaningful
   state-transition assertion.
3. Extend only when fewer than the configured delivery turns remain; early
   checkpoints must not consume the one-shot reserve.
4. Preserve the absolute 256-turn safety ceiling and all existing completion,
   freshness, and terminal-workflow policies.
5. Prompt the agent to perform validation, smallest repairs, package, proof,
   and audit; subsequent mutations still require fresh runtime evidence.

## Verification

- Prove the new behavior red-first with focused language-agent tests.
- Cover late activation, no activation without qualifying coverage, and the
  one-shot bound.
- Run the focused agent suite, then the locked build/distribution gate.
- Repeat the unchanged installed real-Qwen Lumen Vault acceptance benchmark.
- Record evidence in `docs/production/PROGRESS.md`, commit, and push.
