using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using DevDuo;

internal static class ScreenshotChecks
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLbtAAAAABJRU5ErkJggg==");

    internal static async Task Run(string assets, Action<bool, string> check)
    {
        await using var api = new ControlApiService(async (request, token) =>
        {
            await Task.Yield();
            token.ThrowIfCancellationRequested();
            var options = ScreenshotRequest.Parse(request.Body);
            return new ScreenshotImage(Png, options.Format, 1, 1, options.Target, options.Target == "device" ? 2 : 1);
        }, assets);
        await api.StartAsync(0, "capture-token");
        using var http = new HttpClient { BaseAddress = api.BaseUri, Timeout = TimeSpan.FromSeconds(5) };
        using (var denied = await http.PostAsJsonAsync("api/screenshot", new { target = "device" }))
            check(denied.StatusCode == HttpStatusCode.Unauthorized, "Screenshots require the configured REST credential.");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "capture-token");
        foreach (var target in new[] { "viewport", "device" })
        {
            using var image = await http.PostAsJsonAsync("api/screenshot", new { target });
            check(image.IsSuccessStatusCode && image.Content.Headers.ContentType?.MediaType == "image/png" &&
                (await image.Content.ReadAsByteArrayAsync()).SequenceEqual(Png), "REST returns raw PNG bytes after asynchronous native capture: " + target);
            check(image.Headers.GetValues("X-DevDuo-Target").Single() == target &&
                image.Headers.GetValues("X-DevDuo-Width").Single() == "1" && image.Headers.CacheControl?.NoStore == true,
                "Image dimensions, target and no-store policy accompany the binary response.");
        }
        foreach (var body in new[] { "{\"target\":\"desktop\"}", "{\"format\":\"webp\"}", "{\"quality\":80}", "{\"format\":\"jpeg\",\"quality\":101}", "{\"file\":\"image.png\"}" })
        {
            using var response = await http.PostAsync("api/screenshot", new StringContent(body, Encoding.UTF8, "application/json"));
            check(response.StatusCode == HttpStatusCode.BadRequest, "Invalid image requests return 400: " + body);
        }
        using (var spec = await http.GetFromJsonAsync<JsonDocument>("api/openapi.json"))
            check(spec!.RootElement.GetProperty("paths").GetProperty("/api/screenshot").GetProperty("post")
                .GetProperty("responses").GetProperty("200").GetProperty("content").TryGetProperty("image/png", out _),
                "Live OpenAPI advertises binary screenshots.");

        var forwarded = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocked = false;
        await using var cdp = new CdpDebuggingService(_ => { forwarded++; return Task.CompletedTask; }, assets,
            async (parameters, token) =>
            {
                var options = ScreenshotRequest.ParseCdp(parameters);
                if (blocked) { started.TrySetResult(); await Task.Delay(Timeout.Infinite, token); }
                if (options.Format == "jpeg") throw new ControlApiException(409, "Native renderer unavailable.");
                return new { data = Convert.ToBase64String(Png) };
            });
        await cdp.StartAsync();
        cdp.HandlePageMessage("{\"type\":\"ready\",\"documentId\":\"screenshot-doc\",\"url\":\"https://example.test/\",\"title\":\"Screenshot\"}");
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{cdp.Port}/devtools/page/devduo"), CancellationToken.None);
        using (var image = await Command(socket, 1, new { }))
            check(image.RootElement.GetProperty("id").GetInt32() == 1 &&
                Convert.FromBase64String(image.RootElement.GetProperty("result").GetProperty("data").GetString()!).SequenceEqual(Png) && forwarded == 0,
                "Page.captureScreenshot is answered by the native host, preserving the client ID and base64 bytes.");
        using (var invalid = await Command(socket, 2, new { clip = new { x = 0, y = 0, width = 20, height = 20, scale = 1 } }))
            check(invalid.RootElement.GetProperty("error").GetProperty("code").GetInt32() == -32602,
                "Unsupported screenshot parameters return a CDP parameter error.");
        using (var failure = await Command(socket, 3, new { format = "jpeg", quality = 80 }))
            check(failure.RootElement.GetProperty("error").GetProperty("code").GetInt32() == -32000,
                "Native screenshot failures return a CDP operation error without closing the connection.");
        using (var recovered = await Command(socket, 4, new { fromSurface = true, captureBeyondViewport = true }))
            check(recovered.RootElement.TryGetProperty("result", out _), "The screenshot connection remains usable after an error.");
        blocked = true;
        await socket.SendAsync(Encoding.UTF8.GetBytes("{\"id\":5,\"method\":\"Page.captureScreenshot\"}"), WebSocketMessageType.Text, true, CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await cdp.StopAsync().WaitAsync(TimeSpan.FromSeconds(3));
        check(!cdp.IsRunning, "Stopping CDP cancels an in-flight native screenshot.");
    }

    private static async Task<JsonDocument> Command(ClientWebSocket socket, int id, object parameters)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await socket.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { id, method = "Page.captureScreenshot", @params = parameters })),
            WebSocketMessageType.Text, true, timeout.Token);
        using var message = new MemoryStream();
        var buffer = new byte[4096];
        WebSocketReceiveResult received;
        do
        {
            received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
            message.Write(buffer, 0, received.Count);
        } while (!received.EndOfMessage);
        return JsonDocument.Parse(message.ToArray());
    }
}
