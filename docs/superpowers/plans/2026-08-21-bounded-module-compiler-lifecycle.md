# Bounded Module Compiler Lifecycle Plan

Date: 2026-08-21

Status: complete

## Objective

Ensure every agent-authored module compilation has a finite engine-owned
deadline, terminates its entire compiler process tree on timeout or external
cancellation, drains bounded evidence, and returns a stable repairable error.
The engine must never leave Studio or an MCP agent awaiting a wedged compiler
indefinitely.

## Measured failure

Installed real-Qwen benchmark 21 launched `dotnet build` at 03:48:31. The
process became idle and remained alive for more than nine minutes while Studio
waited with no deadline. Manual termination allowed the same agent session to
recover and continue, proving the missing lifecycle boundary rather than game
content was responsible for the stall.

## Design

- Default module compilation deadline: two minutes per module.
- External command cancellation and internal timeout are distinguished.
- Both paths attempt `Kill(entireProcessTree: true)` and bounded exit cleanup.
- Timeout returns `REKALL_MODULE_BUILD_TIMEOUT`, exit code `-1`, and no receipt.
- Ordinary compiler failures remain `REKALL_MODULE_BUILD_FAILED`.
- Tests inject only the process launcher and deadline; production always uses
  the canonical `dotnet build` command and arguments.

## Tasks

- [x] Add a real-process test that wedges, times out quickly, returns the stable
  code, emits no receipt, and leaves no compiler process alive.
- [x] Implement deadline, cancellation, process-tree cleanup, and bounded
  timeout evidence without changing successful build behavior.
- [x] Pass focused/full verification and the locked installed distribution.
- [x] Rerun the unchanged real-Qwen benchmark from an empty project.
- [x] Record, commit, push, and continue to the next measured blocker.

## Outcome

Six focused build-command tests pass, including real wedged child processes for
both internal timeout and external cancellation. The timeout path returns
`REKALL_MODULE_BUILD_TIMEOUT`, exit code `-1`, no receipt, and leaves no child;
external cancellation kills the child tree and remains cancellation.

The zero-warning/error locked Release gate passed 980/980 engine and 7/7
Studio tests twice, then completed the installed matrix. The 1,186-payload-file
archive is 201,528,876 bytes with SHA-256
`5DE2A82788093487520C6B9E33DA42AF0DAB19038512D9B340225D57D285A4A4`.

Clean unchanged real-Qwen benchmark 22 completed without manual intervention or
a compiler hang; successful builds took under one second and reported
`timedOut:false`. The game remained red at 64 turns: 45/76 calls succeeded,
the final viewport had zero renderables, validation reported one blocker, and
no package existed. Independent inspection found validation was itself
fail-open for distant unknown `Rekall.*` types, hiding seven
`Rekall.Components.Transform3D` hallucinations. Evidence SHA-256 is
`2D43EAC81EAC088DC2EF0CF0DBDE175CDBE9B4E7A5C205DA3A99B8453F269CA9`.
