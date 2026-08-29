namespace Rekall.Age.Agent.LanguageModels;

public sealed class RekallAgeGgufLanguageModelClient : IRekallAgeLanguageModelClient
{
    private readonly RekallAgeOllamaLanguageModelClient _ollama;

    public RekallAgeGgufLanguageModelClient(HttpClient httpClient, Uri? baseUri = null)
    {
        _ollama = new RekallAgeOllamaLanguageModelClient(httpClient, baseUri);
    }

    public string ProviderId => "gguf";

    public async ValueTask<IReadOnlyList<RekallAgeLanguageModelInfo>> ListModelsAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return await _ollama.ListModelsAsync(cancellationToken);
        }
        catch (Exception exception) when (IsProviderFailure(exception))
        {
            throw new RekallAgeLanguageModelProviderException(
                "REKALL_GGUF_OLLAMA_DISCOVERY_FAILED",
                ProviderId,
                "Ollama could not list the locally available models.");
        }
    }

    public async ValueTask<RekallAgeLanguageModelResponse> ChatAsync(
        RekallAgeLanguageModelRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _ollama.ChatAsync(request, cancellationToken);
            return response with { ProviderId = ProviderId };
        }
        catch (Exception exception) when (IsProviderFailure(exception))
        {
            throw new RekallAgeLanguageModelProviderException(
                "REKALL_GGUF_OLLAMA_CHAT_FAILED",
                ProviderId,
                "Ollama could not run the selected local GGUF model.");
        }
    }

    private static bool IsProviderFailure(Exception exception) =>
        exception is HttpRequestException
            or InvalidDataException
            or System.Text.Json.JsonException;
}
