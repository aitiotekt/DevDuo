using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DevDuo;

internal static class ControlApiChecks
{
    internal static async Task Run(string assets, Action<bool, string> check)
    {
        var settings = new ControlSettings();
        var dispatches = 0;
        await using var service = new ControlApiService((request, cancellation) =>
        {
            cancellation.ThrowIfCancellationRequested();
            if (request.Path == "/api/settings") settings = ControlSettings.Merge(settings, request.Body);
            dispatches++;
            return Task.FromResult<object>(new { settings });
        }, assets);
        check(!service.IsRunning && service.BaseUri is null, "control API has no listener until explicitly started");
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        await service.StartAsync(0);
        var root = service.BaseUri!;
        check(root.Host == "127.0.0.1" && root.Port != 0, "control listener binds IPv4 loopback with its selected port");
        using (var page = await http.GetAsync(new Uri(root, "api/openapi")))
            check(page.IsSuccessStatusCode && page.Content.Headers.ContentType?.MediaType == "text/html", "OpenAPI has an offline human-readable HTML entry");
        foreach (var asset in new[] { "swagger-ui.css", "swagger-ui-bundle.js", "control-api.js" })
            check((await http.GetAsync(new Uri(root, "api/openapi-assets/" + asset))).IsSuccessStatusCode, "offline documentation asset is served: " + asset);
        using (var spec = JsonDocument.Parse(await http.GetStringAsync(new Uri(root, "api/openapi.json"))))
        {
            check(spec.RootElement.GetProperty("openapi").GetString() == "3.1.0" &&
                spec.RootElement.GetProperty("servers")[0].GetProperty("url").GetString() == root.GetLeftPart(UriPartial.Authority) &&
                spec.RootElement.GetProperty("security").GetArrayLength() == 0,
                "OpenAPI reports the actual configured address and optional local authentication");
            foreach (var path in new[] { "/api/state", "/api/settings", "/api/navigation", "/api/inspector", "/api/proxy" })
            {
                var operation = spec.RootElement.GetProperty("paths").GetProperty(path).EnumerateObject().Single();
                using var request = new HttpRequestMessage(new HttpMethod(operation.Name), new Uri(root, path));
                if (operation.Name != "get") request.Content = JsonContent.Create(new { });
                using var response = await http.SendAsync(request);
                check(response.IsSuccessStatusCode, "documented method/resource reaches the control host: " + path);
            }
        }
        using (var response = await http.PatchAsJsonAsync(new Uri(root, "api/settings"), new { fold = "open", bodyAngle = 270, language = "zh-Hans", previewScale = "fit", interactionMode = "mouse" }))
        {
            var result = await response.Content.ReadFromJsonAsync<JsonElement>();
            check(response.IsSuccessStatusCode && result.GetProperty("settings").GetProperty("bodyAngle").GetInt32() == 270 &&
                settings.Fold == "open" && settings.Language == "zh-Hans" && settings.InteractionMode == "mouse" && settings.ShowTabStrip,
                "settings PATCH applies validated fields and preserves omitted values");
        }
        foreach (var body in new[]
        {
            "{\"fold\":\"closed\",\"bodyAngle\":45}", "{\"fold\":null}", "{\"bodyAngle\":\"90\"}",
            "{\"interactionMode\":\"pen\"}", "{\"cornerRadius\":41}", "{\"shadowOpacity\":0.6}", "{\"unknown\":true}",
            "{\"fold\":\"open\",\"fold\":\"closed\"}", "[]", "{"
        })
        {
            using var request = new HttpRequestMessage(HttpMethod.Patch, new Uri(root, "api/settings"))
            { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            using var response = await http.SendAsync(request);
            check(response.StatusCode == HttpStatusCode.BadRequest && settings.Fold == "open" && settings.BodyAngle == 270,
                "invalid/duplicate settings fail without partial mutations: " + body);
        }
        using (var response = await http.PatchAsJsonAsync(new Uri(root, "api/settings"), new { tintAlpha = .4 }))
            check(response.IsSuccessStatusCode && settings.TintAlpha == .4, "material override is applied");
        using (var request = new HttpRequestMessage(HttpMethod.Patch, new Uri(root, "api/settings"))
        { Content = new StringContent("{\"tintAlpha\":null}", Encoding.UTF8, "application/json") })
            check((await http.SendAsync(request)).IsSuccessStatusCode && settings.TintAlpha is null, "null restores material defaults");
        using (var wrong = await http.GetAsync(new Uri(root, "api/settings")))
            check(wrong.StatusCode == HttpStatusCode.MethodNotAllowed && wrong.Content.Headers.Allow.Contains("PATCH"), "wrong methods return 405 and the supported method");
        check((await http.GetAsync(new Uri(root, "api/missing"))).StatusCode == HttpStatusCode.NotFound, "unknown routes return 404");
        var beforeRejected = dispatches;
        using (var origin = new HttpRequestMessage(HttpMethod.Get, new Uri(root, "api/state")))
        {
            origin.Headers.Add("Origin", "https://untrusted.example");
            check((await http.SendAsync(origin)).StatusCode == HttpStatusCode.Forbidden, "cross-origin browser control is rejected");
        }
        using (var host = new HttpRequestMessage(HttpMethod.Get, new Uri(root, "api/state")))
        {
            host.Headers.Host = "untrusted.example:" + root.Port;
            check((await http.SendAsync(host)).StatusCode == HttpStatusCode.Forbidden, "unknown hosts cannot rebind local control");
        }
        using (var content = new StringContent("{}"))
            check((await http.PostAsync(new Uri(root, "api/navigation"), content)).StatusCode == HttpStatusCode.UnsupportedMediaType,
                "browser form/plain-text submissions cannot mutate host state");
        using (var content = new StringContent("{\"data\":\"" + new string('a', 70_000) + "\"}", Encoding.UTF8, "application/json"))
            check((await http.PostAsync(new Uri(root, "api/navigation"), content)).StatusCode == HttpStatusCode.RequestEntityTooLarge,
                "oversized bodies are rejected by the HTTP server");
        check(dispatches == beforeRejected, "rejected transport requests never reach the host");
        await service.StopAsync();
        await service.StartAsync(root.Port, "fixture-control-token");
        check(service.BaseUri == root, "the same configured control address is reused after restarting");
        foreach (var path in new[] { "api/state" })
            check((await http.GetAsync(new Uri(root, path))).StatusCode == HttpStatusCode.Unauthorized, "configured bearer authentication protects " + path);
        check((await http.GetAsync(new Uri(root, "api/state?token=fixture-control-token"))).StatusCode == HttpStatusCode.Unauthorized,
            "control credentials are not accepted through URL parameters");
        check((await http.GetAsync(new Uri(root, "api/openapi"))).IsSuccessStatusCode, "public documentation remains readable before using Swagger Authorize");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "wrong");
        check((await http.GetAsync(new Uri(root, "api/state"))).StatusCode == HttpStatusCode.Unauthorized, "incorrect control credentials are rejected");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "fixture-control-token");
        using (var spec = JsonDocument.Parse(await http.GetStringAsync(new Uri(root, "api/openapi.json"))))
            check(spec.RootElement.GetProperty("security")[0].TryGetProperty("bearerAuth", out _), "OpenAPI advertises enabled header authentication");
        check((await http.GetAsync(new Uri(root, "api/state"))).IsSuccessStatusCode, "authenticated control clients read state at the same fixed path");
        await service.StopAsync();
        check(!service.IsRunning && service.BaseUri is null, "stopping clears the control listener and displayed address");
        var occupied = new TcpListener(IPAddress.Loopback, 0);
        occupied.Start();
        try
        {
            try
            {
                await service.StartAsync(((IPEndPoint)occupied.LocalEndpoint).Port);
                throw new InvalidOperationException("Port conflict unexpectedly succeeded.");
            }
            catch (IOException) { check(!service.IsRunning, "occupied ports fail explicitly without changing to a random address"); }
        }
        finally { occupied.Stop(); }
        await service.StartAsync(root.Port);
        check(service.IsRunning, "a port-conflict failure leaves the service restartable");
        await service.StopAsync();

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var pendingService = new ControlApiService(async (_, cancellation) =>
        {
            entered.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, cancellation); }
            catch (OperationCanceledException) { cancelled.TrySetResult(); throw; }
            return new { };
        }, assets);
        await pendingService.StartAsync(0);
        var pendingRequest = http.GetAsync(new Uri(pendingService.BaseUri!, "api/state"));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await pendingService.StopAsync();
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try { using var completion = await pendingRequest; }
        catch (HttpRequestException) { }
        check(!pendingService.IsRunning, "stopping cancels pending host work before disposing the listener");
    }
}
