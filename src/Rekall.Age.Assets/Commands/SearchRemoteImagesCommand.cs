using System.Buffers;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rekall.Age.Core.Commands;

namespace Rekall.Age.Assets.Commands;

public sealed record SearchRemoteImagesRequest(
    string Query,
    int MaxResults = 8,
    bool AllowCommercialUse = true,
    bool AllowModification = true);

public sealed record RekallAgeRemoteImageSearchItem(
    string Id,
    string? Title,
    string AssetUrl,
    string? LandingPageUrl,
    string? Creator,
    string? CreatorUrl,
    string Attribution,
    string License,
    string? LicenseVersion,
    string? LicenseUrl,
    string? Provider,
    string? Source,
    int? Width,
    int? Height,
    string? FileType);

public sealed record SearchRemoteImagesResult(
    string Query,
    string Provider,
    IReadOnlyList<RekallAgeRemoteImageSearchItem> Results,
    string LicenseNotice);

public sealed class SearchRemoteImagesCommand : IRekallAgeCommand<SearchRemoteImagesRequest, SearchRemoteImagesResult>
{
    private const int MaximumResponseBytes = 1_048_576;
    private static readonly Uri Endpoint = new("https://api.openverse.org/v1/images/");
    private readonly HttpClient _httpClient;

    public SearchRemoteImagesCommand() : this(CreateClient())
    {
    }

    public SearchRemoteImagesCommand(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public string Name => "rekall.asset.search_remote_images";

    public RekallAgeCommandSchema Schema => new(
        Name,
        "Searches the internet Openverse catalog for agent-selectable openly licensed or public-domain images with direct URL, landing page, attribution, license, and provenance. The agent selects a relevant result, verifies its source/license evidence, then uses remote import; the engine does not choose or author content.",
        typeof(SearchRemoteImagesRequest).FullName!,
        typeof(SearchRemoteImagesResult).FullName!);

    public async ValueTask<RekallAgeCommandResult<SearchRemoteImagesResult>> ExecuteAsync(
        SearchRemoteImagesRequest request,
        RekallAgeCommandContext context)
    {
        var query = request.Query?.Trim() ?? string.Empty;
        if (query.Length is < 2 or > 200)
        {
            return Failure("REKALL_ASSET_SEARCH_QUERY_INVALID", "Remote image query must contain 2 through 200 characters.", nameof(request.Query));
        }
        if (request.MaxResults is < 1 or > 12)
        {
            return Failure("REKALL_ASSET_SEARCH_LIMIT_INVALID", "Remote image maxResults must be between 1 and 12.", nameof(request.MaxResults));
        }

        var parameters = new List<string>
        {
            $"q={Uri.EscapeDataString(query)}",
            $"page_size={request.MaxResults}",
            "mature=false"
        };
        var licenseUses = new List<string>();
        if (request.AllowCommercialUse) licenseUses.Add("commercial");
        if (request.AllowModification) licenseUses.Add("modification");
        if (licenseUses.Count > 0)
        {
            parameters.Add($"license_type={Uri.EscapeDataString(string.Join(',', licenseUses))}");
        }
        var uri = new UriBuilder(Endpoint) { Query = string.Join('&', parameters) }.Uri;

        try
        {
            using var requestMessage = new HttpRequestMessage(HttpMethod.Get, uri);
            requestMessage.Headers.UserAgent.ParseAdd("Rekall-AGE/0.1 open-license-image-search");
            using var response = await _httpClient.SendAsync(
                requestMessage,
                HttpCompletionOption.ResponseHeadersRead,
                context.CancellationToken);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return Failure(
                    "REKALL_ASSET_SEARCH_RATE_LIMITED",
                    "The openly licensed image catalog rate-limited this search. Respect the provider limit and retry later.",
                    Endpoint.Host);
            }
            if (!response.IsSuccessStatusCode)
            {
                return Failure(
                    "REKALL_ASSET_SEARCH_HTTP_FAILED",
                    $"Remote image search returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).",
                    Endpoint.Host);
            }
            if (response.Content.Headers.ContentLength is > MaximumResponseBytes)
            {
                return Failure(
                    "REKALL_ASSET_SEARCH_RESPONSE_TOO_LARGE",
                    $"Remote image search response exceeded {MaximumResponseBytes} bytes.",
                    Endpoint.Host);
            }

            var bytes = await ReadBoundedAsync(response.Content, context.CancellationToken);
            var document = JsonSerializer.Deserialize<OpenverseSearchResponse>(bytes, JsonOptions)
                ?? throw new JsonException("Remote image search returned an empty JSON document.");
            var results = (document.Results ?? [])
                .Where(item => !string.IsNullOrWhiteSpace(item.Id)
                    && Uri.TryCreate(item.Url, UriKind.Absolute, out var assetUri)
                    && assetUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                .Take(request.MaxResults)
                .Select(item => new RekallAgeRemoteImageSearchItem(
                    item.Id!,
                    item.Title,
                    item.Url!,
                    item.LandingPageUrl,
                    item.Creator,
                    item.CreatorUrl,
                    string.IsNullOrWhiteSpace(item.Attribution) ? BuildAttribution(item) : item.Attribution,
                    item.License ?? "unknown",
                    item.LicenseVersion,
                    item.LicenseUrl,
                    item.Provider,
                    item.Source,
                    item.Width,
                    item.Height,
                    item.FileType))
                .ToArray();

            return RekallAgeCommandResult<SearchRemoteImagesResult>.Success(
                new SearchRemoteImagesResult(
                    query,
                    "Openverse",
                    results,
                    "Openverse indexes open-license metadata but cannot guarantee its accuracy. The agent must verify the selected landing page and license, preserve attribution/provenance, and honor the source host's access policy before import."),
                $"Found {results.Length} agent-selectable openly licensed image result{(results.Length == 1 ? string.Empty : "s")}.");
        }
        catch (ResponseTooLargeException)
        {
            return Failure(
                "REKALL_ASSET_SEARCH_RESPONSE_TOO_LARGE",
                $"Remote image search response exceeded {MaximumResponseBytes} bytes.",
                Endpoint.Host);
        }
        catch (OperationCanceledException) when (!context.CancellationToken.IsCancellationRequested)
        {
            return Failure("REKALL_ASSET_SEARCH_TIMEOUT", "Remote image search timed out.", Endpoint.Host);
        }
        catch (HttpRequestException error)
        {
            return Failure("REKALL_ASSET_SEARCH_HTTP_FAILED", error.Message, Endpoint.Host);
        }
        catch (JsonException error)
        {
            return Failure("REKALL_ASSET_SEARCH_RESPONSE_INVALID", error.Message, Endpoint.Host);
        }
    }

    private static async ValueTask<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        using var destination = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(16_384);
        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0) break;
                if (destination.Length + read > MaximumResponseBytes) throw new ResponseTooLargeException();
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            return destination.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static string BuildAttribution(OpenverseImage item) => string.Join(
        ", ",
        new[] { item.Title, item.Creator, item.License, item.LicenseVersion }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private static RekallAgeCommandResult<SearchRemoteImagesResult> Failure(string code, string message, string target)
    {
        var error = new RekallAgeCommandError(code, message, target);
        return RekallAgeCommandResult<SearchRemoteImagesResult>.Failure(default!, message, [error]);
    }

    private static HttpClient CreateClient() => new() { Timeout = TimeSpan.FromSeconds(30) };

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class ResponseTooLargeException : Exception;

    private sealed record OpenverseSearchResponse(
        [property: JsonPropertyName("results")] IReadOnlyList<OpenverseImage>? Results);

    private sealed record OpenverseImage(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("foreign_landing_url")] string? LandingPageUrl,
        [property: JsonPropertyName("creator")] string? Creator,
        [property: JsonPropertyName("creator_url")] string? CreatorUrl,
        [property: JsonPropertyName("attribution")] string? Attribution,
        [property: JsonPropertyName("license")] string? License,
        [property: JsonPropertyName("license_version")] string? LicenseVersion,
        [property: JsonPropertyName("license_url")] string? LicenseUrl,
        [property: JsonPropertyName("provider")] string? Provider,
        [property: JsonPropertyName("source")] string? Source,
        [property: JsonPropertyName("width")] int? Width,
        [property: JsonPropertyName("height")] int? Height,
        [property: JsonPropertyName("filetype")] string? FileType);
}
