# Task 4 Report: Runtime-Compiled WebGPU Proof

Base: `d842e27`

## Delivered

- Added the asset-independent `proof.webgpu.asset-independent` workload. It uses exactly six inline UInt32 vertex values in one 24-byte `Uint32x2` vertex buffer, the 16-byte indirect arguments `[3, 1, 0, 0]`, runtime-authored WGSL vertex/fragment shaders, the browser's preferred canvas format, imported `engine.output`, and `DrawIndirect`.
- Replaced device-ready startup with C# compilation, submission, `FlushAsync`, bounded readback, evidence publication, and retained device/output lifetime. Compiled workload resources are retained through readback and then disposed.
- Configured the canvas for `RENDER_ATTACHMENT | COPY_SRC`. The executor captures the exact `getCurrentTexture()` used by the render pass and appends `copyTextureToBuffer` to that submission, using a 256-byte-aligned row pitch and a 64 MiB bound.
- Added fixed interior/background RGBA samples. Acceptance requires a dark background, cyan/blue/magenta-like distinct samples, nonzero alpha, pairwise color separation, and rejection of all-zero or clear-only output.
- Exposed `window.rekallWebGpuEvidence` with exactly `backend`, `protocolVersion`, `workloadId`, `submittedFrames`, `diagnostics`, and `pixelProof`. The UI reports `GPU WORKLOAD EXECUTED` only when submission, flush, and pixel proof all succeed.
- Kept JavaScript limited to the browser WebGPU/DOM seam; no gameplay semantics or parallel scene model were added.

## TDD Evidence

- Proof workload/compiler/bridge tests failed first because `WebGpuProofWorkload` did not exist, then passed after implementation.
- Pixel readback tests failed first because `readPixels` did not exist, then passed using a faithful fake WebGPU command/texture/copy/map path. Acceptance pixels are produced by the fake renderer from uploaded vertex and indirect buffers, not returned as canned readback values. A clear-only canvas is rejected.
- Evidence tests failed first because the C#/JavaScript evidence types and publisher did not exist, then passed with exact-field and malformed/oversized checks.
- Lifecycle tests failed first because the proof executor did not exist, then passed with flush-before-readback and dispose-after-readback assertions.

## Verification

- `dotnet build Rekall.Age.sln -c Release --no-restore`: passed, 0 warnings, 0 errors.
- Focused WebGPU proof C# tests: 6 passed.
- All WebGPU C# tests: 86 passed.
- Node WebGPU executor/evidence/readback tests: 21 passed.
- `git diff --check`: passed.
- One trimmed publish only: `dotnet publish src/Rekall.Age.Player.Web/Rekall.Age.Player.Web.csproj -c Release --no-restore -p:PublishTrimmed=true -o artifacts/task4-webgpu-final`.
- Publish result: 91 files, 13.5 MiB, including fingerprinted `main`, `webgpu-device`, and `webgpu-evidence` modules.
- F: free space after publish: 724 MiB, above the 200 MiB floor.

## Scope and Remaining Concern

Task 4 does not claim physical-browser acceptance. Chromium/WebGPU validation of canvas `COPY_SRC`, the exact adapter, literal physical pixels, browser logs, and capture remain Task 5. The solution clean removed current Release outputs before the broad build; older ignored Task 3 publish artifacts remain because local command policy blocked their recursive removal.
