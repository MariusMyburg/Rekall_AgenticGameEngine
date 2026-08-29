# Shared Vulkan Studio Viewport Design

## Goal

Studio's World viewport and the Windows Player must render runtime viewport frames through the same long-lived Vulkan renderer. Simulate must present at an interactive cadence without advancing game time in six-frame visual jumps.

## Current failure

Studio builds the same runtime viewport frame contract as the Player, but then sends it through `RekallAgeRuntimeSoftwareRenderer` at 2x supersampling and copies the result into a WPF bitmap. Summit Run spends roughly one CPU core in this rasterizer and presents about 10 frames per second. The Player instead owns a long-lived Veldrid Vulkan device, render targets, pipelines, resource caches, post-processing, and swapchain.

Changing Studio's timer or software-renderer resolution cannot produce renderer parity. Disabling supersampling would also regress the viewport quality issue already reported by the user.

## Architecture

Create a Windows Vulkan presentation library that owns the render-specific state currently embedded in `Rekall.Age.Player.Windows`. Its public session accepts the existing `RekallAgeRuntimeViewportFrame` and resolved viewport assets, targets a supplied Win32 child-window handle, and returns lightweight frame telemetry and interaction metadata. It retains its Vulkan device, pipelines, targets, shader cache, textures, and geometry cache across frames.

The Windows Player keeps its SDL window, input, audio, runtime loop, recovery supervision, screenshots, and OpenXR coordination. It delegates desktop scene presentation to the shared Vulkan session. Studio creates a child HWND through `HwndHost`, gives that HWND to the same shared session, and supplies frames from its existing preview runtime. Edit and Simulate therefore differ only in whether the preview runtime advances; both use the production scene renderer.

WPF remains responsible for Studio chrome, hierarchy, Inspector, prompts, and status. Viewport selection continues to use `RekallAgeStudioViewportInteractionBuilder` metadata from the same runtime frame; the child host forwards pointer coordinates to Studio before the renderer receives input. Authored UI is rendered by the shared Vulkan renderer, not composited into a software bitmap.

## Cadence

Studio requests presentation at 60 Hz. A successful Simulate presentation advances one deterministic fixed step. If rendering misses a deadline, Studio uses elapsed-time accumulation with a bounded catch-up count and renders only the newest state; it never lets simulation speed depend on the number of WPF dispatcher callbacks. Edit mode presents only when scene, camera, layout, asset, or shader state changes.

## Failure handling

Vulkan initialization or device-loss failures must become structured Studio validation lines and status text. Studio must not silently switch to software rendering in the normal viewport, because that would conceal Player/Studio divergence. A clearly labeled unavailable placeholder remains visible while Edit, hierarchy, Inspector, and code authoring stay usable.

## Acceptance

- Source and component tests prove Studio and Player instantiate the same shared Vulkan session type.
- Existing Studio pause, single-step, stop, viewport picking, and Inspector tests remain green.
- Summit Run opens in World view using a Vulkan backend label and hardware acceleration.
- Simulate advances near real time and the UI remains responsive for a sustained live run.
- The Player still launches and renders Summit Run through Vulkan.
- No normal Studio World path calls `RekallAgeRuntimeSoftwareRenderer.RenderRgba`.

