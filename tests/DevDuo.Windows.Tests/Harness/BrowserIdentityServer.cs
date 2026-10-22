using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace DevDuo.Windows.Tests;

internal sealed class BrowserIdentityServer : IDisposable
{
    internal sealed record Request(string Path, string? UserAgent);
    private readonly HttpListener listener = new();
    internal ConcurrentQueue<Request> Requests { get; } = new();
    internal string Address { get; }

    internal BrowserIdentityServer(int port)
    {
        Address = "http://127.0.0.1:" + port + "/";
        listener.Prefixes.Add(Address);
        listener.Start();
        _ = Serve();
    }

    private async Task Serve()
    {
        try
        {
            while (listener.IsListening) _ = Respond(await listener.GetContextAsync());
        }
        catch (Exception error) when (error is HttpListenerException or ObjectDisposedException) { }
    }

    private async Task Respond(HttpListenerContext context)
    {
        try
        {
            var path = context.Request.Url!.AbsolutePath.TrimStart('/');
            var userAgent = context.Request.UserAgent;
            Requests.Enqueue(new Request(path, userAgent));
            context.Response.Headers["Cache-Control"] = "no-store";
            if (path == "redirect")
            {
                context.Response.Redirect(Address + "page?redirect");
                context.Response.Close();
                return;
            }
            var content = path switch
            {
                "asset.js" => "window.identityAsset=true;",
                "fetch" => userAgent ?? "",
                _ => "<!doctype html><meta charset=utf-8><title>Mobile identity acceptance</title><style>body{min-height:4000px;background:#cde}</style>" +
                    "<h1>Mobile identity acceptance</h1><script src='/asset.js'></script><script>window.identityCase=location.search.slice(1);" +
                    "fetch('/fetch',{cache:'no-store'}).then(r=>r.text()).then(value=>window.identityFetchAgent=value)</script>"
            };
            var body = Encoding.UTF8.GetBytes(content);
            context.Response.ContentType = path == "asset.js" ? "application/javascript" : path == "fetch" ? "text/plain" : "text/html; charset=utf-8";
            context.Response.ContentLength64 = body.Length;
            await context.Response.OutputStream.WriteAsync(body);
            context.Response.Close();
        }
        catch (Exception error) when (error is HttpListenerException or ObjectDisposedException or IOException) { }
    }

    public void Dispose() => listener.Close();
}
