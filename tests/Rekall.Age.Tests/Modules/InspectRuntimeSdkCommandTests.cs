using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Modules.Commands;

namespace Rekall.Age.Tests.Modules;

public sealed class InspectRuntimeSdkCommandTests
{
    [Fact]
    public async Task ReturnsExactCompiledSignaturesAndSourceTopologyForAgentQueries()
    {
        var context = new RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("inspect runtime sdk"),
            CancellationToken.None);

        var result = await new InspectRuntimeSdkCommand().ExecuteAsync(
            new InspectRuntimeSdkRequest(
                "input action immutable vector entity component source duplicate system",
                Limit: 32),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.Contains(result.Value.Contracts, contract =>
            contract.Name == "InputActionValue"
            && contract.Signature.Contains("RekallAgeRuntimeWorld world", StringComparison.Ordinal)
            && contract.Signature.Contains("double fallback = 0", StringComparison.Ordinal)
            && contract.Usage!.Contains("world.InputActionValue", StringComparison.Ordinal));
        Assert.Contains(result.Value.Contracts, contract =>
            contract.Name == "WasInputActionPressed"
            && contract.Signature.Contains("string name", StringComparison.Ordinal));
        Assert.Contains(result.Value.Contracts, contract =>
            contract.Name == "RekallAgeRuntimeVector3"
            && contract.Description.Contains("immutable", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Value.Contracts, contract =>
            contract.Name == "module-source-topology"
            && contract.Description.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
            && contract.Description.Contains("rekall.module.list_sources", StringComparison.Ordinal));
        Assert.Contains(result.Value.Contracts, contract =>
            contract.Name == "ComponentNumber"
            && contract.Usage!.Contains("entity.ComponentNumber", StringComparison.Ordinal));
        Assert.Contains(result.Value.Contracts, contract =>
            contract.Name == "WithComponentBoolean"
            && contract.Usage!.Contains("entity.WithComponentBoolean", StringComparison.Ordinal));
        Assert.Contains(result.Value.Contracts, contract =>
            contract.Name == "entity-transform-and-component-state-recipe"
            && contract.Usage!.Contains("entity.Transform.Position3D", StringComparison.Ordinal)
            && contract.Usage.Contains("no JsonObject", StringComparison.Ordinal));
        Assert.Contains(result.Value.Contracts, contract =>
            contract.Name == "scalar-two-axis-input-and-double-math-recipe"
            && contract.Usage!.Contains("move.horizontal", StringComparison.Ordinal)
            && contract.Usage.Contains("move.vertical", StringComparison.Ordinal)
            && contract.Description.Contains("returns double", StringComparison.Ordinal)
            && contract.Description.Contains("never access .X or .Y", StringComparison.Ordinal));
        Assert.Contains(result.Value.Contracts, contract =>
            contract.Name == "semantic-input-map-recipe"
            && contract.Description.Contains("Rekall.InputActionMap", StringComparison.Ordinal)
            && contract.Description.Contains("does not create", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Value.Contracts, contract =>
            contract.Name == "agent-component-registration-recipe"
            && contract.Usage!.Contains("RegisterComponent", StringComparison.Ordinal)
            && contract.Description.Contains("every", StringComparison.OrdinalIgnoreCase));
        Assert.All(result.Value.Contracts, contract => Assert.False(string.IsNullOrWhiteSpace(contract.Signature)));

        var queryResult = await new InspectRuntimeSdkCommand().ExecuteAsync(
            new InspectRuntimeSdkRequest("entities named exact prefix component tag", Limit: 16),
            context);
        Assert.True(queryResult.Ok, queryResult.Summary);
        Assert.Contains(queryResult.Value.Contracts, contract =>
            contract.Name == "EntitiesNamed"
            && contract.Description.Contains("not prefix", StringComparison.OrdinalIgnoreCase)
            && contract.Description.Contains("EntitiesWithComponent", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RejectsAnEmptySdkQueryWithStructuredError()
    {
        var context = new RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("reject empty sdk query"),
            CancellationToken.None);

        var result = await new InspectRuntimeSdkCommand().ExecuteAsync(
            new InspectRuntimeSdkRequest(" "),
            context);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Code == "REKALL_RUNTIME_SDK_QUERY_REQUIRED");
    }

    [Fact]
    public async Task ReturnsTyped2DPhysicsQueryContracts()
    {
        var context = new RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("inspect 2d physics sdk"),
            CancellationToken.None);

        var result = await new InspectRuntimeSdkCommand().ExecuteAsync(
            new InspectRuntimeSdkRequest("2d planar raycast vector", Limit: 16),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.Contains(result.Value.Contracts, contract =>
            contract.Name == "Raycast2D"
            && contract.Signature.Contains("RekallAgeRuntimeVector2 origin", StringComparison.Ordinal)
            && contract.Usage!.Contains("world.Raycast2D", StringComparison.Ordinal));
        Assert.Contains(result.Value.Contracts, contract =>
            contract.Name == "RekallAgeRuntimeVector2"
            && contract.Description.Contains("planar", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ReturnsGenericRuntimeSpawningAndDeterministicRandomContracts()
    {
        var context = new RekallAgeCommandContext(
            "agent",
            RekallAgeTransaction.Begin("inspect spawning sdk"),
            CancellationToken.None);

        var result = await new InspectRuntimeSdkCommand().ExecuteAsync(
            new InspectRuntimeSdkRequest("create add spawn deterministic random range", Limit: 24),
            context);

        Assert.True(result.Ok, result.Summary);
        Assert.Contains(result.Value.Contracts, contract =>
            contract.Name == "CreateEntity"
            && contract.Usage!.Contains("WithComponentNumber", StringComparison.Ordinal));
        Assert.Contains(result.Value.Contracts, contract =>
            contract.Name == "AddEntity"
            && contract.Description.Contains("duplicate", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Value.Contracts, contract =>
            contract.Name == "DeterministicRange"
            && contract.Signature.Contains("long sequence", StringComparison.Ordinal)
            && contract.Usage!.Contains("spawnIndex", StringComparison.Ordinal));
    }
}
