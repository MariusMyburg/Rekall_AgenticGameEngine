# Rekall AGE

Rekall AGE is a greenfield, agent-native C# game engine.

The current MVP includes:

- typed command bus through `IRekallAgeCommand`
- transaction tracking
- project capability manifests
- deterministic scene/entity/component files
- C# module attributes and reflection-based component schema discovery
- C# module scaffolding for agent-authored or human-authored gameplay modules
- C# module source writing through the command bus for MCP-first agents
- C# module build command for compiling scaffolded gameplay modules
- playable game readiness verification workflow for validation, build, and playtest checks
- playable game packaging workflow that verifies, bundles game content, writes a package manifest, creates a zip archive, and publishes launch artifacts
- project module assembly loading for agent-readable schemas after build
- module-authored playable runtime execution; no engine-owned fallback games
- Vulkan-first internal rendering backend catalog with Direct3D 12 extension point
- native Vulkan loader probing for agent-readable backend diagnostics
- native Vulkan logical-device bootstrap validation with physical-device and graphics-queue selection
- native Vulkan command-pool allocation, empty command-buffer recording, queue submission, and fence wait validation
- native Vulkan mapped buffer creation with host-visible memory allocation and byte writes
- native Vulkan 2D image creation with device-local memory allocation and binding
- native Vulkan offscreen render target creation with image view, render pass, and framebuffer
- native Vulkan clear render-pass submission, readback, PNG capture, and agent-controlled clear color
- low-level render plan authoring, validation, command-buffer recording, and deterministic execution artifacts
- Vulkan render-plan execution for clear render passes
- deterministic asset import and catalog listing commands
- entity inspection and single-property component mutation commands
- MVP terminal player for module-authored projects, including deterministic playtest frames
- structured scene playtest assertions for MCP/headless agent loops
- structured validation
- compact agent project summaries
- MCP tool catalog skeleton
- stdio MCP JSON-RPC adapter for `initialize`, `tools/list`, and `tools/call`
- CLI adapter over the same command bus
- headless runtime smoke execution with active gameplay-system observations
- deterministic software screenshot capture through command bus
- built-in starter game workflows
- one-shot playable game workflow that creates, scaffolds, and builds genre-aware C# module code

## Starter Game Workflows

Agents can create these game foundations through the command bus or CLI:

- `pong`
- `breakout`
- `asteroids`
- `top-down-shooter`
- `platformer-2d`
- `tower-defense`
- `visual-novel`
- `first-person-exploration`
- `collectathon-3d`
- `puzzle`

Each template creates a project manifest, `Main` scene, active camera, playable loop marker, and starter entities/components appropriate to the genre.

## Build

```powershell
dotnet build Rekall.AGE.sln
```

## Test

```powershell
dotnet test Rekall.AGE.sln
```

## CLI Example

```powershell
dotnet run --project src/Rekall.Age.Cli -- templates list
dotnet run --project src/Rekall.Age.Cli -- mcp stdio
dotnet run --project src/Rekall.Age.Cli -- render backends
dotnet run --project src/Rekall.Age.Cli -- render vulkan probe
dotnet run --project src/Rekall.Age.Cli -- render vulkan device bootstrap discrete-gpu
dotnet run --project src/Rekall.Age.Cli -- render vulkan command-buffer submit-empty discrete-gpu
dotnet run --project src/Rekall.Age.Cli -- render vulkan buffer create-mapped 256 vertex-buffer discrete-gpu
dotnet run --project src/Rekall.Age.Cli -- render vulkan image create-bound 64 64 R8G8B8A8_UNorm color-attachment discrete-gpu
dotnet run --project src/Rekall.Age.Cli -- render vulkan render-target create 128 72 R8G8B8A8_UNorm discrete-gpu
dotnet run --project src/Rekall.Age.Cli -- render vulkan render-pass read-clear 32 32 R8G8B8A8_UNorm discrete-gpu 0.25 0.5 0.75 1
dotnet run --project src/Rekall.Age.Cli -- render vulkan render-pass capture-clear 32 32 R8G8B8A8_UNorm discrete-gpu .age-sandbox/Artifacts/Vulkan 0.25 0.5 0.75 1
dotnet run --project src/Rekall.Age.Cli -- render plan create .age-sandbox software Preview
dotnet run --project src/Rekall.Age.Cli -- render resource add .age-sandbox preview-color image R8G8B8A8_UNorm color-attachment
dotnet run --project src/Rekall.Age.Cli -- render command-buffer record .age-sandbox main graphics '[{"op":"begin-render-pass","label":"preview","arguments":{"target":"preview-color"}},{"op":"draw-rect","label":"agent-rect","arguments":{"x":"8","y":"8","width":"24","height":"16","color":"#ffcc33"}},{"op":"end-render-pass","label":"preview","arguments":{}}]'
dotnet run --project src/Rekall.Age.Cli -- render plan validate .age-sandbox
dotnet run --project src/Rekall.Age.Cli -- render plan execute .age-sandbox .age-sandbox/Artifacts/Render
dotnet run --project src/Rekall.Age.Cli -- render plan create .age-sandbox vulkan NativePreview
dotnet run --project src/Rekall.Age.Cli -- render resource add .age-sandbox frame-color image R8G8B8A8_UNorm color-attachment,transfer-src
dotnet run --project src/Rekall.Age.Cli -- render command-buffer record .age-sandbox main graphics '[{"op":"begin-render-pass","label":"frame","arguments":{"target":"frame-color","width":"32","height":"16","preferredDeviceType":"discrete-gpu"}},{"op":"clear","label":"sky","arguments":{"r":"0.25","g":"0.5","b":"0.75","a":"1"}},{"op":"end-render-pass","label":"frame","arguments":{}}]'
dotnet run --project src/Rekall.Age.Cli -- render plan execute .age-sandbox .age-sandbox/Artifacts/Render
dotnet run --project src/Rekall.Age.Cli -- module schemas
dotnet run --project src/Rekall.Age.Cli -- game create .age-sandbox "Crystal Mines" pong
dotnet run --project src/Rekall.Age.Cli -- game create-playable .age-sandbox "Playable Pong" pong
dotnet run --project src/Rekall.Age.Cli -- game verify-playable .age-sandbox Main 2 '[{"frameIndex":0,"contains":"PONG"}]'
dotnet run --project src/Rekall.Age.Cli -- game package-playable .age-sandbox Main .age-sandbox/Builds/RekallAgePlayer
dotnet run --project src/Rekall.Age.Cli -- asset import .age-sandbox .\player.png sprite "Player Ship"
dotnet run --project src/Rekall.Age.Cli -- asset list .age-sandbox
dotnet run --project src/Rekall.Age.Cli -- module scaffold-playable .age-sandbox crystal.playable "Crystal Playable" CrystalPlayable crystal
dotnet run --project src/Rekall.Age.Cli -- module sources .age-sandbox
dotnet run --project src/Rekall.Age.Cli -- module read-source .age-sandbox CrystalPlayable CrystalPlayableModule.cs
dotnet run --project src/Rekall.Age.Cli -- module write-source .age-sandbox CrystalPlayable CrystalPlayableModule.cs .\CrystalPlayableModule.cs
dotnet run --project src/Rekall.Age.Cli -- build modules .age-sandbox
dotnet run --project src/Rekall.Age.Cli -- module schemas project .age-sandbox
dotnet run --project src/Rekall.Age.Cli -- context summary .age-sandbox
dotnet run --project src/Rekall.Age.Cli -- context scene .age-sandbox Main
dotnet run --project src/Rekall.Age.Cli -- entity inspect .age-sandbox Main <entity-id>
dotnet run --project src/Rekall.Age.Cli -- component set .age-sandbox Main <entity-id> Rekall.Transform x 42
dotnet run --project src/Rekall.Age.Cli -- play scene .age-sandbox Main 4
dotnet run --project src/Rekall.Age.Cli -- play scene .age-sandbox Main 2 '[{"verticalAxis":1,"primaryAction":true},{"verticalAxis":-1}]'
dotnet run --project src/Rekall.Age.Cli -- playtest scene .age-sandbox Main 2 '[{"verticalAxis":1,"primaryAction":true},{"verticalAxis":-1}]' '[{"frameIndex":0,"contains":"Score 10"},{"frameIndex":1,"contains":"Left paddle lane 0"}]'
dotnet run --project src/Rekall.Age.Cli -- build player .age-sandbox Main
dotnet run --project src/Rekall.Age.Player -- .age-sandbox Main
dotnet run --project src/Rekall.Age.Player -- .age-sandbox Main --frames 2 --inputs '[{"verticalAxis":1,"primaryAction":true},{"verticalAxis":-1}]'
dotnet run --project src/Rekall.Age.Cli -- run scene .age-sandbox Main 0.1
dotnet run --project src/Rekall.Age.Cli -- capture screenshot .age-sandbox Main
```
