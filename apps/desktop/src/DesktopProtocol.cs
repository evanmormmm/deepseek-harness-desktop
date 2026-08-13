using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepSeekHarness.Desktop;

/// <summary>The trusted readiness fact emitted by the desktop Harness child.</summary>
internal sealed record DesktopReady(Uri Url, int ProcessId);

/// <summary>Parses the line protocol shared by the native host and Node bridge.</summary>
internal static class DesktopProtocol
{
    internal const string ReadyPrefix = "DSH_DESKTOP_READY ";
    internal const string StoppedPrefix = "DSH_DESKTOP_STOPPED ";

    /// <summary>Parse a loopback readiness line from the child process.</summary>
    /// <param name="line">One complete stdout line.</param>
    /// <param name="ready">The validated readiness fact when parsing succeeds.</param>
    /// <returns><see langword="true"/> only for an HTTP URL on exact IPv4 loopback with a usable port.</returns>
    internal static bool TryParseReady(string line, out DesktopReady? ready)
    {
        ready = null;
        if (!line.StartsWith(ReadyPrefix, StringComparison.Ordinal)) return false;

        try
        {
            var payload = JsonSerializer.Deserialize<ReadyPayload>(line[ReadyPrefix.Length..]);
            if (payload is null || payload.ProcessId <= 0 || !Uri.TryCreate(payload.Url, UriKind.Absolute, out var url))
            {
                return false;
            }

            if (!string.Equals(url.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
                || !string.Equals(url.Host, "127.0.0.1", StringComparison.Ordinal)
                || url.Port is <= 0 or > 65535
                || !string.IsNullOrEmpty(url.UserInfo))
            {
                return false;
            }

            ready = new DesktopReady(url, payload.ProcessId);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (UriFormatException)
        {
            return false;
        }
    }

    private sealed record ReadyPayload(
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("pid")] int ProcessId);
}
