# Runtime Checkpoint Argument Normalization Plan

Date: 2026-08-21

Status: complete

## Objective

Make the agent's protected gameplay-checkpoint policy evaluate the same
losslessly normalized typed arguments that the command boundary already
accepts. A bounded JSON-encoded array supplied for an array field must not be
rejected by policy before the generic command normalizer can deserialize it.
Malformed, scalar, oversized, or wrong-shaped values continue to fail closed.

## Measured failure

Fresh installed real-Qwen Lumen Vault benchmark 20 supplied valid JSON arrays
for `inputs` and `assertions`, but encoded each array as a JSON string. The
generic command registry already supports type-directed decoding; the earlier
agent checkpoint policy inspected only the raw `JsonNode` shape and rejected
the same call repeatedly. The runtime never executed and direct repair evidence
was unavailable.

## Tasks

- [x] Add a failing agent-policy test proving encoded `inputs` and `assertions`
  reach the real tool executor and count as checkpoint coverage.
- [x] Add bounded, non-mutating structured-array projection for policy checks,
  including nested encoded arrays where the policy reads them.
- [x] Prove malformed, scalar, and oversized strings remain rejected.
- [x] Pass focused and full verification, rebuild the installed distribution,
  and rerun the unchanged real-Qwen benchmark.
- [x] Record evidence, commit, and push before selecting the next measured
  blocker.

## Outcome

The new encoded-array regression failed before implementation and all seven
runtime-checkpoint policy tests passed afterward. The bounded policy view
accepts valid arrays up to 1,000,000 characters without mutating the model call;
malformed JSON, object/scalar shapes, and oversized strings fail closed.

The locked Release pipeline built with zero warnings/errors, passed 978/978
engine and 7/7 Studio tests twice independently, and completed the installed
acceptance matrix. The 1,186-payload-file archive is 201,524,293 bytes with
SHA-256
`6EC4582475E075B27E8E2E99383B37AD1D4E3076B535361AFDA39111E03020DF`.

Unchanged real-Qwen benchmark 21 proved the fix through the installed product:
encoded checkpoint arrays reached runtime, semantic input projected, and tool
call 67 passed three authored gameplay assertions. The run was not clean
acceptance: an unbounded child `dotnet build` wedged and was manually
terminated, after which the agent recovered; the 64-turn run ended with one
blocking UI-canvas issue, six viewport renderables, and no package. Evidence
SHA-256 is
`BC644243E78375936006D3E907890B18FE93541642F48650083EF10312F2BE4C`.
The next measured generic blocker is bounded compiler process lifecycle.
