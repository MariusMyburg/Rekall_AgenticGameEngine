# Restricted Agent-Authored Module Host Design

## Purpose

Rekall AGE deliberately lets developers and agents author arbitrary C# game
behavior. Verified build receipts prevent accidental or unmanifested artifact
substitution, but the engine still loads that code into the player, CLI, MCP,
and Studio processes with the user's full authority. This tranche moves every
production execution and reflection path for project-authored assemblies into
an OS-restricted worker without weakening the generic C# authoring model.

The supported boundary is Windows 10/11 x64. Built-in engine systems continue
to run in-process. A project without authored modules does not start a worker.

## Approaches considered

### Selected: Windows AppContainer worker plus job-object limits

A broker verifies the existing module receipts, copies the manifest-verified
worker runtime plus only receipt-verified module outputs to a private immutable
staging tree, grants one derived AppContainer SID only the access required to
execute that tree, and launches a persistent worker
with no network capabilities. The worker is also assigned to a job object with
kill-on-close, one-active-process, and memory limits. Runtime data crosses a
bounded framed protocol over inherited anonymous pipes.

This retains arbitrary C# and provides a real OS access-control boundary.
Microsoft documents AppContainer isolation from unrelated processes,
files/directories, registry, network, devices, windows, and credentials unless
capabilities or ACLs explicitly grant access. Job objects provide process-tree
lifetime and resource limits.

### Rejected: ordinary child process under the user's token

This contains crashes and enables timeouts, but module code could still read or
modify user files, use credentials, access the network, and start processes.
It would be useful reliability work but would not close the security gap.

### Rejected: WASM, a DSL, or static forbidden-API scanning

WASM or a custom DSL would create a second authoring platform and violate the
engine's C#-module vision. Static API scanning is bypassable by reflection,
interop, dynamic loading, or generated IL and is not a security boundary.

## Security contract

The production posture is `windows-appcontainer-restricted`. It means:

- a regular AppContainer token with no explicit network, device,
  broad-filesystem, COM, or child-process capability is used;
- the derived package SID receives read/execute access only to the broker-owned
  staged host/runtime and module inventory;
- the worker receives project/scene/runtime data only through inherited pipe
  handles and cannot open the project root;
- a job object enforces kill-on-close, active-process limit 1, and a 512 MiB
  process/job memory ceiling;
- startup is limited to 10 seconds and each request to 250 milliseconds by
  default; a timeout terminates the entire worker and fails closed;
- protocol messages are limited to 64 MiB, JSON depth to 128, stderr evidence
  to 64 KiB, module count to 256, and pending requests to one;
- the broker never retries a failed gameplay update within the same session,
  never silently disables a module, and never falls back to in-process load;
- a new session may start a fresh worker after the caller's normal player or
  workflow recovery policy decides to restart.

AppContainer containment materially reduces module authority but is not a
formal proof against Windows kernel vulnerabilities. Build receipts remain
unsigned local integrity evidence, not publisher identity. Release signing and
a publisher trust store remain separate requirements.

## Admission and staging

`RekallAgeProjectModuleTrustInspector` remains the admission authority. A new
receipt schema records `windows-appcontainer-restricted` as the required
execution posture. Older `in-process-full-trust` receipts fail with
`REKALL_MODULE_RECEIPT_HOST_POSTURE_MISMATCH` and an executable rebuild action.

The broker builds an immutable load plan from one trust inspection snapshot.
For each module it copies only the main assembly and receipt-declared dependency
inventory to a uniquely named directory beneath engine-controlled temporary
storage. Every copy is length- and SHA-256-verified before publication. Source,
project files, assets, secrets, and the project root are not copied. Reparse
points and path escapes fail closed. The staged tree is read-only to the
AppContainer SID and removed after the worker exits; failure evidence retains
only bounded codes, types, and messages, not module contents.

The host independently rechecks the staged inventory against the serialized
load plan before loading. This is defense in depth against broker/worker
time-of-check/time-of-use mistakes.

## Host protocol

The protocol is versioned independently from the module SDK. Each frame is:

1. a 4-byte little-endian unsigned payload length;
2. exactly that many UTF-8 JSON bytes;
3. one object containing `protocolVersion`, monotonically increasing
   `sequence`, `operation`, and an operation-specific payload.

Requests and responses use the same sequence. Unknown versions, operations,
duplicate/out-of-order sequences, trailing bytes, over-depth JSON, oversized
frames, and missing required fields terminate the worker with stable protocol
codes. Stdout carries protocol bytes only. Stderr is captured separately and
bounded.

Initial operations:

- `host.initialize`: verify load plan, load modules, configure module builders,
  instantiate systems/playable module, and return IDs, priorities, component
  schemas, playable kind, and exact protocol/posture facts;
- `runtime.update`: run exactly one named runtime system for one supplied world
  and frame context, returning one complete world;
- `playable.create`: create one playable state from scene/entity facts;
- `playable.tick`: mutate the worker-owned state from generic input;
- `playable.render`: return bounded text and draw commands;
- `host.shutdown`: acknowledge orderly teardown.

Only one request is active at a time, preserving module instance state and
deterministic ordering. Runtime proxies retain each authored system's declared
ID and priority, so the existing engine execution loop continues to interleave
authored and built-in systems exactly as it does today.

## Engine integration

`RekallAgeRestrictedModuleHostClient` owns the worker and implements async
request/response, deadlines, lifecycle, and disposal. Runtime loading creates
one proxy per discovered authored system. Playable loading creates a brokered
`IRekallAgePlayableGame`; state never enters the engine process. Project
component-schema discovery uses the initialization descriptors instead of
reflecting over project assemblies.

The existing in-process assembly loader remains internal to the worker and
test fixtures only. Production CLI, MCP, Player, Player.Windows, Studio, schema
discovery, runtime inspection, viewport capture, packaging verification, and
packaged games must have no path that loads a project assembly in their own
process.

The distribution includes the self-contained worker and records its files in
the distribution/package manifests. Packaged games launch the worker from a
relative verified location. Source-tree developer commands resolve the locally
built worker explicitly; missing or mismatched workers fail with
`REKALL_MODULE_HOST_NOT_FOUND` or `REKALL_MODULE_HOST_VERSION_MISMATCH`.

## Diagnostics and agent contract

Stable failure families include:

- `REKALL_MODULE_HOST_PLATFORM_UNSUPPORTED`
- `REKALL_MODULE_HOST_NOT_FOUND`
- `REKALL_MODULE_HOST_APP_CONTAINER_FAILED`
- `REKALL_MODULE_HOST_JOB_LIMIT_FAILED`
- `REKALL_MODULE_HOST_START_TIMEOUT`
- `REKALL_MODULE_HOST_PROTOCOL_INVALID`
- `REKALL_MODULE_HOST_MESSAGE_TOO_LARGE`
- `REKALL_MODULE_HOST_REQUEST_TIMEOUT`
- `REKALL_MODULE_HOST_CRASHED`
- `REKALL_MODULE_HOST_MODULE_REJECTED`
- `REKALL_MODULE_HOST_OUTPUT_INVALID`

`rekall.module.inspect_trust` reports admission readiness separately from the
required and active execution posture. A new read-only
`rekall.module.inspect_host` command reports platform support, host/version
resolution, exact limits, AppContainer/job availability, capabilities (none by
default), and executable rebuild/doctor actions without loading project code.
CLI and MCP expose the same schemas. Engine status recommends host inspection.

Module failures include the system/module identifier and bounded exception
type/message from the worker, never a full stack, environment dump, project
content, or arbitrary stderr. Runtime observations may mirror nonfatal
diagnostic facts, but a failed authored gameplay update remains a blocking
session failure.

## Compatibility and performance

The C# SDK interfaces and agent-authored source shape do not change. Rebuilding
is the only migration required. The framed protocol serializes existing
immutable records with deterministic web JSON options and source-generated
metadata. Persistent workers avoid per-frame process startup. Each proxy call
preserves priority semantics; small-world 600-frame acceptance must sustain at
least 30 FPS, and a representative 1,000-entity/10-component world must stay
within an explicit p95 request budget recorded by the benchmark rather than an
unsubstantiated zero-copy claim.

## Verification

Tests cover framing, partial reads/writes, bounds, sequences, cancellation,
timeouts, crash/hang behavior, worker cleanup, staged inventory confinement,
hash rechecks, receipt-posture migration, deterministic runtime/playable
semantics, schema discovery, priority ordering, and absence of in-process loads.

Windows integration proves the sandbox cannot read a non-granted sentinel,
write the project, connect to loopback or the Internet, or keep a child process;
it can read its staged assemblies and complete protocol calls. Job memory and
timeout fixtures terminate with exact codes. Installed proof scaffolds and
builds an agent-authored module, inspects the restricted posture through CLI
and MCP, runs/captures it, attempts forbidden file/network/process behavior,
and verifies the engine and project remain intact after rejection. The full
Debug and locked two-pass Release/distribution gate then reruns.

## Platform references

- [Microsoft: Launch an AppContainer](https://learn.microsoft.com/en-us/windows/win32/secauthz/implementing-an-appcontainer)
- [Microsoft: AppContainer isolation](https://learn.microsoft.com/en-us/windows/win32/secauthz/appcontainer-isolation)
- [Microsoft: Job Objects](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects)

## Explicit non-goals

- macOS/Linux sandbox implementation in this Windows-first tranche;
- publisher identity, Authenticode policy, or remote module marketplaces;
- granting network, user-file, device, COM, registry, or native-plugin
  capabilities to authored modules;
- parallel module calls or multiple workers per game;
- changing the generic world, input, event, rendering, or module SDK model.
