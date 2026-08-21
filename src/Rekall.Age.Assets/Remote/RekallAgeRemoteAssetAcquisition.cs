using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Rekall.Age.Core.Commands;

namespace Rekall.Age.Assets.Remote;

public interface IRekallAgeHostAddressResolver
{
    ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken);
}

public sealed record RekallAgeRemoteAssetReceipt(
    string OriginalUrl,
    string FinalUrl,
    string StagedPath,
    string? MediaType,
    long ByteCount,
    string Sha256)
{
    public void DeleteStagedFile()
    {
        if (File.Exists(StagedPath))
        {
            File.Delete(StagedPath);
        }
    }
}

public sealed class RekallAgeRemoteAssetException : RekallAgeCodedBoundaryException
{
    public RekallAgeRemoteAssetException(string code, string message, string target, Exception? innerException = null)
        : base(code, message, target, innerException)
    {
    }
}

public sealed class RekallAgeRemoteAssetAcquisition
{
    public const long DefaultMaxBytes = 32L * 1024 * 1024;
    public const int DefaultMaxRedirects = 5;
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _httpClient;
    private readonly IRekallAgeHostAddressResolver _resolver;
    private readonly long _maxBytes;
    private readonly int _maxRedirects;
    private readonly TimeSpan _timeout;

    public RekallAgeRemoteAssetAcquisition()
    {
        _resolver = new SystemHostAddressResolver();
        _httpClient = CreateProductionClient(_resolver);
        _maxBytes = DefaultMaxBytes;
        _maxRedirects = DefaultMaxRedirects;
        _timeout = DefaultTimeout;
    }

    public RekallAgeRemoteAssetAcquisition(
        HttpClient httpClient,
        IRekallAgeHostAddressResolver resolver,
        long maxBytes = DefaultMaxBytes,
        int maxRedirects = DefaultMaxRedirects,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRedirects);
        _httpClient = httpClient;
        _resolver = resolver;
        _maxBytes = maxBytes;
        _maxRedirects = maxRedirects;
        _timeout = timeout ?? DefaultTimeout;
    }

    public async ValueTask<RekallAgeRemoteAssetReceipt> AcquireAsync(
        string projectRoot,
        Uri source,
        CancellationToken cancellationToken,
        string? operatorContact = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentNullException.ThrowIfNull(source);
        var original = ValidateUri(source);
        var normalizedContact = ValidateOperatorContact(operatorContact);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_timeout);
        string? stagedPath = null;

        try
        {
            var current = original;
            var redirectCount = 0;
            var rateLimitRetries = 0;
            while (true)
            {
                await ValidatePublicHostAsync(current, deadline.Token);
                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                request.Headers.UserAgent.ParseAdd(normalizedContact is null
                    ? "Rekall-AGE/0.1 remote-asset-import"
                    : $"Rekall-AGE/0.1 ({normalizedContact}) remote-asset-import");
                HttpResponseMessage response;
                try
                {
                    response = await _httpClient.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        deadline.Token);
                }
                catch (HttpRequestException error)
                {
                    if (FindRemoteException(error) is { } boundary)
                    {
                        throw boundary;
                    }

                    throw Error("REKALL_ASSET_REMOTE_HTTP_FAILED", "Remote asset request could not be completed.", current, error);
                }
                using (response)
                {

                    if (IsRedirect(response.StatusCode))
                    {
                        if (redirectCount >= _maxRedirects)
                        {
                            throw Error("REKALL_ASSET_REMOTE_REDIRECT_LIMIT", $"Remote asset exceeded {_maxRedirects} redirects.", current);
                        }

                        var location = response.Headers.Location
                            ?? throw Error("REKALL_ASSET_REMOTE_REDIRECT_INVALID", "Remote asset redirect omitted its destination.", current);
                        current = ValidateUri(location.IsAbsoluteUri ? location : new Uri(current, location));
                        redirectCount++;
                        continue;
                    }

                    if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        var retryAfter = ResolveRetryAfter(response.Headers.RetryAfter);
                        if (retryAfter is { } delay
                            && delay >= TimeSpan.Zero
                            && delay <= TimeSpan.FromSeconds(5)
                            && rateLimitRetries < 2)
                        {
                            rateLimitRetries++;
                            if (delay > TimeSpan.Zero)
                            {
                                await Task.Delay(delay, deadline.Token);
                            }
                            continue;
                        }

                        var retryDetail = retryAfter is null
                            ? "The host did not provide Retry-After."
                            : $"The host requested retry after {Math.Ceiling(retryAfter.Value.TotalSeconds):0} seconds, outside the bounded automatic retry policy.";
                        throw Error(
                            "REKALL_ASSET_REMOTE_RATE_LIMITED",
                            $"Remote asset host rate-limited the request. {retryDetail} Select another licensed source or retry later; do not circumvent the host limit.",
                            current);
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        throw Error(
                            "REKALL_ASSET_REMOTE_HTTP_FAILED",
                            $"Remote asset request returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).",
                            current);
                    }

                    if (response.Content.Headers.ContentLength is > 0 and var declaredBytes && declaredBytes > _maxBytes)
                    {
                        throw Error("REKALL_ASSET_REMOTE_TOO_LARGE", $"Remote asset declares {declaredBytes} bytes; limit is {_maxBytes} bytes.", current);
                    }

                    var extension = ResolveExtension(current, response.Content.Headers.ContentType?.MediaType);
                    stagedPath = CreateStagedPath(projectRoot, extension);
                    var (bytes, hash) = await CopyBoundedAndHashAsync(response.Content, stagedPath, deadline.Token);
                    return new RekallAgeRemoteAssetReceipt(
                        original.AbsoluteUri,
                        current.AbsoluteUri,
                        stagedPath,
                        response.Content.Headers.ContentType?.MediaType,
                        bytes,
                        hash);
                }
            }
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            TryDelete(stagedPath);
            throw Error("REKALL_ASSET_REMOTE_TIMEOUT", $"Remote asset acquisition exceeded {_timeout.TotalSeconds:0} seconds.", original, error);
        }
        catch
        {
            TryDelete(stagedPath);
            throw;
        }
    }

    private async ValueTask ValidatePublicHostAsync(Uri uri, CancellationToken cancellationToken)
    {
        IReadOnlyList<IPAddress> addresses;
        try
        {
            addresses = await _resolver.ResolveAsync(uri.DnsSafeHost, cancellationToken);
        }
        catch (Exception error) when (error is SocketException or IOException)
        {
            throw Error("REKALL_ASSET_REMOTE_DNS_FAILED", "Remote asset host could not be resolved.", uri, error);
        }

        if (addresses.Count == 0 || addresses.Any(address => !IsPublicAddress(address)))
        {
            throw Error("REKALL_ASSET_REMOTE_ADDRESS_BLOCKED", "Remote asset host resolved to a non-public or unsafe network address.", uri);
        }
    }

    private async ValueTask<(long Bytes, string Hash)> CopyBoundedAndHashAsync(
        HttpContent content,
        string stagedPath,
        CancellationToken cancellationToken)
    {
        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        await using var destination = new FileStream(
            stagedPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        long total = 0;
        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                total += read;
                if (total > _maxBytes)
                {
                    throw Error("REKALL_ASSET_REMOTE_TOO_LARGE", $"Remote asset exceeded the {_maxBytes}-byte limit while downloading.", stagedPath);
                }

                hasher.AppendData(buffer, 0, read);
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            await destination.FlushAsync(cancellationToken);
            return (total, Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static Uri ValidateUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || uri.Port != 443
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment)
            || string.IsNullOrWhiteSpace(uri.DnsSafeHost))
        {
            throw Error("REKALL_ASSET_REMOTE_URL_INVALID", "Remote assets require an absolute HTTPS URL without credentials, fragments, or a non-default port.", uri);
        }

        return uri;
    }

    private static string? ValidateOperatorContact(string? contact)
    {
        if (string.IsNullOrWhiteSpace(contact))
        {
            return null;
        }

        var normalized = contact.Trim();
        var isSafeHeaderText = normalized.Length <= 256
            && normalized.All(character => character is >= (char)0x21 and <= (char)0x7E)
            && normalized.IndexOfAny(['(', ')', '\\', '"']) < 0;
        var isHttpsUrl = Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            && uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrEmpty(uri.UserInfo)
            && string.IsNullOrEmpty(uri.Fragment);
        var at = normalized.IndexOf('@');
        var isEmail = at > 0 && at < normalized.Length - 1 && normalized.IndexOf('@', at + 1) < 0;
        if (!isSafeHeaderText || (!isHttpsUrl && !isEmail))
        {
            throw Error(
                "REKALL_ASSET_REMOTE_CONTACT_INVALID",
                "Remote asset operatorContact must be one printable HTTPS URL or email address without header delimiters.",
                contact);
        }

        return normalized;
    }

    private static TimeSpan? ResolveRetryAfter(System.Net.Http.Headers.RetryConditionHeaderValue? retryAfter)
    {
        if (retryAfter?.Delta is { } delta)
        {
            return delta;
        }
        if (retryAfter?.Date is { } date)
        {
            return date - DateTimeOffset.UtcNow;
        }
        return null;
    }

    private static string CreateStagedPath(string projectRoot, string extension)
    {
        var root = Path.GetFullPath(projectRoot);
        Directory.CreateDirectory(root);
        var stagingDirectory = Path.GetFullPath(Path.Combine(root, ".age-cache", "remote-assets"));
        if (!stagingDirectory.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw Error("REKALL_ASSET_REMOTE_STAGE_INVALID", "Remote asset staging escaped the project root.", stagingDirectory);
        }

        Directory.CreateDirectory(stagingDirectory);
        var cacheDirectory = Path.GetDirectoryName(stagingDirectory)!;
        if (IsReparsePoint(root) || IsReparsePoint(cacheDirectory) || IsReparsePoint(stagingDirectory))
        {
            throw Error("REKALL_ASSET_REMOTE_STAGE_INVALID", "Remote asset project and staging directories must not be reparse points.", stagingDirectory);
        }

        return Path.Combine(stagingDirectory, $"{Guid.NewGuid():N}{extension}");
    }

    private static string ResolveExtension(Uri uri, string? mediaType)
    {
        var extension = Path.GetExtension(Uri.UnescapeDataString(uri.AbsolutePath));
        if (extension.Length is > 1 and <= 16 && extension.Skip(1).All(char.IsLetterOrDigit))
        {
            return extension.ToLowerInvariant();
        }

        return mediaType?.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            "audio/wav" or "audio/x-wav" => ".wav",
            "audio/mpeg" => ".mp3",
            "model/gltf-binary" => ".glb",
            "application/json" => ".json",
            _ => throw Error("REKALL_ASSET_REMOTE_FILENAME_UNSUPPORTED", "Remote asset URL or media type does not provide a safe supported file extension.", uri)
        };
    }

    private static bool IsRedirect(HttpStatusCode status) => status is
        HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod
        or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] is not 0 and not 10 and not 127
                && !(bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
                && !(bytes[0] == 169 && bytes[1] == 254)
                && !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                && !(bytes[0] == 192 && bytes[1] == 0 && bytes[2] is 0 or 2)
                && !(bytes[0] == 192 && bytes[1] == 88 && bytes[2] == 99)
                && !(bytes[0] == 192 && bytes[1] == 168)
                && !(bytes[0] == 198 && bytes[1] is 18 or 19)
                && !(bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100)
                && !(bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113)
                && bytes[0] < 224;
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6
            || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.IPv6Loopback)
            || address.IsIPv6LinkLocal
            || address.IsIPv6Multicast
            || address.IsIPv6SiteLocal)
        {
            return false;
        }

        var ipv6 = address.GetAddressBytes();
        var uniqueLocal = (ipv6[0] & 0xFE) == 0xFC;
        var documentation = ipv6[0] == 0x20 && ipv6[1] == 0x01 && ipv6[2] == 0x0D && ipv6[3] == 0xB8;
        var nat64 = ipv6.AsSpan(0, 12).SequenceEqual(new byte[] { 0x00, 0x64, 0xFF, 0x9B, 0, 0, 0, 0, 0, 0, 0, 0 });
        var teredo = ipv6[0] == 0x20 && ipv6[1] == 0x01 && ipv6[2] == 0 && ipv6[3] == 0;
        var sixToFour = ipv6[0] == 0x20 && ipv6[1] == 0x02;
        return !uniqueLocal && !documentation && !nat64 && !teredo && !sixToFour;
    }

    private static HttpClient CreateProductionClient(IRekallAgeHostAddressResolver resolver)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            ConnectCallback = async (context, cancellationToken) =>
            {
                var addresses = await resolver.ResolveAsync(context.DnsEndPoint.Host, cancellationToken);
                var safe = addresses.Where(IsPublicAddress).ToArray();
                if (safe.Length != addresses.Count || safe.Length == 0)
                {
                    throw Error("REKALL_ASSET_REMOTE_ADDRESS_BLOCKED", "Remote asset connection resolved to a non-public or unsafe network address.", context.DnsEndPoint.Host);
                }

                Exception? lastError = null;
                foreach (var address in safe)
                {
                    var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    try
                    {
                        await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch (Exception error) when (error is SocketException or IOException)
                    {
                        lastError = error;
                        socket.Dispose();
                    }
                }

                throw new IOException("Remote asset host could not be connected.", lastError);
            }
        };
        return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    private static RekallAgeRemoteAssetException Error(string code, string message, object target, Exception? inner = null) =>
        new(code, message, target.ToString() ?? "remote asset", inner);

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static void TryDelete(string? path)
    {
        if (path is null || !File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static RekallAgeRemoteAssetException? FindRemoteException(Exception? error)
    {
        while (error is not null)
        {
            if (error is RekallAgeRemoteAssetException remote)
            {
                return remote;
            }
            error = error.InnerException;
        }
        return null;
    }

    private sealed class SystemHostAddressResolver : IRekallAgeHostAddressResolver
    {
        public async ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken) =>
            await Dns.GetHostAddressesAsync(host, cancellationToken);
    }
}
