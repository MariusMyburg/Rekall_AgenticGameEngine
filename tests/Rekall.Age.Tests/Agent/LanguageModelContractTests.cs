using System.Text.Json.Nodes;
using Rekall.Age.Agent.LanguageModels;

namespace Rekall.Age.Tests.Agent;

public sealed class LanguageModelContractTests
{
    [Fact]
    public void ExistingPositionalRecordsRemainSourceCompatible()
    {
        var messages = new[] { new RekallAgeLanguageModelMessage("user", "hello") };
        var tools = new[]
        {
            new RekallAgeLanguageModelTool(
                "rekall.engine.status",
                "Inspect engine status.",
                new JsonObject { ["type"] = "object" })
        };
        var calls = new[]
        {
            new RekallAgeLanguageModelToolCall("rekall.engine.status", new JsonObject())
        };
        var usage = new RekallAgeLanguageModelUsage(11, 7, 19);
        var request = new RekallAgeLanguageModelRequest("model", messages, tools);
        var response = new RekallAgeLanguageModelResponse(
            "provider",
            "model",
            "content",
            "thinking",
            calls,
            "stop",
            usage);
        var model = new RekallAgeLanguageModelInfo("model", 23);

        Assert.Equal("model", request.Model);
        Assert.Same(messages, request.Messages);
        Assert.Same(tools, request.Tools);
        Assert.Equal("user", messages[0].Role);
        Assert.Equal("rekall.engine.status", tools[0].Name);
        Assert.Equal("rekall.engine.status", calls[0].Name);
        Assert.Equal("provider", response.ProviderId);
        Assert.Same(usage, response.Usage);
        Assert.Equal(11, usage.PromptTokens);
        Assert.Equal("model", model.Id);
    }

    [Fact]
    public void ProviderNeutralIdentityUsageAndStreamFactsRoundTrip()
    {
        var call = new RekallAgeLanguageModelToolCall("rekall.engine.status", new JsonObject())
        {
            Id = "call_123"
        };
        var toolResult = new RekallAgeLanguageModelMessage("tool", "{}", "rekall.engine.status")
        {
            ToolCallId = "call_123"
        };
        var usage = new RekallAgeLanguageModelUsage(13, 8, 21)
        {
            CachedInputTokens = 5,
            ReasoningTokens = 3
        };
        var response = new RekallAgeLanguageModelResponse(
            "provider",
            "model",
            "done",
            "thought",
            [call],
            "stop",
            usage)
        {
            ResponseId = "response_456"
        };
        var streamEvent = new RekallAgeLanguageModelStreamEvent(
            RekallAgeLanguageModelStreamEventKind.Completed,
            string.Empty,
            response);

        Assert.Equal("call_123", call.Id);
        Assert.Equal(call.Id, toolResult.ToolCallId);
        Assert.Equal("response_456", response.ResponseId);
        Assert.Equal(5, usage.CachedInputTokens);
        Assert.Equal(3, usage.ReasoningTokens);
        Assert.Equal(RekallAgeLanguageModelStreamEventKind.Completed, streamEvent.Kind);
        Assert.Same(response, streamEvent.Response);
    }

    [Fact]
    public void ProviderExceptionPreservesStructuredFactsAndRedactsSuppliedSecrets()
    {
        const string secret = "sk-secret-provider-credential";
        var error = new RekallAgeLanguageModelProviderException(
            "REKALL_PROVIDER_RATE_LIMITED",
            "provider",
            $"Provider rejected Authorization: Bearer {secret}.",
            httpStatus: 429,
            requestId: "request_789",
            retryable: true,
            requestedValue: "reasoning=high",
            resolvedValue: "reasoning=medium",
            sensitiveValues: [secret]);

        Assert.Equal("REKALL_PROVIDER_RATE_LIMITED", error.Code);
        Assert.Equal("provider", error.ProviderId);
        Assert.Equal(429, error.HttpStatus);
        Assert.Equal("request_789", error.RequestId);
        Assert.True(error.Retryable);
        Assert.Equal("reasoning=high", error.RequestedValue);
        Assert.Equal("reasoning=medium", error.ResolvedValue);
        Assert.DoesNotContain(secret, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, error.ToString(), StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderExceptionBoundsProviderControlledMessageText()
    {
        var error = new RekallAgeLanguageModelProviderException(
            "REKALL_PROVIDER_INVALID",
            "provider",
            new string('x', 20_000));

        Assert.InRange(error.Message.Length, 1, 4_096);
    }

    [Fact]
    public void ProviderExceptionRedactsSuppliedSecretsFromStructuredLoggableFields()
    {
        const string secret = "sk-structured-secret";
        var error = new RekallAgeLanguageModelProviderException(
            "REKALL_PROVIDER_INVALID",
            "provider",
            "Provider request failed.",
            requestId: $"request-{secret}",
            requestedValue: $"api_key={secret}",
            resolvedValue: $"Authorization: Bearer {secret}",
            sensitiveValues: [secret]);

        Assert.All(
            new[] { error.RequestId!, error.RequestedValue!, error.ResolvedValue! },
            value => Assert.DoesNotContain(secret, value, StringComparison.Ordinal));
    }
}
