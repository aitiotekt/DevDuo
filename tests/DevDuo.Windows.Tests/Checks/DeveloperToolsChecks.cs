using System.Collections.Concurrent;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace DevDuo.Windows.Tests;

internal sealed partial class WindowsChecks
{
    private async Task CheckDeveloperTools()
    {
        using var pageServer = new BrowserTestServer();
        await OpenBrowserTestPage();
        var browser = Web.WindowsPanel!.Browser;
        var identity = await Web.EvaluateResultAsync("JSON.stringify([performance.timeOrigin,location.href,history.length])");

        // Exercise the real native listener, including an already accepted frontend.
        var port = AvailableDeveloperPort();
        browser.StartRemoteInspection("127.0.0.1", port);
        Check(browser.RemoteInspection is { Enabled: true, Port: var bound } && bound == port, "Native inspection reports the bound socket port.");
        using (var client = new TcpClient())
        {
            await client.ConnectAsync(IPAddress.Loopback, port);
            Check(client.Connected, "A real client connects to the native inspector listener.");
            var stream = client.GetStream();
            await WriteNativeInspectorMessage(stream, new { @event = "SetupInspectorClient" });
            var listing = await ReadNativeInspectorMessage(stream, value =>
            {
                if (value.GetProperty("event").GetString() != "SetTargetList") return false;
                using var listed = JsonDocument.Parse(value.GetProperty("message").GetString()!);
                return listed.RootElement.EnumerateArray().Any(target => target.GetProperty("type").GetString() == "web-page" &&
                    target.GetProperty("url").GetString() == BrowserTestServer.Address);
            });
            using var targets = JsonDocument.Parse(listing.GetProperty("message").GetString()!);
            var pages = targets.RootElement.EnumerateArray().Where(value => value.GetProperty("type").GetString() == "web-page").ToArray();
            Check(pages.Length > 0, "The native inspector exposes a real page target.");
            var target = pages.First(value => value.GetProperty("url").GetString() == BrowserTestServer.Address);
            // The pinned socket protocol serializes its uint32 identifier through
            // JSON::setInteger, so values above INT_MAX appear signed on the wire.
            var connection = listing.GetProperty("connectionID").GetInt64();
            var targetId = target.GetProperty("targetID").GetUInt64();
            await WriteNativeInspectorMessage(stream, new { @event = "Setup", connectionID = connection, targetID = targetId });
            var created = await ReadNativeInspectorMessage(stream, value =>
            {
                if (value.GetProperty("event").GetString() != "SendMessageToFrontend") return false;
                using var protocol = JsonDocument.Parse(value.GetProperty("message").GetString()!);
                return protocol.RootElement.TryGetProperty("method", out var method) && method.GetString() == "Target.targetCreated" &&
                    protocol.RootElement.GetProperty("params").GetProperty("targetInfo").GetProperty("type").GetString() == "page";
            });
            using var pageTarget = JsonDocument.Parse(created.GetProperty("message").GetString()!);
            var pageTargetId = pageTarget.RootElement.GetProperty("params").GetProperty("targetInfo").GetProperty("targetId").GetString();
            await WriteNativeInspectorMessage(stream, new
            {
                @event = "SendMessageToBackend", connectionID = connection, targetID = targetId,
                // WK2 exposes a multiplexing web-page target. Runtime belongs to
                // its page subtarget, exactly as the native Inspector frontend routes it.
                message = JsonSerializer.Serialize(new
                {
                    id = 100, method = "Target.sendMessageToTarget", @params = new
                    {
                        targetId = pageTargetId,
                        message = JsonSerializer.Serialize(new { id = 101, method = "Runtime.evaluate", @params = new { expression = "window.nativeRemoteSentinel='native-roundtrip';document.title", returnByValue = true } }),
                    },
                }),
            });
            var response = await ReadNativeInspectorMessage(stream, value =>
            {
                if (value.GetProperty("event").GetString() != "SendMessageToFrontend") return false;
                using var outer = JsonDocument.Parse(value.GetProperty("message").GetString()!);
                if (!outer.RootElement.TryGetProperty("method", out var method) || method.GetString() != "Target.dispatchMessageFromTarget") return false;
                using var reply = JsonDocument.Parse(outer.RootElement.GetProperty("params").GetProperty("message").GetString()!);
                return reply.RootElement.TryGetProperty("id", out var id) && id.GetInt32() == 101;
            });
            using var dispatched = JsonDocument.Parse(response.GetProperty("message").GetString()!);
            using var evaluated = JsonDocument.Parse(dispatched.RootElement.GetProperty("params").GetProperty("message").GetString()!);
            File.WriteAllText(Path.Combine(Program.Output, "developer-native-runtime-reply.json"), evaluated.RootElement.GetRawText());
            Check(evaluated.RootElement.TryGetProperty("result", out _), "Native Runtime.evaluate succeeded: " + evaluated.RootElement.GetRawText());
            Check(evaluated.RootElement.GetProperty("result").GetProperty("result").GetProperty("value").GetString() == "Browser lifecycle acceptance",
                "Native Runtime.evaluate returns the actual WKView document title.");
            Check(await Web.EvaluateResultAsync("window.nativeRemoteSentinel") == "native-roundtrip", "Native inspector evaluation changes the actual page.");
            var rejected = false;
            try { browser.SetInspectorVisible(true); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Local Inspector reports its conflict with native remote inspection.");
            browser.StopRemoteInspection();
            Check(!browser.RemoteInspection.Enabled, "Stop reports the real native listener as closed.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                var remaining = new byte[4096];
                while (await stream.ReadAsync(remaining, timeout.Token) != 0) { }
                Check(true, "Stop closes an already accepted native inspector connection.");
            }
            catch (IOException) { Check(true, "Stop resets an already accepted native inspector connection."); }
        }
        using (var probe = new TcpClient())
        {
            var refused = false;
            try { await probe.ConnectAsync(IPAddress.Loopback, port); }
            catch (SocketException) { refused = true; }
            Check(refused, "Stopped native inspector no longer accepts connections.");
        }
        browser.StartRemoteInspection("127.0.0.1", port);
        browser.StopRemoteInspection();
        browser.SetInspectorVisible(true);
        await Wait(_ => browser.InspectorVisible, "Local Inspector after native remote stop");
        var localConflict = false;
        try { browser.StartRemoteInspection("127.0.0.1", port); }
        catch (InvalidOperationException) { localConflict = true; }
        Check(localConflict, "Native remote inspection reports its conflict with local Inspector.");
        browser.CloseInspector();
        await Wait(_ => !browser.InspectorVisible, "Local Inspector close");
        Check(await Web.EvaluateResultAsync("JSON.stringify([performance.timeOrigin,location.href,history.length])") == identity,
            "Native inspector start, stop and local Inspector preserve the current document and history.");
        Console.WriteLine("PASS native WIP: current target discovery, page-subtarget Runtime.evaluate, accepted-client stop, restart and local Inspector conflicts.");

        await CheckDeveloperCdp();
        await CheckDeveloperProxy();
        await CheckDeveloperHttpsProxyEntry();
        Save("developer-tools", State());
        Console.WriteLine("PASS developer tools: real native listener lifecycle, Inspector conflicts, CDP DOM/rotation and HTTP/SOCKS5 proxy routing.");
    }

    private static int AvailableDeveloperPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task WriteNativeInspectorMessage(NetworkStream stream, object message)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, bytes.Length);
        await stream.WriteAsync(header);
        await stream.WriteAsync(bytes);
    }

    private static async Task<JsonElement> ReadNativeInspectorMessage(NetworkStream stream, Func<JsonElement, bool> accepted)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (true)
        {
            var header = new byte[4];
            await stream.ReadExactlyAsync(header, timeout.Token);
            var length = BinaryPrimitives.ReadInt32BigEndian(header);
            if (length is < 1 or > 16 * 1024 * 1024) throw new InvalidDataException("Invalid native inspector frame size.");
            var payload = new byte[length];
            await stream.ReadExactlyAsync(payload, timeout.Token);
            using var parsed = JsonDocument.Parse(payload);
            if (accepted(parsed.RootElement)) return parsed.RootElement.Clone();
        }
    }

    private async Task CheckDeveloperCdp()
    {
        var controller = Web.DeveloperTools;
        controller.ApplyCdp(true, new(false, AvailableDeveloperPort()));
        await Wait(_ => controller.CdpStatus.Enabled || controller.CdpStatus.Error is not null, "CDP native injection");
        Check(controller.CdpStatus.Enabled, "CDP starts against the real native page: " + controller.CdpStatus.Error);
        using var http = new HttpClient();
        var discovery = new Uri(controller.CdpFrontendUrl!, "../json/list");
        var socketAddress = await WaitForDeveloperTarget(http, discovery, BrowserTestServer.Address);
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(socketAddress, CancellationToken.None);
        var document = await DeveloperCdpCommand(socket, 1, "DOM.getDocument", new { depth = 1 });
        Check(!document.TryGetProperty("error", out _), "DOM.getDocument is handled by the real page bridge.");
        var root = document.GetProperty("result").GetProperty("root").GetProperty("nodeId").GetInt32();
        Check(root > 0, "CDP exposes the actual native document root.");
        var search = await DeveloperCdpCommand(socket, 2, "DOM.performSearch", new { query = "input" });
        Check(search.GetProperty("result").GetProperty("resultCount").GetInt32() > 0, "CDP searches the actual native DOM.");
        var selected = await DeveloperCdpCommand(socket, 7, "DOM.getSearchResults", new
        {
            searchId = search.GetProperty("result").GetProperty("searchId").GetString(), fromIndex = 0, toIndex = 1,
        });
        var input = selected.GetProperty("result").GetProperty("nodeIds")[0].GetInt32();
        var markup = await DeveloperCdpCommand(socket, 8, "DOM.getOuterHTML", new { nodeId = input });
        Check(markup.GetProperty("result").GetProperty("outerHTML").GetString()!.StartsWith("<input", StringComparison.Ordinal),
            "CDP reads an actual input node from WKView through the supported chobitsu DOM subset.");
        var changed = await DeveloperCdpCommand(socket, 3, "Runtime.evaluate", new
        {
            expression = "document.querySelector('input').value='cdp-draft';window.cdpNativeSentinel='retained';JSON.stringify([performance.timeOrigin,history.length,location.href])",
            returnByValue = true,
        });
        var identity = changed.GetProperty("result").GetProperty("result").GetProperty("value").GetString();
        Check(await Web.EvaluateResultAsync("document.querySelector('input').value") == "cdp-draft", "CDP evaluation changes the native DOM.");
        var original = Web.WindowsPanel!.Browser;
        Rotate();
        await Task.Delay(350);
        Check(ReferenceEquals(original, Web.WindowsPanel!.Browser), "Rotation retains the native view while a CDP client is connected.");
        var after = await DeveloperCdpCommand(socket, 4, "Runtime.evaluate", new
        {
            expression = "JSON.stringify([performance.timeOrigin,history.length,location.href])",
            returnByValue = true,
        });
        Check(after.GetProperty("result").GetProperty("result").GetProperty("value").GetString() == identity,
            "The same CDP connection survives rotation with document identity and history intact.");
        Check(await Web.EvaluateResultAsync("window.cdpNativeSentinel+':'+document.querySelector('input').value") == "retained:cdp-draft",
            "Rotation preserves DOM changes made through CDP.");

        var nextUrl = BrowserTestServer.Address + "?cdp-navigation=next";
        Web.Navigate(nextUrl);
        await WaitForScript("location.href", value => value == nextUrl, "CDP next document navigation");
        using (var nextSocket = new ClientWebSocket())
        {
            await nextSocket.ConnectAsync(await WaitForDeveloperTarget(http, discovery, nextUrl), CancellationToken.None);
            var next = await DeveloperCdpCommand(nextSocket, 5, "Runtime.evaluate", new { expression = "location.href", returnByValue = true });
            Check(next.GetProperty("result").GetProperty("result").GetProperty("value").GetString() == nextUrl,
                "The document-start script exposes the actual replacement document through a new CDP connection.");
        }
        var replacementPort = AvailableDeveloperPort();
        controller.ApplyCdp(false, new(false, replacementPort));
        controller.ApplyCdp(true, new(false, replacementPort));
        await Wait(_ => controller.CdpStatus.Enabled || controller.CdpStatus.Error is not null, "Rapid CDP stop/start");
        Check(controller.CdpStatus.Enabled, "A previous asynchronous stop does not remove the replacement CDP script.");
        discovery = new Uri(controller.CdpFrontendUrl!, "../json/list");
        await WaitForDeveloperTarget(http, discovery, nextUrl);
        Web.GoBack();
        await WaitForScript("location.href", value => value == BrowserTestServer.Address, "CDP history return");
        using (var restoredSocket = new ClientWebSocket())
        {
            await restoredSocket.ConnectAsync(await WaitForDeveloperTarget(http, discovery, BrowserTestServer.Address), CancellationToken.None);
            var restored = await DeveloperCdpCommand(restoredSocket, 6, "Runtime.evaluate", new
            {
                expression = "JSON.stringify({url:location.href,timeOrigin:performance.timeOrigin,marker:window.cdpNativeSentinel??null})",
                returnByValue = true,
            });
            using var returned = JsonDocument.Parse(restored.GetProperty("result").GetProperty("result").GetProperty("value").GetString()!);
            Check(returned.RootElement.GetProperty("url").GetString() == BrowserTestServer.Address,
                "The current script epoch reconnects history-return documents and executes real CDP commands.");
            var cacheHit = returned.RootElement.GetProperty("marker").GetString() == "retained";
            File.WriteAllText(Path.Combine(Program.Output, "developer-cdp-history.json"), JsonSerializer.Serialize(new
            {
                observedBackForwardCacheHit = cacheHit,
                actual = returned.RootElement,
                qualification = "A history return without preserved sentinel does not establish BFCache acceptance.",
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        controller.ApplyCdp(false, new(false, 9223));
        await Wait(_ => !controller.CdpStatus.Enabled && !controller.CdpStatus.Starting, "CDP stop");
        await WaitForScript("typeof window.__devduoPostDeveloperMessage", value => value == "undefined", "CDP removes the current-page bridge on stop");
        Console.WriteLine("PASS CDP: real DOM search/read/write, rotation, navigation, rapid stop/start and history return with recorded BFCache evidence.");
    }

    private static async Task<Uri> WaitForDeveloperTarget(HttpClient http, Uri discovery, string expectedUrl)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            using var targets = JsonDocument.Parse(await http.GetStringAsync(discovery));
            foreach (var target in targets.RootElement.EnumerateArray())
                if (target.GetProperty("url").GetString() == expectedUrl)
                    return new Uri(target.GetProperty("webSocketDebuggerUrl").GetString()!);
            await Task.Delay(100);
        }
        throw new TimeoutException("CDP target for the current native document was not discovered.");
    }

    private static async Task<JsonElement> DeveloperCdpCommand(ClientWebSocket socket, int id, string method, object parameters)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var command = JsonSerializer.SerializeToUtf8Bytes(new { id, method, @params = parameters });
        await socket.SendAsync(command, WebSocketMessageType.Text, true, timeout.Token);
        var buffer = new byte[8192];
        while (true)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult received;
            do
            {
                received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
                if (received.MessageType == WebSocketMessageType.Close) throw new IOException("CDP closed before replying to " + method);
                message.Write(buffer, 0, received.Count);
            } while (!received.EndOfMessage);
            using var parsed = JsonDocument.Parse(message.ToArray());
            if (parsed.RootElement.TryGetProperty("id", out var actual) && actual.GetInt32() == id)
                return parsed.RootElement.Clone();
        }
    }

    private async Task CheckDeveloperProxy()
    {
        var controller = Web.DeveloperTools;
        foreach (var socks in new[] { false, true })
        {
            using var proxy = new DeveloperProxyProbe(socks);
            var identity = await Web.EvaluateResultAsync("String(performance.timeOrigin)");
            var scheme = socks ? "socks5" : "http";
            controller.ApplyProxy(new("custom", scheme + "://127.0.0.1:" + proxy.Port));
            Check(await Web.EvaluateResultAsync("String(performance.timeOrigin)") == identity, "Changing proxy settings preserves the current native document.");
            // SOCKS5 uses local DNS in the curl backend; a numeric destination
            // proves routing without requiring DNS or a real origin listener.
            var destination = socks ? "127.0.0.1" : "proxy-route.invalid";
            var route = "http://" + destination + ":18089/devduo-" + scheme;
            Web.Navigate(route);
            try
            {
                await WaitForScript("location.href", value => value == route, scheme + " actual destination document commit");
                await WaitForScript("document.title", value => value == "Native proxy acceptance", scheme + " native proxy response");
                await WaitForScript("document.documentElement.dataset.proxyFetch", value => value == "ok", scheme + " native fetch through proxy");
            }
            finally
            {
                File.WriteAllText(Path.Combine(Program.Output, "developer-proxy-" + scheme + ".json"), JsonSerializer.Serialize(new
                {
                    requests = proxy.Requests.ToArray(), navigation = Web.Navigation,
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
            Check(proxy.Requests.Any(value => value.Contains(destination, StringComparison.Ordinal)),
                scheme + " proxy observes the native browser destination without external network access.");
            await Wait(_ => proxy.Requests.Any(value => value.Contains("/asset-proof", StringComparison.Ordinal)), scheme + " subresource through proxy");
            Check(proxy.Requests.Any(value => value.Contains("/fetch-proof", StringComparison.Ordinal)), scheme + " native fetch reaches proxy.");
            controller.ApplyProxy(new("direct", null));
            var count = proxy.Requests.Count;
            await OpenBrowserTestPage();
            await WaitForScript("location.href", value => value == BrowserTestServer.Address, "Actual direct-mode document commit");
            await WaitForScript("document.title", value => value == "Browser lifecycle acceptance", "Actual direct-mode document completion");
            await Task.Delay(200);
            Check(proxy.Requests.Count == count, "Direct mode bypasses the prior " + scheme + " proxy.");
        }
        using (var origin = new DeveloperProxyProbe(false))
        {
            controller.ApplyProxy(new("custom", "http://127.0.0.1:" + AvailableDeveloperPort()));
            Web.Navigate("http://127.0.0.1:" + origin.Port + "/fail-closed");
            await Wait(_ => Web.Navigation.Error is not null, "Unavailable custom proxy reports a real load failure");
            Check(origin.Requests.IsEmpty, "A failed custom proxy does not silently fall back to direct origin access.");
            controller.ApplyProxy(new("direct", null));
            Web.Navigate("http://127.0.0.1:" + origin.Port + "/direct-proof");
            await Wait(_ => !origin.Requests.IsEmpty, "The same origin is reachable in direct mode");
        }
        controller.ApplyProxy(new("system", null));
    }

    private async Task CheckDeveloperHttpsProxyEntry()
    {
        using var proxy = new DeveloperTlsProxyProbe();
        var controller = Web.DeveloperTools;
        controller.ApplyProxy(new("custom", "https://127.0.0.1:" + proxy.Port));
        Web.Navigate("http://proxy-route.invalid/https-entry");
        await Wait(_ => proxy.ClientHelloObserved && Web.Navigation.Error is not null, "HTTPS proxy TLS entry and untrusted certificate rejection");
        Check(proxy.ClientHelloObserved, "An HTTPS proxy receives a real native TLS ClientHello.");
        Check(Web.Navigation.Error is not null, "The native load rejects the untrusted test proxy without changing certificate trust.");
        controller.ApplyProxy(new("system", null));
        await OpenBrowserTestPage();
        // This checks TLS entry and failure, not a fully trusted HTTPS proxy chain.
    }
}
