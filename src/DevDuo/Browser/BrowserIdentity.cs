namespace DevDuo;

internal static class BrowserIdentity
{
    // Preserve the observed Safari string, including its frozen OS token. This is
    // a mobile compatibility identity, not the desktop engine or hardware version.
    // No separately measured App WebView identity is available in this cohort.
    // See docs/en/014-WINDOWS-WEBKIT.md for provenance and acceptance limits.
    internal const string MobileUserAgent = "Mozilla/5.0 (iPhone; CPU iPhone OS 18_7 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/27.0 Mobile/15E148 Safari/604.1";
}
