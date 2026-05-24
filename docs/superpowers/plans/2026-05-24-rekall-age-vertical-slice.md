# Rekall AGE Vertical Slice 0 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the first agent-native Rekall AGE vertical slice: solution structure, command bus, transactions, project capabilities, world serialization, validation, agent context, CLI, MCP skeleton, headless runtime, and deterministic screenshot capture.

**Architecture:** Start with small, testable libraries and keep all mutation behind `IRekallAgeCommand`. The CLI and MCP skeleton are adapters over the same command bus. Rendering is a deterministic software preview first, so agents can validate screenshots before a real graphics backend exists.

**Tech Stack:** C# 13, .NET 10.0, xUnit, `System.Text.Json`, built-in `System.IO.Compression` for PNG output.

---

## Scope

This plan implements the first vertical slice from the approved design spec. It does not implement full v1 graphics, physics, audio, packaging, editor UI, source generators, or real MCP SDK integration. It creates the stable foundation those later plans will build on.

## File Structure

Create these projects:

- `src/Rekall.Age.Core/Rekall.Age.Core.csproj`: command bus, schemas, command context, command results, transactions, diagnostics.
- `src/Rekall.Age.Project/Rekall.Age.Project.csproj`: project manifest, capability model, manifest persistence.
- `src/Rekall.Age.World/Rekall.Age.World.csproj`: scene, entity, component document model, deterministic JSON persistence.
- `src/Rekall.Age.Validation/Rekall.Age.Validation.csproj`: validation result model and built-in project/scene validators.
- `src/Rekall.Age.Agent/Rekall.Age.Agent.csproj`: compact agent context summaries.
- `src/Rekall.Age.Runtime/Rekall.Age.Runtime.csproj`: headless runtime loop abstraction.
- `src/Rekall.Age.Rendering/Rekall.Age.Rendering.csproj`: deterministic software preview and PNG capture.
- `src/Rekall.Age.Cli/Rekall.Age.Cli.csproj`: command-line adapter over command bus.
- `src/Rekall.Age.Mcp/Rekall.Age.Mcp.csproj`: MCP tool catalog skeleton over command bus.
- `tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj`: unit and vertical-slice tests.

Create these repo files:

- `Rekall.AGE.sln`
- `Directory.Build.props`
- `.gitignore`
- `README.md`

---

### Task 1: Solution Skeleton

**Files:**
- Create: `Rekall.AGE.sln`
- Create: `Directory.Build.props`
- Create: `.gitignore`
- Create: `README.md`
- Create: all `.csproj` files listed in File Structure

- [ ] **Step 1: Create solution and projects**

Run:

```powershell
dotnet new sln -n Rekall.AGE
dotnet new classlib -n Rekall.Age.Core -o src/Rekall.Age.Core
dotnet new classlib -n Rekall.Age.Project -o src/Rekall.Age.Project
dotnet new classlib -n Rekall.Age.World -o src/Rekall.Age.World
dotnet new classlib -n Rekall.Age.Validation -o src/Rekall.Age.Validation
dotnet new classlib -n Rekall.Age.Agent -o src/Rekall.Age.Agent
dotnet new classlib -n Rekall.Age.Runtime -o src/Rekall.Age.Runtime
dotnet new classlib -n Rekall.Age.Rendering -o src/Rekall.Age.Rendering
dotnet new console -n Rekall.Age.Cli -o src/Rekall.Age.Cli
dotnet new classlib -n Rekall.Age.Mcp -o src/Rekall.Age.Mcp
dotnet new xunit -n Rekall.Age.Tests -o tests/Rekall.Age.Tests
dotnet sln Rekall.AGE.sln add src/Rekall.Age.Core/Rekall.Age.Core.csproj
dotnet sln Rekall.AGE.sln add src/Rekall.Age.Project/Rekall.Age.Project.csproj
dotnet sln Rekall.AGE.sln add src/Rekall.Age.World/Rekall.Age.World.csproj
dotnet sln Rekall.AGE.sln add src/Rekall.Age.Validation/Rekall.Age.Validation.csproj
dotnet sln Rekall.AGE.sln add src/Rekall.Age.Agent/Rekall.Age.Agent.csproj
dotnet sln Rekall.AGE.sln add src/Rekall.Age.Runtime/Rekall.Age.Runtime.csproj
dotnet sln Rekall.AGE.sln add src/Rekall.Age.Rendering/Rekall.Age.Rendering.csproj
dotnet sln Rekall.AGE.sln add src/Rekall.Age.Cli/Rekall.Age.Cli.csproj
dotnet sln Rekall.AGE.sln add src/Rekall.Age.Mcp/Rekall.Age.Mcp.csproj
dotnet sln Rekall.AGE.sln add tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj
```

Expected: each `dotnet new` command succeeds and `dotnet sln list` shows ten projects.

- [ ] **Step 2: Add project references**

Run:

```powershell
dotnet add src/Rekall.Age.Project/Rekall.Age.Project.csproj reference src/Rekall.Age.Core/Rekall.Age.Core.csproj
dotnet add src/Rekall.Age.World/Rekall.Age.World.csproj reference src/Rekall.Age.Core/Rekall.Age.Core.csproj
dotnet add src/Rekall.Age.Validation/Rekall.Age.Validation.csproj reference src/Rekall.Age.Core/Rekall.Age.Core.csproj src/Rekall.Age.Project/Rekall.Age.Project.csproj src/Rekall.Age.World/Rekall.Age.World.csproj
dotnet add src/Rekall.Age.Agent/Rekall.Age.Agent.csproj reference src/Rekall.Age.Project/Rekall.Age.Project.csproj src/Rekall.Age.World/Rekall.Age.World.csproj src/Rekall.Age.Validation/Rekall.Age.Validation.csproj
dotnet add src/Rekall.Age.Runtime/Rekall.Age.Runtime.csproj reference src/Rekall.Age.Core/Rekall.Age.Core.csproj src/Rekall.Age.World/Rekall.Age.World.csproj
dotnet add src/Rekall.Age.Rendering/Rekall.Age.Rendering.csproj reference src/Rekall.Age.World/Rekall.Age.World.csproj
dotnet add src/Rekall.Age.Cli/Rekall.Age.Cli.csproj reference src/Rekall.Age.Core/Rekall.Age.Core.csproj src/Rekall.Age.Project/Rekall.Age.Project.csproj src/Rekall.Age.World/Rekall.Age.World.csproj src/Rekall.Age.Validation/Rekall.Age.Validation.csproj src/Rekall.Age.Agent/Rekall.Age.Agent.csproj src/Rekall.Age.Rendering/Rekall.Age.Rendering.csproj
dotnet add src/Rekall.Age.Mcp/Rekall.Age.Mcp.csproj reference src/Rekall.Age.Core/Rekall.Age.Core.csproj src/Rekall.Age.Agent/Rekall.Age.Agent.csproj
dotnet add tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj reference src/Rekall.Age.Core/Rekall.Age.Core.csproj src/Rekall.Age.Project/Rekall.Age.Project.csproj src/Rekall.Age.World/Rekall.Age.World.csproj src/Rekall.Age.Validation/Rekall.Age.Validation.csproj src/Rekall.Age.Agent/Rekall.Age.Agent.csproj src/Rekall.Age.Runtime/Rekall.Age.Runtime.csproj src/Rekall.Age.Rendering/Rekall.Age.Rendering.csproj src/Rekall.Age.Mcp/Rekall.Age.Mcp.csproj
```

Expected: each reference command reports that references were added.

- [ ] **Step 3: Replace generated placeholder files**

Delete generated `Class1.cs` files and the generated xUnit `UnitTest1.cs`:

```powershell
Remove-Item src/*/Class1.cs
Remove-Item tests/Rekall.Age.Tests/UnitTest1.cs
```

Expected: no generated placeholder source files remain.

- [ ] **Step 4: Add shared build settings**

Create `Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <LangVersion>13.0</LangVersion>
    <AnalysisLevel>latest</AnalysisLevel>
  </PropertyGroup>
</Project>
```

- [ ] **Step 5: Add repo ignore file**

Create `.gitignore`:

```gitignore
bin/
obj/
.vs/
.idea/
*.user
*.suo
TestResults/
Artifacts/
*.agecache
```

- [ ] **Step 6: Add README**

Create `README.md`:

```markdown
# Rekall AGE

Rekall AGE is a greenfield, agent-native C# game engine.

The first vertical slice focuses on the command bus, project capabilities, deterministic world files, validation, agent context, CLI/MCP adapters, headless runtime, and deterministic screenshot capture.
```

- [ ] **Step 7: Verify clean build**

Run:

```powershell
dotnet build Rekall.AGE.sln
```

Expected: build succeeds with zero warnings.

- [ ] **Step 8: Commit**

Run:

```powershell
git -c safe.directory=F:/Dev/Rekall_AGE add Rekall.AGE.sln Directory.Build.props .gitignore README.md src tests
git -c safe.directory=F:/Dev/Rekall_AGE commit -m "chore: scaffold Rekall AGE solution"
```

Expected: commit succeeds.

---

### Task 2: Command Bus And Transactions

**Files:**
- Create: `src/Rekall.Age.Core/Commands/IRekallAgeCommand.cs`
- Create: `src/Rekall.Age.Core/Commands/RekallAgeCommandSchema.cs`
- Create: `src/Rekall.Age.Core/Commands/RekallAgeCommandContext.cs`
- Create: `src/Rekall.Age.Core/Commands/RekallAgeCommandResult.cs`
- Create: `src/Rekall.Age.Core/Commands/RekallAgeCommandRegistry.cs`
- Create: `src/Rekall.Age.Core/Transactions/RekallAgeTransaction.cs`
- Create: `tests/Rekall.Age.Tests/Core/CommandBusTests.cs`

- [ ] **Step 1: Write failing command registry test**

Create `tests/Rekall.Age.Tests/Core/CommandBusTests.cs`:

```csharp
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;

namespace Rekall.Age.Tests.Core;

public sealed class CommandBusTests
{
    [Fact]
    public async Task RegisteredCommandExecutesInsideTransaction()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new EchoCommand());

        var transaction = RekallAgeTransaction.Begin("echo test");
        var context = new RekallAgeCommandContext("agent", transaction, CancellationToken.None);

        var result = await registry.ExecuteAsync<EchoRequest, EchoResult>(
            "test.echo",
            new EchoRequest("hello"),
            context);

        Assert.True(result.Ok);
        Assert.Equal("hello", result.Value.Message);
        Assert.Contains("echo:hello", transaction.ChangedResources);
    }

    private sealed record EchoRequest(string Message);

    private sealed record EchoResult(string Message);

    private sealed class EchoCommand : IRekallAgeCommand<EchoRequest, EchoResult>
    {
        public string Name => "test.echo";

        public RekallAgeCommandSchema Schema => new(
            Name,
            "Echoes a message for command-bus tests.",
            typeof(EchoRequest).FullName!,
            typeof(EchoResult).FullName!);

        public ValueTask<RekallAgeCommandResult<EchoResult>> ExecuteAsync(
            EchoRequest request,
            RekallAgeCommandContext context)
        {
            context.Transaction.RecordChangedResource($"echo:{request.Message}");
            return ValueTask.FromResult(RekallAgeCommandResult<EchoResult>.Success(new EchoResult(request.Message)));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter FullyQualifiedName~CommandBusTests
```

Expected: FAIL because `Rekall.Age.Core.Commands` types do not exist.

- [ ] **Step 3: Implement command abstractions**

Create `src/Rekall.Age.Core/Commands/IRekallAgeCommand.cs`:

```csharp
namespace Rekall.Age.Core.Commands;

public interface IRekallAgeCommand<TRequest, TResult>
{
    string Name { get; }

    RekallAgeCommandSchema Schema { get; }

    ValueTask<RekallAgeCommandResult<TResult>> ExecuteAsync(
        TRequest request,
        RekallAgeCommandContext context);
}
```

Create `src/Rekall.Age.Core/Commands/RekallAgeCommandSchema.cs`:

```csharp
namespace Rekall.Age.Core.Commands;

public sealed record RekallAgeCommandSchema(
    string Name,
    string Description,
    string RequestType,
    string ResultType);
```

Create `src/Rekall.Age.Core/Commands/RekallAgeCommandContext.cs`:

```csharp
using Rekall.Age.Core.Transactions;

namespace Rekall.Age.Core.Commands;

public sealed record RekallAgeCommandContext(
    string Actor,
    RekallAgeTransaction Transaction,
    CancellationToken CancellationToken);
```

Create `src/Rekall.Age.Core/Commands/RekallAgeCommandResult.cs`:

```csharp
namespace Rekall.Age.Core.Commands;

public sealed record RekallAgeCommandError(string Code, string Message, string? Target = null);

public sealed record RekallAgeCommandResult<TResult>(
    bool Ok,
    string Summary,
    TResult Value,
    IReadOnlyList<RekallAgeCommandError> Errors)
{
    public static RekallAgeCommandResult<TResult> Success(TResult value, string summary = "Command succeeded.")
    {
        return new RekallAgeCommandResult<TResult>(true, summary, value, Array.Empty<RekallAgeCommandError>());
    }

    public static RekallAgeCommandResult<TResult> Failure(
        TResult value,
        string summary,
        IReadOnlyList<RekallAgeCommandError> errors)
    {
        return new RekallAgeCommandResult<TResult>(false, summary, value, errors);
    }
}
```

- [ ] **Step 4: Implement transaction and registry**

Create `src/Rekall.Age.Core/Transactions/RekallAgeTransaction.cs`:

```csharp
namespace Rekall.Age.Core.Transactions;

public sealed class RekallAgeTransaction
{
    private readonly List<string> _changedResources = [];

    private RekallAgeTransaction(string name)
    {
        Id = $"txn_{Guid.NewGuid():N}";
        Name = name;
        StartedAtUtc = DateTimeOffset.UtcNow;
    }

    public string Id { get; }

    public string Name { get; }

    public DateTimeOffset StartedAtUtc { get; }

    public IReadOnlyList<string> ChangedResources => _changedResources;

    public static RekallAgeTransaction Begin(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Transaction name is required.", nameof(name));
        }

        return new RekallAgeTransaction(name);
    }

    public void RecordChangedResource(string resource)
    {
        if (string.IsNullOrWhiteSpace(resource))
        {
            throw new ArgumentException("Changed resource is required.", nameof(resource));
        }

        if (!_changedResources.Contains(resource, StringComparer.Ordinal))
        {
            _changedResources.Add(resource);
        }
    }
}
```

Create `src/Rekall.Age.Core/Commands/RekallAgeCommandRegistry.cs`:

```csharp
namespace Rekall.Age.Core.Commands;

public sealed class RekallAgeCommandRegistry
{
    private readonly Dictionary<string, object> _commands = new(StringComparer.Ordinal);

    public IReadOnlyList<RekallAgeCommandSchema> Schemas =>
        _commands.Values
            .Select(command => (RekallAgeCommandSchema)command.GetType().GetProperty(nameof(IRekallAgeCommand<object, object>.Schema))!.GetValue(command)!)
            .OrderBy(schema => schema.Name, StringComparer.Ordinal)
            .ToArray();

    public void Register<TRequest, TResult>(IRekallAgeCommand<TRequest, TResult> command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!_commands.TryAdd(command.Name, command))
        {
            throw new InvalidOperationException($"Command '{command.Name}' is already registered.");
        }
    }

    public async ValueTask<RekallAgeCommandResult<TResult>> ExecuteAsync<TRequest, TResult>(
        string name,
        TRequest request,
        RekallAgeCommandContext context)
    {
        if (!_commands.TryGetValue(name, out var command))
        {
            var error = new RekallAgeCommandError("REKALL_COMMAND_NOT_FOUND", $"Command '{name}' is not registered.");
            return RekallAgeCommandResult<TResult>.Failure(default!, error.Message, [error]);
        }

        if (command is not IRekallAgeCommand<TRequest, TResult> typed)
        {
            var error = new RekallAgeCommandError("REKALL_COMMAND_TYPE_MISMATCH", $"Command '{name}' was called with incompatible request or result types.");
            return RekallAgeCommandResult<TResult>.Failure(default!, error.Message, [error]);
        }

        context.CancellationToken.ThrowIfCancellationRequested();
        return await typed.ExecuteAsync(request, context);
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run:

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter FullyQualifiedName~CommandBusTests
```

Expected: PASS.

- [ ] **Step 6: Commit**

Run:

```powershell
git -c safe.directory=F:/Dev/Rekall_AGE add src/Rekall.Age.Core tests/Rekall.Age.Tests/Core/CommandBusTests.cs
git -c safe.directory=F:/Dev/Rekall_AGE commit -m "feat: add Rekall AGE command bus"
```

Expected: commit succeeds.

---

### Task 3: Project Manifest And Capabilities

**Files:**
- Create: `src/Rekall.Age.Project/RekallAgeCapability.cs`
- Create: `src/Rekall.Age.Project/RekallAgeProjectManifest.cs`
- Create: `src/Rekall.Age.Project/RekallAgeProjectStore.cs`
- Create: `src/Rekall.Age.Project/Commands/CreateProjectCommand.cs`
- Create: `src/Rekall.Age.Project/Commands/AddCapabilityCommand.cs`
- Create: `tests/Rekall.Age.Tests/Project/ProjectManifestTests.cs`

- [ ] **Step 1: Write failing manifest persistence test**

Create `tests/Rekall.Age.Tests/Project/ProjectManifestTests.cs`:

```csharp
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Project;
using Rekall.Age.Project.Commands;

namespace Rekall.Age.Tests.Project;

public sealed class ProjectManifestTests
{
    [Fact]
    public async Task CreateProjectWritesDeterministicManifest()
    {
        var root = TestPaths.CreateTempDirectory();
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new CreateProjectCommand());

        var transaction = RekallAgeTransaction.Begin("create project");
        var context = new RekallAgeCommandContext("test", transaction, CancellationToken.None);

        var result = await registry.ExecuteAsync<CreateProjectRequest, CreateProjectResult>(
            "rekall.project.create",
            new CreateProjectRequest(root, "Crystal Mines", ["world", "rendering2d"]),
            context);

        Assert.True(result.Ok);
        var manifestPath = Path.Combine(root, "rekall.project.json");
        Assert.True(File.Exists(manifestPath));

        var json = await File.ReadAllTextAsync(manifestPath);
        Assert.Contains("\"name\": \"Crystal Mines\"", json);
        Assert.Contains("\"world\"", json);
        Assert.Contains("\"rendering2d\"", json);
    }
}
```

Add `tests/Rekall.Age.Tests/TestPaths.cs`:

```csharp
namespace Rekall.Age.Tests;

internal static class TestPaths
{
    public static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "rekall-age-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter FullyQualifiedName~ProjectManifestTests
```

Expected: FAIL because project manifest types do not exist.

- [ ] **Step 3: Implement manifest model and store**

Create `src/Rekall.Age.Project/RekallAgeCapability.cs`:

```csharp
namespace Rekall.Age.Project;

public sealed record RekallAgeCapability(string Id)
{
    public static RekallAgeCapability Create(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Capability id is required.", nameof(id));
        }

        return new RekallAgeCapability(id.Trim().ToLowerInvariant());
    }
}
```

Create `src/Rekall.Age.Project/RekallAgeProjectManifest.cs`:

```csharp
namespace Rekall.Age.Project;

public sealed record RekallAgeProjectManifest(
    string Name,
    int SchemaVersion,
    IReadOnlyList<string> Capabilities)
{
    public static RekallAgeProjectManifest Create(string name, IEnumerable<string> capabilities)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Project name is required.", nameof(name));
        }

        var normalized = capabilities
            .Select(id => RekallAgeCapability.Create(id).Id)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        return new RekallAgeProjectManifest(name.Trim(), 1, normalized);
    }

    public RekallAgeProjectManifest AddCapability(string capability)
    {
        return this with
        {
            Capabilities = Capabilities
                .Concat([RekallAgeCapability.Create(capability).Id])
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray()
        };
    }
}
```

Create `src/Rekall.Age.Project/RekallAgeProjectStore.cs`:

```csharp
using System.Text.Json;

namespace Rekall.Age.Project;

public sealed class RekallAgeProjectStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public const string ManifestFileName = "rekall.project.json";

    public async ValueTask SaveAsync(string projectRoot, RekallAgeProjectManifest manifest, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(projectRoot);
        var path = Path.Combine(projectRoot, ManifestFileName);
        var json = JsonSerializer.Serialize(manifest, JsonOptions);
        await File.WriteAllTextAsync(path, json + Environment.NewLine, cancellationToken);
    }

    public async ValueTask<RekallAgeProjectManifest> LoadAsync(string projectRoot, CancellationToken cancellationToken)
    {
        var path = Path.Combine(projectRoot, ManifestFileName);
        await using var stream = File.OpenRead(path);
        var manifest = await JsonSerializer.DeserializeAsync<RekallAgeProjectManifest>(stream, JsonOptions, cancellationToken);
        return manifest ?? throw new InvalidOperationException($"Manifest '{path}' could not be read.");
    }
}
```

- [ ] **Step 4: Implement project commands**

Create `src/Rekall.Age.Project/Commands/CreateProjectCommand.cs`:

```csharp
using Rekall.Age.Core.Commands;

namespace Rekall.Age.Project.Commands;

public sealed record CreateProjectRequest(string ProjectRoot, string Name, IReadOnlyList<string> Capabilities);

public sealed record CreateProjectResult(string ManifestPath, RekallAgeProjectManifest Manifest);

public sealed class CreateProjectCommand : IRekallAgeCommand<CreateProjectRequest, CreateProjectResult>
{
    private readonly RekallAgeProjectStore _store = new();

    public string Name => "rekall.project.create";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Creates a Rekall AGE project manifest.",
        typeof(CreateProjectRequest).FullName!,
        typeof(CreateProjectResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<CreateProjectResult>> ExecuteAsync(
        CreateProjectRequest request,
        RekallAgeCommandContext context)
    {
        var manifest = RekallAgeProjectManifest.Create(request.Name, request.Capabilities);
        await _store.SaveAsync(request.ProjectRoot, manifest, context.CancellationToken);
        var manifestPath = Path.Combine(request.ProjectRoot, RekallAgeProjectStore.ManifestFileName);
        context.Transaction.RecordChangedResource(manifestPath);

        return RekallAgeCommandResult<CreateProjectResult>.Success(
            new CreateProjectResult(manifestPath, manifest),
            $"Created Rekall AGE project '{manifest.Name}'.");
    }
}
```

Create `src/Rekall.Age.Project/Commands/AddCapabilityCommand.cs`:

```csharp
using Rekall.Age.Core.Commands;

namespace Rekall.Age.Project.Commands;

public sealed record AddCapabilityRequest(string ProjectRoot, string Capability);

public sealed record AddCapabilityResult(RekallAgeProjectManifest Manifest);

public sealed class AddCapabilityCommand : IRekallAgeCommand<AddCapabilityRequest, AddCapabilityResult>
{
    private readonly RekallAgeProjectStore _store = new();

    public string Name => "rekall.capability.add";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Adds a capability to a Rekall AGE project.",
        typeof(AddCapabilityRequest).FullName!,
        typeof(AddCapabilityResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<AddCapabilityResult>> ExecuteAsync(
        AddCapabilityRequest request,
        RekallAgeCommandContext context)
    {
        var manifest = await _store.LoadAsync(request.ProjectRoot, context.CancellationToken);
        var updated = manifest.AddCapability(request.Capability);
        await _store.SaveAsync(request.ProjectRoot, updated, context.CancellationToken);
        context.Transaction.RecordChangedResource(Path.Combine(request.ProjectRoot, RekallAgeProjectStore.ManifestFileName));

        return RekallAgeCommandResult<AddCapabilityResult>.Success(
            new AddCapabilityResult(updated),
            $"Added capability '{request.Capability}'.");
    }
}
```

- [ ] **Step 5: Run project tests**

Run:

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter FullyQualifiedName~ProjectManifestTests
```

Expected: PASS.

- [ ] **Step 6: Commit**

Run:

```powershell
git -c safe.directory=F:/Dev/Rekall_AGE add src/Rekall.Age.Project tests/Rekall.Age.Tests/Project tests/Rekall.Age.Tests/TestPaths.cs
git -c safe.directory=F:/Dev/Rekall_AGE commit -m "feat: add project manifest capabilities"
```

Expected: commit succeeds.

---

### Task 4: Deterministic World Serialization

**Files:**
- Create: `src/Rekall.Age.World/RekallAgeComponentDocument.cs`
- Create: `src/Rekall.Age.World/RekallAgeEntityDocument.cs`
- Create: `src/Rekall.Age.World/RekallAgeSceneDocument.cs`
- Create: `src/Rekall.Age.World/RekallAgeSceneStore.cs`
- Create: `src/Rekall.Age.World/Commands/CreateSceneCommand.cs`
- Create: `src/Rekall.Age.World/Commands/CreateEntityCommand.cs`
- Create: `tests/Rekall.Age.Tests/World/SceneStoreTests.cs`

- [ ] **Step 1: Write failing scene command test**

Create `tests/Rekall.Age.Tests/World/SceneStoreTests.cs`:

```csharp
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.World;
using Rekall.Age.World.Commands;

namespace Rekall.Age.Tests.World;

public sealed class SceneStoreTests
{
    [Fact]
    public async Task CreateSceneAndEntityWritesStableJson()
    {
        var root = TestPaths.CreateTempDirectory();
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new CreateSceneCommand());
        registry.Register(new CreateEntityCommand());
        var context = new RekallAgeCommandContext("test", RekallAgeTransaction.Begin("world"), CancellationToken.None);

        await registry.ExecuteAsync<CreateSceneRequest, CreateSceneResult>(
            "rekall.scene.create",
            new CreateSceneRequest(root, "Main", ["world", "rendering2d"]),
            context);

        var entity = await registry.ExecuteAsync<CreateEntityRequest, CreateEntityResult>(
            "rekall.entity.create",
            new CreateEntityRequest(root, "Main", "Player", ["player"]),
            context);

        Assert.True(entity.Ok);

        var scenePath = Path.Combine(root, "Scenes", "Main.age.scene.json");
        var json = await File.ReadAllTextAsync(scenePath);
        Assert.Contains("\"name\": \"Main\"", json);
        Assert.Contains("\"name\": \"Player\"", json);
        Assert.Contains("\"tags\"", json);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter FullyQualifiedName~SceneStoreTests
```

Expected: FAIL because world document types do not exist.

- [ ] **Step 3: Implement world documents and store**

Create `src/Rekall.Age.World/RekallAgeComponentDocument.cs`:

```csharp
using System.Text.Json.Nodes;

namespace Rekall.Age.World;

public sealed record RekallAgeComponentDocument(string Type, JsonObject Properties);
```

Create `src/Rekall.Age.World/RekallAgeEntityDocument.cs`:

```csharp
namespace Rekall.Age.World;

public sealed record RekallAgeEntityDocument(
    string Id,
    string Name,
    IReadOnlyList<string> Tags,
    IReadOnlyList<RekallAgeComponentDocument> Components)
{
    public static RekallAgeEntityDocument Create(string name, IEnumerable<string> tags)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Entity name is required.", nameof(name));
        }

        var id = $"ent_{Guid.NewGuid():N}";
        var normalizedTags = tags
            .Select(tag => tag.Trim().ToLowerInvariant())
            .Where(tag => tag.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(tag => tag, StringComparer.Ordinal)
            .ToArray();

        return new RekallAgeEntityDocument(id, name.Trim(), normalizedTags, Array.Empty<RekallAgeComponentDocument>());
    }
}
```

Create `src/Rekall.Age.World/RekallAgeSceneDocument.cs`:

```csharp
namespace Rekall.Age.World;

public sealed record RekallAgeSceneDocument(
    string Id,
    string Name,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<RekallAgeEntityDocument> Entities)
{
    public static RekallAgeSceneDocument Create(string name, IEnumerable<string> capabilities)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Scene name is required.", nameof(name));
        }

        var normalizedCapabilities = capabilities
            .Select(capability => capability.Trim().ToLowerInvariant())
            .Where(capability => capability.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(capability => capability, StringComparer.Ordinal)
            .ToArray();

        return new RekallAgeSceneDocument($"scene_{Guid.NewGuid():N}", name.Trim(), normalizedCapabilities, Array.Empty<RekallAgeEntityDocument>());
    }

    public RekallAgeSceneDocument AddEntity(RekallAgeEntityDocument entity)
    {
        return this with
        {
            Entities = Entities
                .Concat([entity])
                .OrderBy(item => item.Name, StringComparer.Ordinal)
                .ThenBy(item => item.Id, StringComparer.Ordinal)
                .ToArray()
        };
    }
}
```

Create `src/Rekall.Age.World/RekallAgeSceneStore.cs`:

```csharp
using System.Text.Json;

namespace Rekall.Age.World;

public sealed class RekallAgeSceneStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string GetScenePath(string projectRoot, string sceneName)
    {
        return Path.Combine(projectRoot, "Scenes", $"{sceneName}.age.scene.json");
    }

    public async ValueTask SaveAsync(string projectRoot, RekallAgeSceneDocument scene, CancellationToken cancellationToken)
    {
        var scenesDirectory = Path.Combine(projectRoot, "Scenes");
        Directory.CreateDirectory(scenesDirectory);
        var path = GetScenePath(projectRoot, scene.Name);
        var json = JsonSerializer.Serialize(scene, JsonOptions);
        await File.WriteAllTextAsync(path, json + Environment.NewLine, cancellationToken);
    }

    public async ValueTask<RekallAgeSceneDocument> LoadAsync(string projectRoot, string sceneName, CancellationToken cancellationToken)
    {
        var path = GetScenePath(projectRoot, sceneName);
        await using var stream = File.OpenRead(path);
        var scene = await JsonSerializer.DeserializeAsync<RekallAgeSceneDocument>(stream, JsonOptions, cancellationToken);
        return scene ?? throw new InvalidOperationException($"Scene '{path}' could not be read.");
    }
}
```

- [ ] **Step 4: Implement scene and entity commands**

Create `src/Rekall.Age.World/Commands/CreateSceneCommand.cs`:

```csharp
using Rekall.Age.Core.Commands;

namespace Rekall.Age.World.Commands;

public sealed record CreateSceneRequest(string ProjectRoot, string Name, IReadOnlyList<string> Capabilities);

public sealed record CreateSceneResult(string ScenePath, RekallAgeSceneDocument Scene);

public sealed class CreateSceneCommand : IRekallAgeCommand<CreateSceneRequest, CreateSceneResult>
{
    private readonly RekallAgeSceneStore _store = new();

    public string Name => "rekall.scene.create";

    public RekallAgeCommandSchema Schema => new(Name, "Creates a Rekall AGE scene.", typeof(CreateSceneRequest).FullName!, typeof(CreateSceneResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<CreateSceneResult>> ExecuteAsync(CreateSceneRequest request, RekallAgeCommandContext context)
    {
        var scene = RekallAgeSceneDocument.Create(request.Name, request.Capabilities);
        await _store.SaveAsync(request.ProjectRoot, scene, context.CancellationToken);
        var path = _store.GetScenePath(request.ProjectRoot, scene.Name);
        context.Transaction.RecordChangedResource(path);
        return RekallAgeCommandResult<CreateSceneResult>.Success(new CreateSceneResult(path, scene), $"Created scene '{scene.Name}'.");
    }
}
```

Create `src/Rekall.Age.World/Commands/CreateEntityCommand.cs`:

```csharp
using Rekall.Age.Core.Commands;

namespace Rekall.Age.World.Commands;

public sealed record CreateEntityRequest(string ProjectRoot, string SceneName, string Name, IReadOnlyList<string> Tags);

public sealed record CreateEntityResult(string EntityId, RekallAgeSceneDocument Scene);

public sealed class CreateEntityCommand : IRekallAgeCommand<CreateEntityRequest, CreateEntityResult>
{
    private readonly RekallAgeSceneStore _store = new();

    public string Name => "rekall.entity.create";

    public RekallAgeCommandSchema Schema => new(Name, "Creates an entity in a Rekall AGE scene.", typeof(CreateEntityRequest).FullName!, typeof(CreateEntityResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<CreateEntityResult>> ExecuteAsync(CreateEntityRequest request, RekallAgeCommandContext context)
    {
        var scene = await _store.LoadAsync(request.ProjectRoot, request.SceneName, context.CancellationToken);
        var entity = RekallAgeEntityDocument.Create(request.Name, request.Tags);
        var updated = scene.AddEntity(entity);
        await _store.SaveAsync(request.ProjectRoot, updated, context.CancellationToken);
        context.Transaction.RecordChangedResource(_store.GetScenePath(request.ProjectRoot, request.SceneName));
        return RekallAgeCommandResult<CreateEntityResult>.Success(new CreateEntityResult(entity.Id, updated), $"Created entity '{entity.Name}'.");
    }
}
```

- [ ] **Step 5: Run world tests**

Run:

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter FullyQualifiedName~SceneStoreTests
```

Expected: PASS.

- [ ] **Step 6: Commit**

Run:

```powershell
git -c safe.directory=F:/Dev/Rekall_AGE add src/Rekall.Age.World tests/Rekall.Age.Tests/World
git -c safe.directory=F:/Dev/Rekall_AGE commit -m "feat: add deterministic world serialization"
```

Expected: commit succeeds.

---

### Task 5: Validation And Agent Context

**Files:**
- Create: `src/Rekall.Age.Validation/RekallAgeValidationIssue.cs`
- Create: `src/Rekall.Age.Validation/RekallAgeValidationReport.cs`
- Create: `src/Rekall.Age.Validation/RekallAgeProjectValidator.cs`
- Create: `src/Rekall.Age.Agent/RekallAgeProjectSummary.cs`
- Create: `src/Rekall.Age.Agent/RekallAgeContextBuilder.cs`
- Create: `tests/Rekall.Age.Tests/Agent/AgentContextTests.cs`

- [ ] **Step 1: Write failing agent context test**

Create `tests/Rekall.Age.Tests/Agent/AgentContextTests.cs`:

```csharp
using Rekall.Age.Agent;
using Rekall.Age.Project;
using Rekall.Age.Validation;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Agent;

public sealed class AgentContextTests
{
    [Fact]
    public async Task ProjectSummaryReportsMissingSceneCamera()
    {
        var root = TestPaths.CreateTempDirectory();
        var projectStore = new RekallAgeProjectStore();
        var sceneStore = new RekallAgeSceneStore();

        await projectStore.SaveAsync(root, RekallAgeProjectManifest.Create("Crystal Mines", ["world", "rendering2d"]), CancellationToken.None);
        await sceneStore.SaveAsync(root, RekallAgeSceneDocument.Create("Main", ["world", "rendering2d"]), CancellationToken.None);

        var builder = new RekallAgeContextBuilder(projectStore, sceneStore, new RekallAgeProjectValidator(sceneStore));
        var summary = await builder.BuildProjectSummaryAsync(root, CancellationToken.None);

        Assert.Equal("Crystal Mines", summary.Project);
        Assert.Equal("blocked", summary.Health.Status);
        Assert.Contains(summary.Health.BlockingIssues, issue => issue.Contains("active camera", StringComparison.OrdinalIgnoreCase));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter FullyQualifiedName~AgentContextTests
```

Expected: FAIL because validation and agent context types do not exist.

- [ ] **Step 3: Implement validation model**

Create `src/Rekall.Age.Validation/RekallAgeValidationIssue.cs`:

```csharp
namespace Rekall.Age.Validation;

public sealed record RekallAgeValidationIssue(
    string Code,
    string Message,
    string Severity,
    string? Target);
```

Create `src/Rekall.Age.Validation/RekallAgeValidationReport.cs`:

```csharp
namespace Rekall.Age.Validation;

public sealed record RekallAgeValidationReport(IReadOnlyList<RekallAgeValidationIssue> Issues)
{
    public string Status => Issues.Any(issue => issue.Severity == "blocking") ? "blocked" : "ok";

    public IReadOnlyList<string> BlockingMessages =>
        Issues.Where(issue => issue.Severity == "blocking").Select(issue => issue.Message).ToArray();
}
```

Create `src/Rekall.Age.Validation/RekallAgeProjectValidator.cs`:

```csharp
using Rekall.Age.World;

namespace Rekall.Age.Validation;

public sealed class RekallAgeProjectValidator
{
    private readonly RekallAgeSceneStore _sceneStore;

    public RekallAgeProjectValidator(RekallAgeSceneStore sceneStore)
    {
        _sceneStore = sceneStore;
    }

    public async ValueTask<RekallAgeValidationReport> ValidateSceneAsync(string projectRoot, string sceneName, CancellationToken cancellationToken)
    {
        var scene = await _sceneStore.LoadAsync(projectRoot, sceneName, cancellationToken);
        var issues = new List<RekallAgeValidationIssue>();

        var hasCamera = scene.Entities.Any(entity =>
            entity.Components.Any(component =>
                component.Type.Equals("Rekall.Camera2D", StringComparison.Ordinal) ||
                component.Type.Equals("Rekall.Camera3D", StringComparison.Ordinal)));

        if (!hasCamera)
        {
            issues.Add(new RekallAgeValidationIssue(
                "REKALL_CAMERA_MISSING",
                $"Scene '{scene.Name}' has no active camera.",
                "blocking",
                scene.Name));
        }

        return new RekallAgeValidationReport(issues);
    }
}
```

- [ ] **Step 4: Implement agent summary builder**

Create `src/Rekall.Age.Agent/RekallAgeProjectSummary.cs`:

```csharp
namespace Rekall.Age.Agent;

public sealed record RekallAgeProjectSummary(
    string Project,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> PlayableScenes,
    RekallAgeProjectHealth Health,
    IReadOnlyList<string> RecommendedNextActions);

public sealed record RekallAgeProjectHealth(
    string Status,
    IReadOnlyList<string> BlockingIssues);
```

Create `src/Rekall.Age.Agent/RekallAgeContextBuilder.cs`:

```csharp
using Rekall.Age.Project;
using Rekall.Age.Validation;
using Rekall.Age.World;

namespace Rekall.Age.Agent;

public sealed class RekallAgeContextBuilder
{
    private readonly RekallAgeProjectStore _projectStore;
    private readonly RekallAgeSceneStore _sceneStore;
    private readonly RekallAgeProjectValidator _validator;

    public RekallAgeContextBuilder(
        RekallAgeProjectStore projectStore,
        RekallAgeSceneStore sceneStore,
        RekallAgeProjectValidator validator)
    {
        _projectStore = projectStore;
        _sceneStore = sceneStore;
        _validator = validator;
    }

    public async ValueTask<RekallAgeProjectSummary> BuildProjectSummaryAsync(string projectRoot, CancellationToken cancellationToken)
    {
        var manifest = await _projectStore.LoadAsync(projectRoot, cancellationToken);
        var sceneNames = Directory.Exists(Path.Combine(projectRoot, "Scenes"))
            ? Directory.EnumerateFiles(Path.Combine(projectRoot, "Scenes"), "*.age.scene.json")
                .Select(path => Path.GetFileName(path).Replace(".age.scene.json", string.Empty, StringComparison.Ordinal))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray()
            : Array.Empty<string>();

        var blocking = new List<string>();
        foreach (var sceneName in sceneNames)
        {
            var report = await _validator.ValidateSceneAsync(projectRoot, sceneName, cancellationToken);
            blocking.AddRange(report.BlockingMessages);
        }

        var status = blocking.Count == 0 ? "ok" : "blocked";
        var nextActions = blocking.Count == 0
            ? ["Run rekall.capture.screenshot for a visual check."]
            : ["Run rekall.workflow.fix_validation_errors."];

        return new RekallAgeProjectSummary(
            manifest.Name,
            manifest.Capabilities,
            sceneNames,
            new RekallAgeProjectHealth(status, blocking),
            nextActions);
    }
}
```

- [ ] **Step 5: Run agent tests**

Run:

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter FullyQualifiedName~AgentContextTests
```

Expected: PASS.

- [ ] **Step 6: Commit**

Run:

```powershell
git -c safe.directory=F:/Dev/Rekall_AGE add src/Rekall.Age.Validation src/Rekall.Age.Agent tests/Rekall.Age.Tests/Agent
git -c safe.directory=F:/Dev/Rekall_AGE commit -m "feat: add validation and agent context"
```

Expected: commit succeeds.

---

### Task 6: CLI Adapter

**Files:**
- Modify: `src/Rekall.Age.Cli/Program.cs`
- Create: `tests/Rekall.Age.Tests/Cli/CliSmokeTests.cs`

- [ ] **Step 1: Write failing CLI smoke test**

Create `tests/Rekall.Age.Tests/Cli/CliSmokeTests.cs`:

```csharp
using System.Diagnostics;

namespace Rekall.Age.Tests.Cli;

public sealed class CliSmokeTests
{
    [Fact]
    public async Task CliCreatesProjectAndScene()
    {
        var root = TestPaths.CreateTempDirectory();
        var project = Path.Combine(FindRepositoryRoot(), "src", "Rekall.Age.Cli", "Rekall.Age.Cli.csproj");

        var createProject = await RunAsync(project, "project", "create", root, "Crystal Mines", "world,rendering2d");
        Assert.Equal(0, createProject.ExitCode);
        Assert.Contains("Created Rekall AGE project", createProject.Output);

        var createScene = await RunAsync(project, "scene", "create", root, "Main", "world,rendering2d");
        Assert.Equal(0, createScene.ExitCode);
        Assert.True(File.Exists(Path.Combine(root, "Scenes", "Main.age.scene.json")));
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string project, params string[] args)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(project);
        startInfo.ArgumentList.Add("--");
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        output += await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, output);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Rekall.AGE.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find Rekall.AGE.sln from the test output directory.");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter FullyQualifiedName~CliSmokeTests
```

Expected: FAIL because CLI commands are not implemented.

- [ ] **Step 3: Implement CLI commands**

Replace `src/Rekall.Age.Cli/Program.cs`:

```csharp
using Rekall.Age.Agent;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Project;
using Rekall.Age.Project.Commands;
using Rekall.Age.Validation;
using Rekall.Age.World;
using Rekall.Age.World.Commands;

var exitCode = await RekallAgeCli.RunAsync(args, CancellationToken.None);
return exitCode;

internal static class RekallAgeCli
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: rekall-age <project|capability|scene|entity|context> ...");
            return 2;
        }

        var registry = BuildRegistry();
        var transaction = RekallAgeTransaction.Begin(string.Join(' ', args));
        var context = new RekallAgeCommandContext("cli", transaction, cancellationToken);

        try
        {
            return args switch
            {
                ["project", "create", var root, var name, var capabilities] => await CreateProjectAsync(registry, context, root, name, capabilities),
                ["capability", "add", var root, var capability] => await AddCapabilityAsync(registry, context, root, capability),
                ["scene", "create", var root, var name, var capabilities] => await CreateSceneAsync(registry, context, root, name, capabilities),
                ["entity", "create", var root, var scene, var name, var tags] => await CreateEntityAsync(registry, context, root, scene, name, tags),
                ["context", "summary", var root] => await PrintSummaryAsync(root, cancellationToken),
                _ => PrintUnknown(args)
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static RekallAgeCommandRegistry BuildRegistry()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new CreateProjectCommand());
        registry.Register(new AddCapabilityCommand());
        registry.Register(new CreateSceneCommand());
        registry.Register(new CreateEntityCommand());
        return registry;
    }

    private static async Task<int> CreateProjectAsync(RekallAgeCommandRegistry registry, RekallAgeCommandContext context, string root, string name, string capabilities)
    {
        var result = await registry.ExecuteAsync<CreateProjectRequest, CreateProjectResult>(
            "rekall.project.create",
            new CreateProjectRequest(root, name, SplitCsv(capabilities)),
            context);
        Console.WriteLine(result.Summary);
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> AddCapabilityAsync(RekallAgeCommandRegistry registry, RekallAgeCommandContext context, string root, string capability)
    {
        var result = await registry.ExecuteAsync<AddCapabilityRequest, AddCapabilityResult>(
            "rekall.capability.add",
            new AddCapabilityRequest(root, capability),
            context);
        Console.WriteLine(result.Summary);
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> CreateSceneAsync(RekallAgeCommandRegistry registry, RekallAgeCommandContext context, string root, string name, string capabilities)
    {
        var result = await registry.ExecuteAsync<CreateSceneRequest, CreateSceneResult>(
            "rekall.scene.create",
            new CreateSceneRequest(root, name, SplitCsv(capabilities)),
            context);
        Console.WriteLine(result.Summary);
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> CreateEntityAsync(RekallAgeCommandRegistry registry, RekallAgeCommandContext context, string root, string scene, string name, string tags)
    {
        var result = await registry.ExecuteAsync<CreateEntityRequest, CreateEntityResult>(
            "rekall.entity.create",
            new CreateEntityRequest(root, scene, name, SplitCsv(tags)),
            context);
        Console.WriteLine(result.Summary);
        return result.Ok ? 0 : 1;
    }

    private static async Task<int> PrintSummaryAsync(string root, CancellationToken cancellationToken)
    {
        var sceneStore = new RekallAgeSceneStore();
        var builder = new RekallAgeContextBuilder(
            new RekallAgeProjectStore(),
            sceneStore,
            new RekallAgeProjectValidator(sceneStore));
        var summary = await builder.BuildProjectSummaryAsync(root, cancellationToken);
        Console.WriteLine($"{summary.Project}: {summary.Health.Status}");
        foreach (var issue in summary.Health.BlockingIssues)
        {
            Console.WriteLine($"- {issue}");
        }

        return summary.Health.Status == "ok" ? 0 : 1;
    }

    private static int PrintUnknown(string[] args)
    {
        Console.Error.WriteLine($"Unknown command: {string.Join(' ', args)}");
        return 2;
    }

    private static string[] SplitCsv(string value)
    {
        return value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }
}
```

- [ ] **Step 4: Run CLI tests**

Run:

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter FullyQualifiedName~CliSmokeTests
```

Expected: PASS.

- [ ] **Step 5: Commit**

Run:

```powershell
git -c safe.directory=F:/Dev/Rekall_AGE add src/Rekall.Age.Cli tests/Rekall.Age.Tests/Cli
git -c safe.directory=F:/Dev/Rekall_AGE commit -m "feat: add CLI adapter"
```

Expected: commit succeeds.

---

### Task 7: MCP Tool Catalog Skeleton

**Files:**
- Create: `src/Rekall.Age.Mcp/RekallAgeMcpTool.cs`
- Create: `src/Rekall.Age.Mcp/RekallAgeMcpCatalog.cs`
- Create: `tests/Rekall.Age.Tests/Mcp/McpCatalogTests.cs`

- [ ] **Step 1: Write failing MCP catalog test**

Create `tests/Rekall.Age.Tests/Mcp/McpCatalogTests.cs`:

```csharp
using Rekall.Age.Core.Commands;
using Rekall.Age.Project.Commands;
using Rekall.Age.Mcp;

namespace Rekall.Age.Tests.Mcp;

public sealed class McpCatalogTests
{
    [Fact]
    public void CatalogExposesRegisteredCommandSchemasAsTools()
    {
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new CreateProjectCommand());

        var catalog = RekallAgeMcpCatalog.FromRegistry(registry);
        var tool = Assert.Single(catalog.Tools);

        Assert.Equal("rekall.project.create", tool.Name);
        Assert.Contains("Creates", tool.Description);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter FullyQualifiedName~McpCatalogTests
```

Expected: FAIL because MCP catalog types do not exist.

- [ ] **Step 3: Implement MCP catalog skeleton**

Create `src/Rekall.Age.Mcp/RekallAgeMcpTool.cs`:

```csharp
namespace Rekall.Age.Mcp;

public sealed record RekallAgeMcpTool(
    string Name,
    string Description,
    string RequestType,
    string ResultType);
```

Create `src/Rekall.Age.Mcp/RekallAgeMcpCatalog.cs`:

```csharp
using Rekall.Age.Core.Commands;

namespace Rekall.Age.Mcp;

public sealed record RekallAgeMcpCatalog(IReadOnlyList<RekallAgeMcpTool> Tools)
{
    public static RekallAgeMcpCatalog FromRegistry(RekallAgeCommandRegistry registry)
    {
        var tools = registry.Schemas
            .Select(schema => new RekallAgeMcpTool(
                schema.Name,
                schema.Description,
                schema.RequestType,
                schema.ResultType))
            .OrderBy(tool => tool.Name, StringComparer.Ordinal)
            .ToArray();

        return new RekallAgeMcpCatalog(tools);
    }
}
```

- [ ] **Step 4: Run MCP tests**

Run:

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter FullyQualifiedName~McpCatalogTests
```

Expected: PASS.

- [ ] **Step 5: Commit**

Run:

```powershell
git -c safe.directory=F:/Dev/Rekall_AGE add src/Rekall.Age.Mcp tests/Rekall.Age.Tests/Mcp
git -c safe.directory=F:/Dev/Rekall_AGE commit -m "feat: add MCP tool catalog skeleton"
```

Expected: commit succeeds.

---

### Task 8: Headless Runtime And Screenshot Capture

**Files:**
- Create: `src/Rekall.Age.Runtime/RekallAgeRuntimeResult.cs`
- Create: `src/Rekall.Age.Runtime/RekallAgeHeadlessRuntime.cs`
- Create: `src/Rekall.Age.Rendering/RekallAgeSoftwarePreview.cs`
- Create: `src/Rekall.Age.Rendering/RekallAgePngWriter.cs`
- Create: `tests/Rekall.Age.Tests/Runtime/RuntimeAndCaptureTests.cs`

- [ ] **Step 1: Write failing runtime and capture test**

Create `tests/Rekall.Age.Tests/Runtime/RuntimeAndCaptureTests.cs`:

```csharp
using Rekall.Age.Rendering;
using Rekall.Age.Runtime;
using Rekall.Age.World;

namespace Rekall.Age.Tests.Runtime;

public sealed class RuntimeAndCaptureTests
{
    [Fact]
    public async Task HeadlessRuntimeRunsAndSoftwarePreviewWritesPng()
    {
        var root = TestPaths.CreateTempDirectory();
        var sceneStore = new RekallAgeSceneStore();
        var scene = RekallAgeSceneDocument.Create("Main", ["world", "rendering2d"]);
        await sceneStore.SaveAsync(root, scene, CancellationToken.None);

        var runtime = new RekallAgeHeadlessRuntime(sceneStore);
        var result = await runtime.RunAsync(root, "Main", TimeSpan.FromMilliseconds(50), CancellationToken.None);

        Assert.True(result.Ok);
        Assert.True(result.FramesSimulated > 0);

        var preview = new RekallAgeSoftwarePreview(sceneStore);
        var screenshotPath = await preview.CaptureAsync(root, "Main", Path.Combine(root, "Artifacts", "Screenshots"), CancellationToken.None);

        Assert.True(File.Exists(screenshotPath));
        Assert.True(new FileInfo(screenshotPath).Length > 64);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter FullyQualifiedName~RuntimeAndCaptureTests
```

Expected: FAIL because runtime and rendering types do not exist.

- [ ] **Step 3: Implement headless runtime**

Create `src/Rekall.Age.Runtime/RekallAgeRuntimeResult.cs`:

```csharp
namespace Rekall.Age.Runtime;

public sealed record RekallAgeRuntimeResult(
    bool Ok,
    int FramesSimulated,
    TimeSpan Duration,
    IReadOnlyList<string> Errors);
```

Create `src/Rekall.Age.Runtime/RekallAgeHeadlessRuntime.cs`:

```csharp
using Rekall.Age.World;

namespace Rekall.Age.Runtime;

public sealed class RekallAgeHeadlessRuntime
{
    private readonly RekallAgeSceneStore _sceneStore;

    public RekallAgeHeadlessRuntime(RekallAgeSceneStore sceneStore)
    {
        _sceneStore = sceneStore;
    }

    public async ValueTask<RekallAgeRuntimeResult> RunAsync(
        string projectRoot,
        string sceneName,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        await _sceneStore.LoadAsync(projectRoot, sceneName, cancellationToken);
        var frameTime = TimeSpan.FromSeconds(1.0 / 60.0);
        var frames = Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds / frameTime.TotalSeconds));

        for (var frame = 0; frame < frames; frame++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
        }

        return new RekallAgeRuntimeResult(true, frames, duration, Array.Empty<string>());
    }
}
```

- [ ] **Step 4: Implement deterministic PNG capture**

Create `src/Rekall.Age.Rendering/RekallAgeSoftwarePreview.cs`:

```csharp
using Rekall.Age.World;

namespace Rekall.Age.Rendering;

public sealed class RekallAgeSoftwarePreview
{
    private readonly RekallAgeSceneStore _sceneStore;

    public RekallAgeSoftwarePreview(RekallAgeSceneStore sceneStore)
    {
        _sceneStore = sceneStore;
    }

    public async ValueTask<string> CaptureAsync(
        string projectRoot,
        string sceneName,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        var scene = await _sceneStore.LoadAsync(projectRoot, sceneName, cancellationToken);
        Directory.CreateDirectory(outputDirectory);

        var pixels = new byte[128 * 72 * 4];
        for (var y = 0; y < 72; y++)
        {
            for (var x = 0; x < 128; x++)
            {
                var index = (y * 128 + x) * 4;
                var stripe = (x / 16 + y / 16) % 2 == 0;
                pixels[index + 0] = stripe ? (byte)32 : (byte)12;
                pixels[index + 1] = stripe ? (byte)120 : (byte)40;
                pixels[index + 2] = scene.Entities.Count > 0 ? (byte)220 : (byte)120;
                pixels[index + 3] = 255;
            }
        }

        var path = Path.Combine(outputDirectory, $"{scene.Name}_preview.png");
        await RekallAgePngWriter.WriteRgbaAsync(path, 128, 72, pixels, cancellationToken);
        return path;
    }
}
```

Create `src/Rekall.Age.Rendering/RekallAgePngWriter.cs`:

```csharp
using System.Buffers.Binary;
using System.IO.Compression;

namespace Rekall.Age.Rendering;

public static class RekallAgePngWriter
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static async ValueTask WriteRgbaAsync(
        string path,
        int width,
        int height,
        byte[] rgba,
        CancellationToken cancellationToken)
    {
        if (rgba.Length != width * height * 4)
        {
            throw new ArgumentException("RGBA buffer length does not match image dimensions.", nameof(rgba));
        }

        await using var stream = File.Create(path);
        await stream.WriteAsync(Signature, cancellationToken);
        await WriteChunkAsync(stream, "IHDR", CreateHeader(width, height), cancellationToken);
        await WriteChunkAsync(stream, "IDAT", CompressRows(width, height, rgba), cancellationToken);
        await WriteChunkAsync(stream, "IEND", Array.Empty<byte>(), cancellationToken);
    }

    private static byte[] CreateHeader(int width, int height)
    {
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height);
        header[8] = 8;
        header[9] = 6;
        header[10] = 0;
        header[11] = 0;
        header[12] = 0;
        return header;
    }

    private static byte[] CompressRows(int width, int height, byte[] rgba)
    {
        using var raw = new MemoryStream();
        for (var y = 0; y < height; y++)
        {
            raw.WriteByte(0);
            raw.Write(rgba, y * width * 4, width * 4);
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            raw.Position = 0;
            raw.CopyTo(zlib);
        }

        return compressed.ToArray();
    }

    private static async ValueTask WriteChunkAsync(Stream stream, string type, byte[] data, CancellationToken cancellationToken)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        await stream.WriteAsync(length, cancellationToken);

        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        await stream.WriteAsync(typeBytes, cancellationToken);
        await stream.WriteAsync(data, cancellationToken);

        var crcInput = new byte[typeBytes.Length + data.Length];
        Buffer.BlockCopy(typeBytes, 0, crcInput, 0, typeBytes.Length);
        Buffer.BlockCopy(data, 0, crcInput, typeBytes.Length, data.Length);
        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, ComputeCrc32(crcInput));
        await stream.WriteAsync(crc, cancellationToken);
    }

    private static uint ComputeCrc32(byte[] bytes)
    {
        var crc = 0xffffffffu;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                var mask = 0u - (crc & 1u);
                crc = (crc >> 1) ^ (0xedb88320u & mask);
            }
        }

        return ~crc;
    }
}
```

- [ ] **Step 5: Run runtime and capture tests**

Run:

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter FullyQualifiedName~RuntimeAndCaptureTests
```

Expected: PASS.

- [ ] **Step 6: Commit**

Run:

```powershell
git -c safe.directory=F:/Dev/Rekall_AGE add src/Rekall.Age.Runtime src/Rekall.Age.Rendering tests/Rekall.Age.Tests/Runtime
git -c safe.directory=F:/Dev/Rekall_AGE commit -m "feat: add headless runtime and preview capture"
```

Expected: commit succeeds.

---

### Task 9: Vertical Slice Integration

**Files:**
- Create: `tests/Rekall.Age.Tests/VerticalSlice/AgenticVerticalSliceTests.cs`
- Modify: `README.md`

- [ ] **Step 1: Write end-to-end vertical slice test**

Create `tests/Rekall.Age.Tests/VerticalSlice/AgenticVerticalSliceTests.cs`:

```csharp
using Rekall.Age.Agent;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Project;
using Rekall.Age.Project.Commands;
using Rekall.Age.Rendering;
using Rekall.Age.Runtime;
using Rekall.Age.Validation;
using Rekall.Age.World;
using Rekall.Age.World.Commands;

namespace Rekall.Age.Tests.VerticalSlice;

public sealed class AgenticVerticalSliceTests
{
    [Fact]
    public async Task AgentCanCreateInspectRunAndCaptureProject()
    {
        var root = TestPaths.CreateTempDirectory();
        var registry = new RekallAgeCommandRegistry();
        registry.Register(new CreateProjectCommand());
        registry.Register(new CreateSceneCommand());
        registry.Register(new CreateEntityCommand());
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("vertical slice"), CancellationToken.None);

        var project = await registry.ExecuteAsync<CreateProjectRequest, CreateProjectResult>(
            "rekall.project.create",
            new CreateProjectRequest(root, "Crystal Mines", ["world", "rendering2d"]),
            context);
        Assert.True(project.Ok);

        var scene = await registry.ExecuteAsync<CreateSceneRequest, CreateSceneResult>(
            "rekall.scene.create",
            new CreateSceneRequest(root, "Main", ["world", "rendering2d"]),
            context);
        Assert.True(scene.Ok);

        var entity = await registry.ExecuteAsync<CreateEntityRequest, CreateEntityResult>(
            "rekall.entity.create",
            new CreateEntityRequest(root, "Main", "Player", ["player"]),
            context);
        Assert.True(entity.Ok);

        var sceneStore = new RekallAgeSceneStore();
        var summary = await new RekallAgeContextBuilder(
            new RekallAgeProjectStore(),
            sceneStore,
            new RekallAgeProjectValidator(sceneStore)).BuildProjectSummaryAsync(root, CancellationToken.None);

        Assert.Equal("Crystal Mines", summary.Project);
        Assert.Equal("blocked", summary.Health.Status);

        var runtime = new RekallAgeHeadlessRuntime(sceneStore);
        var run = await runtime.RunAsync(root, "Main", TimeSpan.FromMilliseconds(16), CancellationToken.None);
        Assert.True(run.Ok);

        var screenshot = await new RekallAgeSoftwarePreview(sceneStore)
            .CaptureAsync(root, "Main", Path.Combine(root, "Artifacts", "Screenshots"), CancellationToken.None);
        Assert.True(File.Exists(screenshot));
    }
}
```

- [ ] **Step 2: Run integration test**

Run:

```powershell
dotnet test tests/Rekall.Age.Tests/Rekall.Age.Tests.csproj --filter FullyQualifiedName~AgenticVerticalSliceTests
```

Expected: PASS.

- [ ] **Step 3: Update README with first commands**

Replace `README.md`:

```markdown
# Rekall AGE

Rekall AGE is a greenfield, agent-native C# game engine.

The first vertical slice includes:

- typed command bus
- transactions
- project capabilities
- deterministic scene files
- validation
- compact agent context
- CLI adapter
- MCP tool catalog skeleton
- headless runtime
- deterministic software preview screenshots

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
dotnet run --project src/Rekall.Age.Cli -- project create .age-sandbox "Crystal Mines" world,rendering2d
dotnet run --project src/Rekall.Age.Cli -- scene create .age-sandbox Main world,rendering2d
dotnet run --project src/Rekall.Age.Cli -- entity create .age-sandbox Main Player player
dotnet run --project src/Rekall.Age.Cli -- context summary .age-sandbox
```
```

- [ ] **Step 4: Run full verification**

Run:

```powershell
dotnet build Rekall.AGE.sln
dotnet test Rekall.AGE.sln
```

Expected: both commands succeed with zero warnings and all tests passing.

- [ ] **Step 5: Commit**

Run:

```powershell
git -c safe.directory=F:/Dev/Rekall_AGE add README.md tests/Rekall.Age.Tests/VerticalSlice
git -c safe.directory=F:/Dev/Rekall_AGE commit -m "test: prove agentic vertical slice"
```

Expected: commit succeeds.

---

## Plan Self-Review

**Spec coverage:** This plan covers the approved spec's first implementation notes: solution/package structure, project manifest and capabilities, command bus and transaction result model, deterministic scene serialization, validation skeleton, agent context, MCP skeleton, CLI adapter, headless runtime, and screenshot capture. Full editor UI, real graphics backends, physics, audio, build packaging, source generators, and formal MCP SDK integration are intentionally deferred to later focused plans.

**Placeholder scan:** The plan contains no placeholder markers or vague implementation steps. Each source-changing step includes exact code.

**Type consistency:** Public API names use Rekall-branded names: `IRekallAgeCommand`, `RekallAgeCommandSchema`, `RekallAgeCommandContext`, `RekallAgeTransaction`, `RekallAgeProjectManifest`, `RekallAgeSceneDocument`, and related types.
