using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using System.Text;
using System.Text.Json;
using DevDuo;

namespace DevDuo.Windows.Tests;

internal sealed class NativeTabTitleServer : IDisposable
{
    private readonly HttpListener listener = new();
    private readonly TaskCompletionSource next = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal string Address { get; }

    internal NativeTabTitleServer(int port)
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
            var replacement = context.Request.Url!.AbsolutePath == "/next";
            if (replacement) await next.Task;
            var html = "<!doctype html><meta charset=utf-8><title>" + (replacement ? "Replacement document title" : "Initial title") +
                "</title><style>body{min-height:5000px;background:#dce}</style><h1>Native tab title acceptance</h1>";
            var body = Encoding.UTF8.GetBytes(html);
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.ContentLength64 = body.Length;
            await context.Response.OutputStream.WriteAsync(body);
            context.Response.Close();
        }
        catch (Exception error) when (error is HttpListenerException or ObjectDisposedException or IOException) { }
    }

    internal void ReleaseNext() => next.TrySetResult();
    public void Dispose() { ReleaseNext(); listener.Close(); }
}
