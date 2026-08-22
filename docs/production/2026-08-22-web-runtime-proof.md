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
