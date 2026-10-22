using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace DevDuo;

// Hosts the unmodified chii frontend and a page-scoped chobitsu CDP transport.
// This is a JavaScript protocol subset, not an engine debugging endpoint.
public sealed class CdpDebuggingService : IAsyncDisposable
{
    private const int MaximumMessageBytes = 16 * 1024 * 1024;
    private readonly Func<string, Task> _sendCommand;
    private readonly string _assetsDirectory;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly ConcurrentDictionary<TcpClient, Task> _connections = new();
    private readonly object _pageLock = new();
    private readonly object _shutdownLock = new();
    private CancellationTokenSource? _lifetime;
    private volatile TcpListener? _listener;
    private Task? _acceptTask;
    private ClientSession? _session;
    private HashSet<string> _allowedHosts = new(StringComparer.OrdinalIgnoreCase);
    private string _token = "";
    private string? _documentId;
    private string _pageUrl = "";
    private string _pageTitle = "DevDuo";
    private long _nextCommandId;

    public CdpDebuggingService(Func<string, Task> sendCommand, string? assetsDirectory = null)
    {
        _sendCommand = sendCommand;
        _assetsDirectory = assetsDirectory ?? Path.Combine(AppContext.BaseDirectory, "debugging");
    }

    public event Action<string>? Error;
    public event Action? TargetChanged;
    public bool IsRunning => _listener is not null;
    public int Port { get; private set; }
    public Uri? BaseUri { get; private set; }
    public bool HasTarget { get { lock (_pageLock) return IsRunning && _documentId is not null; } }
    public Uri? FrontendUri => HasTarget && BaseUri is { } root ? CreateFrontendUri(root.Authority) : null;
    public IReadOnlyList<Uri> AccessUris { get; private set; } = Array.Empty<Uri>();

    public static string CreateInjectionScript(string? assetsDirectory = null)
    {
        var directory = assetsDirectory ?? Path.Combine(AppContext.BaseDirectory, "debugging");
        var bootstrap = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "WebAssets", "Scripts", "remote-debugging.js"));
        var chobitsu = File.ReadAllText(Path.Combine(directory, "chobitsu.js"));
        return bootstrap.Replace("/* CHOBITSU */", chobitsu, StringComparison.Ordinal);
    }

    public async Task StartAsync(bool allowLan = false, int port = 0)
    {
        if (port is < 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_lifetime is not null) throw new InvalidOperationException("The CDP service is running or still stopping.");
            if (!File.Exists(Path.Combine(_assetsDirectory, "front_end", "chii_app.html")) ||
                !File.Exists(Path.Combine(_assetsDirectory, "chobitsu.js")))
                throw new FileNotFoundException("Offline debugging assets are missing. Run just prepare-debugging and rebuild the app.");
            _token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            _allowedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "127.0.0.1", "localhost" };
            if (allowLan)
            {
                foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (network.OperationalStatus != OperationalStatus.Up) continue;
                    foreach (var address in network.GetIPProperties().UnicastAddresses)
                        if (address.Address.AddressFamily == AddressFamily.InterNetwork)
                            _allowedHosts.Add(address.Address.ToString());
                }
            }
            lock (_shutdownLock)
            {
                var listener = new TcpListener(allowLan ? IPAddress.Any : IPAddress.Loopback, port);
                listener.Start(16);
                Port = ((IPEndPoint)listener.LocalEndpoint).Port;
                BaseUri = new Uri($"http://127.0.0.1:{Port}/{_token}/");
                AccessUris = _allowedHosts.Where(host => host != "localhost")
                    .OrderBy(host => host == "127.0.0.1" ? "" : host, StringComparer.Ordinal)
                    .Select(host => new Uri($"http://{host}:{Port}/{_token}/")).ToArray();
                _lifetime = new CancellationTokenSource();
                _listener = listener;
                _acceptTask = AcceptConnectionsAsync(listener, _lifetime.Token);
            }
        }
        finally { _lifecycle.Release(); }
    }

    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_lifetime is null) return;
            Stop();
            if (_acceptTask is not null) await _acceptTask.ConfigureAwait(false);
            await Task.WhenAll(_connections.Values).ConfigureAwait(false);
            _connections.Clear();
            lock (_shutdownLock)
            {
                _lifetime.Dispose();
                _lifetime = null;
            }
            _acceptTask = null;
            BaseUri = null;
            AccessUris = Array.Empty<Uri>();
            Port = 0;
            _token = "";
        }
        finally { _lifecycle.Release(); }
    }

    // Window disposal must release the port immediately without waiting for a
    // command dispatched to the UI thread. StopAsync subsequently joins workers.
    public void Stop()
    {
        lock (_shutdownLock)
        {
            _lifetime?.Cancel();
            _listener?.Stop();
            _listener = null;
            InvalidateTarget();
            foreach (var connection in _connections.Keys) connection.Dispose();
            BaseUri = null;
            AccessUris = Array.Empty<Uri>();
            Port = 0;
        }
    }

    // Navigation starts invalidate discovery before a replacement document is
    // ready. A cancelled navigation may later re-advertise the same document.
    public void InvalidateTarget()
    {
        lock (_pageLock)
        {
            var previous = _session;
            _session = null;
            _documentId = null;
            _pageUrl = "";
            _pageTitle = "DevDuo";
            previous?.Socket.Abort();
        }
    }

    public void HandlePageMessage(string envelope)
    {
        if (!IsRunning || envelope.Length > MaximumMessageBytes) return;
        try
        {
            var targetChanged = false;
            string? injectionError = null;
            using var parsed = JsonDocument.Parse(envelope);
            var root = parsed.RootElement;
            if (!root.TryGetProperty("type", out var type)) return;
            lock (_pageLock)
            {
                switch (type.GetString())
                {
                    case "ready":
                        var documentId = root.GetProperty("documentId").GetString();
                        if (string.IsNullOrEmpty(documentId)) return;
                        // The JavaScript backend is replaced on navigation. A new frontend
                        // connection must enable its domains against the new document.
                        if (documentId != _documentId)
                        {
                            targetChanged = true;
                            var previousSession = _session;
                            _session = null;
                            previousSession?.Socket.Abort();
                        }
                        _documentId = documentId;
                        _pageUrl = root.GetProperty("url").GetString() ?? "";
                        _pageTitle = root.GetProperty("title").GetString() ?? "DevDuo";
                        break;
                    case "cdp":
                        if (root.GetProperty("documentId").GetString() != _documentId) return;
                        var message = root.GetProperty("message").GetString();
                        if (message is not null && _session is { } session)
                        {
                            var response = JsonNode.Parse(message) as JsonObject;
                            if (response is null) return;
                            if (response["id"] is { } id)
                            {
                                // IDs are unique across frontend connections, so an old
                                // asynchronous response cannot reach a new debugger.
                                if (!id.AsValue().TryGetValue<long>(out var transportId) ||
                                    !session.Pending.TryRemove(transportId, out var originalId)) return;
                                response["id"] = originalId;
                                message = response.ToJsonString();
                            }
                            if (!session.Outgoing.Writer.TryWrite(message)) session.Socket.Abort();
                        }
                        break;
                    case "error":
                        injectionError = root.GetProperty("message").GetString() ?? "Page debugging injection failed.";
                        break;
                }
            }
            if (targetChanged) TargetChanged?.Invoke();
            if (injectionError is not null) Error?.Invoke(injectionError);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            Error?.Invoke("Invalid page debugging message: " + exception.Message);
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private async Task AcceptConnectionsAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                if (_connections.Count >= 64)
                {
                    client.Dispose();
                    continue;
                }
                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _connections[client] = completion.Task;
                _ = ServeAndReleaseAsync(client, completion, cancellationToken);
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
        {
            if (!cancellationToken.IsCancellationRequested) Error?.Invoke(exception.Message);
        }
    }

    private async Task ServeAndReleaseAsync(TcpClient client, TaskCompletionSource completion, CancellationToken cancellationToken)
    {
        try { await ServeAsync(client.GetStream(), cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is IOException or SocketException or WebSocketException or
                                          OperationCanceledException or ObjectDisposedException or JsonException or FormatException or DecoderFallbackException)
        {
            // Disconnects, rejected requests and shutdown are per-connection events.
        }
        finally
        {
            client.Dispose();
            completion.TrySetResult();
            _connections.TryRemove(client, out _);
        }
    }

    private async Task ServeAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        using var headerTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        headerTimeout.CancelAfter(TimeSpan.FromSeconds(5));
        var request = await ReadRequestAsync(stream, headerTimeout.Token).ConfigureAwait(false);
        if (request is null || !request.Headers.TryGetValue("Host", out var host) ||
            !Uri.TryCreate($"http://{host}/", UriKind.Absolute, out var authority) || authority.Port != Port ||
            authority.UserInfo.Length != 0 || !_allowedHosts.Contains(authority.Host))
        {
            await RespondAsync(stream, "400 Bad Request", "text/plain", "Invalid request.", cancellationToken).ConfigureAwait(false);
            return;
        }
        var prefix = $"/{_token}/";
        if (!request.Path.StartsWith(prefix, StringComparison.Ordinal))
        {
            await RespondAsync(stream, "404 Not Found", "text/plain", "Not found.", cancellationToken).ConfigureAwait(false);
            return;
        }
        var path = request.Path[prefix.Length..];
        if (path == "devtools/page/devduo")
        {
            await ServeWebSocketAsync(stream, request, authority, cancellationToken).ConfigureAwait(false);
            return;
        }
        if (path is "json" or "json/" or "json/list")
        {
            object[] targets;
            lock (_pageLock)
                targets = _documentId is null ? Array.Empty<object>() : new object[] { new
                {
                    id = "devduo", type = "page", title = _pageTitle, url = _pageUrl,
                    description = "chobitsu JavaScript CDP subset; engine debugging is unavailable.",
                    webSocketDebuggerUrl = $"ws://{authority.Authority}/{_token}/devtools/page/devduo",
                    devtoolsFrontendUrl = CreateFrontendUri(authority.Authority).PathAndQuery,
                } };
            await RespondAsync(stream, "200 OK", "application/json", JsonSerializer.Serialize(targets), cancellationToken).ConfigureAwait(false);
            return;
        }
        if (path == "json/version")
        {
            var version = new Dictionary<string, string>
            {
                ["Browser"] = "DevDuo/chobitsu-1.8.6", ["Protocol-Version"] = "1.3",
                ["User-Agent"] = "DevDuo page instrumentation", ["DevDuo-CDP-Scope"] = "JavaScript subset",
            };
            await RespondAsync(stream, "200 OK", "application/json", JsonSerializer.Serialize(version), cancellationToken).ConfigureAwait(false);
            return;
        }
        if (path.Length == 0)
        {
            // Keep application-owned strings out of a second UI. The frontend is chii's own UI.
            var location = CreateFrontendUri(authority.Authority).PathAndQuery;
            await WriteHeadersAsync(stream, $"302 Found\r\nLocation: {location}", "text/plain", 0, cancellationToken).ConfigureAwait(false);
            return;
        }
        if (path.StartsWith("front_end/", StringComparison.Ordinal) && IsSafeAssetPath(path))
        {
            var file = Path.Combine(_assetsDirectory, path.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(file))
            {
                await using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
                await WriteHeadersAsync(stream, "200 OK", ContentType(file), input.Length, cancellationToken).ConfigureAwait(false);
                await input.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
                return;
            }
        }
        await RespondAsync(stream, "404 Not Found", "text/plain", "Not found.", cancellationToken).ConfigureAwait(false);
    }

    private async Task ServeWebSocketAsync(NetworkStream stream, HttpRequest request, Uri authority, CancellationToken cancellationToken)
    {
        if (!request.Headers.TryGetValue("Upgrade", out var upgrade) || !upgrade.Equals("websocket", StringComparison.OrdinalIgnoreCase) ||
            !request.Headers.TryGetValue("Connection", out var connection) || !connection.Split(',').Any(value => value.Trim().Equals("Upgrade", StringComparison.OrdinalIgnoreCase)) ||
            !request.Headers.TryGetValue("Sec-WebSocket-Version", out var version) || version != "13" ||
            !request.Headers.TryGetValue("Sec-WebSocket-Key", out var key) || !IsWebSocketKey(key) ||
            (request.Headers.TryGetValue("Origin", out var origin) && origin != authority.GetLeftPart(UriPartial.Authority) &&
             origin != "devtools://devtools" && origin != "chrome-devtools://devtools"))
        {
            await RespondAsync(stream, "403 Forbidden", "text/plain", "WebSocket request rejected.", cancellationToken).ConfigureAwait(false);
            return;
        }
        var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
        ClientSession session;
        lock (_pageLock)
        {
            if (_session is not null || _documentId is null) session = null!;
            else
            {
                session = new ClientSession(WebSocket.CreateFromStream(stream, true, null, TimeSpan.FromSeconds(20)), _documentId);
                _session = session;
            }
        }
        if (session is null)
        {
            await RespondAsync(stream, "409 Conflict", "text/plain", "The page is not ready or already has a debugger.", cancellationToken).ConfigureAwait(false);
            return;
        }
        try
        {
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n"), cancellationToken).ConfigureAwait(false);
            using var sessionLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var sending = SendMessagesAsync(session, sessionLifetime.Token);
            try { await ReceiveCommandsAsync(session, sessionLifetime.Token).ConfigureAwait(false); }
            finally
            {
                sessionLifetime.Cancel();
                session.Socket.Abort();
                try { await sending.ConfigureAwait(false); }
                catch (Exception exception) when (exception is OperationCanceledException or WebSocketException) { }
            }
        }
        finally
        {
            lock (_pageLock) { if (ReferenceEquals(_session, session)) _session = null; }
            session.Outgoing.Writer.TryComplete();
            session.Socket.Dispose();
            if (!cancellationToken.IsCancellationRequested)
            {
                // The page checks both generations, so delayed cleanup cannot
                // cancel a newer frontend's inspect mode or touch another page.
                var cleanup = JsonSerializer.Serialize(new
                {
                    __devduoClientId = session.Id,
                    __devduoDocumentId = session.DocumentId,
                    __devduoCleanup = true,
                });
                try { await _sendCommand(cleanup).WaitAsync(cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
                catch (Exception exception)
                {
                    bool relevant;
                    lock (_pageLock) relevant = _documentId == session.DocumentId && _session is null;
                    if (relevant && !cancellationToken.IsCancellationRequested)
                        Error?.Invoke("Cannot clear CDP frontend modes: " + exception.Message);
                }
            }
        }
    }

    private async Task ReceiveCommandsAsync(ClientSession session, CancellationToken cancellationToken)
    {
        var buffer = new byte[65536];
        while (!cancellationToken.IsCancellationRequested && session.Socket.State == WebSocketState.Open)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult received;
            do
            {
                received = await session.Socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
                if (received.MessageType == WebSocketMessageType.Close) return;
                if (received.MessageType != WebSocketMessageType.Text || message.Length + received.Count > MaximumMessageBytes)
                    throw new WebSocketException("Unsupported or oversized CDP message.");
                message.Write(buffer, 0, received.Count);
            } while (!received.EndOfMessage);
            var text = new UTF8Encoding(false, true).GetString(message.GetBuffer(), 0, (int)message.Length);
            using var parsed = JsonDocument.Parse(text);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object ||
                !parsed.RootElement.TryGetProperty("method", out var method) || method.ValueKind != JsonValueKind.String ||
                !parsed.RootElement.TryGetProperty("id", out var originalId) || !originalId.TryGetInt64(out var id))
                throw new JsonException("CDP commands require a method and integer ID.");
            if (session.Pending.Count >= 4096) throw new WebSocketException("Too many pending CDP commands.");
            var transportId = Interlocked.Increment(ref _nextCommandId);
            session.Pending[transportId] = id;
            var command = JsonNode.Parse(text)!.AsObject();
            command["id"] = transportId;
            command["__devduoClientId"] = session.Id;
            command["__devduoDocumentId"] = session.DocumentId;
            command.Remove("__devduoCleanup");
            try { await _sendCommand(command.ToJsonString()).WaitAsync(cancellationToken).ConfigureAwait(false); }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Error?.Invoke("Cannot dispatch CDP command: " + exception.Message);
                return;
            }
        }
    }

    private static async Task SendMessagesAsync(ClientSession session, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var message in session.Outgoing.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                var data = Encoding.UTF8.GetBytes(message);
                if (data.Length > MaximumMessageBytes) throw new WebSocketException("Oversized CDP response.");
                await session.Socket.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
            }
        }
        finally { session.Socket.Abort(); }
    }

    private Uri CreateFrontendUri(string authority)
    {
        var endpoint = $"{authority}/{_token}/devtools/page/devduo";
        return new Uri($"http://{authority}/{_token}/front_end/chii_app.html?ws={Uri.EscapeDataString(endpoint)}");
    }

    private static bool IsSafeAssetPath(string path) => !path.Contains('\\') && !path.Contains(':') &&
        !path.Contains('%') && !path.Contains('\0') && path.Split('/').All(segment => segment is not ("." or "..") && segment.Length != 0);

    private static bool IsWebSocketKey(string value)
    {
        Span<byte> bytes = stackalloc byte[16];
        return Convert.TryFromBase64String(value, bytes, out var count) && count == 16;
    }

    private static async Task<HttpRequest?> ReadRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        // A deliberately narrow GET-only HTTP/1.1 surface: no bodies, pipelining,
        // proxies or keep-alive. Read exactly to CRLFCRLF before WebSocket handoff.
        var header = new byte[16384];
        var length = 0;
        while (length < header.Length)
        {
            if (await stream.ReadAsync(header.AsMemory(length, 1), cancellationToken).ConfigureAwait(false) == 0) return null;
            length++;
            if (length >= 4 && header[length - 4] == 13 && header[length - 3] == 10 && header[length - 2] == 13 && header[length - 1] == 10) break;
        }
        if (length == header.Length) return null;
        var lines = Encoding.ASCII.GetString(header, 0, length).Split("\r\n", StringSplitOptions.None);
        var first = lines[0].Split(' ');
        if (first.Length != 3 || first[0] != "GET" || first[2] != "HTTP/1.1" ||
            !first[1].StartsWith('/') || first[1].Contains('#')) return null;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1).Where(line => line.Length != 0))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0 || !headers.TryAdd(line[..colon], line[(colon + 1)..].Trim())) return null;
        }
        if (headers.ContainsKey("Transfer-Encoding") || headers.ContainsKey("Content-Length")) return null;
        var path = first[1].Split('?', 2)[0];
        return new HttpRequest(path, headers);
    }

    private static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8", ".js" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8", ".json" or ".map" => "application/json",
        ".svg" => "image/svg+xml", ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp", ".gif" => "image/gif", ".woff2" => "font/woff2", ".woff" => "font/woff",
        ".wasm" => "application/wasm", _ => "application/octet-stream",
    };

    private static async Task RespondAsync(NetworkStream stream, string status, string contentType, string text, CancellationToken cancellationToken)
    {
        var body = Encoding.UTF8.GetBytes(text);
        await WriteHeadersAsync(stream, status, contentType, body.Length, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
    }

    private static Task WriteHeadersAsync(NetworkStream stream, string status, string contentType, long length, CancellationToken cancellationToken)
    {
        var headers = $"HTTP/1.1 {status}\r\nContent-Type: {contentType}\r\nContent-Length: {length}\r\nConnection: close\r\nCache-Control: no-store\r\nReferrer-Policy: no-referrer\r\nX-Content-Type-Options: nosniff\r\n\r\n";
        return stream.WriteAsync(Encoding.ASCII.GetBytes(headers), cancellationToken).AsTask();
    }

    private sealed record HttpRequest(string Path, Dictionary<string, string> Headers);
    private sealed record ClientSession(WebSocket Socket, string DocumentId)
    {
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public ConcurrentDictionary<long, long> Pending { get; } = new();
        public Channel<string> Outgoing { get; } = Channel.CreateBounded<string>(new BoundedChannelOptions(256)
        {
            SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait,
        });
    }
}
