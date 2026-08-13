namespace DeepSeekHarness.Desktop;

/// <summary>Disposition for one top-level WebView navigation.</summary>
internal enum NavigationDisposition
{
    Allow,
    OpenExternal,
    Block,
}

/// <summary>Keeps Harness privileges on one exact loopback origin.</summary>
internal static class NavigationPolicy
{
    /// <summary>Classify a requested document URL.</summary>
    /// <param name="harnessOrigin">The child process origin accepted by this window.</param>
    /// <param name="target">The requested top-level target.</param>
    /// <returns>The host action for the navigation.</returns>
    internal static NavigationDisposition Classify(Uri harnessOrigin, Uri target)
    {
        if (target.IsAbsoluteUri && string.Equals(target.Scheme, "about", StringComparison.OrdinalIgnoreCase)
            && string.Equals(target.OriginalString, "about:blank", StringComparison.OrdinalIgnoreCase))
        {
            return NavigationDisposition.Allow;
        }

        if (SameOrigin(harnessOrigin, target)) return NavigationDisposition.Allow;

        if (target.IsAbsoluteUri && target.Scheme is "http" or "https")
        {
            return IsLoopbackName(target.Host) ? NavigationDisposition.Block : NavigationDisposition.OpenExternal;
        }

        if (target.IsAbsoluteUri && string.Equals(target.Scheme, Uri.UriSchemeMailto, StringComparison.OrdinalIgnoreCase))
        {
            return NavigationDisposition.OpenExternal;
        }

        return NavigationDisposition.Block;
    }

    private static bool SameOrigin(Uri expected, Uri actual) =>
        actual.IsAbsoluteUri
        && string.Equals(expected.Scheme, actual.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(expected.Host, actual.Host, StringComparison.OrdinalIgnoreCase)
        && expected.Port == actual.Port;

    private static bool IsLoopbackName(string host) =>
        string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, "[::1]", StringComparison.OrdinalIgnoreCase);
}
