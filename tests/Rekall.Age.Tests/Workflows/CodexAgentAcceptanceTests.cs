using Rekall.Age.Agent.Codex;

namespace Rekall.Age.Tests.Workflows;

public sealed class CodexAgentAcceptanceTests
{
    [Fact]
    public async Task RealAuthenticatedAppServerCompletesAnEphemeralReadOnlySolTurnWhenEnabled()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("REKALL_RUN_CODEX_ACCEPTANCE"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var client = await RekallAgeCodexAppServerClient.StartAsync(
            cancellationToken: timeout.Token);
        var root = Directory.CreateTempSubdirectory("rekall-codex-smoke-");
        try
        {
            var account = await client.ReadAccountAsync(cancellationToken: timeout.Token);
            Assert.True(account.IsAuthenticated);
            Assert.Contains(account.AuthenticationType, new[] { "chatgpt", "apiKey" });
            var models = await client.ListModelsAsync(cancellationToken: timeout.Token);
            Assert.Contains(models, model =>
                !model.Hidden
                && model.Model.Equals("gpt-5.6-sol", StringComparison.Ordinal));

            var thread = await client.StartThreadAsync(
                new RekallAgeCodexThreadStartRequest(
                    root.FullName,
                    "gpt-5.6-sol",
                    "Perform only the bounded protocol smoke check requested by the user.")
                {
                    ApprovalPolicy = "never",
                    Ephemeral = true,
                    NetworkEnabled = false,
                    Sandbox = "read-only"
                },
                timeout.Token);
            var turn = await client.StartTurnAsync(
                thread.Id,
                "Reply with exactly CODEX_AGE_SMOKE_OK. Do not use tools or modify files.",
                "medium",
                timeout.Token);
            var completion = await client.WaitForTurnCompletionAsync(turn, timeout.Token);

            Assert.Equal("completed", completion.Status);
            Assert.Equal(turn.Id, completion.TurnId);
        }
        finally
        {
            await client.DisposeAsync();
            root.Delete(recursive: true);
        }
    }
}
