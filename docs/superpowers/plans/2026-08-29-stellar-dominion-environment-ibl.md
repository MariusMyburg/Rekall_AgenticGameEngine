# Stellar Dominion Environment IBL Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task.

**Goal:** Add a generic image-based environment-lighting path and quality-scaled anisotropic texture sampling, then prove both in the playable Stellar Dominion testbed.

**Architecture:** `Rekall.Environment3D.SkyAsset` remains the authored, engine-general environment reference. The existing viewport asset resolver loads that image like any other project texture. Native Vulkan binds it as a global equirectangular radiance source; the PBR shader samples diffuse and roughness-dependent specular lighting while preserving the existing analytical ambient fallback when no image is authored. Sampler anisotropy is resolved from the active quality profile and clamped to Vulkan device support.

**Tech Stack:** .NET 10, C#, Silk.NET Vulkan, GLSL/SPIR-V, xUnit, Rekall AGE scene blueprints and asset catalog.

**Spec:** `docs/superpowers/specs/2026-08-24-high-fidelity-3d-rendering-design.md`

## Global Constraints

- Keep the renderer and authoring contracts generic; Stellar Dominion consumes them as an ordinary authored game.
- Preserve deterministic fallbacks for projects without environment images and devices without anisotropy support.
- Use focused tests during implementation. Run broader acceptance only after the playable tranche is integrated.
- A playable capture and deterministic gameplay assertion are required before delivery.
- Do not inflate the scene with additional megabytes of inline geometry; subsequent hero content should use reusable assets.

### Task 1: Resolve authored environment images

**Files:**
- Modify: `src/Rekall.Age.Rendering/RekallAgeRuntimeViewportAssetResolver.cs`
- Test: `tests/Rekall.Age.Tests/Rendering/RuntimeViewportAssetRenderingTests.cs`

1. Add a focused failing resolver test whose frame references a catalogued image through `Environment.SkyAssetId` and asserts that image appears in the resolved asset set.
2. Include non-empty environment sky asset IDs in the resolver's texture/image dependency set and missing/unsupported diagnostics.
3. Run only the exact resolver test and confirm it passes.

### Task 2: Add environment IBL to native Vulkan PBR

**Files:**
- Modify: `src/Rekall.Age.Rendering/RekallAgeNativeVulkanSceneCapture.cs`
- Modify: `src/Rekall.Age.Rendering/Shaders/rekall_scene.frag`
- Modify: `src/Rekall.Age.Player.Windows/Program.cs`
- Test: `tests/Rekall.Age.Tests/Rendering/VulkanHighFidelityCaptureTests.cs`
- Test: `tests/Rekall.Age.Tests/Rendering/VulkanSceneCaptureTests.cs`

1. Add a focused capture test rendering a metallic/roughness pair under a deliberately directional equirectangular environment and assert that the environment changes the result and that roughness changes reflected radiance.
2. Add a stable fallback environment texture and global sampled-image/sampler descriptor bindings without changing material binding semantics.
3. Upload the authored environment image with a full mip chain, bind it globally, and expose its presence/intensity to both native-capture and interactive-player shaders.
4. Add direction-to-equirectangular UV conversion, diffuse environment sampling, and roughness-selected specular mip sampling to both PBR shader paths. Retain analytical sky/ground ambient when no authored environment is present.
5. Run the two exact Vulkan test classes and repair regressions.

### Task 3: Quality-scaled anisotropic sampling

**Files:**
- Modify: `src/Rekall.Age.Rendering/RekallAgeNativeVulkanSceneCapture.cs`
- Modify as needed: `src/Rekall.Age.Rendering.Abstractions/RekallAgeRenderWorldContracts.cs`
- Modify as needed: `src/Rekall.Age.Rendering/RekallAgeRuntimeRenderFrameBuilder.cs`
- Test: `tests/Rekall.Age.Tests/Rendering/VulkanSceneCaptureTests.cs`

1. Add a focused test for preset-to-anisotropy resolution: Low 1x, Medium 2x, High 8x, Epic 16x, clamped by device capability.
2. Enable `samplerAnisotropy` only when reported by the physical device and use the resolved value for scene texture samplers.
3. Keep post-process and shadow samplers at their deliberate fixed filtering modes.
4. Run the exact focused test class.

### Task 4: Author and integrate Stellar Dominion's environment

**Files:**
- Add: `Examples/StellarDominion/Assets/Textures/stellar-environment.png`
- Modify: Stellar Dominion asset catalog/import metadata as required
- Modify: `Examples/StellarDominion/Scenes/Mission1.scene.json`
- Modify: the existing Stellar Dominion authoring script/tool that owns generated scene content

1. Generate a legal, deterministic, high-resolution equirectangular deep-space environment with restrained nebular radiance, star fields, and a dominant directional source.
2. Import it through the ordinary AGE asset pipeline and assign it to the scene environment.
3. Rebalance ambient energy, exposure, key/fill/rim lights, emissive engines, bloom, and material roughness so the result remains dark, legible, and physically coherent.
4. Capture at 1920x1080 on High and inspect the image for black-frame regressions, blown highlights, noise, and insufficient material separation.
5. Iterate until the environment materially improves silhouettes, metal response, depth, and atmosphere.

### Task 5: Playable acceptance and delivery

**Files:**
- Modify: `docs/production/PROGRESS.md`
- Modify: relevant rendering/authoring documentation if contracts changed

1. Rebuild the Windows player and relaunch Stellar Dominion with Vulkan.
2. Run the deterministic `rekall.runtime.inspect_scene` gameplay probe with representative movement/fire input and strict transform or `Game.*` state assertions.
3. Run focused render tests, package audit, and capture proof. Do not run the full solution suite unless a final gate genuinely requires it.
4. Record observed FPS and the remaining visual ceiling gaps in `PROGRESS.md`.
5. Commit and push the verified tranche before starting the next independent ceiling tranche.

## Completion evidence

- Authored environment resolution and native/interactive Vulkan shader parity implemented.
- Roughness-selected environment mips and quality-scaled anisotropy implemented.
- Focused renderer gate: 23 passed, 0 failed.
- Windows player Release build: 0 warnings, 0 errors.
- Stellar Dominion gameplay gate: all nine mission, beam-tracking, and weapon-audio probes passed.
- Windows playable package and consolidated package audit: ready, 397 files, zero missing key artifacts, run exit code 0, nonblank informative capture.
- Stable proof frame: `Examples/StellarDominion/Captures/environment-ibl-1920x1080.png`.
- Remaining ceiling work is explicitly carried forward in `docs/production/PROGRESS.md`; this tranche does not claim final photorealism.
