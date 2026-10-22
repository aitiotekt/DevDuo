using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace DevDuo.Windows.Tests;

internal sealed partial class WindowsChecks
{
    private async Task CheckBrowserIdentity()
    {
        using var server = new BrowserIdentityServer(AvailableDeveloperPort());
        await Wait(state => Text(state, "initializationState", "ready"), "Identity browser initialization");
        Web.Navigate(server.Address + "page?initial");
        await WaitForScript("window.identityFetchAgent", value => value == BrowserIdentity.MobileUserAgent, "Initial mobile identity");
        await Wait(state => !Web.Navigation.IsLoading && Text(state, "pageUrl", server.Address + "page?initial"), "Initial identity navigation finished");

        // A mode change already creates a new native container. Its automatic
        // initial load must carry the identity before the first HTTP request.
        foreach (var mode in new[] { 1, 0 })
        {
            await Wait(_ => !Web.Navigation.IsLoading, "Committed identity page before mode change");
            var initialUrl = Web.Navigation.Url!;
            server.Requests.Clear();
            var oldBrowser = Web;
            Select(Settings.Mode, mode);
            await Wait(state => Text(state, "initializationState", "ready") && !ReferenceEquals(oldBrowser, Web) &&
                Text(state, "pageUrl", initialUrl), "New identity container");
            await WaitForScript("window.identityFetchAgent", value => value == BrowserIdentity.MobileUserAgent, "First document fetch identity");
            CheckIdentityRequests(server, "page", "asset.js", "fetch");
            Check(server.Requests.First().Path == "page", "The first native request is the main document with the configured identity.");
            Check(await Web.EvaluateResultAsync("navigator.userAgent") == BrowserIdentity.MobileUserAgent,
                "Native navigator.userAgent matches the HTTP User-Agent.");
            Check(await Web.EvaluateResultAsync("String(Object.hasOwn(navigator,'userAgent'))") == "false",
                "The mobile identity does not replace navigator.userAgent with an own JavaScript property.");

            Web.Navigate(server.Address + "redirect");
            await WaitForScript("window.identityCase + ':' + window.identityFetchAgent", value => value == "redirect:" + BrowserIdentity.MobileUserAgent,
                "Redirect identity");
            CheckIdentityRequests(server, "redirect", "page", "asset.js", "fetch");
            Web.Navigate(server.Address + "page?later");
            await WaitForScript("window.identityCase + ':' + window.identityFetchAgent", value => value == "later:" + BrowserIdentity.MobileUserAgent,
                "Later navigation identity");

            await Web.EvaluateResultAsync("window.identitySentinel='retained';scrollTo(0,240);'ready'");
            await WaitForScript("String(scrollY)", value => value == "240", "Identity document scroll settled");
            var document = await Web.EvaluateResultAsync("JSON.stringify([performance.timeOrigin,window.identitySentinel,scrollY])");
            var browser = Web.WindowsPanel!.Browser;
            for (var angle = 0; angle < 4; angle++)
            {
                Rotate();
                await Task.Delay(350);
                Check(ReferenceEquals(browser, Web.WindowsPanel!.Browser), "Rotation retains the native identity container.");
                Check(await Web.EvaluateResultAsync("navigator.userAgent") == BrowserIdentity.MobileUserAgent,
                    "Rotation retains the mobile identity.");
                Check(await Web.EvaluateResultAsync("JSON.stringify([performance.timeOrigin,window.identitySentinel,scrollY])") == document,
                    "Rotation retains document identity, JavaScript state and scroll position.");
            }
            CheckIdentityRequests(server, "redirect", "page", "asset.js", "fetch");
            var observations = new
            {
                mode = mode == 0 ? "safari" : "app",
                requests = server.Requests.ToArray(),
                actual = JsonSerializer.Deserialize<JsonElement>((await Web.EvaluateResultAsync(
                    "JSON.stringify({userAgent:navigator.userAgent,platform:navigator.platform,dpr:devicePixelRatio,screen:[screen.width,screen.height],viewport:[innerWidth,innerHeight],scrollY})"))!),
                host = State()
            };
            File.WriteAllText(Path.Combine(Program.Output, "browser-identity-" + observations.mode + ".json"),
                JsonSerializer.Serialize(observations, new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine("PASS mobile identity: both containers, first document/subresource/fetch headers, redirects, later navigation and four rotations with preserved document/scroll.");
    }

    private void CheckIdentityRequests(BrowserIdentityServer server, params string[] paths)
    {
        var requests = server.Requests.ToArray();
        Check(paths.All(path => requests.Any(request => request.Path == path)), "Observe actual document, subresource and fetch requests.");
        Check(requests.All(request => request.UserAgent == BrowserIdentity.MobileUserAgent), "Every actual HTTP request retains the mobile identity.");
    }
}
