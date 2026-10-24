using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text.Json;
using DevDuo;

internal static class CdpAuthenticationChecks
{
    internal static async Task Run(string assets, Action<bool, string> check)
    {
        const string token = "fixture-header-credential";
        await using var service = new CdpDebuggingService(_ => Task.CompletedTask, assets);
        using var anonymous = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        using var authenticated = new HttpClient();
        authenticated.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        try
        {
            await service.StartAsync(allowLan: true);
            throw new InvalidOperationException("Unauthenticated LAN startup unexpectedly succeeded.");
        }
        catch (ArgumentException) { check(!service.IsRunning, "unauthenticated LAN CDP is rejected before opening a listener"); }
        try
        {
            await service.StartAsync(bearerToken: "invalid\r\nheader");
            throw new InvalidOperationException("A malformed credential unexpectedly succeeded.");
        }
        catch (ArgumentException) { check(!service.IsRunning, "header-control characters are rejected before listener startup"); }

        await service.StartAsync(bearerToken: token);
        var root = service.BaseUri!;
        Ready(service);
        foreach (var path in new[] { "", "json/list", "json/version", "devtools/page/devduo" })
        {
            using var response = await anonymous.GetAsync(new Uri(root, path));
            check(response.StatusCode == HttpStatusCode.Unauthorized, "configured authentication protects " + path);
        }
        using (var wrong = new HttpRequestMessage(HttpMethod.Get, new Uri(root, "json/list")))
        {
            wrong.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-credential");
            check((await anonymous.SendAsync(wrong)).StatusCode == HttpStatusCode.Unauthorized, "an incorrect bearer header is rejected");
        }
        check((await anonymous.GetAsync(new Uri(root, "json/list?token=" + token))).StatusCode == HttpStatusCode.Unauthorized,
            "the persistent bearer credential is not accepted through a URL parameter");
        using var discovery = JsonDocument.Parse(await authenticated.GetStringAsync(new Uri(root, "json/list")));
        var socketUri = new Uri(discovery.RootElement[0].GetProperty("webSocketDebuggerUrl").GetString()!);
        check(socketUri.AbsolutePath == "/devtools/page/devduo" && socketUri.Query == "" && !root.AbsoluteUri.Contains(token, StringComparison.Ordinal),
            "authenticated discovery returns a fixed credential-free connection address");
        check((await authenticated.GetAsync(new Uri(root, "json/version"))).StatusCode == HttpStatusCode.OK,
            "bearer-authenticated version discovery works");
        using (var socket = new ClientWebSocket())
        {
            socket.Options.SetRequestHeader("Authorization", "Bearer " + token);
            await socket.ConnectAsync(socketUri, CancellationToken.None);
            check(socket.State == WebSocketState.Open, "agent clients authenticate the fixed WebSocket through a request header");
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        }
        await service.StopAsync();
        await service.StartAsync(port: root.Port, bearerToken: token);
        Ready(service);
        check(service.BaseUri == root, "authentication does not change the persisted base address across service restarts");
        var frontend = service.FrontendUri!;
        var browserSocket = new Uri("ws://" + System.Web.HttpUtility.ParseQueryString(frontend.Query)["ws"]);
        check(!frontend.AbsoluteUri.Contains(token, StringComparison.Ordinal), "the built-in frontend never puts the persistent credential in its URL");
        check((await anonymous.GetAsync(frontend)).StatusCode == HttpStatusCode.OK,
            "bundled frontend assets remain available without exposing page discovery");
        check((await anonymous.GetAsync(new Uri(root, "json/list" + browserSocket.Query))).StatusCode == HttpStatusCode.Unauthorized,
            "a browser socket capability cannot authenticate HTTP discovery");
        using (var browser = new ClientWebSocket())
        {
            browser.Options.SetRequestHeader("Origin", root.GetLeftPart(UriPartial.Authority));
            await browser.ConnectAsync(browserSocket, CancellationToken.None);
            check(browser.State == WebSocketState.Open, "the built-in frontend can connect when header authentication is enabled");
            await browser.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        }
        await service.StopAsync();
        await service.StartAsync(port: root.Port, bearerToken: token);
        Ready(service);
        using (var expired = new ClientWebSocket())
        {
            expired.Options.SetRequestHeader("Origin", root.GetLeftPart(UriPartial.Authority));
            try
            {
                await expired.ConnectAsync(browserSocket, CancellationToken.None);
                throw new InvalidOperationException("An expired browser capability unexpectedly connected.");
            }
            catch (WebSocketException) { check(true, "restarting invalidates previous built-in frontend capabilities"); }
        }
        using (var crossOrigin = new ClientWebSocket())
        {
            crossOrigin.Options.SetRequestHeader("Authorization", "Bearer " + token);
            crossOrigin.Options.SetRequestHeader("Origin", "https://untrusted.example");
            try
            {
                await crossOrigin.ConnectAsync(socketUri, CancellationToken.None);
                throw new InvalidOperationException("Cross-origin socket unexpectedly connected.");
            }
            catch (WebSocketException) { check(true, "a valid bearer header does not bypass browser-origin validation"); }
        }
        await service.StopAsync();
    }

    private static void Ready(CdpDebuggingService service) => service.HandlePageMessage(
        "{\"type\":\"ready\",\"documentId\":\"authenticated-document\",\"url\":\"https://example.test/\",\"title\":\"Authentication test\"}");
}
