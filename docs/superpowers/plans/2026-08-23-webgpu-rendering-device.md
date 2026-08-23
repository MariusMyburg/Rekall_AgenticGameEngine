# WebGPU RenderingDevice Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Execute AGE's complete portable GPU workload contract in a published .NET browser-WASM player and prove its rendered pixels through real WebGPU.

**Architecture:** A platform-neutral C# adapter composes the in-memory conformance device and emits versioned bounded protocol packets through an injected bridge. The browser project implements only the WebGPU API seam in JavaScript; resource semantics, validation, compilation, and diagnostics remain C#. A WGSL indirect-draw proof is compiled by the existing runtime workload compiler and verified by browser readback.

**Tech Stack:** C# 14 / .NET 10, `System.Text.Json`, browser WASM source-generated JavaScript interop, W3C WebGPU, xUnit, in-app Chromium acceptance.

**Spec:** `docs/superpowers/specs/2026-08-23-webgpu-rendering-device-design.md`

## Global Constraints

- Preserve the existing `IRekallAgeRenderingDevice` and runtime workload public contracts; additions must be additive.
- Agent-authored browser shaders are WGSL; no hidden GLSL translation or native handles.
- Reuse conformance validation before every backend mutation and submission.
- Serialized bridge requests cannot exceed 16 MiB and must fail closed on unknown versions/kinds.
- Do not claim complete web export until AOT modules, assets, input/audio/storage, packaging, relocation, and gameplay evidence also pass.
- Every production behavior starts with a failing real-behavior test.

---

### Task 1: Versioned WebGPU protocol and bridge boundary

**Files:**
- Create: `src/Rekall.Age.Rendering.WebGpu/Rekall.Age.Rendering.WebGpu.csproj`
- Create: `src/Rekall.Age.Rendering.WebGpu/RekallAgeWebGpuProtocol.cs`
- Create: `src/Rekall.Age.Rendering.WebGpu/IRekallAgeWebGpuBridge.cs`
- Create: `tests/Rekall.Age.Tests/Rendering/WebGpuProtocolTests.cs`
- Modify: `tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj`
- Modify: `Rekall.Age.sln`

**Interfaces:**
- Produces: `RekallAgeWebGpuProtocol.Version == 1`, `Serialize<T>(T packet)`, `Deserialize<T>(string json)`, `RekallAgeWebGpuBridgeResult`, `IRekallAgeWebGpuBridge.Execute(string packetJson)`, and `FlushAsync(CancellationToken)`.
- Packets use public AGE descriptors/handles/commands as immutable payload data and never contain browser objects.

- [x] **Step 1: Write failing protocol tests**

```csharp
[Fact]
public void ProtocolRoundTripsAHandCheckedBufferPacket()
{
    var packet = new RekallAgeWebGpuCreatePacket(
        1, "buffer", new(Guid.Parse("11111111-1111-1111-1111-111111111111"), RekallAgeGraphicsResourceKind.Buffer, 7, 1),
        JsonSerializer.SerializeToElement(new RekallAgeBufferDescriptor(16, RekallAgeBufferUsage.Vertex, Label: "triangle")));
    var json = RekallAgeWebGpuProtocol.Serialize(packet);
    var restored = RekallAgeWebGpuProtocol.Deserialize<RekallAgeWebGpuCreatePacket>(json);
    Assert.Equal(7, restored.Handle.Slot);
    Assert.Equal("buffer", restored.ResourceType);
}

[Fact]
public void ProtocolRejectsUnknownVersionsAndOversizedPackets()
{
    Assert.Throws<RekallAgeWebGpuProtocolException>(() =>
        RekallAgeWebGpuProtocol.Deserialize<RekallAgeWebGpuCreatePacket>("{\"version\":2}"));
    Assert.Throws<RekallAgeWebGpuProtocolException>(() =>
        RekallAgeWebGpuProtocol.Serialize(new { Version = 1, Data = new string('x', RekallAgeWebGpuProtocol.MaximumPacketBytes) }));
}
```

- [x] **Step 2: Run the tests and verify RED**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj -c Release --filter FullyQualifiedName~WebGpuProtocolTests`

Expected: compile failure because the WebGPU protocol project and types do not exist.

- [x] **Step 3: Implement the bounded protocol and bridge result**

```csharp
public static class RekallAgeWebGpuProtocol
{
    public const int Version = 1;
    public const int MaximumPacketBytes = 16 * 1024 * 1024;
    public static string Serialize<T>(T value);
    public static T Deserialize<T>(string json) where T : IRekallAgeWebGpuPacket;
}

public interface IRekallAgeWebGpuBridge
{
    RekallAgeWebGpuBridgeResult Execute(string packetJson);
    ValueTask<RekallAgeWebGpuBridgeResult> FlushAsync(CancellationToken cancellationToken = default);
}

public sealed record RekallAgeWebGpuBridgeResult(bool Succeeded, IReadOnlyList<RekallAgeGraphicsDiagnostic> Diagnostics)
{
    public static RekallAgeWebGpuBridgeResult Success { get; } = new(true, []);
}
```

Use camel-case JSON, string enums, strict version validation, bounded UTF-8 byte counts, and stable `REKALL_WEBGPU_PROTOCOL_*` exception diagnostics.

- [x] **Step 4: Run protocol tests and verify GREEN**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj -c Release --filter FullyQualifiedName~WebGpuProtocolTests`

Expected: all WebGPU protocol tests pass.

- [x] **Step 5: Commit**

```powershell
git add Rekall.Age.sln src/Rekall.Age.Rendering.WebGpu tests/Rekall.Age.Tests
git commit -m "feat(webgpu): add bounded rendering protocol"
```

### Task 2: C# conformance-backed WebGPU device

**Files:**
- Create: `src/Rekall.Age.Rendering.WebGpu/RekallAgeWebGpuRenderingDevice.cs`
- Create: `src/Rekall.Age.Rendering.WebGpu/RekallAgeWebGpuCommandEncoder.cs`
- Create: `tests/Rekall.Age.Tests/Rendering/WebGpuRenderingDeviceTests.cs`

**Interfaces:**
- Consumes: Task 1 bridge/protocol and `RekallAgeInMemoryRenderingDevice`.
- Produces: `RekallAgeWebGpuRenderingDevice(IRekallAgeWebGpuBridge bridge, RekallAgeRenderingDeviceCapabilities capabilities)` implementing the complete public device interface, plus concrete `ImportCanvasOutput(...)` and `FlushAsync(...)` lifecycle operations.

- [x] **Step 1: Write failing creation/rollback/language tests**

```csharp
[Fact]
public void BridgeFailureRollsBackResourceAndReturnsStableDiagnostic()
{
    var bridge = new RecordingWebGpuBridge(result: new(false, [new("REKALL_WEBGPU_CREATE_FAILED", "rejected")]));
    using var device = CreateDevice(bridge);
    var result = device.CreateBuffer(new(16, RekallAgeBufferUsage.Vertex));
    Assert.False(result.Created);
    Assert.Empty(device.InspectResources());
    Assert.Contains(result.Diagnostics, d => d.Code == "REKALL_WEBGPU_CREATE_FAILED");
}

[Fact]
public void BrowserBackendRejectsNonWgslShadersBeforeBridgeMutation()
{
    var bridge = new RecordingWebGpuBridge();
    using var device = CreateDevice(bridge);
    var result = device.CreateShaderModule(new(RekallAgeShaderStage.Vertex, RekallAgeShaderSourceLanguage.Glsl, "void main(){}"));
    Assert.Contains(result.Diagnostics, d => d.Code == "REKALL_WEBGPU_SHADER_LANGUAGE_REQUIRED");
    Assert.Empty(bridge.Packets);
}
```

- [x] **Step 2: Run device tests and verify RED**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj -c Release --filter FullyQualifiedName~WebGpuRenderingDeviceTests`

Expected: compile failure because the adapter does not exist.

- [x] **Step 3: Implement all resource operations with rollback**

Each create method calls the conformance device, emits one typed create packet,
and destroys the conformance handle if the bridge result fails. `WriteBuffer`,
`WriteTexture`, and `Destroy` validate through conformance first and emit exact
offset/subresource/base64 data. No bridge call occurs after a conformance error.

- [x] **Step 4: Add failing submission-order and backend-failure tests**

```csharp
[Fact]
public void SubmitValidatesBeforeBridgeAndPropagatesBackendFailure()
{
    var bridge = new RecordingWebGpuBridge(submitResult: new(false, [new("REKALL_WEBGPU_SUBMIT_FAILED", "device lost")]));
    using var device = CreateDevice(bridge);
    var commandBuffer = BuildValidTriangleCommands(device);
    var result = device.Submit(commandBuffer);
    Assert.Contains(result.Diagnostics, d => d.Code == "REKALL_WEBGPU_SUBMIT_FAILED");
    Assert.Equal("submit", bridge.Packets[^1].Operation);
}
```

- [x] **Step 5: Implement encoder delegation and submission**

`BeginCommandEncoder` returns an adapter over the real conformance encoder.
`Finish` preserves the conformance command buffer. `Submit` validates/submits
there first, serializes the immutable command list, and only then calls the
bridge. Preserve command order exactly. `ImportCanvasOutput` creates validated
conformance color/present texture and render-target handles and emits an import
packet; authored modules see only the resulting `engine.output` handle.

- [x] **Step 6: Add failing asynchronous flush/device-loss tests**

Use a bridge whose immediate operations succeed but whose `FlushAsync` returns
`REKALL_WEBGPU_DEVICE_LOST`. Assert the concrete device becomes faulted, later
mutations fail closed without bridge calls, and disposal destroys all retained
resources.

- [x] **Step 7: Implement and verify asynchronous flush**

`FlushAsync` awaits bridge error scopes/queue completion. On failure it records
bounded diagnostics, marks the adapter faulted, and blocks future creates,
uploads, encoders, and submissions. The browser player disposes its compiled
workload on any flush failure.

- [x] **Step 8: Run all WebGPU C# tests and verify GREEN**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj -c Release --filter FullyQualifiedName~WebGpu`

Expected: protocol and device tests pass with zero warnings.

- [x] **Step 9: Commit**

```powershell
git add src/Rekall.Age.Rendering.WebGpu tests/Rekall.Age.Tests
git commit -m "feat(webgpu): implement conformance-backed device"
```

### Task 3: Browser WebGPU executor

**Files:**
- Create: `src/Rekall.Age.Player.Web/WebGpuBrowserBridge.cs`
- Create: `src/Rekall.Age.Player.Web/wwwroot/webgpu-device.js`
- Modify: `src/Rekall.Age.Player.Web/Rekall.Age.Player.Web.csproj`
- Modify: `src/Rekall.Age.Player.Web/wwwroot/main.js`
- Create: `tests/Rekall.Age.Tests/Rendering/WebGpuBrowserPacketFixtureTests.cs`

**Interfaces:**
- Consumes: `IRekallAgeWebGpuBridge.Execute` packets.
- Produces: JS module imports `webgpu.execute(packetJson)` for synchronous recording, `webgpu.flush()` for asynchronous error-scope/queue completion, and `webgpu.initialize(canvasSelector)` for device/canvas capabilities.

- [x] **Step 1: Write failing packet-fixture tests**

Build literal create/upload/submit JSON fixtures and assert the C# serializer
matches the field names, enum strings, handle identity, command order, and
base64 bytes the JavaScript executor consumes. Mutating version, resource kind,
or command kind must fail deserialization.

- [x] **Step 2: Run fixture tests and verify RED**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj -c Release --filter FullyQualifiedName~WebGpuBrowserPacketFixtureTests`

Expected: failures identify missing browser packet shapes.

- [x] **Step 3: Implement initialization and resource maps**

`initialize` must await adapter/device, configure `#viewport` using the preferred
format, register uncaptured-error/device-lost handlers, and create per-kind maps.
`execute` parses only protocol version 1, checks the 16 MiB ceiling again, and
dispatches create/write/destroy/submit operations. Unknown input returns
`REKALL_WEBGPU_PROTOCOL_INVALID` rather than throwing across interop.
Every operation uses a WebGPU error scope; `flush` awaits pending scope results,
shader compilation information, `queue.onSubmittedWorkDone()`, and device loss,
then returns one bounded diagnostic list.

- [x] **Step 4: Implement the complete WebGPU mapping**

Map AGE usages/formats/stages/topologies/vertex formats/blend/depth/samplers to
WebGPU literals. Resolve binding layouts/sets and pipelines from handle keys.
Execute render/compute pass commands, buffer copies, direct/indirect draws and
dispatches. `engine.output` resolves to `context.getCurrentTexture().createView()`
at render-pass execution time, never at compile time.

- [x] **Step 5: Implement C# source-generated interop bridge**

```csharp
internal sealed class WebGpuBrowserBridge : IRekallAgeWebGpuBridge
{
    public RekallAgeWebGpuBridgeResult Execute(string packetJson) =>
        RekallAgeWebGpuProtocol.DeserializeBridgeResult(BrowserImports.ExecuteWebGpu(packetJson));
}
```

Initialization occurs before device construction. Any malformed or empty JS
result becomes `REKALL_WEBGPU_BRIDGE_RESULT_INVALID`.

- [x] **Step 6: Publish and verify no browser-load regression**

Run: `dotnet publish src/Rekall.Age.Player.Web/Rekall.Age.Player.Web.csproj -c Release --no-restore`

Expected: publish succeeds with zero warnings/errors and all referenced JS
modules appear in `publish/wwwroot`.

- [x] **Step 7: Commit**

```powershell
git add src/Rekall.Age.Player.Web src/Rekall.Age.Rendering.WebGpu tests/Rekall.Age.Tests
git commit -m "feat(webgpu): execute AGE packets in browser"
```

### Task 4: Runtime-compiled WGSL proof and machine-readable evidence

**Files:**
- Create: `src/Rekall.Age.Player.Web/WebGpuProofWorkload.cs`
- Modify: `src/Rekall.Age.Player.Web/Program.cs`
- Modify: `src/Rekall.Age.Player.Web/wwwroot/index.html`
- Modify: `src/Rekall.Age.Player.Web/wwwroot/main.js`
- Create: `tests/Rekall.Age.Tests/Rendering/WebGpuProofWorkloadTests.cs`

**Interfaces:**
- Consumes: `RekallAgeRuntimeGpuWorkloadCompiler` and WebGPU device.
- Produces: a WGSL workload named `proof.webgpu.asset-independent`, machine-readable `window.rekallWebGpuEvidence`, and browser pixel samples.

- [x] **Step 1: Write failing proof-workload test**

```csharp
[Fact]
public void ProofCompilesToAnIndirectColorTriangleOnWebGpu()
{
    var bridge = new RecordingWebGpuBridge();
    using var device = CreateDevice(bridge);
    using var compiled = new RekallAgeRuntimeGpuWorkloadCompiler().Compile(WebGpuProofWorkload.Create(), device, EngineOutput(device));
    Assert.True(compiled.Valid, string.Join(Environment.NewLine, compiled.Diagnostics.Select(d => d.Message)));
    Assert.Contains(compiled.CommandBuffer!.Commands, c => c is RekallAgeDrawIndirectCommand);
}
```

- [x] **Step 2: Run proof test and verify RED**

Run: `dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj -c Release --filter FullyQualifiedName~WebGpuProofWorkloadTests`

Expected: compile failure because the proof workload does not exist.

- [x] **Step 3: Implement the WGSL proof**

Use one 24-byte vertex buffer encoded through six `InitialDataUInt32` values,
one 16-byte indirect buffer `[3, 1, 0, 0]`, explicit `Uint32x2` vertex layout,
WGSL vertex/fragment shaders, one render pipeline targeting the browser preferred
format, `engine.output`, and one `DrawIndirect` command. The shader decodes the
same L/D/R/U byte convention as the Vulkan proof and writes interpolated color.

- [x] **Step 4: Replace the 2D canvas animation with real workload execution**

Program startup initializes WebGPU, constructs the C# device, calls its concrete
`ImportCanvasOutput` operation, compiles/submits the proof against the returned
target, and publishes literal state fields:
`backend`, `protocolVersion`, `workloadId`, `submittedFrames`, `diagnostics`, and
`pixelProof`. State becomes `GPU WORKLOAD EXECUTED` only after `FlushAsync`,
submission completion, and readback succeed.

- [x] **Step 5: Add browser readback**

Copy the canvas render result to a `MAP_READ` buffer with 256-byte row alignment,
sample three fixed triangle-interior coordinates, and return their RGBA bytes.
Acceptance bounds must require a dark background plus distinct cyan/blue/magenta
samples; identical or all-zero samples fail.

- [x] **Step 6: Verify tests and publish**

Run:

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj -c Release --filter FullyQualifiedName~WebGpu
dotnet publish src/Rekall.Age.Player.Web/Rekall.Age.Player.Web.csproj -c Release --no-restore
```

Expected: all WebGPU tests and Release publish pass with zero warnings/errors.

- [x] **Step 7: Commit**

```powershell
git add src/Rekall.Age.Player.Web tests/Rekall.Age.Tests
git commit -m "feat(web): render runtime workload with WebGPU"
```

### Task 5: Real-browser acceptance, documentation, and safety checkpoint

**Files:**
- Create: `eng/accept-webgpu-player.ps1`
- Modify: `docs/production/2026-08-22-web-runtime-proof.md`
- Modify: `docs/production/PROGRESS.md`
- Modify: `docs/superpowers/plans/2026-08-22-rendering-device.md`
- Modify: `README.md`

**Interfaces:**
- Consumes: published Web player and `window.rekallWebGpuEvidence`.
- Produces: deterministic JSON acceptance output and a committed browser capture.

- [x] **Step 1: Write the acceptance script around observable evidence**

The script publishes to a clean temporary directory, starts a loopback static
server, and reports the URL/output root. Browser control opens that URL, waits
for `body[data-device="ready"]`, reads `window.rekallWebGpuEvidence`, checks
`submittedFrames >= 1`, asserts empty diagnostics and literal pixel bounds,
checks browser warnings/errors, and captures the real canvas.

- [x] **Step 2: Run real Chromium acceptance**

Expected machine state:

```json
{
  "backend": "WebGPU",
  "protocolVersion": 1,
  "workloadId": "proof.webgpu.asset-independent",
  "submittedFrames": 1,
  "diagnostics": [],
  "pixelProof": { "passed": true }
}
```

- [x] **Step 3: Run the full release gate**

```powershell
dotnet build Rekall.Age.sln -c Release --no-restore
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj -c Release --no-build --no-restore
dotnet test tests/Rekall.Age.Studio.Tests/Rekall.Age.Studio.Tests.csproj -c Release --no-build --no-restore
git diff --check
```

Expected: zero warnings/errors, all engine and Studio tests pass, and no diff
errors.

- [x] **Step 4: Update evidence without overstating web export**

Record exact test counts, publish output, browser/runtime versions, WebGPU
adapter/device status, command count, pixel samples, screenshot path, and the
remaining AOT/input/audio/storage/WebGL/package/gameplay gates.

- [x] **Step 5: Request focused review and fix all blockers**

Review protocol bounds, rollback, browser object lifecycle, resource-kind
mapping, WGSL layout compatibility, readback row alignment, device loss, and
claim wording. Rerun affected tests and browser acceptance after every fix.

- [x] **Step 6: Commit and push**

```powershell
git add eng/accept-webgpu-player.ps1 docs README.md src tests Rekall.Age.sln
git commit -m "feat(web): prove AGE WebGPU workload execution"
git push origin codex/studio-interaction
```
