using System.Net;
using Rekall.Age.Project;

namespace Rekall.Age.Workflows.Web;

public sealed class RekallAgeHttpWebContentFetcher(HttpClient client)
{
    private const int ReadBufferBytes = 64 * 1024;

    public async ValueTask<ReadOnlyMemory<byte>> FetchAsync(
        string logicalPath,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBytes, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumBytes, int.MaxValue);
        var normalized = RekallAgeGameContentPath.Normalize(logicalPath);
        if (!normalized.Equals(logicalPath, StringComparison.Ordinal) || normalized.Contains(':'))
        {
            throw new RekallAgeGameContentException(
                "REKALL_WEB_FETCH_PATH_INVALID",
                normalized,
                $"Web content path '{logicalPath}' is not canonical.");
        }

        var requestPath = string.Join('/', normalized.Split('/').Select(Uri.EscapeDataString));
        using var request = new HttpRequestMessage(HttpMethod.Get, requestPath);
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is < HttpStatusCode.OK or >= HttpStatusCode.MultipleChoices)
        {
            throw new RekallAgeGameContentException(
                "REKALL_WEB_FETCH_FAILED",
                normalized,
                $"Web content '{normalized}' returned HTTP {(int)response.StatusCode}.");
        }
        if (response.Content.Headers.ContentLength is long contentLength
            && contentLength > maximumBytes)
        {
            throw TooLarge(normalized, maximumBytes);
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream((int)Math.Min(maximumBytes, ReadBufferBytes));
        var buffer = new byte[ReadBufferBytes];
        while (true)
        {
            var remainingProbe = maximumBytes - output.Length + 1;
            var read = await source.ReadAsync(
                buffer.AsMemory(0, (int)Math.Min(buffer.Length, remainingProbe)),
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }
            output.Write(buffer, 0, read);
            if (output.Length > maximumBytes)
            {
                throw TooLarge(normalized, maximumBytes);
            }
        }
        return output.ToArray();
    }

    private static RekallAgeGameContentException TooLarge(string logicalPath, long maximumBytes) =>
        new(
            "REKALL_WEB_FETCH_TOO_LARGE",
            logicalPath,
            $"Web content '{logicalPath}' exceeds the {maximumBytes}-byte fetch limit.");
}
