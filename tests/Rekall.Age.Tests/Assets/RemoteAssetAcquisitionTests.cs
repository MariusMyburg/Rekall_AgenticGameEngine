using System.Net;
using Rekall.Age.Assets.Remote;

namespace Rekall.Age.Tests.Assets;

public sealed class RemoteAssetAcquisitionTests
{
    [Fact]
    public async Task AcquireStagesPublicHttpsContentAndReturnsDigestReceipt()
    {
        var root = TestPaths.CreateTempDirectory();
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        using var http = new HttpClient(new ScriptedHandler(_ => Response(HttpStatusCode.OK, bytes, "image/png")));
        var acquisition = new RekallAgeRemoteAssetAcquisition(http, new FixedResolver(IPAddress.Parse("93.184.216.34")));

        var receipt = await acquisition.AcquireAsync(root, new Uri("https://assets.example/rain.png"), CancellationToken.None);

        Assert.Equal("https://assets.example/rain.png", receipt.OriginalUrl);
        Assert.Equal(receipt.OriginalUrl, receipt.FinalUrl);
        Assert.Equal("image/png", receipt.MediaType);
        Assert.Equal(bytes.Length, receipt.ByteCount);
        Assert.Equal("74f81fe167d99b4cb41d6d0ccda82278caee9f3e2f25d5e5a3936ff3dcec60d0", receipt.Sha256);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(receipt.StagedPath));
        Assert.StartsWith(Path.GetFullPath(root), Path.GetFullPath(receipt.StagedPath), StringComparison.OrdinalIgnoreCase);

        receipt.DeleteStagedFile();
        Assert.False(File.Exists(receipt.StagedPath));
    }

    [Theory]
    [InlineData("http://assets.example/rain.png", "REKALL_ASSET_REMOTE_URL_INVALID")]
    [InlineData("https://user:secret@assets.example/rain.png", "REKALL_ASSET_REMOTE_URL_INVALID")]
    [InlineData("https://assets.example:8443/rain.png", "REKALL_ASSET_REMOTE_URL_INVALID")]
    [InlineData("https://assets.example/rain.png#fragment", "REKALL_ASSET_REMOTE_URL_INVALID")]
    public async Task AcquireRejectsUnsafeUrlForms(string url, string expectedCode)
    {
        using var http = new HttpClient(new ScriptedHandler(_ => throw new Xunit.Sdk.XunitException("HTTP must not run.")));
        var acquisition = new RekallAgeRemoteAssetAcquisition(http, new FixedResolver(IPAddress.Parse("93.184.216.34")));

        var error = await Assert.ThrowsAsync<RekallAgeRemoteAssetException>(
            () => acquisition.AcquireAsync(TestPaths.CreateTempDirectory(), new Uri(url), CancellationToken.None).AsTask());

        Assert.Equal(expectedCode, error.Code);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("192.168.1.2")]
    [InlineData("::1")]
    [InlineData("fc00::1")]
    [InlineData("2001:db8::1")]
    public async Task AcquireRejectsNonPublicResolvedAddresses(string address)
    {
        using var http = new HttpClient(new ScriptedHandler(_ => throw new Xunit.Sdk.XunitException("HTTP must not run.")));
        var acquisition = new RekallAgeRemoteAssetAcquisition(http, new FixedResolver(IPAddress.Parse(address)));

        var error = await Assert.ThrowsAsync<RekallAgeRemoteAssetException>(
            () => acquisition.AcquireAsync(TestPaths.CreateTempDirectory(), new Uri("https://assets.example/rain.png"), CancellationToken.None).AsTask());

        Assert.Equal("REKALL_ASSET_REMOTE_ADDRESS_BLOCKED", error.Code);
    }

    [Fact]
    public async Task AcquireRevalidatesRedirectDestination()
    {
        var handler = new ScriptedHandler(request => request.RequestUri!.Host == "assets.example"
            ? Redirect("https://internal.example/secret.png")
            : throw new Xunit.Sdk.XunitException("Blocked redirect must not be requested."));
        using var http = new HttpClient(handler);
        var acquisition = new RekallAgeRemoteAssetAcquisition(
            http,
            new HostResolver(new Dictionary<string, IPAddress>
            {
                ["assets.example"] = IPAddress.Parse("93.184.216.34"),
                ["internal.example"] = IPAddress.Loopback
            }));

        var error = await Assert.ThrowsAsync<RekallAgeRemoteAssetException>(
            () => acquisition.AcquireAsync(TestPaths.CreateTempDirectory(), new Uri("https://assets.example/rain.png"), CancellationToken.None).AsTask());

        Assert.Equal("REKALL_ASSET_REMOTE_ADDRESS_BLOCKED", error.Code);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task AcquireRejectsContentBeyondConfiguredLimitAndDeletesPartialStage()
    {
        var root = TestPaths.CreateTempDirectory();
        using var http = new HttpClient(new ScriptedHandler(_ => Response(HttpStatusCode.OK, new byte[17], "image/png")));
        var acquisition = new RekallAgeRemoteAssetAcquisition(
            http,
            new FixedResolver(IPAddress.Parse("93.184.216.34")),
            maxBytes: 16);

        var error = await Assert.ThrowsAsync<RekallAgeRemoteAssetException>(
            () => acquisition.AcquireAsync(root, new Uri("https://assets.example/rain.png"), CancellationToken.None).AsTask());

        Assert.Equal("REKALL_ASSET_REMOTE_TOO_LARGE", error.Code);
        var staging = Path.Combine(root, ".age-cache", "remote-assets");
        Assert.Empty(Directory.Exists(staging) ? Directory.GetFiles(staging) : []);
    }

    [Fact]
    public async Task AcquireEnforcesLimitWhenServerOmitsContentLength()
    {
        var root = TestPaths.CreateTempDirectory();
        using var http = new HttpClient(new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new UnknownLengthContent(new byte[17])
        }));
        var acquisition = new RekallAgeRemoteAssetAcquisition(
            http,
            new FixedResolver(IPAddress.Parse("93.184.216.34")),
            maxBytes: 16);

        var error = await Assert.ThrowsAsync<RekallAgeRemoteAssetException>(
            () => acquisition.AcquireAsync(root, new Uri("https://assets.example/rain.png"), CancellationToken.None).AsTask());

        Assert.Equal("REKALL_ASSET_REMOTE_TOO_LARGE", error.Code);
        Assert.Empty(Directory.GetFiles(Path.Combine(root, ".age-cache", "remote-assets")));
    }

    [Fact]
    public async Task AcquireIdentifiesSuppliedOperatorAndRetriesBoundedRetryAfter()
    {
        var requestCount = 0;
        string? userAgent = null;
        var handler = new ScriptedHandler(request =>
        {
            requestCount++;
            userAgent = request.Headers.UserAgent.ToString();
            if (requestCount == 1)
            {
                var limited = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                limited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
                return limited;
            }
            return Response(HttpStatusCode.OK, [1, 2, 3], "image/png");
        });
        using var http = new HttpClient(handler);
        var acquisition = new RekallAgeRemoteAssetAcquisition(http, new FixedResolver(IPAddress.Parse("93.184.216.34")));

        var receipt = await acquisition.AcquireAsync(
            TestPaths.CreateTempDirectory(),
            new Uri("https://assets.example/rain.png"),
            CancellationToken.None,
            "https://operator.example/rekall-age");

        Assert.Equal(2, requestCount);
        Assert.Contains("Rekall-AGE/0.1", userAgent, StringComparison.Ordinal);
        Assert.Contains("https://operator.example/rekall-age", userAgent, StringComparison.Ordinal);
        receipt.DeleteStagedFile();
    }

    [Fact]
    public async Task AcquireReturnsStableRateLimitErrorWithoutRetryAfter()
    {
        using var http = new HttpClient(new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)));
        var acquisition = new RekallAgeRemoteAssetAcquisition(http, new FixedResolver(IPAddress.Parse("93.184.216.34")));

        var error = await Assert.ThrowsAsync<RekallAgeRemoteAssetException>(
            () => acquisition.AcquireAsync(
                TestPaths.CreateTempDirectory(),
                new Uri("https://assets.example/rain.png"),
                CancellationToken.None).AsTask());

        Assert.Equal("REKALL_ASSET_REMOTE_RATE_LIMITED", error.Code);
    }

    [Theory]
    [InlineData("bad contact with spaces")]
    [InlineData("contact@example.org\r\nX-Evil: yes")]
    [InlineData("ftp://operator.example/contact")]
    public async Task AcquireRejectsUnsafeOperatorContact(string contact)
    {
        using var http = new HttpClient(new ScriptedHandler(_ => throw new Xunit.Sdk.XunitException("HTTP must not run.")));
        var acquisition = new RekallAgeRemoteAssetAcquisition(http, new FixedResolver(IPAddress.Parse("93.184.216.34")));

        var error = await Assert.ThrowsAsync<RekallAgeRemoteAssetException>(
            () => acquisition.AcquireAsync(
                TestPaths.CreateTempDirectory(),
                new Uri("https://assets.example/rain.png"),
                CancellationToken.None,
                contact).AsTask());

        Assert.Equal("REKALL_ASSET_REMOTE_CONTACT_INVALID", error.Code);
    }

    private static HttpResponseMessage Response(HttpStatusCode status, byte[] body, string mediaType)
    {
        var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(body) };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);
        return response;
    }

    private static HttpResponseMessage Redirect(string location) => new(HttpStatusCode.Redirect)
    {
        Headers = { Location = new Uri(location) }
    };

    private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(handler(request));
        }
    }

    private sealed class FixedResolver(params IPAddress[] addresses) : IRekallAgeHostAddressResolver
    {
        public ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<IPAddress>>(addresses);
    }

    private sealed class HostResolver(IReadOnlyDictionary<string, IPAddress> addresses) : IRekallAgeHostAddressResolver
    {
        public ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<IPAddress>>([addresses[host]]);
    }

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override Stream CreateContentReadStream(CancellationToken cancellationToken) =>
            new MemoryStream(bytes, writable: false);
    }
}
