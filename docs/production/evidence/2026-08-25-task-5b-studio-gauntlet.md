# Task 5B Studio Headless-Gauntlet Evidence

Date: 2026-08-25 (Africa/Johannesburg, UTC+02:00).
Status: complete; Studio 65/65, engine 1,815/1,815, solution build clean.

This tracked record is the durable clean-checkout evidence for Task 5B. Raw
TRX/sequence/dump files are deliberately retained only in the ignored local
evidence directories named below; their sizes and SHA-256 hashes identify the
exact artifacts without committing multi-megabyte diagnostics.

## Root cause and phase ledger

The unchanged Studio test
`StudioViewModelTests.HeadlessAutomationCreatesProjectAndCompletesAgentGauntlet`
was reproduced under a 30-second hang boundary. Its TRX ran from
`2026-08-25T02:48:36.932+02:00` through `02:49:08.956+02:00` (32.023 seconds)
without a completed result, and its sequence named only that test with
`Completed="False"`. Earlier evidence named the same active case after five
minutes; the original complete run remained CPU-active for 31:06.

A one-shot gauntlet call against the retained failed Studio project completed
in 746 ms and exposed the first non-progressing boundary:

| Phase | Result | Evidence |
| --- | --- | --- |
| `project-created` | pass | Existing Studio project reused. |
| `scene-created` | pass | Existing `Main` scene reused. |
| `scene-preserved` | erroneous pass | The loaded scene had zero entities. |
| `package-created` | fail | Failure occurred before copy, audit, or capture. |
| module build | fail | `REKALL_MODULE_PROJECTS_MISSING`. |

A temporary three-turn diagnostic (reverted before the fix) completed in 2.376
seconds. Turn 1 passed `rekall.context.engine_status`; turns 2 and 3 repeated
the identical failing `rekall.workflow.agent_authoring_gauntlet`; the viewport
remained at zero renderables. With the production `MaxTurns` intentionally
unset, the deterministic fixture kept returning the non-terminal failed
gauntlet while the transcript and tool ledger grew. Fresh-project and authored
3D-scene comparisons completed in 8-9 seconds, isolating the bad predicate to
"scene file exists" rather than "scene contains authored entities."

## Fixed invariants and TDD

The first regression creates a persisted, zero-entity editor scene. Before the
content-based preservation fix it failed in 83 ms with
`REKALL_MODULE_PROJECTS_MISSING`. The fixed preservation invariant is:

- preserve an existing scene only when it contains at least one authored
  entity;
- author the generic blueprint and agent-owned module for a missing or empty
  scene.

Fix Round 1 strengthened the same regression before further production changes.
The witnessed RED completed in 8 seconds (9.913 seconds wall) with
`REKALL_RUNTIME_ASSERTION_FAILED`: the
`Game.Modules.AgentGauntlet.GauntletState` component was missing, its
`progress` delta was missing, and `delta.position2d.x` was `0`.

The gauntlet-authored marker now attaches that `Game.*` component with
`progress = 0` and a native `Rekall.InputActionMap` for the semantic action
`agent.gauntlet.advance`. The agent-authored module registers both the component
and `AgentGauntletRuntimeSystem`; the system consumes the semantic action, uses
`context.DeltaTime`, and applies immutable component and transform updates. The
workflow builds the module and runs `rekall.runtime.inspect_scene` after the
latest scene/module mutation and before packaging. One representative input
frame (`value = 1`, down/pressed, `DeltaSeconds = 1`) must satisfy all three
executable assertions:

- the attached `Game.Modules.AgentGauntlet.GauntletState` exists;
- `delta.component.property(progress) equals 1`;
- `delta.position2d.x equals 1` (marker X moves from 160 to 161).

Any failed build or runtime assertion fails the gauntlet. Package archive,
audit, nonblank capture, and proof-artifact checks remain mandatory. No timeout,
retry, skip, scope reduction, adapter-only bypass, or weakened assertion was
added. The expanded regression also reads the generated module source and
requires component/system registration, semantic action consumption,
engine-provided delta time, immutable component mutation, transform mutation,
archive existence, ready audit, and captured nonblank proof output.

The new gauntlet action map made one existing package-capture test's old
"exactly one action in the whole scene" assumption invalid. Its witnessed
failure showed the intended neutral `agent.gauntlet.advance` plus the injected
`capture.move`. The repaired test is stronger and exact: it requires exactly
both named actions, a neutral gauntlet action, and the original complete
`capture.move` state. That focused case passed 1/1 in 18 seconds (27.360 seconds
wall including build).

## Exact final verification

All commands below were run from the assigned worktree. Because C: had only
about 91 MB free and F: is exFAT, final filesystem-sensitive gates used the
authorized short NTFS root `D:\RekallTask5BTemp`:

```powershell
$env:TEMP = 'D:\RekallTask5BTemp'
$env:TMP = 'D:\RekallTask5BTemp'

dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj -c Debug --no-build --no-restore --filter "FullyQualifiedName~Rekall.Age.Tests.Workflows.AgentAuthoringGauntletTests"
dotnet test tests\Rekall.Age.Studio.Tests\Rekall.Age.Studio.Tests.csproj -c Debug --filter "FullyQualifiedName=Rekall.Age.Studio.Tests.StudioViewModelTests.HeadlessAutomationCreatesProjectAndCompletesAgentGauntlet"
dotnet test tests\Rekall.Age.Studio.Tests\Rekall.Age.Studio.Tests.csproj -c Debug --no-build --no-restore
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj -c Debug --no-build --no-restore
dotnet build Rekall.AGE.sln -c Debug --no-restore --verbosity minimal
```

| Gate | Local start | Exact result | Timing |
| --- | --- | --- | --- |
| Gauntlet class | `2026-08-25T03:56:32.9371073+02:00` | 4 passed, 0 failed/skipped | 29 s test; 30.719 s wall |
| Original Studio case 1 | `2026-08-25T03:52:49.6838035+02:00` | 1 passed | 11 s test; 19.257 s wall |
| Original Studio case 2 | `2026-08-25T03:53:08.9836905+02:00` | 1 passed | 10 s test; 15.571 s wall |
| Original Studio case 3 | `2026-08-25T03:53:24.5552260+02:00` | 1 passed | 10 s test; 15.583 s wall |
| Full Studio project | `2026-08-25T03:53:49.1134398+02:00` | 65 passed, 0 failed/skipped | 56 s test; 57.891 s wall |
| Full engine project | `2026-08-25T03:47:07.6236700+02:00` | 1,815 passed, 0 failed/skipped | 4 m 32 s test; 273.661 s wall |
| Debug solution build | `2026-08-25T03:54:58.3844047+02:00` | 0 warnings, 0 errors | 6.35 s build; 6.550 s wall |

## Environmental gate chronology

The failed broad gates were diagnosed and retained rather than hidden by a
retry:

1. At `03:25:08.6099301+02:00`, the default-C: engine run ended after 224.217
   seconds with 1,505 passed/310 failed. The first failures were
   `System.IO.IOException: There is not enough space on the disk` while creating
   `C:\Users\Marius\AppData\Local\Temp\rekall-age-tests\<guid>`. Inspection
   found about 91 MB free and a pre-existing generated test root containing
   47,412 directories, 363,847 files, and 55,500,588,104 bytes. Exact recursive
   cleanup outside the workspace was blocked by policy; nothing was deleted.
2. At `03:31:42.2370339+02:00`, a bounded F:-workspace TEMP rerun ended after
   395.579 seconds with 1,791 passed/24 failed. `Get-Volume` proved F: is exFAT;
   failures explicitly required local NTFS hard-link/junction semantics, and
   the long path also exceeded a worker launch limit. Its exact temporary tree
   was subsequently removed with bounded `git clean -c core.longpaths=true` and
   absence was proved.
3. At `03:40:21.1787596+02:00`, the authorized short D: NTFS rerun reached
   1,814 passed/1 failed in 276.932 seconds. The sole related stale action-count
   assertion described above was reproduced alone, strengthened, and passed.
4. The final unfiltered D: NTFS run passed 1,815/1,815.

## Raw artifact identity

Paths are relative to
`.superpowers/sdd/2026-08-24-high-fidelity-forward-plus-foundation/` unless
otherwise noted.

| Retained ignored artifact | Bytes | SHA-256 |
| --- | ---: | --- |
| `task-5b-evidence/task-5b-original-hang.trx` | 2,438 | `7F33FC5CEF4B86E195D502DC131BACF0D6601C2671474BBA41DCF24B1C4A0D69` |
| `task-5b-evidence/3a19d10e-c35d-4861-9113-f893812cbbe9/Sequence_0d85d39511914560b6ead8ced3845bc9.xml` | 460 | `87A245EA3925EA2415D68200914C900D93E99646103B02A528C2024901398E3B` |
| `task-5a-evidence/35827907-8866-4a94-b4c7-9a394a02628f/testhost_57332_20260825T020605_hangdump.dmp` | 23,906,846 | `E468271F5242ABE8C18C7157768365A14EB06CC1F58109633341AAE808C8CA67` |
| `task-5b-evidence/task-5b-fix1-gauntlet-class-final.trx` | 7,539 | `53DA5E373ADD43132837195DDB67029E900F519A665DB05C92EE76EF18D7B4CE` |
| `task-5b-evidence/task-5b-fix1-final-original-1.trx` | 3,126 | `FBF889799A98901C94C8E9CF378815DC822779D4D5A3DD644B2C55A6ED61079D` |
| `task-5b-evidence/task-5b-fix1-final-original-2.trx` | 3,126 | `8374EE4562BC746401C8AFB04E3A01B11BD129963A6727AB662F12C4616053EF` |
| `task-5b-evidence/task-5b-fix1-final-original-3.trx` | 3,126 | `FC9BC1BB92A22906A59D1293E0309FDD5B8D446982B3000F5F4CC31F5801B921` |
| `task-5b-evidence/task-5b-fix1-studio-full-final.trx` | 99,889 | `46E6B558EE3B8F2B70D4AD06B600BD7166CC176C44A6DA4681232AF2F23BE8EB` |
| `task-5b-evidence/task-5b-fix1-engine-full.trx` (C: disk-full) | 7,508,144 | `D50726995D5EAF1F06695369FFC52673FFA880A1A38CB6FB72A81E96F3B2F7AC` |
| `task-5b-evidence/task-5b-fix1-engine-full-green.trx` (F: exFAT) | 2,801,694 | `4FC7968FBEE2D3D7FE7E8D4FA307F5410CA73346D0B15CEB76C098F3A7CCE4B6` |
| `task-5b-evidence/task-5b-fix1-engine-full-ntfs.trx` (stale assertion) | 2,657,498 | `0081F3ED214C90A65B8E386694105EAAD4CC7539FCF8D4EBE825A7FE420D125A` |
| `task-5b-evidence/task-5b-fix1-engine-full-final.trx` | 2,654,991 | `EB58D4721E99866D86B4CCAC75671909CB4AE54B06E141C274BB02C8C9588150` |

## Residue

Final inspection found zero `testhost`, Rekall module-host/player/Studio
processes and zero staged `session-*` trees in the authorized D: verification
root. Bounded cleanup removed all 2.35+ GB of D: test content and proved zero
remaining entries. Policy blocked removal of the now-empty
`D:\RekallTask5BTemp` directory itself; it contains no files or subdirectories.
The F: redirected verification root was removed and absence proved. Raw TRX,
sequence, and dump files listed above remain intentionally as ignored evidence.
