using System.Net;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace DevDuo.Windows.Tests;

internal sealed class BrowserTestServer : IDisposable
{
    internal const string Address = "http://127.0.0.1:18879/";
    internal const string LatencyDocument = "<!doctype html><title>Navigation latency</title><style>body{background:#bad;height:4000px}</style><h1>Inline without resources</h1>";
    internal sealed record ReceivedRequest(string Path, string? UserAgent, double ReceivedMilliseconds, double HeadersMilliseconds);
    internal readonly System.Collections.Concurrent.ConcurrentQueue<ReceivedRequest> Requests = new();
    private readonly bool latency;
    private readonly HttpListener listener = new();
    internal BrowserTestServer(bool latency = false)
    {
        this.latency = latency;
        listener.Prefixes.Add(Address);
        listener.Start();
        _ = Serve();
    }
    private async Task Serve()
    {
        try
        {
            while (listener.IsListening)
            {
                var request = await listener.GetContextAsync();
                _ = Respond(request);
            }
        }
        catch (Exception error) when (error is HttpListenerException or ObjectDisposedException) { }
    }
    private async Task Respond(HttpListenerContext request)
    {
        try
        {
            var received = System.Diagnostics.Stopwatch.GetTimestamp() * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            var path = request.Request.Url!.AbsolutePath;
            if (path == "/delay.js") await Task.Delay(2500);
            if (path == "/initial-edge.svg") await Task.Delay(2500);
            var edgeKind = request.Request.QueryString["kind"] == "mask" ? "mask" : "absolute";
            var html = path == "/desktop-input.html" ? File.ReadAllText("tests/DevDuo.WebKitProbe/pages/desktop-input.html") :
                path == "/screenshot.html" ? File.ReadAllText("tests/DevDuo.WebKitProbe/pages/screenshot.html") :
                path == "/delay.js" ? "window.delayedScriptLoaded=true" :
                path == "/initial-edge.svg" ? "<svg xmlns='http://www.w3.org/2000/svg' width='750' height='112'><path fill='white' d='M0 0H750V112H0Z'/></svg>" :
                path == "/initial-edge.html" ? "<!doctype html><meta charset=utf-8><title>Initial edge " + edgeKind + "</title>" +
                    "<style>html{font-size:100px;background:rgb(40,80,120)}html,body{margin:0;height:100%}" +
                    "#banner{position:absolute;bottom:0;left:0;width:100%;height:.56rem;background:white url('/initial-edge.svg?" + Guid.NewGuid().ToString("N") + "') 0 0/100% 100%}" +
                    "#mask{position:fixed;inset:0;background:black}</style><div id=banner></div>" +
                    (edgeKind == "mask" ? "<div id=mask></div>" : "") :
                "<!doctype html><meta charset=utf-8><title>Browser lifecycle acceptance</title><style>body{min-height:4000px;background:#cde}input{margin:30px}</style><h1 id='early'>Browser lifecycle acceptance</h1><input aria-label='Draft'><p>Persistent website storage and rotation.</p>" +
                (path == "/slow.html" ? "<script src='/delay.js'></script>" : "");
            var body = Encoding.UTF8.GetBytes(latency ? LatencyDocument : html);
            request.Response.ContentType = path == "/delay.js" ? "application/javascript" : path == "/initial-edge.svg" ? "image/svg+xml" : "text/html; charset=utf-8";
            request.Response.ContentLength64 = body.Length;
            request.Response.Headers["Cache-Control"] = "no-store";
            Requests.Enqueue(new(request.Request.RawUrl ?? "", request.Request.UserAgent, received,
                System.Diagnostics.Stopwatch.GetTimestamp() * 1000.0 / System.Diagnostics.Stopwatch.Frequency));
            await request.Response.OutputStream.WriteAsync(body);
            request.Response.Close();
        }
        catch (Exception error) when (error is HttpListenerException or ObjectDisposedException or IOException) { }
    }
    public void Dispose() => listener.Close();
}
