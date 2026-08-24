# Task 3 Report: HDR Scene Target, Bloom, and Tone Mapping

## Status

`DONE_WITH_CONCERNS`

Task 3 is implemented and committed in `1b2ba10` (`feat: render HDR scenes with bloom and tone mapping`). The native Vulkan acceptance path is executable and deterministic: scene geometry renders into linear `R16G16B16A16_SFloat`, a compute bloom pass writes the graph-planned bloom resource, a fullscreen graphics pass tone maps into `R8G8B8A8_UNorm`, and the final LDR image is copied to PNG readback. The prior renderer remains the compatibility path when no resolved high-fidelity plan plus enabled authored post stack is present.

The focused native, shader, rendering, build, formatting, and locked-restore gates pass. The repository-wide test run has eight failures confined to the existing Windows AppContainer module-host isolation fixture; no rendering, shader, publish, or locked-restore failure remains.

## TDD evidence

### Baseline

Before adding the Task 3 test, the existing focused Vulkan scene, command-plan, and compiler coverage passed:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~VulkanSceneCaptureTests|FullyQualifiedName~VulkanSceneCommandPlanTests|FullyQualifiedName~VulkanShaderCompilerTests"
```

Result: `Passed: 17, Failed: 0`.

### Genuine RED

I first added `VulkanHighFidelityCaptureTests.HighProfileWithBloomExecutesHdrPostPassesAndProducesBoundedEmissiveOutput`. It authors a small emissive PBR cube, a `High` resolved quality plan, and an enabled `Rekall.PostProcessStack`, then performs both legacy and high-fidelity native captures.

The first run occurred before production changes:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~VulkanHighFidelityCaptureTests"
```

Result: `Failed: 1, Passed: 0`. The strict output assertion failed because both paths returned checksum `14424642238340027872` (`Assert.NotEqual` expected a different value). This was the intended metadata-only failure: the authored profile and post stack did not affect native Vulkan execution.

The native assertions were not weakened. Production code was then implemented until the same test reported the HDR resources and executed passes and produced bounded, visibly different output.

### GREEN

Required brief command, rerun after the final lock refresh:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~VulkanHighFidelityCaptureTests|FullyQualifiedName~VulkanSceneCaptureTests|FullyQualifiedName~VulkanSceneCommandPlanTests"
```

Result: `Passed: 17, Failed: 0, Skipped: 0` in 1 second.

Shader-inclusive focused suite:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~VulkanHighFidelityCaptureTests|FullyQualifiedName~VulkanSceneCaptureTests|FullyQualifiedName~VulkanSceneCommandPlanTests|FullyQualifiedName~VulkanShaderCompilerTests"
```

Result: `Passed: 20, Failed: 0, Skipped: 0` in 1 second.

Broader renderer coverage:

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore --filter "FullyQualifiedName~Rekall.Age.Tests.Rendering"
```

Result: `Passed: 539, Failed: 0, Skipped: 0`.

## Implementation

- Added `RekallAgeVulkanHighFidelityFrameRenderer` as an orchestration boundary. It consumes the backend-neutral resolved plan and validated render graph, derives generic post settings, and produces an inspectable frame plan/report without parsing scenes or owning Vulkan objects.
- Added a high-fidelity offscreen target with linear HDR scene color and an explicitly separate LDR output format. Existing offscreen and swapchain target contracts remain unchanged.
- Extended command planning to carry validated post passes while preserving the legacy command plan when high fidelity is not authored.
- Kept mesh, emissive, atmosphere, and cloud shader output linear behind `REKALL_HDR_SCENE_OUTPUT`; the original in-shader LDR mapping is retained for compatibility captures.
- Added bundled compute and fragment shaders plus compiler support for compute stages and the HDR scene define.
- Implemented a thresholded 4x4 bloom downsample into the graph-planned quarter-resolution `rgba16f` resource. The tone-map shader performs a normalized nine-tap tent reconstruction, preserving constant-image energy for the downsample/upsample pair.
- Implemented an AgX-style log-domain shoulder with exposure, white point, saturation, contrast, grade strength, bloom intensity, and output transfer controls. Scene values are not clamped; clamping occurs only at the final LDR write.
- Added real Vulkan images, views, samplers, descriptor layouts/sets, compute and graphics pipelines, a tone-map render pass/framebuffer, push constants, dispatch, fullscreen draw, explicit image-layout/memory barriers, output readback, and deterministic cleanup.
- Added pre-allocation capability checks for compute queue support and optimal-tiling format features. Failures are stable and inspectable through `REKALL_RENDER_COMPUTE_QUEUE_UNSUPPORTED` and `REKALL_RENDER_FORMAT_UNSUPPORTED` diagnostics and a non-executed high-fidelity report.
- Reports now truthfully identify allocated graph resources and executed `opaque-hdr`, `bloom`, `tone-map`, `present`, and optional `ui` passes, including dispatch/draw counts.
- UI remains the existing CPU composition stage after LDR tone mapping and is appended to the report exactly once. Existing UI pixel assertions prove there is no double composition.

## Native and visual evidence

The final native test reports:

- scene color: `R16G16B16A16_SFloat`;
- output color: `R8G8B8A8_UNorm`;
- allocated `scene-hdr`, `bloom-pyramid`, and `ldr-color` graph resources;
- one real bloom compute dispatch and one real tone-map fullscreen draw;
- a nonblank LDR frame with no RGB component at 255;
- at least one high-fidelity pixel brighter than its legacy counterpart by more than 3.

Final deterministic capture pair:

- legacy: `C:\Users\Marius\AppData\Local\Temp\rekall-age-tests\2bf3b18fad954bf19056386183d932e2\vulkan-scene-96x64-20260824171820376.png`
- high fidelity: `C:\Users\Marius\AppData\Local\Temp\rekall-age-tests\2bf3b18fad954bf19056386183d932e2\vulkan-scene-96x64-20260824171820742.png`

Both images were opened at original resolution. The legacy image is a hard-clipped yellow emissive square; the high-fidelity image has a bounded orange core and a clear surrounding bloom halo. An independent `System.Drawing` pixel comparison returned: size `96x64`, legacy maximum `255`, high-fidelity maximum `241`, `532` pixels brighter by more than 3, all `6144` pixels different, legacy mean RGB `32.221`, and high-fidelity mean RGB `10.926`. This verifies both highlight bounding and visible bloom beyond structural report assertions.

## Restore, build, and repository evidence

Tasks 1-2 introduced `Rekall.Age.Rendering.Abstractions` project edges but left downstream lock files stale. Publish-oriented tests initially produced `NU1004`. Per the task ruling, I ran a scoped force evaluation and then the solution evaluation needed to reach player publish projects:

```powershell
dotnet restore tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --force-evaluate
dotnet restore Rekall.AGE.sln --force-evaluate
```

Both exited 0. The reviewed lock diff is mechanical: 20 causally affected `packages.lock.json` files have 93 insertions and one dependency-line rewrite, all representing the new `Rekall.Age.Rendering.Abstractions` project edge; there is no external package or version churn. The affected locks are under Agent, Build, Cli, Editor, Mcp, ModuleHost, Modules, Playback, Player, Player.Web, Player.Windows, Rendering, Rendering.WebGpu, Runtime, Runtime.Abstractions, Studio, Validation, Workflows, Studio.Tests, and Rekall.Age.Tests.

```powershell
dotnet restore Rekall.AGE.sln --locked-mode
```

Result: exit 0; all 30 solution projects restored in locked mode.

```powershell
dotnet build src\Rekall.Age.Rendering\Rekall.Age.Rendering.csproj --no-restore
```

Result: build succeeded with `0 Warning(s), 0 Error(s)`.

```powershell
dotnet format Rekall.AGE.sln --no-restore --verify-no-changes --include <all changed C# files>
git diff --check
```

Result: both exited 0. Git emitted only the repository's CRLF conversion notices.

```powershell
dotnet test tests\Rekall.Age.Tests\Rekall.Age.Tests.csproj --no-restore
```

Result: `Passed: 1738, Failed: 8, Skipped: 0, Total: 1746` in 3 minutes 53 seconds. All eight failures are `ModuleHostWindowsIsolationTests` and terminate during AppContainer worker startup with `Module-host frame ended before its declared length` / `EndOfStreamException`. This fixture also failed in isolation before the final repository run. The post-refresh run has no `NU1004`, publish, rendering, Vulkan, or shader failures.

## Files

Primary production files:

- `src/Rekall.Age.Rendering/RekallAgeVulkanHighFidelityFrameRenderer.cs`
- `src/Rekall.Age.Rendering/RekallAgeNativeVulkanSceneCapture.cs`
- `src/Rekall.Age.Rendering/RekallAgeVulkanSceneCaptureResult.cs`
- `src/Rekall.Age.Rendering/RekallAgeVulkanSceneRenderTarget.cs`
- `src/Rekall.Age.Rendering/RekallAgeVulkanSceneCommandPlan.cs`
- `src/Rekall.Age.Rendering/RekallAgeVulkanShaderCompiler.cs`
- `src/Rekall.Age.Rendering/Rekall.Age.Rendering.csproj`
- `src/Rekall.Age.Rendering/Shaders/rekall_scene.frag`
- `src/Rekall.Age.Rendering/Shaders/rekall_bloom.comp`
- `src/Rekall.Age.Rendering/Shaders/rekall_tonemap.frag`

Test files:

- `tests/Rekall.Age.Tests/Rendering/VulkanHighFidelityCaptureTests.cs`
- `tests/Rekall.Age.Tests/Rendering/VulkanSceneCaptureTests.cs`
- `tests/Rekall.Age.Tests/Rendering/VulkanSceneCommandPlanTests.cs`
- `tests/Rekall.Age.Tests/Rendering/VulkanShaderCompilerTests.cs`

Also committed the 20 causally affected lock files described above. The approved spec and implementation plan were not changed because implementation evidence did not change any approved contract, pass order, or acceptance gate.

## Self-review

- Compatibility path: high fidelity activates only when both a resolved quality plan and enabled authored post stack are present; legacy captures retain their former target, shader behavior, command recording, and null high-fidelity report.
- Genericity: the executor consumes backend-neutral graph/resource/pass contracts and generic authored post settings. No scene/game-specific runtime behavior was added.
- Inspectability: validation failures return stable diagnostics and non-executed reports; successful reports reflect actual Vulkan work rather than planned metadata.
- Ordering: scene HDR precedes bloom, tone map, present/readback, and one optional UI composition. Barriers cover color-write-to-sample, compute-write-to-sample, and output-write-to-transfer-read hazards.
- Ownership: every Task 3 Vulkan handle is state-owned and conditionally destroyed in dependency-safe order.
- Shader behavior: HDR scene lighting remains linear, bloom reconstruction weights normalize to one, and only the final LDR write clamps.
- TDD: production work followed a native observed-output RED; the strict image and executed-pass assertions remained intact through GREEN.

## Concerns and follow-up

- The eight repository-wide Windows AppContainer isolation failures are an environment/module-host concern outside Task 3. They do not prevent trustworthy native Vulkan evidence, but they keep the overall repository test command from being fully green.
- Task 3 intentionally implements the minimum executable bloom resource represented by the current validated graph (one quarter-resolution `bloom-pyramid` image with thresholded downsample and normalized reconstruction). Expanding this into a multi-level chain can occur when the backend-neutral graph exposes per-level resources; no contract was invented in this executor.
- GPU timestamp timing remains deferred to its planned instrumentation task; Task 3 reports executed work and counts, not fabricated timings.
