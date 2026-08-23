# .NET Web Runtime Proof

Date: 2026-08-22

## Evidence

- Installed official .NET workloads `wasm-tools` and `wasm-experimental`
  (`10.0.111/10.0.100`).
- Published `Rekall.Age.Player.Web` for `browser-wasm` in Release through the
  .NET WebAssembly SDK and Emscripten, including real AGE Core, World, and
  Rendering.Abstractions assemblies.
- Published static payload: 39 files, 11,848,440 bytes before web-server
  compression policy.
- Loaded the output from `http://127.0.0.1:9327/` in the in-app Chromium browser.
- Live C#/JavaScript bridge reported `.NET 10.0.11 / browser-wasm`, `WebGPU`, and
  `DEVICE CONTRACT READY`.
- DOM and visual inspection confirmed the canvas shell rendered; browser logs
  contained zero warnings and zero errors.

## Architectural basis

Microsoft's .NET 10 browser-app documentation supports generated
`[JSImport]`/`[JSExport]` interop and Release publication from a standalone
WebAssembly Browser App. The W3C WebGPU contract exposes adapters/devices,
buffers, textures, bind groups, pipelines, command encoders/buffers, render
passes, and compute passes. AGE's public RenderingDevice deliberately maps to
those concepts while retaining stricter opaque handles and stable diagnostics.

Primary references:

- https://learn.microsoft.com/en-us/aspnet/core/client-side/dotnet-interop/wasm-browser-app?view=aspnetcore-10.0
- https://learn.microsoft.com/en-us/aspnet/core/client-side/dotnet-on-webworkers?view=aspnetcore-10.0
- https://www.w3.org/TR/webgpu/
- https://www.w3.org/TR/WGSL/

## Honest boundary

This is runtime/interop/contract evidence, not a game export. It does not yet
execute an AGE scene, compiled game module, audio mixer, semantic input stream,
or browser gameplay loop. The WebGPU draw boundary is now physically proven
below. A production game-export claim still requires scene/module loading,
browser services, package generation/audit, and a real playable browser
acceptance with deterministic gameplay evidence. WebGL 2 remains a later
compatibility tier rather than a prerequisite for the primary WebGPU path.

## Task 3 linker-safe WebGPU protocol checkpoint

Date: 2026-08-23

The browser executor's trimmed Release publish was initially RED. A clean
`dotnet publish src/Rekall.Age.Player.Web/Rekall.Age.Player.Web.csproj -c Release`
reached assembly optimization and then failed with exactly four `IL2026` trim
analysis errors in `RekallAgeWebGpuProtocol`: generic packet serialization,
runtime-`Type` descriptor deserialization, descriptor reserialization, and
runtime-`Type` command-payload deserialization. The SDK then returned
`NETSDK1144` because trimming could not safely preserve those reflection paths.

The shared WebGPU library now owns source-generated `JsonSerializerContext`
metadata for every protocol packet, all nine supported resource descriptors,
all concrete command payloads, and the browser bridge/initialization envelope.
Protocol entry points explicitly dispatch only those concrete types. Unknown
payload types fail closed with `REKALL_WEBGPU_PROTOCOL_PAYLOAD_TYPE_INVALID`;
camel-case fields, nonnumeric string enums, strict unmapped-member rejection,
the 16 MiB UTF-8 ceiling, and exact nested handle/render-pass validation remain
enforced. No trim warning suppression, `RequiresUnreferencedCode` annotation,
or trimming opt-out was added.

Final GREEN evidence:

- The focused WebGPU C# selection passed 80/80, including complete supported
  descriptor/command/packet dispatch and nested bridge-result rejection.
- The Node browser-executor suite passed 17/17.
- The Release browser Player build completed with zero warnings and zero errors.
- A clean-output trimmed browser publish completed with zero warnings and zero
  errors and emitted both the fingerprinted `webgpu-device` JavaScript module
  and `Rekall.Age.Player.Web` WASM module.
- The Release solution build completed with zero warnings and zero errors.

The final command-variant review exposed one existing wire mismatch:
`JsonNamingPolicy.CamelCase` serialized `RekallAgeIndexFormat.UInt16` and
`UInt32` as `uInt16` and `uInt32`, while WebGPU requires `uint16` and `uint32`.
The new literal fixture was RED for both emitted spellings and proved the legacy
spellings were accepted on input. A dedicated source-generation-compatible
`JsonConverter<RekallAgeIndexFormat>` now writes and reads only the two stable
canonical protocol strings. Numeric, unknown, and legacy `uInt*` spellings fail
closed as invalid command payloads. A real Node executor test submits both
canonical variants through `setIndexBuffer` and verifies the exact browser API
arguments. The updated totals above include this coverage.

The final Task 3 review also proved bounded and balanced validation error
scopes under more than 64 operations, strict protocol-v1 3D writes whose full
mip depth is derived from the retained texture descriptor, declared bind-group
texture views and generic texture-metadata compatibility, requested-device
limits/features plus a typed preferred canvas format, legal readback usage,
strict resource-kind lookup, and bounded bridge responses. The final 80/80 C#
and 17/17 Node totals include these cases. A fresh clean-output Release publish
again completed trimming and native WASM linking with zero warnings/errors and
emitted fingerprinted `main`, `webgpu-device`, and
`Rekall.Age.Rendering.WebGpu` WASM modules.

A final follow-up tightened binding legality to WebGPU's multisampled sampled
texture and storage texture view constraints, retained the requested device's
exact immutable enabled-feature set, gated BGRA8 storage on the generic
`bgra8unorm-storage` capability while leaving native baselines capable, and
made render-pass target lookup kind-strict. The focused follow-up C# selection
passed 13/13 and contains the conformance cases; the complete WebGPU protocol
selection and Node executor totals above remain green.

## Physical WebGPU workload acceptance

Date: 2026-08-23

A trimmed Release browser-WASM publish executed the ordinary AGE runtime GPU
workload compiler through the production C# WebGPU RenderingDevice and the
browser WebGPU executor in the in-app Chromium browser. The workload uploaded
the six `UInt32` values of its 24-byte `Uint32x2` vertex buffer, uploaded the
four `UInt32` indirect-draw arguments `[3, 1, 0, 0]`, compiled runtime WGSL,
bound `engine.output`, and submitted a five-command stream ending in
`DrawIndirect`. The visible runtime identified itself as `.NET 10.0.11 /
browser-wasm`; graphics and state fields reported `WebGPU` and
`GPU WORKLOAD EXECUTED`, proving adapter/device initialization reached the
ready state. Protocol-v1 evidence does not expose a hardware adapter name, so
none is claimed.

Acceptance was not inferred from DOM text. The browser copied the same rendered
canvas texture into a map-readable buffer in that frame and sampled four
locations from that mapped data. C# independently validated the returned
dimensions, 256-byte-aligned row pitch, bounded readback envelope, sample
coordinate bounds, and the expected dark-background plus cyan, blue, and
magenta color thresholds. The bridge reported one submitted
frame, no WebGPU diagnostics, and browser automation found no warning/error log
entries. The final schema-v2 acceptance used Google Chrome 151.0.7922.170 and
bound all controller artifacts to one nonce-bearing prepared session. It
recomputed an immutable 92-file, 14,210,180-byte publish identity with manifest
SHA-256 `8EC7A673243B33CD0056E78BFA5EA8AF78A02567D1C0E3F6742837D42239B2A6`,
decoded the physical 1280x720 PNG through a real image decoder, required varied
nonblank pixels, verified its byte count and SHA-256, and stopped only the exact
token-bound server process. It completed with
`validated-browser-supplied-evidence`.

Committed evidence:

- `docs/production/evidence/webgpu-physical-proof-2026-08-23.json`
- `docs/production/evidence/webgpu-physical-proof-2026-08-23.png`
- screenshot SHA-256 `69411E4435E077180018BAF82465491FC138D5A33B5213594536D9AD725652DB`

The Node executor tests remain intentionally narrower: they prove protocol and
readback transport mechanics and cannot synthesize acceptance pixels. The
physical browser capture above is the real shader-execution evidence.

The safety checkpoint completed with a zero-warning, zero-error Release
solution build, 1,303/1,303 engine tests, 25/25 Studio tests, and the acceptance
harness self-test green. Independent review required stronger PNG decoding,
publish/session binding, owned-server verification, and exact browser/publish
identity. Those repairs are covered by adversarial self-tests, and the fresh
physical run above passed the strengthened gate.
