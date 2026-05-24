# Rekall AGE

Rekall AGE is a greenfield, agent-native C# game engine.

The current MVP includes:

- typed command bus through `IRekallAgeCommand`
- transaction tracking
- project capability manifests
- deterministic scene/entity/component files
- C# module attributes and reflection-based component schema discovery
- C# module scaffolding for agent-authored or human-authored gameplay modules
- C# module build command for compiling scaffolded gameplay modules
- project module assembly loading for agent-readable schemas after build
- entity inspection and single-property component mutation commands
- structured validation
- compact agent project summaries
- MCP tool catalog skeleton
- stdio MCP JSON-RPC adapter for `initialize`, `tools/list`, and `tools/call`
- CLI adapter over the same command bus
- headless runtime smoke execution with active gameplay-system observations
- deterministic software screenshot capture through command bus
- built-in starter game workflows

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
dotnet run --project src/Rekall.Age.Cli -- module schemas
dotnet run --project src/Rekall.Age.Cli -- game create .age-sandbox "Crystal Mines" puzzle
dotnet run --project src/Rekall.Age.Cli -- module scaffold .age-sandbox crystal.mining "Crystal Mining" CrystalMining MiningController
dotnet run --project src/Rekall.Age.Cli -- build modules .age-sandbox
dotnet run --project src/Rekall.Age.Cli -- module schemas project .age-sandbox
dotnet run --project src/Rekall.Age.Cli -- context summary .age-sandbox
dotnet run --project src/Rekall.Age.Cli -- context scene .age-sandbox Main
dotnet run --project src/Rekall.Age.Cli -- entity inspect .age-sandbox Main <entity-id>
dotnet run --project src/Rekall.Age.Cli -- component set .age-sandbox Main <entity-id> Rekall.Transform x 42
dotnet run --project src/Rekall.Age.Cli -- run scene .age-sandbox Main 0.1
dotnet run --project src/Rekall.Age.Cli -- capture screenshot .age-sandbox Main
```
