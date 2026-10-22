using System.Text.RegularExpressions;
using DevDuo;

internal static class NavigationChecks
{
    internal static void Run(Action<bool, string> check)
    {
        // Calibration labels apply only to the local measurement page and keep the established query format.
        var labels = new CalibrationLabels("open", "macOS-WKWebView", "app-webview", "body-clockwise-90-scene-0-1to1");
        foreach (var developerUrl in new[] { "https://example.test/app?view=1#section", "http://127.0.0.1:3000/?x=1", "http://127.0.0.1:8765/docs/", "https://127.0.0.1:8765/" })
            check(CalibrationUrl.ApplyLabels(new Uri(developerUrl), labels) == new Uri(developerUrl), "Developer URLs are not labelled: " + developerUrl);
        var legacy = CalibrationUrl.ApplyLabels(new Uri(CalibrationUrl.DefaultAddress),
            new CalibrationLabels("closed", "macOS-WKWebView", "safari-shell-approx", "body-clockwise-0-scene-0-1to1"));
        check(legacy.AbsoluteUri == "http://127.0.0.1:8765/?device=iPhone%20Duo&pose=closed&browser=macOS-WKWebView&state=initial&displayMode=safari-shell-approx&notes=body-clockwise-0-scene-0-1to1",
            "Calibration labels keep the established query format.");
        var labelled = CalibrationUrl.ApplyLabels(new Uri("http://127.0.0.1:8765/?keep=1#notes"), labels);
        check(labelled.Fragment == "#notes" && labelled.Query.StartsWith("?keep=1&device=iPhone%20Duo&pose=open&", StringComparison.Ordinal),
            "Labels follow retained parameters and precede the fragment.");
        var relabelled = CalibrationUrl.ApplyLabels(labelled, labels with { Pose = "closed" });
        check(Regex.Matches(relabelled.Query, "(^|[?&])pose=").Count == 1 && relabelled.Query.Contains("pose=closed"), "Relabelling replaces labels without duplicates.");
        check(CalibrationUrl.RemoveLabels(relabelled).AbsoluteUri == "http://127.0.0.1:8765/?keep=1#notes", "Removing labels restores the developer page.");
        check(CalibrationUrl.ApplyLabels(new Uri("http://localhost:8765/index.html"), labels).Query.Contains("displayMode=app-webview"), "The loopback receiver page is labelled.");
        foreach (var (input, expected) in new[]
        {
            ("localhost:5173/app", "http://localhost:5173/app"), ("127.0.0.1:8765", "http://127.0.0.1:8765/"),
            ("203.0.113.20:8080", "https://203.0.113.20:8080/"), ("[::1]:3000", "http://[::1]:3000/"),
            ("example.com/path?q=1#top", "https://example.com/path?q=1#top"), ("HTTPS://Example.com", "https://example.com/"),
            (" http://127.0.0.1:8765/?a=1 ", "http://127.0.0.1:8765/?a=1")
        })
            check(CalibrationUrl.TryNormalize(input, out var normalized) && normalized.AbsoluteUri == expected, "Address normalization: " + input);
        // Exercise private-address defaults with a constructed fixture, never a recorded network endpoint.
        var privateAddressFixture = new System.Net.IPAddress(new byte[] { 10, 0, 0, 1 }).ToString();
        check(CalibrationUrl.TryNormalize(privateAddressFixture + ":8080", out var privateUri) && privateUri.AbsoluteUri == "http://" + privateAddressFixture + ":8080/",
            "Constructed private-address fixture keeps the local HTTP default.");
        foreach (var invalid in new[] { "", "   ", "file:///etc/hosts", "javascript:alert(1)", "mailto:x@example.com", "ftp://example.com", "about:blank", "http://", "two words.com" })
            check(!CalibrationUrl.TryNormalize(invalid, out _), "Rejected address: " + invalid);

        // Native navigation payloads.
        var navigation = NavigationState.FromJson("{\"url\":\"http://127.0.0.1:8765/\",\"title\":\"T\",\"isLoading\":true,\"progress\":1.5," +
            "\"canGoBack\":true,\"canGoForward\":false,\"error\":{\"code\":\"NSURLErrorDomain:-1004\",\"message\":\"Could not connect\"}}");
        check(navigation.IsLoading && navigation.Progress == 1 && navigation.CanGoBack && !navigation.CanGoForward &&
            navigation.Error == new NavigationError("NSURLErrorDomain:-1004", "Could not connect"), "Navigation payload fields are parsed and bounded.");
        check(NavigationState.FromJson("not json") == NavigationState.Empty && NavigationState.FromJson(null) == NavigationState.Empty, "Malformed navigation payloads are ignored.");
        check(NavigationState.FromJson("{\"progress\":null}").Progress is null, "An engine without progress fractions reports indeterminate loading.");
        check(NavigationState.FromJson("{\"url\":\"http://devduo.local/\"}").IsLocalFixture && !NavigationState.FromJson("{\"url\":\"about:blank\"}").IsLocalFixture &&
            !NavigationState.FromJson("{\"url\":\"https://example.test/\"}").IsLocalFixture, "The color laboratory address is recognized.");
    }
}
