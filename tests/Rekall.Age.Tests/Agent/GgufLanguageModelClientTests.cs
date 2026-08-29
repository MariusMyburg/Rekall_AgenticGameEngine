using System.Net;
using System.Text;
using Rekall.Age.Agent.LanguageModels;

namespace Rekall.Age.Tests.Agent;

public sealed class GgufLanguageModelClientTests
{
    [Theory]
    [InlineData(false, "REKALL_GGUF_OLLAMA_DISCOVERY_FAILED")]
    [InlineData(true, "REKALL_GGUF_OLLAMA_CHAT_FAILED")]
    public async Task OllamaFailuresDoNotExposeLocalPaths(
        bool chat,
        string expectedCode)
    {
        const string privatePath = "C:/private/models/my-model.gguf";
        using var http = new HttpClient(new FailureHandler(privatePath));
        var client = new RekallAgeGgufLanguageModelClient(http);

        var exception = chat
            ? await Assert.ThrowsAsync<RekallAgeLanguageModelProviderException>(async () =>
                await client.ChatAsync(
                    new RekallAgeLanguageModelRequest(
                        "rekall-model",
                        [new RekallAgeLanguageModelMessage("user", "hello")],
                        []),
                    CancellationToken.None))
            : await Assert.ThrowsAsync<RekallAgeLanguageModelProviderException>(async () =>
                await client.ListModelsAsync(CancellationToken.None));

        Assert.Equal(expectedCode, exception.Code);
        Assert.Equal("gguf", exception.ProviderId);
        Assert.DoesNotContain(privatePath, exception.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FailureHandler(string privatePath) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent(
                    $"Ollama failed while opening {privatePath}",
                    Encoding.UTF8,
                    "text/plain")
            });
    }
}
