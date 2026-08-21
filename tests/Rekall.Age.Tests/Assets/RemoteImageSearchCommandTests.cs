using System.Net;
using Rekall.Age.Assets.Commands;
using Rekall.Age.Core.Commands;
using Rekall.Age.Core.Transactions;
using Rekall.Age.Workflows;

namespace Rekall.Age.Tests.Assets;

public sealed class RemoteImageSearchCommandTests
{
    [Fact]
    public async Task SearchReturnsAgentSelectableOpenLicenseProvenance()
    {
        Uri? requested = null;
        using var http = new HttpClient(new Handler(request =>
        {
            requested = request.RequestUri;
            return Json("""
                {
                  "result_count": 1,
                  "results": [{
                    "id": "image-1",
                    "title": "Forest lake",
                    "url": "https://images.example/forest.jpg",
                    "foreign_landing_url": "https://catalog.example/forest",
                    "creator": "A. Photographer",
                    "creator_url": "https://catalog.example/creator",
                    "attribution": "Forest lake by A. Photographer, CC0 1.0",
                    "license": "cc0",
                    "license_version": "1.0",
                    "license_url": "https://creativecommons.org/publicdomain/zero/1.0/",
                    "provider": "example",
                    "source": "example-source",
                    "width": 1600,
                    "height": 900,
                    "filetype": "jpg"
                  }]
                }
                """);
        }));
        var command = new SearchRemoteImagesCommand(http);
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("search images"), CancellationToken.None);

        var result = await command.ExecuteAsync(new SearchRemoteImagesRequest("misty forest lake", 5), context);

        Assert.True(result.Ok, result.Summary);
        Assert.Contains("q=misty%20forest%20lake", requested!.Query, StringComparison.Ordinal);
        Assert.Contains("license_type=commercial%2Cmodification", requested.Query, StringComparison.Ordinal);
        var image = Assert.Single(result.Value.Results);
        Assert.Equal("https://images.example/forest.jpg", image.AssetUrl);
        Assert.Equal("https://catalog.example/forest", image.LandingPageUrl);
        Assert.Equal("Forest lake by A. Photographer, CC0 1.0", image.Attribution);
        Assert.Equal("cc0", image.License);
        Assert.Contains("verify", result.Value.LicenseNotice, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(context.Transaction.ChangedResources);
    }

    [Fact]
    public void SearchIsRegisteredAndDescribesOrdinaryInternetAssetIntent()
    {
        var schema = Assert.Single(
            RekallAgeDefaultCommandRegistry.Create().Schemas,
            item => item.Name == "rekall.asset.search_remote_images");

        Assert.Contains("internet", schema.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("openly licensed", schema.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("provenance", schema.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("agent selects", schema.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchFailsClosedOnOversizedProviderResponse()
    {
        using var http = new HttpClient(new Handler(_ => Json(new string('x', 1_048_577))));
        var command = new SearchRemoteImagesCommand(http);
        var context = new RekallAgeCommandContext("agent", RekallAgeTransaction.Begin("search images"), CancellationToken.None);

        var result = await command.ExecuteAsync(new SearchRemoteImagesRequest("forest"), context);

        Assert.False(result.Ok);
        Assert.Equal("REKALL_ASSET_SEARCH_RESPONSE_TOO_LARGE", Assert.Single(result.Errors).Code);
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
    };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
