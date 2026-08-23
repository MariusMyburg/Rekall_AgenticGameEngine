# Task 4 Report: Runtime-Compiled WebGPU Proof

Base: `d842e27`

## Delivered

- Added the asset-independent `proof.webgpu.asset-independent` workload. It uses exactly six inline UInt32 vertex values in one 24-byte `Uint32x2` vertex buffer, the 16-byte indirect arguments `[3, 1, 0, 0]`, runtime-authored WGSL vertex/fragment shaders, the browser's preferred canvas format, imported `engine.output`, and `DrawIndirect`.
- Replaced device-ready startup with C# compilation, submission, `FlushAsync`, bounded readback, evidence publication, and retained device/output lifetime. Compiled workload resources are retained through readback and then disposed.
- Configured the canvas for `RENDER_ATTACHMENT | COPY_SRC`. The executor captures the exact `getCurrentTexture()` used by the render pass and appends `copyTextureToBuffer` to that submission, using a 256-byte-aligned row pitch and a 64 MiB bound.
- Added fixed interior/background RGBA samples. Acceptance requires a dark background, cyan/blue/magenta-like distinct samples, nonzero alpha, pairwise color separation, and rejection of all-zero or clear-only output.
- Treats the browser result as untrusted input. C# requires the browser's `succeeded` flag and independently recomputes acceptance from the four bounded raw RGBA samples; the browser's `pixelProof.passed` claim cannot make execution succeed or fail by itself.
- Exposed `window.rekallWebGpuEvidence` with exactly `backend`, `protocolVersion`, `workloadId`, `submittedFrames`, `diagnostics`, and `pixelProof`. The UI reports `GPU WORKLOAD EXECUTED` only when submission, flush, and pixel proof all succeed.
- Kept JavaScript limited to the browser WebGPU/DOM seam; no gameplay semantics or parallel scene model were added.

## TDD Evidence

- Proof workload/compiler/bridge tests failed first because `WebGpuProofWorkload` did not exist, then passed after implementation.
- Pixel readback tests failed first because `readPixels` did not exist, then passed using a readback-only WebGPU fake. Node proves generic exact-current-texture capture, same-submission copy ordering, error scopes, 256-byte row alignment, bounded mapping, and raw-byte transport only. It contains no production shader interpretation or triangle/color rasterizer; arbitrary non-acceptance bytes and an all-dark canvas both fail the production proof.
- Evidence tests failed first because the C#/JavaScript evidence types and publisher did not exist, then passed with exact-field and malformed/oversized checks.
- Lifecycle tests failed first because the proof executor did not exist, then passed with flush-before-readback and dispose-after-readback assertions.
- Trust-boundary regressions failed first when self-asserted browser proof values were accepted, then passed once C# required `succeeded` and recomputed the canonical pixel thresholds from raw samples.

## Verification

- `dotnet build Rekall.Age.sln -c Release --no-restore`: passed, 0 warnings, 0 errors.
- Focused WebGPU proof/evidence C# tests: 10 passed.
- All WebGPU C# tests: 90 passed.
- Node WebGPU executor/evidence/readback tests: 21 passed.
- Review follow-up `Rekall.Age.Player.Web` Release build: passed, 0 warnings, 0 errors.
- `git diff --check`: passed.
- One trimmed publish only: `dotnet publish src/Rekall.Age.Player.Web/Rekall.Age.Player.Web.csproj -c Release --no-restore -p:PublishTrimmed=true -o artifacts/task4-webgpu-final`.
- Publish result: 91 files, 13.5 MiB, including fingerprinted `main`, `webgpu-device`, and `webgpu-evidence` modules.
- No second publish was performed for the review-only trust/test changes.
- F: free space after publish: 724 MiB, above the 200 MiB floor.

## Scope and Remaining Concern

Task 4 does not claim shader execution or physical-browser pixel acceptance from Node. Chromium/WebGPU validation of canvas `COPY_SRC`, the exact adapter, real WGSL execution, literal physical pixels, browser logs, and capture remain Task 5. The solution clean removed current Release outputs before the broad build; older ignored Task 3 publish artifacts remain because local command policy blocked their recursive removal.
