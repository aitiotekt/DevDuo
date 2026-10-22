using System.Runtime.InteropServices;
using System.Text.Json;
using DevDuo;

namespace DevDuo.Windows.Tests;

internal sealed partial class WindowsChecks
{
    private async Task LoadOriginalReference(string palette = "auto", string? meta = null)
    {
        var previous = State().TryGetProperty("pageDocumentId", out var id) ? id.GetString() : null;
        var url = "http://127.0.0.1:8765/safari-reference.html?palette=" + palette +
            (meta is null ? "" : "&themeColor=" + Uri.EscapeDataString(meta));
        Web.Navigate(url);
        await Wait(state => Text(state, "initializationState", "ready") && Text(state, "pageTitle", "DevDuo Reference A") &&
            state.GetProperty("pageDocumentId").GetString() is { } current && current != previous &&
            Text(state, "tintSource", "WebKit native edge average"), "Original reference HTTP document and native edge color");
    }

    private void NativeWheel(bool downward)
    {
        var browser = Web.WindowsPanel!.Browser;
        var sourceCenter = new NativePoint { X = browser.Width / 3, Y = browser.Height / 2 };
        ClientToScreen(browser.BrowserWindow, ref sourceCenter);
        var presenter = (WindowsScreenPresenter)Web.WindowsPanel.Parent!;
        var geometry = State().GetProperty("webKit").GetProperty("logicalGeometry");
        var scale = browser.BackingScale;
        var viewport = System.Drawing.Rectangle.FromLTRB(
            (int)Math.Ceiling(geometry.GetProperty("left").GetInt32() * scale),
            (int)Math.Ceiling(geometry.GetProperty("top").GetInt32() * scale),
            (int)Math.Floor((geometry.GetProperty("width").GetInt32() - geometry.GetProperty("right").GetInt32()) * scale),
            (int)Math.Floor((geometry.GetProperty("height").GetInt32() - geometry.GetProperty("bottom").GetInt32()) * scale));
        var viewportOnScreen = browser.PresentationBounds!(viewport);
        var monitor = System.Windows.Forms.Screen.FromHandle(presenter.Handle).WorkingArea;
        var visible = System.Drawing.Rectangle.Intersect(viewportOnScreen, monitor);
        var ancestors = new List<object>();
        for (var handle = presenter.Handle; handle != 0; handle = WheelGetParent(handle))
        {
            PresentationGetClientRect(handle, out var client);
            var origin = new NativePoint();
            ClientToScreen(handle, ref origin);
            ancestors.Add(new { handle = handle.ToInt64(), x = origin.X, y = origin.Y,
                width = client.Right, height = client.Bottom });
            visible.Intersect(new(origin.X, origin.Y, client.Right, client.Bottom));
        }
        Check(visible.Width > 8 && visible.Height > 8, "Native wheel acceptance requires a visible presented document viewport.");
        // Native routing consumes physical presentation screen coordinates. The
        // rotated source HWND is cloaked and has a different screen origin.
        var point = new NativePoint { X = visible.Left + visible.Width / 3, Y = visible.Top + visible.Height / 2 };
        Save("wheel-input-bounds", JsonSerializer.SerializeToElement(new {
            originalSourcePoint = new { x = sourceCenter.X, y = sourceCenter.Y },
            point = new { x = point.X, y = point.Y }, viewportOnScreen, visible, monitor, ancestors }));
        var position = (nint)((point.X & 0xffff) | ((point.Y & 0xffff) << 16));
        var delta = (nint)((uint)(ushort)(downward ? -120 : 120) << 16);
        SendMessage(browser.BrowserWindow, 0x20A, delta, position);
    }

    private async Task ScrollNativeUntil(bool compact)
    {
        await Web.EvaluateResultAsync("window.__wheelHistory=[];if(!window.__nativeWheelObserved){window.__nativeWheelObserved=true;window.addEventListener('wheel',e=>window.__wheelHistory.push({x:e.clientX,y:e.clientY,dy:e.deltaY,prevented:e.defaultPrevented,target:e.target.tagName,at:performance.now()}),{passive:true})};'observing'");
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            NativeWheel(compact);
            await Task.Delay(120);
            Check(Text(State(), "initializationState", "ready"), "Native presentation remains ready during wheel input.");
            if (Text(State(), "chromeState", compact ? "compact" : "expanded")) return;
        }
        var events = await Web.EvaluateResultAsync("JSON.stringify({url:location.href,ready:document.readyState,documentId:window.devduoShellObservation?.documentId,events:window.__wheelHistory,scroll:[scrollX,scrollY],visibility:document.visibilityState,focused:document.hasFocus(),size:[innerWidth,innerHeight],range:(document.scrollingElement?.scrollHeight??0)-innerHeight,hit:document.elementFromPoint(innerWidth/3,innerHeight/2)?.outerHTML.slice(0,300)})");
        File.WriteAllText(Path.Combine(Program.Output, "wheel-failure-events.json"), events ?? "null");
        throw new InvalidOperationException("Native wheel did not reach expected chrome state: " + State());
    }

    private async Task CheckSafariReferenceAlignment()
    {
        await Wait(state => Text(state, "initializationState", "ready"), "Runtime for scoped Safari reference checks");
        CheckShellStatePolicy();
        Select(Settings.Scale, 1);
        foreach (var fold in new[] { 0, 1 })
        {
            Select(Settings.Fold, fold);
            for (var angle = 0; angle < 4; angle++)
            {
                await Wait(state => Text(state, "initializationState", "ready"), "Native pose initialization");
                await LoadOriginalReference();
                var state = State();
                var body = state.GetProperty("bodyRotationDegrees").GetInt32();
                Check(body == angle * 90, "Body angle follows the requested pose.");
                Check(state.GetProperty("presentation").GetProperty("sceneInCanvasDegrees").GetInt32() ==
                    (fold == 0 && angle == 2 ? 90 : 0), "Live scene direction matches original Safari canvas.");
                var tabs = Web.WindowsPanel!.Browser.Chrome.Count(item => item.Kind is WindowsComposition.Kind.SelectedTab or WindowsComposition.Kind.Tab);
                Check(tabs == (fold == 1 ? 2 : 0), "Only Open expanded has exactly two illustrated tabs.");
                Save($"reference-pose-{fold}-{angle * 90}", state);
                Rotate();
            }
        }
        Select(Settings.Fold, 1);
        await Wait(state => Text(state, "initializationState", "ready"), "Open reference controls");
        foreach (var (dark, palette, meta) in new[] {
            (false, "light", "#101923"), (false, "dark", "#eef3f8"),
            (true, "light", (string?)null), (true, "auto", (string?)null) })
        {
            Select(Settings.ThemeChoice, dark ? 2 : 1);
            await LoadOriginalReference(palette, meta);
            var state = await Wait(state => Text(state, "shellAppearance", dark ? "dark" : "light"), "Independent shell appearance");
            var dom = await Web.EvaluateResultAsync("JSON.stringify({dark:matchMedia('(prefers-color-scheme:dark)').matches,background:getComputedStyle(document.documentElement).backgroundColor,positioned:[...document.querySelectorAll('*')].filter(e=>getComputedStyle(e).position==='fixed').length})");
            using var actual = JsonDocument.Parse(dom!);
            Check(actual.RootElement.GetProperty("dark").GetBoolean() == dark, "Native appearance updates the real page media query.");
            Check(actual.RootElement.GetProperty("positioned").GetInt32() == 0, "Original reference page has no inserted fixed markers.");
            Check(Web.WindowsPanel!.Browser.Chrome.Where(item => item.Kind == WindowsComposition.Kind.Address).All(item => item.Foreground == (dark ? 0xffffffu : 0u)),
                "Glyph treatment follows appearance independently of backdrop luminance.");
            Check(Text(state, "tintSource", "WebKit native edge average"), "Opposing meta hints do not disable native page sampling.");
            var backdropIsDark = state.GetProperty("tintRgb")[0].GetInt32() < 128;
            Check(backdropIsDark == (palette == "dark" || palette == "auto" && dark), "Native backdrop follows authored page colors independently of shell appearance.");
            Save($"reference-color-{dark}-{palette}", state);
        }
        Select(Settings.ThemeChoice, 1);
        await LoadOriginalReference();
        var document = State().GetProperty("pageDocumentId").GetString();
        Web.Evaluate("scrollTo(0,509);window.__devduoReportContainerObservation()");
        await Wait(state => state.GetProperty("scrollY").GetDouble() == 509, "Programmatic document position");
        await Task.Delay(450);
        Check(Text(State(), "chromeState", "expanded"), "Programmatic scrollTo does not compact Open chrome.");
        await Web.EvaluateResultAsync("window.__wheelHistory=[];addEventListener('wheel',e=>queueMicrotask(()=>window.__wheelHistory.push({delta:e.deltaY,cancelled:e.defaultPrevented,y:scrollY})),{passive:true});window.__blockWheel=e=>e.preventDefault();addEventListener('wheel',window.__blockWheel,{passive:false});'attached'");
        NativeWheel(true);
        await Task.Delay(450);
        Check(State().GetProperty("scrollY").GetDouble() == 509 && Text(State(), "chromeState", "expanded"),
            "Cancelled native wheel with no document motion does not compact chrome.");
        await Web.EvaluateResultAsync("removeEventListener('wheel',window.__blockWheel);'removed'");
        await ScrollNativeUntil(true);
        var compact = State();
        Check(Web.WindowsPanel!.Browser.Chrome.Any(item => item.Kind == WindowsComposition.Kind.CompactAddress), "Compact address has a distinct material role.");
        Save("reference-native-down", compact);
        await Task.Delay(450);
        await ScrollNativeUntil(false);
        Check(State().GetProperty("scrollY").GetDouble() > 2, "Native reverse scroll expands before reaching document top.");
        Check(State().GetProperty("pageDocumentId").GetString() == document, "Chrome changes retain the real document.");
        var before = State().GetProperty("actualPageViewport").GetRawText();
        var y = State().GetProperty("scrollY").GetDouble();
        Select(Settings.ThemeChoice, 2);
        await Wait(state => Text(state, "shellAppearance", "dark"), "Appearance transition without navigation");
        Check(State().GetProperty("pageDocumentId").GetString() == document && State().GetProperty("scrollY").GetDouble() == y &&
            State().GetProperty("actualPageViewport").GetRawText() == before, "Appearance changes preserve document, scroll and viewport.");
        Save("reference-theme-preserved", State());
        Check(Text(State(), "initializationState", "ready"), "Presentation remains valid after appearance and scroll changes.");
        Save("reference-result", JsonSerializer.SerializeToElement(new { status = "passed", assertions, poses = 8,
            colorConditions = 4, fullSuite = false, presentedWindowAcceptance = "separate" }));
        Console.WriteLine($"Scoped Safari reference checks passed: {assertions}. Evidence: {Program.Output}");
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll", EntryPoint = "GetParent")] private static extern nint WheelGetParent(nint window);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint window, ref NativePoint point);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessage(nint window, int message, nint wParam, nint lParam);
}
