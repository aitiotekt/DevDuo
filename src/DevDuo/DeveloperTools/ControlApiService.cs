using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DevDuo;

// Owned by the host window, so changing a device/container does not restart the control listener.
// Kestrel owns HTTP parsing, framing and body limits; only native operations are sent to the UI thread.
public sealed class ControlApiService(Func<ControlApiRequest, CancellationToken, Task<object>> dispatch, string? assetsDirectory = null) : IAsyncDisposable
{
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private WebApplication? application;
    public Uri? BaseUri { get; private set; }
    public bool IsRunning => application is not null;

    public async Task StartAsync(int port, string bearerToken = "")
    {
        if (port is < 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        if (bearerToken.Length > 4096 || !bearerToken.All(c => char.IsAsciiLetterOrDigit(c) || "-._~+/=".Contains(c)))
            throw new ArgumentException("The API bearer token must use ASCII token characters.", nameof(bearerToken));
        await lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (application is not null) throw new InvalidOperationException("Control API is already running.");
            if (!File.Exists(Path.Combine(assetsDirectory ?? Path.Combine(AppContext.BaseDirectory, "debugging"), "openapi", "swagger-ui-bundle.js")))
                throw new FileNotFoundException("Offline Swagger UI assets are missing. Run just prepare-debugging and rebuild the app.");
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
            {
                Args = [], ApplicationName = typeof(ControlApiService).Assembly.GetName().Name,
                ContentRootPath = AppContext.BaseDirectory
            });
            // Desktop preferences exclusively own this listener, not shell ASPNETCORE_* configuration.
            builder.Configuration.Sources.Clear();
            builder.Logging.ClearProviders();
            builder.Services.AddSingleton<IHostLifetime, DesktopLifetime>();
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.AddServerHeader = false;
                options.Limits.MaxRequestBodySize = 64 * 1024;
                options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
                options.Listen(IPAddress.Loopback, port);
            });
            var next = builder.Build();
            next.Run(context => ServeAsync(context, bearerToken, next.Lifetime.ApplicationStopping));
            try
            {
                await next.StartAsync().ConfigureAwait(false);
                BaseUri = new Uri(next.Urls.Single() + "/");
                application = next;
            }
            catch
            {
                await next.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally { lifecycle.Release(); }
    }

    public async Task StopAsync()
    {
        await lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (application is not { } previous) return;
            application = null;
            BaseUri = null;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await previous.StopAsync(timeout.Token).ConfigureAwait(false); }
            finally { await previous.DisposeAsync().ConfigureAwait(false); }
        }
        finally { lifecycle.Release(); }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private async Task ServeAsync(HttpContext context, string token, CancellationToken stopping)
    {
        var request = context.Request;
        var response = context.Response;
        response.Headers.CacheControl = "no-store";
        response.Headers.XContentTypeOptions = "nosniff";
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(request.HttpContext.RequestAborted, stopping);
        try
        {
            var port = context.Connection.LocalPort;
            var authority = $"http://{request.Host}";
            if (request.Host.Port != port || request.Host.Host is not ("127.0.0.1" or "localhost"))
                throw new ControlApiException(403, "Unknown Host.");
            if (request.Headers.TryGetValue("Origin", out var origin) && origin != authority)
                throw new ControlApiException(403, "Cross-origin browser access is not allowed.");
            var path = request.Path.Value ?? "";
            // Documentation contains only the public API contract. Keeping it readable permits
            // Swagger's Authorize dialog without putting the configured credential in a URL.
            var documentation = path is "/api/openapi" or "/api/openapi.json" || path.StartsWith("/api/openapi-assets/", StringComparison.Ordinal);
            if (!documentation && token.Length > 0)
            {
                var header = request.Headers.Authorization.ToString();
                if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ||
                    !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(header[7..]), Encoding.UTF8.GetBytes(token)))
                {
                    response.Headers.WWWAuthenticate = "Bearer";
                    throw new ControlApiException(401, "Authentication required.");
                }
            }
            var method = documentation ? "GET" : path switch
            {
                "/api/state" => "GET",
                "/api/settings" => "PATCH",
                "/api/navigation" => "POST",
                "/api/screenshot" => "POST",
                "/api/inspector" or "/api/proxy" => "PUT",
                _ => throw new ControlApiException(404, "Unknown API resource.")
            };
            if (request.Method != method)
            {
                response.Headers.Allow = method;
                throw new ControlApiException(405, "Method not allowed.");
            }
            if (documentation && path != "/api/openapi.json")
            {
                var assets = assetsDirectory ?? Path.Combine(AppContext.BaseDirectory, "debugging");
                var (file, contentType) = path switch
                {
                    "/api/openapi" => (Path.Combine(AppContext.BaseDirectory, "WebAssets", "Pages", "control-api.html"), "text/html; charset=utf-8"),
                    "/api/openapi-assets/control-api.js" => (Path.Combine(AppContext.BaseDirectory, "WebAssets", "Scripts", "control-api.js"), "text/javascript; charset=utf-8"),
                    "/api/openapi-assets/swagger-ui-bundle.js" => (Path.Combine(assets, "openapi", "swagger-ui-bundle.js"), "text/javascript; charset=utf-8"),
                    "/api/openapi-assets/swagger-ui.css" => (Path.Combine(assets, "openapi", "swagger-ui.css"), "text/css; charset=utf-8"),
                    _ => throw new ControlApiException(404, "Unknown documentation asset.")
                };
                if (!File.Exists(file)) throw new ControlApiException(503, "Offline API documentation asset is missing; rebuild the application.");
                response.Headers.ContentSecurityPolicy = "default-src 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; connect-src 'self'; frame-ancestors 'none'";
                response.ContentType = contentType;
                await response.SendFileAsync(file, lifetime.Token).ConfigureAwait(false);
                return;
            }
            if (path == "/api/openapi.json")
            {
                await response.WriteAsJsonAsync(OpenApi(authority, token.Length > 0), ControlApiJson.Options, lifetime.Token).ConfigureAwait(false);
                return;
            }
            JsonElement body = default;
            if (method != "GET")
            {
                if (!request.HasJsonContentType()) throw new ControlApiException(415, "Use application/json.");
                using var document = await JsonDocument.ParseAsync(request.Body, new JsonDocumentOptions { MaxDepth = 32 }, lifetime.Token).ConfigureAwait(false);
                if (document.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("Expected a JSON object.");
                var fields = new HashSet<string>(StringComparer.Ordinal);
                foreach (var field in document.RootElement.EnumerateObject())
                    if (!fields.Add(field.Name)) throw new ArgumentException("Duplicate field: " + field.Name);
                body = document.RootElement.Clone();
            }
            var result = await dispatch(new ControlApiRequest(method, path, body), lifetime.Token).ConfigureAwait(false);
            if (result is ScreenshotImage image)
            {
                response.ContentType = image.ContentType;
                response.Headers["X-DevDuo-Width"] = image.Width.ToString(System.Globalization.CultureInfo.InvariantCulture);
                response.Headers["X-DevDuo-Height"] = image.Height.ToString(System.Globalization.CultureInfo.InvariantCulture);
                response.Headers["X-DevDuo-Target"] = image.Target;
                response.Headers["X-DevDuo-Pixel-Scale"] = image.PixelScale.ToString(System.Globalization.CultureInfo.InvariantCulture);
                await response.Body.WriteAsync(image.Data, lifetime.Token).ConfigureAwait(false);
                return;
            }
            await response.WriteAsJsonAsync(result, ControlApiJson.Options, lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (ControlApiException error) { await ErrorAsync(response, error.Status, error.Message).ConfigureAwait(false); }
        catch (JsonException) { await ErrorAsync(response, 400, "Invalid JSON body or field type.").ConfigureAwait(false); }
        catch (ArgumentException error) { await ErrorAsync(response, 400, error.Message).ConfigureAwait(false); }
        catch (BadHttpRequestException error) { await ErrorAsync(response, error.StatusCode, "Invalid or oversized request.").ConfigureAwait(false); }
        catch (Exception) { await ErrorAsync(response, 500, "The native operation failed; inspect application diagnostics.").ConfigureAwait(false); }
    }

    private static Task ErrorAsync(HttpResponse response, int status, string message)
    {
        response.StatusCode = status;
        return response.WriteAsJsonAsync(new { error = message }, ControlApiJson.Options);
    }

    private static JsonNode OpenApi(string authority, bool authenticated)
    {
        using var source = typeof(ControlApiService).Assembly.GetManifestResourceStream("DevDuo.ControlApi.OpenApi.json")!;
        var document = JsonNode.Parse(source)!;
        document["servers"] = new JsonArray(new JsonObject { ["url"] = authority });
        document["security"] = authenticated
            ? new JsonArray(new JsonObject { ["bearerAuth"] = new JsonArray() }) : new JsonArray();
        return document;
    }

    // A desktop service must not install console signal handlers or own the application lifetime.
    private sealed class DesktopLifetime : IHostLifetime
    {
        public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
