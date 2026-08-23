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
or WebGPU draw command. A production claim requires backend implementation,
WebGL 2 feature lowering, browser services, package generation/audit, and a real
playable browser acceptance with deterministic gameplay evidence.

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

- The focused WebGPU C# selection passed 69/69, including complete supported
  descriptor/command/packet dispatch and nested bridge-result rejection.
- The Node browser-executor suite passed 9/9.
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
arguments. The updated 69/69 C# and 9/9 Node totals above include this coverage.
