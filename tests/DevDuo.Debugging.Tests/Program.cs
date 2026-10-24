using System.Net;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using DevDuo;

var assets = Path.GetFullPath(Path.Combine("temp", "build", "debugging"));
var errors = new List<string>();
var documentId = "first-document";
var targetChanges = 0;
var dispatches = new ConcurrentQueue<JsonElement>();
string? cleanupClient = null;
var cleanupObserved = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
var cleanupRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
CdpDebuggingService? service = null;
service = new CdpDebuggingService(command =>
{
    using var parsed = JsonDocument.Parse(command);
    dispatches.Enqueue(parsed.RootElement.Clone());
    if (parsed.RootElement.TryGetProperty("__devduoCleanup", out _))
    {
        if (parsed.RootElement.GetProperty("__devduoClientId").GetString() == cleanupClient)
        {
            cleanupObserved.TrySetResult(parsed.RootElement.Clone());
            return cleanupRelease.Task;
        }
        return Task.CompletedTask;
    }
    service!.HandlePageMessage(JsonSerializer.Serialize(new
    {
        type = "cdp", documentId,
        message = JsonSerializer.Serialize(new { id = parsed.RootElement.GetProperty("id").GetInt32(), result = new { value = "round trip" } }),
    }));
    return Task.CompletedTask;
}, assets);
service.Error += errors.Add;
service.TargetChanged += () => targetChanges++;
await using var lifetime = service;
await service.StartAsync();
var root = service.BaseUri!;
var port = service.Port;
Check(port != 0 && root.Host == "127.0.0.1", "port zero selects a loopback listener");
Check(!service.HasTarget && service.FrontendUri is null, "the frontend remains unavailable before native page readiness");
using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
Check((await http.GetAsync($"http://127.0.0.1:{port}/json/list")).StatusCode == HttpStatusCode.OK, "local discovery uses a fixed path without mandatory authentication");
Check((await http.GetStringAsync(new Uri(root, "json/list"))) == "[]", "uninitialized pages are not advertised");
service.HandlePageMessage("{\"type\":\"ready\",\"documentId\":\"first-document\",\"url\":\"https://example.test/\",\"title\":\"Example\"}");
service.HandlePageMessage("{\"type\":\"ready\",\"documentId\":\"first-document\",\"url\":\"https://example.test/\",\"title\":\"Example\"}");
Check(targetChanges == 1, "repeated readiness for one document does not reload the frontend");
using var targets = JsonDocument.Parse(await http.GetStringAsync(new Uri(root, "json/list")));
var target = targets.RootElement[0];
Check(target.GetProperty("url").GetString() == "https://example.test/", "discovery advertises the instrumented document");
var websocketUri = new Uri(target.GetProperty("webSocketDebuggerUrl").GetString()!);
using var frontend = await http.GetAsync(service.FrontendUri);
Check(frontend.StatusCode == HttpStatusCode.OK && frontend.Content.Headers.ContentType?.MediaType == "text/html",
    "the packaged chii frontend is served offline");
var module = Directory.EnumerateFiles(Path.Combine(assets, "front_end"), "*.js", SearchOption.AllDirectories).First();
var modulePath = Path.GetRelativePath(assets, module).Replace('\\', '/');
Check((await http.GetAsync(new Uri(root, modulePath))).StatusCode == HttpStatusCode.OK, "frontend modules resolve at stable public asset paths");
using var version = JsonDocument.Parse(await http.GetStringAsync(new Uri(root, "json/version")));
Check(!string.IsNullOrEmpty(version.RootElement.GetProperty("Protocol-Version").GetString()), "version discovery supplies a protocol version");

using (var request = new HttpRequestMessage(HttpMethod.Get, new Uri(root, "json/list")))
{
    request.Headers.Host = $"untrusted.example:{port}";
    Check((await http.SendAsync(request)).StatusCode == HttpStatusCode.BadRequest, "unrecognized hosts are rejected");
}
using (var rejected = new ClientWebSocket())
{
    rejected.Options.SetRequestHeader("Origin", "https://untrusted.example");
    await ExpectWebSocketFailureAsync(() => rejected.ConnectAsync(websocketUri, CancellationToken.None), "cross-origin browser websocket is rejected");
}
using var socket = new ClientWebSocket();
await socket.ConnectAsync(websocketUri, CancellationToken.None);
using (var second = new ClientWebSocket())
    await ExpectWebSocketFailureAsync(() => second.ConnectAsync(websocketUri, CancellationToken.None), "a second debugger is rejected");
await socket.SendAsync(Encoding.UTF8.GetBytes("{\"id\":7,\"method\":\"Runtime.evaluate\",\"params\":{\"expression\":\"1+1\"}}"), WebSocketMessageType.Text, true, CancellationToken.None);
var buffer = new byte[4096];
using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
{
    var received = await socket.ReceiveAsync(buffer, timeout.Token);
    using var reply = JsonDocument.Parse(Encoding.UTF8.GetString(buffer, 0, received.Count));
    Check(reply.RootElement.GetProperty("id").GetInt32() == 7, "raw CDP commands and native-bridge responses round trip");
}
service.HandlePageMessage("{\"type\":\"cdp\",\"documentId\":\"stale-document\",\"message\":\"stale\"}");
await socket.SendAsync(Encoding.UTF8.GetBytes("{\"id\":8,\"method\":\"Runtime.enable\"}"), WebSocketMessageType.Text, true, CancellationToken.None);
using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
{
    var received = await socket.ReceiveAsync(buffer, timeout.Token);
    using var reply = JsonDocument.Parse(Encoding.UTF8.GetString(buffer, 0, received.Count));
    Check(reply.RootElement.GetProperty("id").GetInt32() == 8, "stale-document responses are ignored");
}
service.InvalidateTarget();
Check(!service.HasTarget && service.FrontendUri is null && (await http.GetStringAsync(new Uri(root, "json/list"))) == "[]",
    "navigation invalidation immediately hides the old target and frontend");
Check(targetChanges == 1, "navigation invalidation does not request an early frontend reload");
using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
    await ExpectWebSocketFailureAsync(async () => { await socket.ReceiveAsync(buffer, timeout.Token); }, "navigation invalidation aborts the old socket");
service.HandlePageMessage("{\"type\":\"ready\",\"documentId\":\"first-document\",\"url\":\"https://example.test/\",\"title\":\"Example\"}");
Check(service.HasTarget && targetChanges == 2, "a cancelled navigation can restore the same document and request reconnect");
using var restored = new ClientWebSocket();
await restored.ConnectAsync(websocketUri, CancellationToken.None);
documentId = "second-document";
service.HandlePageMessage("{\"type\":\"ready\",\"documentId\":\"second-document\",\"url\":\"https://example.test/next\",\"title\":\"Next\"}");
Check(targetChanges == 3, "navigation requests one frontend reconnect");
using var reconnected = new ClientWebSocket();
await reconnected.ConnectAsync(websocketUri, CancellationToken.None);
await reconnected.SendAsync(Encoding.UTF8.GetBytes("{\"id\":1,\"method\":\"Runtime.enable\"}"), WebSocketMessageType.Text, true, CancellationToken.None);
using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
{
    var received = await reconnected.ReceiveAsync(buffer, timeout.Token);
    using var reply = JsonDocument.Parse(Encoding.UTF8.GetString(buffer, 0, received.Count));
    Check(reply.RootElement.GetProperty("id").GetInt32() == 1, "navigation permits immediate reconnection without an old-session conflict");
}
var previousCommand = dispatches.Last(command => command.TryGetProperty("method", out _));
cleanupClient = previousCommand.GetProperty("__devduoClientId").GetString();
Check(previousCommand.GetProperty("__devduoDocumentId").GetString() == documentId && !string.IsNullOrEmpty(cleanupClient),
    "native command envelopes carry frontend and document generations");
await reconnected.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "frontend closed", CancellationToken.None);
var cleanupEnvelope = await cleanupObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
Check(cleanupEnvelope.GetProperty("__devduoDocumentId").GetString() == documentId &&
    cleanupEnvelope.GetProperty("__devduoClientId").GetString() == cleanupClient && !cleanupEnvelope.TryGetProperty("method", out _),
    "closing the frontend sends generation-qualified transient-mode cleanup");
using var successor = new ClientWebSocket();
await successor.ConnectAsync(websocketUri, CancellationToken.None);
await successor.SendAsync(Encoding.UTF8.GetBytes("{\"id\":1,\"method\":\"Runtime.enable\"}"), WebSocketMessageType.Text, true, CancellationToken.None);
using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
    await successor.ReceiveAsync(buffer, timeout.Token);
var successorCommand = dispatches.Last(command => command.TryGetProperty("method", out _));
Check(service.IsRunning && service.HasTarget && successorCommand.GetProperty("__devduoClientId").GetString() != cleanupClient,
    "a replacement frontend connects with a new generation while old cleanup is pending");
cleanupRelease.TrySetResult();
service.Stop();
Check(!service.IsRunning && service.BaseUri is null, "synchronous stop clears public endpoints");
var replacement = new TcpListener(IPAddress.Loopback, port);
replacement.Start();
replacement.Stop();
await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(service.Stop)).Append(service.StopAsync()));
Check(!service.HasTarget, "concurrent repeated stop and asynchronous disposal remain idempotent");
Check(errors.Count == 0, "normal shutdown emits no errors");
await service.StartAsync(port: port);
Check(service.BaseUri == root, "restarting preserves the configured connection address");
await service.StopAsync();
await CdpAuthenticationChecks.Run(assets, Check);
await ControlApiChecks.Run(assets, Check);
await ScreenshotChecks.Run(assets, Check);
Console.WriteLine("All CDP and control API transport checks passed. Native injection and actual chobitsu domain support require platform acceptance.");

static void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    Console.WriteLine("PASS " + name);
}

static async Task ExpectWebSocketFailureAsync(Func<Task> action, string name)
{
    try { await action(); }
    catch (WebSocketException) { Console.WriteLine("PASS " + name); return; }
    throw new InvalidOperationException(name);
}
