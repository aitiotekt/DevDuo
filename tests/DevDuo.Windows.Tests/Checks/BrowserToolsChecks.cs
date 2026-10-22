using System.Net;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace DevDuo.Windows.Tests;

internal sealed partial class WindowsChecks
{
    private async Task CheckLoadingPerformance()
    {
        await Ready();
        Settings.Inspector.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Wait(_ => Web.InspectorVisible, "Performance Inspector");
        foreach (var name in new[] { "color-1", "color-2", "color-3", "mdn-cold", "mdn-warm" })
        {
            var previous = State().GetProperty("pageDocumentId").GetString();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            if (name.StartsWith("color", StringComparison.Ordinal))
                window.GetVisualDescendants().OfType<DevDuo.HostSettingsPanel>().Single().ColorDemo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            else Web.Navigate("https://developer.mozilla.org/");
            var deadline = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(100);
                var state = State();
                if (!Web.Navigation.IsLoading && state.TryGetProperty("pageDocumentId", out var id) && id.ValueKind == JsonValueKind.String && id.GetString() != previous) break;
            }
            var elapsed = clock.Elapsed.TotalMilliseconds;
            var performance = await Web.EvaluateResultAsync("JSON.stringify({readyState:document.readyState,observerWorkMilliseconds:window.devduoShellObservation?.observationWorkMilliseconds,navigation:performance.getEntriesByType('navigation').map(e=>e.toJSON()),paint:performance.getEntriesByType('paint').map(e=>e.toJSON()),resources:performance.getEntriesByType('resource').map(e=>e.toJSON())})");
            using var dom = performance is null ? null : JsonDocument.Parse(performance);
            var result = new { caseName = name, observedCompletionMilliseconds = elapsed, host = State(), performance = dom?.RootElement };
            File.WriteAllText(Path.Combine(Program.Output, "performance-" + name + ".json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("PERFORMANCE " + name + " completion=" + Math.Round(elapsed) + "ms host=" + State().GetProperty("webKit").GetProperty("timings"));
        }
        // Leave a bounded interval for a presented Inspector observation.
        await Task.Delay(10000);
    }

    private async Task OpenBrowserTestPage()
    {
        await Wait(state => Text(state, "initializationState", "ready"), "Browser initialization");
        Web.Navigate(BrowserTestServer.Address);
        await Wait(state => Text(state, "pageUrl", BrowserTestServer.Address) && !Web.Navigation.IsLoading &&
            state.TryGetProperty("pageDocumentId", out var id) && id.ValueKind == JsonValueKind.String, "Browser test document");
    }

    private async Task CheckPersistentStorage(bool write)
    {
        using var server = new BrowserTestServer();
        await OpenBrowserTestPage();
        var token = Environment.GetEnvironmentVariable("DEVDUO_STORAGE_TEST_TOKEN") ?? throw new InvalidOperationException("Storage tests require DEVDUO_STORAGE_TEST_TOKEN.");
        var quoted = JsonSerializer.Serialize(token);
        var script = write ?
            "localStorage.setItem('devduo-test'," + quoted + ");document.cookie='devduoTest=" + token + ";Max-Age=3600;SameSite=Lax;Path=/';" +
            "const request=indexedDB.open('devduo-test',1);request.onupgradeneeded=()=>request.result.createObjectStore('values');request.onsuccess=()=>{const db=request.result;const tx=db.transaction('values','readwrite');tx.objectStore('values').put(" + quoted + ",'token');tx.oncomplete=()=>{db.close();window.storageResult='written'}};" :
            "const request=indexedDB.open('devduo-test',1);request.onsuccess=()=>{const db=request.result;const get=db.transaction('values').objectStore('values').get('token');get.onsuccess=()=>{window.storageResult=JSON.stringify({local:localStorage.getItem('devduo-test'),indexed:get.result,cookie:document.cookie});db.close()}};";
        Web.Evaluate(script);
        await WaitForScript("window.storageResult", result => write ? result == "written" : result?.Contains(token, StringComparison.Ordinal) == true, "Persistent website storage");
        if (!write)
        {
            using var result = JsonDocument.Parse((await Web.EvaluateResultAsync("window.storageResult"))!);
            Check(result.RootElement.GetProperty("local").GetString() == token, "localStorage survives process restart.");
            Check(result.RootElement.GetProperty("indexed").GetString() == token, "IndexedDB survives process restart.");
            Check(result.RootElement.GetProperty("cookie").GetString()!.Contains("devduoTest=" + token, StringComparison.Ordinal), "Persistent cookies survive process restart.");
        }
        Save(write ? "storage-write" : "storage-read", State());
        // Give the real network process time to synchronize localStorage/Cookie writes.
        await Task.Delay(1500);
        Console.WriteLine("PASS persistent storage " + (write ? "write" : "read after process restart"));
    }

    private async Task WaitForScript(string script, Func<string?, bool> accepted, string description)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (accepted(await Web.EvaluateResultAsync(script))) return;
            await Task.Delay(100);
        }
        throw new TimeoutException(description);
    }

    private async Task CheckBrowserTools()
    {
        using var server = new BrowserTestServer();
        await OpenBrowserTestPage();
        Web.Navigate(BrowserTestServer.Address + "slow.html");
        await WaitForScript("document.getElementById('early') ? document.readyState : null", result => result == "loading", "Early content before DOMContentLoaded");
        var early = State();
        var flow = early.GetProperty("webKit").GetProperty("continuation").GetProperty("validFlowRectanglePx");
        Check(flow[2].GetInt32() > flow[0].GetInt32() && flow[3].GetInt32() > flow[1].GetInt32(), "The host must not cover the layout viewport while parsing.");
        Save("early-paint-before-dom-ready", early);
        NativePaintEvidence.Save(Web.WindowsPanel!.Browser, early, Path.Combine(Program.Output, "early-paint"));
        await WaitForScript("document.readyState", result => result == "complete", "Delayed document completion");
        await OpenBrowserTestPage();
        var original = Web;
        var browser = Web.WindowsPanel!.Browser;
        await Web.EvaluateResultAsync("window.rotationSentinel='preserved';document.querySelector('input').value='draft';history.pushState({},'', '?history=1');scrollTo(0,240);'ready'");
        var before = await Web.EvaluateResultAsync("JSON.stringify({id:window.devduoShellObservation.documentId,url:location.href,history:history.length,time:performance.timeOrigin})");
        foreach (var fold in new[] { 0, 1 })
        {
            if (fold != 0)
            {
                Select(Settings.Fold, fold);
                await OpenBrowserTestPage();
                original = Web;
                browser = Web.WindowsPanel!.Browser;
                await Web.EvaluateResultAsync("window.rotationSentinel='preserved';document.querySelector('input').value='draft';history.pushState({},'', '?history=1');'ready'");
                before = await Web.EvaluateResultAsync("JSON.stringify({id:window.devduoShellObservation.documentId,url:location.href,history:history.length,time:performance.timeOrigin})");
            }
            for (var angle = 0; angle < 4; angle++)
            {
                Rotate();
                await Task.Delay(350);
                Check(ReferenceEquals(original, Web), "Rotation retains the native host.");
                Check(ReferenceEquals(browser, Web.WindowsPanel!.Browser), "Rotation retains the WKView.");
                Check(await Web.EvaluateResultAsync("JSON.stringify({id:window.devduoShellObservation.documentId,url:location.href,history:history.length,time:performance.timeOrigin})") == before,
                    "Rotation preserves document identity, URL, history and performance.timeOrigin.");
                Check(await Web.EvaluateResultAsync("window.rotationSentinel + ':' + document.querySelector('input').value") == "preserved:draft", "Rotation preserves JS and unsaved form state.");
                var state = State();
                var geometry = state.GetProperty("webKit").GetProperty("logicalGeometry");
                var width = geometry.GetProperty("width").GetDouble() - geometry.GetProperty("left").GetDouble() - geometry.GetProperty("right").GetDouble();
                var height = geometry.GetProperty("height").GetDouble() - geometry.GetProperty("top").GetDouble() - geometry.GetProperty("bottom").GetDouble();
                Check(await Web.EvaluateResultAsync("JSON.stringify([innerWidth,innerHeight])") == JsonSerializer.Serialize(new[] { width, height }), "Rotation updates the actual DOM viewport.");
                Save("rotation-preserved-" + fold + "-" + angle, state);
            }
        }
        var documentBeforeInspector = await Web.EvaluateResultAsync("JSON.stringify([performance.timeOrigin,innerWidth,innerHeight])");
        Settings.Inspector.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Wait(state => state.GetProperty("webKit").GetProperty("inspector").GetProperty("visible").GetBoolean() &&
            !state.GetProperty("webKit").GetProperty("inspector").GetProperty("attached").GetBoolean(), "Independent Inspector window");
        await Task.Delay(300);
        Check(await Web.EvaluateResultAsync("JSON.stringify([performance.timeOrigin,innerWidth,innerHeight])") == documentBeforeInspector,
            "Independent Inspector preserves document and viewport.");
        Save("inspector-open", State());
        Settings.Inspector.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Wait(state => !state.GetProperty("webKit").GetProperty("inspector").GetProperty("visible").GetBoolean(), "Inspector closed by host button");
        Check(!State().GetProperty("webKit").GetProperty("nonPersistentStore").GetBoolean(), "Use a persistent website store.");
        Select(Settings.Fold, 0);
        await CheckNativeTabTitles();
        await CheckWelcomePageRuntime();
        Console.WriteLine("PASS browser tools: both folds, eight rotations, document/form/history preservation and independent Inspector button.");
    }
}
