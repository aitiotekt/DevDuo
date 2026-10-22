using System.Diagnostics;
using System.Text.Json;
using System.Drawing.Imaging;

namespace DevDuo.Windows.Tests;

internal sealed partial class WindowsChecks
{
    private async Task CheckNavigationLatency()
    {
        await Wait(state => (Program.NavigationLatencyInline ? Web.Navigation.IsLocalFixture : Text(state, "pageUrl", BrowserTestServer.Address)) && !Web.Navigation.IsLoading &&
            state.TryGetProperty("pageDocumentId", out var id) && id.ValueKind == JsonValueKind.String, "Cold first local document");
        if (Program.NavigationNetworkRetry)
        {
            var interruption = await Program.NetworkRetryTask!;
            var childrenAfter = Program.NavigationSentinel!.FindChildren();
            Save("navigation-network-retry", JsonSerializer.SerializeToElement(new { interruption, childrenAfter, host = State(), requests = Program.NavigationServer!.Requests.ToArray() }));
            Check(childrenAfter.Any(child => child.ProcessId == interruption.WebProcessId && child.Executable == "WebKitWebProcess.exe"),
                "The same first WebProcess survives the initial NetworkProcess interruption.");
            Check(childrenAfter.Any(child => child.Executable == "WebKitNetworkProcess.exe" && child.ProcessId != interruption.KilledNetworkProcessId),
                "A replacement NetworkProcess completes the first real HTTP document within the bounded wait.");
            Console.WriteLine("PASS first HTTP document after interrupting the owned initial NetworkProcess; original WebProcess retained.");
            return;
        }
        Save("navigation-cold-" + (Program.NavigationLatencyInline ? "inline" : "http"), State());
        var requestMilliseconds = Web.WindowsPanel!.Browser.LastNavigationRequestTimestamp * 1000.0 / Stopwatch.Frequency;
        Save("navigation-cold-server", JsonSerializer.SerializeToElement(Program.NavigationServer!.Requests.Select(request => new {
            request.Path, request.UserAgent, request.ReceivedMilliseconds, request.HeadersMilliseconds, requestMilliseconds,
            requestToServerMilliseconds = request.ReceivedMilliseconds - requestMilliseconds,
            requestToHeadersMilliseconds = request.HeadersMilliseconds - requestMilliseconds
        }).ToArray()));
        Check(!Program.NavigationLatencyInline || Program.NavigationServer!.Requests.IsEmpty,
            "Cold inline load has no HTTP predecessor or resource requests.");
        Save("navigation-processes", JsonSerializer.SerializeToElement(Program.NavigationSentinel!.FindChildren()));
        var coldPaintAccepted = Program.NavigationLatencyInline || await CaptureColdNavigationPaint();
        Save("navigation-cold-dom", JsonSerializer.SerializeToElement(new { result = await Web.EvaluateResultAsync(
            "JSON.stringify({ua:navigator.userAgent,ready:document.readyState,resources:performance.getEntriesByType('resource').map(e=>e.toJSON()),navigation:performance.getEntriesByType('navigation').map(e=>e.toJSON())})") }));
        Check(coldPaintAccepted,
            "The real HTTP document's visible source pixels match its background within 1.5 seconds of the native request.");
        var children = Program.NavigationSentinel!.InspectChildren();
        Save("navigation-inheritance", JsonSerializer.SerializeToElement(children));
        Check(children.Any(child => child.Executable == "WebKitWebProcess.exe") && children.Any(child => child.Executable == "WebKitGPUProcess.exe"),
            "Inspect the actual launched WebProcess and GPU process.");
        Check(children.All(child => !child.InheritedSentinel), "The launcher handle allowlist excludes an unrelated explicitly inheritable event from every child.");
        CheckLocalNavigationTimings(State());
        Console.WriteLine("LATENCY cold-" + (Program.NavigationLatencyInline ? "inline " : "http ") + State().GetProperty("webKit").GetProperty("timings"));
        for (var index = 0; index < 4; index++)
        {
            var name = "Navigation latency " + index;
            var previous = State().GetProperty("pageDocumentId").GetString();
            var processesBefore = Program.NavigationSentinel!.FindChildren();
            var clock = Stopwatch.StartNew();
            if (index % 2 == 0)
                Web.WindowsPanel!.ShowHtml(BrowserTestServer.LatencyDocument);
            else Web.Navigate(BrowserTestServer.Address + "?latency=" + index);
            var callMilliseconds = clock.Elapsed.TotalMilliseconds;
            var maxQueueDelay = 0.0;
            var completed = false;
            while (clock.Elapsed < TimeSpan.FromSeconds(20))
            {
                var before = clock.Elapsed.TotalMilliseconds;
                await Task.Delay(10);
                maxQueueDelay = Math.Max(maxQueueDelay, clock.Elapsed.TotalMilliseconds - before - 10);
                var state = State();
                if (!Web.Navigation.IsLoading && state.TryGetProperty("pageDocumentId", out var id) &&
                    id.ValueKind == JsonValueKind.String && id.GetString() != previous)
                {
                    completed = true;
                    break;
                }
            }
            var completionMilliseconds = clock.Elapsed.TotalMilliseconds;
            if (!completed)
            {
                var peek = await Web.EvaluateResultAsync("JSON.stringify({observation:window.devduoShellObservation,ready:document.readyState})").WaitAsync(TimeSpan.FromSeconds(3));
                Save("navigation-" + index + "-missing-observer-peek", JsonSerializer.SerializeToElement(new { peek, host = State() }));
            }
            var dom = completed ? await Web.EvaluateResultAsync("JSON.stringify({ua:navigator.userAgent,ready:document.readyState,observerWork:window.devduoShellObservation?.observationWorkMilliseconds,resources:performance.getEntriesByType('resource').map(e=>e.toJSON()),navigation:performance.getEntriesByType('navigation').map(e=>e.toJSON())})") : null;
            Save("navigation-" + index + "-completion", JsonSerializer.SerializeToElement(new {
                completed, callMilliseconds, maxQueueDelay, completionMilliseconds, dom, processesBefore,
                processesAfter = Program.NavigationSentinel!.FindChildren(), host = State(), requests = Program.NavigationServer!.Requests.ToArray()
            }));
            Check(completed, "Each isolated local navigation completes with a new observed document within 20 seconds.");
            Check(completionMilliseconds < 1500, "The unchanged local document must reach its first observation without the former 2.5-second native delay.");
            CheckLocalNavigationTimings(State());
            var result = JsonSerializer.SerializeToElement(new { name, callMilliseconds, maxQueueDelay,
                completionMilliseconds, dom, host = State(), requests = Program.NavigationServer!.Requests.ToArray() });
            Save("navigation-" + index, result);
            Console.WriteLine("LATENCY " + name + " call=" + callMilliseconds + " queue=" + maxQueueDelay + " " + State().GetProperty("webKit").GetProperty("timings"));
        }
        await Web.EvaluateResultAsync("window.latencyFrames={started:performance.now(),count:0};requestAnimationFrame(function tick(){if(++window.latencyFrames.count<30)requestAnimationFrame(tick);else window.latencyFrames.finished=performance.now()});'started'");
        await WaitForScript("String(window.latencyFrames.count)", value => value == "30", "First animation frames after process swaps");
        using var animation = JsonDocument.Parse((await Web.EvaluateResultAsync("JSON.stringify(window.latencyFrames)"))!);
        Check(animation.RootElement.GetProperty("finished").GetDouble() - animation.RootElement.GetProperty("started").GetDouble() < 1500,
            "The real page completes its first 30 animation frames without a 2.5-second handle-open pause.");
        Save("navigation-animation", animation.RootElement);
        await CheckObservationMessageBurst(false);
        await CheckObservationMessageBurst(true);
        var canvasClock = Stopwatch.StartNew();
        var oldCanvasDocument = State().GetProperty("pageDocumentId").GetString();
        Web.WindowsPanel!.ShowHtml(BrowserTestServer.LatencyDocument +
            "<script>window.canvasAcceptance={started:performance.now()};const c=document.createElement('canvas');c.width=c.height=1;" +
            "const x=c.getContext('2d',{willReadFrequently:true});x.fillStyle='#123';x.fillRect(0,0,1,1);" +
            "window.canvasAcceptance.pixel=Array.from(x.getImageData(0,0,1,1).data);window.canvasAcceptance.finished=performance.now();</script>");
        await Wait(state => !Web.Navigation.IsLoading && state.TryGetProperty("pageDocumentId", out var id) &&
            id.ValueKind == JsonValueKind.String && id.GetString() != oldCanvasDocument, "Parser-time real Canvas readback document");
        var canvasCompletionMilliseconds = canvasClock.Elapsed.TotalMilliseconds;
        var canvasResult = await Web.EvaluateResultAsync("JSON.stringify(window.canvasAcceptance)");
        Save("navigation-canvas-first-readback", JsonSerializer.SerializeToElement(new {
            canvasCompletionMilliseconds, canvasResult, host = State(), processes = Program.NavigationSentinel!.FindChildren()
        }));
        Check(canvasResult is not null, "The real document executes its parser-time Canvas readback.");
        using var canvas = JsonDocument.Parse(canvasResult!);
        Check(canvas.RootElement.GetProperty("pixel").EnumerateArray().Select(channel => channel.GetInt32()).SequenceEqual(new[] { 17, 34, 51, 255 }),
            "The first readback of the real page's new Canvas returns the actual filled pixel.");
        Check(canvasCompletionMilliseconds < 1500 &&
            canvas.RootElement.GetProperty("finished").GetDouble() - canvas.RootElement.GetProperty("started").GetDouble() < 1500,
            "A parser-time Canvas reaches real pixels and document observation without the isolated stream's 15-second initialization timeout.");

        for (var cycle = 0; cycle < 3; cycle++)
        {
            var oldBrowser = Web;
            var mode = Settings.Mode.SelectedIndex;
            Select(Settings.Mode, mode == 0 ? 1 : 0);
            await Task.Delay(30);
            Select(Settings.Mode, mode);
            await Wait(state => !ReferenceEquals(oldBrowser, Web) && Text(state, "initializationState", "ready") &&
                !Web.Navigation.IsLoading && state.TryGetProperty("pageDocumentId", out var id) && id.ValueKind == JsonValueKind.String,
                "Rapid disposal and replacement complete with a real observed document");
            Save("navigation-rebuild-" + cycle, State());
        }
        // The normal application finally block disposes this newly attached
        // container while its first navigation/bootstrap may still be pending.
        Select(Settings.Mode, Settings.Mode.SelectedIndex == 0 ? 1 : 0);
        Console.WriteLine("PASS cold local navigation, inline/HTTP process swaps, source pixels, animation, rapid replacements and normal shutdown of a fresh container.");
    }

    private async Task<bool> CaptureColdNavigationPaint()
    {
        var browser = Web.WindowsPanel!.Browser;
        var flow = State().GetProperty("webKit").GetProperty("continuation").GetProperty("validFlowRectanglePx");
        var points = new[] { .25, .5, .75 }.Select(fraction => new {
            x = flow[0].GetInt32() + (int)((flow[2].GetInt32() - flow[0].GetInt32()) * fraction),
            y = flow[1].GetInt32() + (int)((flow[3].GetInt32() - flow[1].GetInt32()) * .6)
        }).ToArray();
        var matched = false;
        var milliseconds = 0.0;
        do
        {
            using var bitmap = NativePaintEvidence.CaptureLiveRaster(browser);
            matched = points.All(point => {
                var pixel = bitmap.GetPixel(point.x, point.y);
                return pixel.R == 187 && pixel.G == 170 && pixel.B == 221;
            });
            milliseconds = Stopwatch.GetElapsedTime(browser.LastNavigationRequestTimestamp).TotalMilliseconds;
            if (matched)
            {
                bitmap.Save(Path.Combine(Program.Output, "navigation-cold-http-live-raster.png"), ImageFormat.Png);
                break;
            }
            await Task.Delay(10);
        } while (milliseconds < 1500);
        Save("navigation-cold-paint", JsonSerializer.SerializeToElement(new { matched, milliseconds, points,
            method = "Existing source raster via GetDC/BitBlt; no WM_PRINTCLIENT or paint request" }));
        return matched && milliseconds < 1500;
    }

    private async Task CheckObservationMessageBurst(bool firstSnapshotUnavailable)
    {
        var original = await Web.EvaluateResultAsync("JSON.stringify(window.devduoShellObservation)");
        Check(original is not null, "A real document has a complete observation before the message-order regression.");
        using var originalDocument = JsonDocument.Parse(original!);
        var originalId = originalDocument.RootElement.GetProperty("documentId").GetString();
        var currentId = Guid.NewGuid().ToString();
        await Web.EvaluateResultAsync("(()=>{const current={...window.devduoShellObservation,documentId:'" + currentId +
            "'};const stale={...current,documentId:'queued-previous-document'};" +
            (firstSnapshotUnavailable ? "let reads=0;Object.defineProperty(window,'devduoShellObservation',{configurable:true,get(){return ++reads===1?undefined:current}});" : "window.devduoShellObservation=current;") +
            "const send=value=>window.webkit.messageHandlers.devduoShellJSON.postMessage(JSON.stringify(value));" +
            "send(stale);send(current);send(stale);return 'sent'})()");
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(3) && !Text(State(), "pageDocumentId", currentId)) await Task.Delay(10);
        var accepted = Text(State(), "pageDocumentId", currentId);
        Save("observation-queued-message-" + (firstSnapshotUnavailable ? "initially-unavailable" : "current-snapshot"), JsonSerializer.SerializeToElement(new {
            accepted, firstSnapshotUnavailable, currentId, elapsedMilliseconds = clock.Elapsed.TotalMilliseconds, host = State(),
            renderer = await Web.EvaluateResultAsync("JSON.stringify(window.devduoShellObservation)")
        }));
        // Restore the real observer's identity after the isolated message-order case.
        await Web.EvaluateResultAsync("Object.defineProperty(window,'devduoShellObservation',{value:JSON.parse(" + JsonSerializer.Serialize(original) +
            "),writable:true,configurable:true});window.webkit.messageHandlers.devduoShellJSON.postMessage(JSON.stringify(window.devduoShellObservation));'restored'");
        Check(accepted, "An in-flight stale verification cannot permanently discard the current renderer snapshot, even when another stale message arrives last.");
        await Wait(state => Text(state, "pageDocumentId", originalId!), "Restore the real document's observation identity after the message-order regression");
    }

    private void CheckLocalNavigationTimings(JsonElement state)
    {
        var timing = state.GetProperty("webKit").GetProperty("timings");
        var started = timing.GetProperty("requestToStartMilliseconds").GetDouble();
        Check(started + timing.GetProperty("firstObservationMilliseconds").GetDouble() < 1500,
            "A fresh local navigation's native request-to-first-observation interval stays below 1.5 seconds on this acceptance host.");
        Check(started + timing.GetProperty("navigationFinishMilliseconds").GetDouble() < 1500,
            "Native completion timing includes the request-to-start interval.");
    }
}
