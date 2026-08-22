# WebGPU RenderingDevice Design

Status: accepted under the user's standing autonomous implementation approval

Date: 2026-08-23

## Purpose

Turn `Rekall.Age.Player.Web` from a browser/WASM capability page with a 2D
canvas animation into a real consumer of AGE's backend-neutral
`IRekallAgeRenderingDevice` contract. The milestone is complete only when a
freshly published browser player executes a bounded agent-authored WGSL
workload through WebGPU and deterministic browser evidence proves the expected
pixels. This milestone does not by itself claim complete web game export.

## Architectural decision

Create a platform-neutral `Rekall.Age.Rendering.WebGpu` C# library. It will
implement `IRekallAgeRenderingDevice` by composing the existing in-memory
conformance device for handles, limits, validation, lifecycle, and command
state. A narrow `IRekallAgeWebGpuBridge` boundary will receive immutable,
bounded resource and submission packets. The browser project will implement
that boundary with source-generated .NET JavaScript interop and a small
JavaScript WebGPU executor.

All game-facing types, resource ownership, validation, workload compilation,
and diagnostics remain C#. JavaScript is restricted to the browser API calls
that cannot be expressed directly by .NET browser WASM: adapter/device/canvas
setup, WebGPU object creation, queue writes/submission, and pixel readback. No
JavaScript gameplay API or parallel scene model will exist.

## Alternatives considered

1. **Recommended: C# adapter plus bounded bridge protocol.** This reuses AGE's
   tested contract, keeps the platform seam narrow, and lets ordinary .NET
   tests validate every packet before real-browser acceptance. It adds a small
   serialization layer but makes backend behavior inspectable to agents.
2. **Direct JavaScript backend called by the web page.** This is initially
   shorter, but duplicates validation and resource semantics outside AGE,
   creates a second game-authoring API, and cannot be exercised by the normal
   C# compiler tests. Rejected.
3. **A native C# WebGPU binding compiled to WASM.** This would reduce handwritten
   JavaScript but introduces a large native dependency and still needs browser
   interop. It does not improve the agent-facing contract for this milestone.
   Rejected until a maintained binding demonstrates a clear production benefit.

## Components

### `Rekall.Age.Rendering.WebGpu`

- `RekallAgeWebGpuRenderingDevice` implements the public rendering device.
- `IRekallAgeWebGpuBridge` owns only backend operations and returns structured
  failure results; it never receives native pointers from authored code.
- `RekallAgeWebGpuProtocol` defines versioned JSON packets for resource create,
  destroy, bounded uploads, and command-buffer submission.
- The adapter creates the conformance resource first, emits the bridge packet,
  and rolls the conformance resource back if the bridge rejects it.
- Submission is validated by the conformance backend before bridge execution.
  Failed backend submission returns stable AGE diagnostics and does not pretend
  that a frame executed.
- The first protocol version covers the complete currently supported portable
  surface: buffers, textures, samplers, WGSL shader modules, binding layouts and
  sets, render/compute pipelines, render targets, buffer/texture uploads,
  render and compute passes, direct and indirect draw/dispatch, and copies.

### Browser host

- Browser startup awaits `navigator.gpu.requestAdapter()` and
  `adapter.requestDevice()` before C# creates its backend.
- The canvas uses `navigator.gpu.getPreferredCanvasFormat()` and an explicit
  opaque configuration.
- JavaScript stores WebGPU objects in maps keyed by AGE's device/slot/generation
  identity and rejects stale, duplicate, missing, or wrong-kind references.
- WGSL compilation info and uncaptured device errors are returned as bounded
  structured diagnostics and displayed in the player status panel.
- Device loss changes the player state to failed and includes a recovery
  action; it is never reported as ready.
- Canvas resizing is device-pixel-ratio aware and recreates only
  size-dependent targets.

## Shader and portability rules

WebGPU accepts agent-authored WGSL through the existing
`RekallAgeRuntimeGpuShaderLanguage.Wgsl` contract. The browser backend rejects
GLSL and SPIR-V with a stable source-language diagnostic. The Windows Veldrid
backend continues to accept GLSL. Cross-backend projects can provide backend
variants through shader-library/package work; this milestone will not embed an
unbounded GLSL-to-WGSL compiler in the browser.

## Proof workload

The browser proof uses the same generic runtime workload compiler as Windows.
It declares a catalog-independent triangle vertex buffer using bounded
`InitialDataUInt32`, WGSL vertex/fragment shaders, an explicit vertex layout,
the canvas output target, and a render pass ending in `DrawIndirect`. The
triangle's three vertices produce distinct cyan, blue, and magenta regions so
a blank canvas or wrong-surface capture cannot satisfy acceptance.

The proof must expose machine-readable state containing backend name, protocol
version, workload ID, submitted frame count, and any diagnostics. A browser
readback samples fixed interior pixels and compares literal RGBA bounds. DOM
text alone is insufficient.

## Error handling and limits

- Protocol payloads inherit the RenderingDevice resource, shader, command, and
  memory limits; bridge packets add a 16 MiB serialized-packet ceiling.
- All IDs and labels are length-bounded before serialization.
- Upload byte arrays are bounded before base64 encoding and are decoded into
  exact-size typed arrays in the browser.
- Unknown protocol versions, enum values, object kinds, and command kinds fail
  closed with stable diagnostic codes.
- Bridge exceptions are translated to `REKALL_WEBGPU_*` diagnostics without
  leaking browser implementation objects.
- Resource destruction is idempotent from the engine's perspective and removes
  browser map entries immediately.

## Testing and acceptance

1. Contract tests use a recording bridge and real conformance device. They
   prove resource rollback, exact packet data, language rejection, upload
   bounds, lifecycle, validation-before-submit, and backend-failure propagation.
2. Protocol tests deserialize literal fixtures and reject malformed/versioned
   packets. Expected values are hand-derived rather than regenerated by the
   serializer under test.
3. JavaScript executor tests, where practical, exercise packet validation with
   deliberately malformed objects without requiring a physical GPU.
4. Release publish is served from a clean output directory. The in-app Chromium
   browser must report WebGPU ready, execute the proof workload, return clean
   browser logs, and pass literal pixel samples/readback.
5. The complete engine and Studio suites and zero-warning Release build remain
   mandatory before commit and push.

## Explicit non-claims and next work

This milestone proves a real WebGPU RenderingDevice backend and browser GPU
execution. Complete web export still requires ahead-of-time inclusion of
agent-authored modules, project/scene/asset packaging, semantic keyboard/mouse/
controller input, audio, storage/network adapters, WebGL 2 compatibility,
package audit/relocation, and deterministic playable gameplay acceptance.
Those remain visible production requirements and will not be hidden behind the
WebGPU proof.
