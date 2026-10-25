using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using DevDuo;

namespace DevDuo.Windows.Tests;

internal sealed partial class WindowsChecks
{
    private async Task CheckPresentationDamage()
    {
        await Wait(state => Text(state, "initializationState", "ready"), "Presentation runtime initialization");
        Select(Settings.Fold, 1);
        Select(Settings.Scale, 1);
        window.MinHeight = 400;
        window.Width = 1242;
        window.Height = 687;
        await CheckPlainFixtureInsetDamage();
        window.Height = 450;
        await CheckAcceleratedPresentation();
        window.Height = 687;
        Web.WindowsPanel!.ShowHtml("<!doctype html><title>Presentation damage acceptance</title>" +
            "<style>html,body{margin:0;background:rgb(36,104,172)}body{height:4000px}input{position:absolute;left:20px;top:20px}</style><input>");
        await Wait(state => Text(state, "pageTitle", "Presentation damage acceptance") && !Web.Navigation.IsLoading,
            "Flat presentation damage fixture");
        await WaitForScript("document.readyState", value => value == "complete", "Presentation damage document completion");
        await Web.EvaluateResultAsync("window.presentationSentinel='retained';document.querySelector('input').value='draft';scrollTo(0,240);'ready'");
        await Task.Delay(250);
        var browser = Web.WindowsPanel.Browser;
        var fingerprint = "JSON.stringify({timeOrigin:performance.timeOrigin,url:location.href,history:history.length,inner:[innerWidth,innerHeight],scroll:[scrollX,scrollY],sentinel:window.presentationSentinel,input:document.querySelector('input').value})";
        var identity = await Web.EvaluateResultAsync(fingerprint);
        CheckNativeBoundsWithoutLogicalChange(browser);
        CheckPresentationMoveDamage(browser);
        await Task.Delay(250);
        Check(await Web.EvaluateResultAsync(fingerprint) == identity,
            "Physical HWND synchronization and clip invalidation preserve the document, scroll and logical viewport.");
        CheckLivePresentationRaster(browser, "restore-baseline");
        for (var cycle = 0; cycle < 3; cycle++)
        {
            window.WindowState = WindowState.Maximized;
            await Task.Delay(350);
            // Establish a visibly different old document before navigating to the
            // new one, matching a real site -> color laboratory transition.
            Web.WindowsPanel.ShowHtml(PresentationColorDocument("Old presentation raster", "rgb(180,35,70)"));
            await Wait(state => Text(state, "pageTitle", "Old presentation raster") && !Web.Navigation.IsLoading,
                "Contrasting old document");
            await Task.Delay(250);
            CheckLivePresentationRaster(browser, "old-page-maximized-" + cycle, Color.FromArgb(180, 35, 70));
            Web.WindowsPanel.ShowHtml(PresentationColorDocument("Presentation damage acceptance", "rgb(36,104,172)"));
            await Wait(state => Text(state, "pageTitle", "Presentation damage acceptance") && !Web.Navigation.IsLoading,
                "New document before restore");
            await WaitForScript("document.readyState", value => value == "complete", "New document completion before resize");
            await Web.EvaluateResultAsync("window.presentationSentinel='retained';document.querySelector('input').value='draft';scrollTo(0,240);'ready'");
            await Task.Delay(250);
            var cycleIdentity = await Web.EvaluateResultAsync(fingerprint);
            CheckLivePresentationRaster(browser, "new-page-maximized-" + cycle);
            browser.SetInspectorVisible(true);
            await Wait(_ => browser.InspectorVisible, "Inspector on maximized window");
            window.WindowState = WindowState.Normal;
            browser.CloseInspector();
            await Wait(_ => !browser.InspectorVisible, "Inspector after restoring window");
            await Task.Delay(350);
            var state = State();
            var frame = state.GetProperty("nativeWebViewFramePixels");
            var source = state.GetProperty("webKit").GetProperty("composition").GetProperty("sourceWindow");
            Check(source.GetProperty("clientWidth").GetInt32() == frame.GetProperty("width").GetInt32() &&
                source.GetProperty("clientHeight").GetInt32() == frame.GetProperty("height").GetInt32(),
                "The live WebKit source matches the restored physical host bounds.");
            Check(await Web.EvaluateResultAsync(fingerprint) == cycleIdentity,
                "Maximize, Inspector activation and restore retain timeOrigin, URL, scroll, form state and logical viewport.");
            CheckLivePresentationRaster(browser, "restore-cycle-" + cycle);
            Save("restore-cycle-" + cycle, state);
        }
        Console.WriteLine("PASS presentation damage: unchanged logical-key native resize, complete dirty regions, no idle repaint, live-raster restore cycles and preserved document/scroll/viewport.");
    }

    private async Task CheckInsetPresentationOnly()
    {
        await Wait(state => Text(state, "initializationState", "ready"), "Inset presentation runtime initialization");
        Select(Settings.Fold, 1);
        Select(Settings.Scale, 1);
        Settings.TabStrip.IsChecked = true;
        window.WindowState = WindowState.Maximized;
        await Task.Delay(350);
        Web.WindowsPanel!.ShowHtml("<!doctype html><title>Accelerated inset predecessor</title><style>html,body{margin:0;background:#b42346}div{position:fixed;inset:0;transform:translateZ(0);background:#b42346}</style><div></div>");
        await WaitForScript("document.title", value => value == "Accelerated inset predecessor", "Accelerated predecessor commit");
        await Wait(state => state.GetProperty("webKit").GetProperty("renderingPipeline").GetProperty("acceleratedTransitions").GetUInt64() > 0,
            "Real predecessor accelerated composition");
        await Task.Delay(350);
        await CheckPlainFixtureInsetDamage(true);
        Console.WriteLine("PASS isolated plain-fixture inset transition after accelerated predecessor.");
    }

    private async Task CheckPlainFixtureInsetDamage(bool acceleratedPredecessor = false)
    {
        var previousTransitions = State().GetProperty("webKit").GetProperty("renderingPipeline").GetProperty("acceleratedTransitions").GetUInt64();
        var previousDocument = State().TryGetProperty("pageDocumentId", out var previous) ? previous.GetString() : null;
        window.GetVisualDescendants().OfType<HostSettingsPanel>().Single().ColorDemo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Wait(state => state.GetProperty("pageDocumentId").ValueKind == JsonValueKind.String &&
            state.GetProperty("pageDocumentId").GetString() != previousDocument && Web.Navigation.IsLocalFixture,
            "Unmodified color fixture observation");
        await WaitForScript("document.readyState", value => value == "complete", "Plain fixture completion");
        Check(await Web.EvaluateResultAsync("String([...document.querySelectorAll('*')].filter(e=>getComputedStyle(e).position==='fixed').length)") == "0",
            "The real color fixture has no injected fixed marker or compositing trigger.");
        var before = State();
        Check(before.GetProperty("webKit").GetProperty("renderingPipeline").GetProperty("acceleratedTransitions").GetUInt64() == (acceleratedPredecessor ? previousTransitions : 0),
            "The plain fixture does not introduce an accelerated composition transition.");
        var expandedTop = before.GetProperty("webKit").GetProperty("logicalGeometry").GetProperty("top").GetInt32();
        var identity = await Web.EvaluateResultAsync("JSON.stringify([performance.timeOrigin,location.href,history.length])");
        Save("plain-fixture-expanded", before);
        for (var index = 0; index < 6; index++)
        {
            NativeWheel(true);
            await Task.Delay(120);
        }
        var compact = await Wait(state => Text(state, "chromeState", "compact") && state.GetProperty("scrollY").GetDouble() > 400,
            "Actual plain-fixture wheel reaches compact chrome");
        await Task.Delay(350);
        compact = State();
        Save("plain-fixture-compact", compact);
        var browser = Web.WindowsPanel!.Browser;
        var compactTop = compact.GetProperty("webKit").GetProperty("logicalGeometry").GetProperty("top").GetInt32();
        Check(expandedTop - compactTop == 75, "The real expanded-to-compact transition exposes exactly 75 CSS pixels.");
        Check(compact.GetProperty("webKit").GetProperty("continuation").GetProperty("validFlowRectanglePx")[1].GetInt32() == 0,
            "Host continuation does not cover the newly exposed top strip.");
        using var raster = NativePaintEvidence.CaptureLiveRaster(browser);
        raster.Save(Path.Combine(Program.Output, "plain-fixture-compact-live-raster.png"), ImageFormat.Png);
        var white = 0;
        var samples = 0;
        for (var y = (int)Math.Ceiling((compactTop + 2) * browser.BackingScale); y < (expandedTop - 2) * browser.BackingScale; y += 2)
            foreach (var fraction in new[] { .2, .35, .5, .65, .8 })
            {
                var pixel = raster.GetPixel((int)(raster.Width * fraction), y);
                if (pixel.R > 250 && pixel.G > 250 && pixel.B > 250) white++;
                samples++;
            }
        File.WriteAllText(Path.Combine(Program.Output, "plain-fixture-inset-strip.json"), JsonSerializer.Serialize(new {
            expandedTop, compactTop, scale = browser.BackingScale, white, samples,
            scrollY = compact.GetProperty("scrollY").GetDouble() }));
        Check(white < samples * .05, $"Newly exposed plain-fixture inset pixels contain rendered content, not a retained white band: {white}/{samples} white samples.");
        Check(await Web.EvaluateResultAsync("JSON.stringify([performance.timeOrigin,location.href,history.length])") == identity,
            "Compacting the plain fixture preserves document identity and history.");
        var oldViewport = before.GetProperty("actualPageViewport");
        var newViewport = compact.GetProperty("actualPageViewport");
        Check(newViewport.GetProperty("width").GetDouble() == oldViewport.GetProperty("width").GetDouble() &&
            newViewport.GetProperty("height").GetDouble() == oldViewport.GetProperty("height").GetDouble() + 75,
            "The inset transition changes only the measured compact viewport reservation.");
        Check(await Web.EvaluateResultAsync("String(scrollY)") == compact.GetProperty("scrollY").GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture),
            "New native inset damage does not change the observed scroll position.");
    }

    private async Task CheckAcceleratedPresentation()
    {
        Web.WindowsPanel!.ShowHtml("<!doctype html><title>Accelerated presentation acceptance</title>" +
            "<style>html,body{margin:0;background:rgb(180,35,70)}body{height:5000px}" +
            "#layer{position:fixed;inset:0;background:rgb(20,170,90);transform:translateZ(0);will-change:transform}" +
            "#marker{position:fixed;left:10px;width:40px;top:50px;height:20px;background:rgb(40,80,220);transform:translateZ(0)}</style>" +
            "<div id=layer></div><div id=marker></div>");
        await Wait(state => Text(state, "pageTitle", "Accelerated presentation acceptance") && !Web.Navigation.IsLoading,
            "Accelerated CSS layer fixture");
        await Task.Delay(750);
        var browser = Web.WindowsPanel.Browser;
        window.Activate();
        PresentationSetForegroundWindow(PresentationGetAncestor(browser.BrowserWindow, 2));
        PresentationSetFocus(browser.BrowserWindow);
        File.WriteAllText(Path.Combine(Program.Output, "accelerated-activity.json"),
            await Web.EvaluateResultAsync("JSON.stringify({visibility:document.visibilityState,hidden:document.hidden,focused:document.hasFocus(),dpr:devicePixelRatio})") ?? "null");
        Save("accelerated-window-activity", JsonSerializer.SerializeToElement(new {
            foreground = PresentationGetForegroundWindow().ToInt64(),
            sourceRoot = PresentationGetAncestor(browser.BrowserWindow, 2).ToInt64(),
            active = PresentationGetActiveWindow().ToInt64() }));
        Save("accelerated-before-scroll", State());
        Check(browser.BackingScale > 0 && browser.BackingScale < .95,
            $"The accelerated damage fixture exercises fractional Fit below one: {browser.BackingScale}.");
        CheckLivePresentationRaster(browser, "accelerated-before-scroll", Color.FromArgb(20, 170, 90));
        var before = State();
        Check(before.GetProperty("webKit").GetProperty("renderingPipeline").GetProperty("acceleratedTransitions").GetUInt64() > 0,
            "The real WC renderer entered accelerated composition for the CSS layer fixture.");
        await Web.EvaluateResultAsync("window.presentationAnimation={frames:0,started:performance.now()};" +
            "window.markerAnimation=document.getElementById('marker').animate([{transform:'translate3d(0,0,0)'},{transform:'translate3d(30px,0,0)'}],{duration:500,iterations:Infinity,direction:'alternate'});" +
            "requestAnimationFrame(function tick(){let p=window.presentationAnimation;p.frames++;" +
            "if(p.frames<30)requestAnimationFrame(tick);else p.finished=performance.now()});'started'");
        for (var step = 0; step < 4; step++)
        {
            var point = browser.PointToScreen(new(browser.Width / 2, browser.Height / 2));
            PresentationSendMessage(browser.BrowserWindow, 0x20A, (nint)(-120 << 16),
                (nint)((point.Y << 16) | (point.X & 0xffff)));
            var expected = step % 2 == 0 ? Color.FromArgb(20, 170, 90) : Color.FromArgb(30, 150, 100);
            await Web.EvaluateResultAsync($"document.getElementById('layer').style.background='rgb({expected.R},{expected.G},{expected.B})';'changed'");
            await Task.Delay(750);
            Save("accelerated-scroll-" + step, State());
            File.WriteAllText(Path.Combine(Program.Output, "accelerated-scroll-" + step + "-dom.json"),
                await Web.EvaluateResultAsync("JSON.stringify({animation:window.presentationAnimation,visibility:document.visibilityState,scrollY,marker:document.getElementById('marker').getBoundingClientRect().toJSON(),color:getComputedStyle(document.getElementById('layer')).backgroundColor})") ?? "null");
            CheckLivePresentationRaster(browser, "accelerated-scroll-" + step, expected);
        }
        var after = State();
        Save("accelerated-after-scroll", after);
        File.WriteAllText(Path.Combine(Program.Output, "accelerated-animation.json"),
            await Web.EvaluateResultAsync("JSON.stringify(window.presentationAnimation)") ?? "null");
        var pipelineBefore = before.GetProperty("webKit").GetProperty("renderingPipeline");
        var pipelineAfter = after.GetProperty("webKit").GetProperty("renderingPipeline");
        Check(pipelineAfter.GetProperty("updates").GetUInt64() > pipelineBefore.GetProperty("updates").GetUInt64() &&
            pipelineAfter.GetProperty("nativeWindow").GetProperty("paints").GetInt64() > pipelineBefore.GetProperty("nativeWindow").GetProperty("paints").GetInt64(),
            "Accelerated renderer updates reach the actual WM_PAINT handler during real wheel input and animation.");
        Check(after.GetProperty("scrollY").GetDouble() > before.GetProperty("scrollY").GetDouble(),
            "Native wheel input actually scrolls the accelerated document.");
        var observerBefore = before.GetProperty("observerPipeline");
        var observerAfter = after.GetProperty("observerPipeline");
        // Indicator fades and fixed-edge results publish without relaying out
        // the page. Their legitimate updates are included in the total count.
        Check(observerAfter.GetProperty("observations").GetInt64() > observerBefore.GetProperty("observations").GetInt64() &&
            observerAfter.GetProperty("layouts").GetInt64() > observerBefore.GetProperty("layouts").GetInt64() &&
            observerAfter.GetProperty("telemetryPublications").GetInt64() - observerBefore.GetProperty("telemetryPublications").GetInt64() >=
            observerAfter.GetProperty("layouts").GetInt64() - observerBefore.GetProperty("layouts").GetInt64(),
            "Scroll layouts publish telemetry alongside independent indicator and fixed-edge updates.");
        await Web.EvaluateResultAsync("window.presentationWarmAnimation={frames:0,started:performance.now()};" +
            "requestAnimationFrame(function tick(){let p=window.presentationWarmAnimation;p.frames++;" +
            "if(p.frames<30)requestAnimationFrame(tick);else p.finished=performance.now()});'started'");
        await WaitForScript("String(window.presentationWarmAnimation.frames)", value => value == "30", "Warm accelerated animation progresses");
        File.WriteAllText(Path.Combine(Program.Output, "accelerated-warm-animation-dom.json"),
            await Web.EvaluateResultAsync("JSON.stringify({animation:window.presentationWarmAnimation,observationWorkMilliseconds:window.devduoShellObservation?.observationWorkMilliseconds,visibility:document.visibilityState,focused:document.hasFocus()})") ?? "null");
        Save("accelerated-warm-animation", State());
        await Web.EvaluateResultAsync("window.markerAnimation.cancel();document.getElementById('marker').style.transform='translate3d(30px,0,0)';'finished'");
        await Task.Delay(250);
        using (var raster = NativePaintEvidence.CaptureLiveRaster(browser))
        {
            var top = State().GetProperty("webKit").GetProperty("logicalGeometry").GetProperty("top").GetDouble();
            var oldPixel = raster.GetPixel((int)(12 * browser.BackingScale), (int)((top + 60) * browser.BackingScale));
            Check(Math.Abs(oldPixel.R - 30) <= 1 && Math.Abs(oldPixel.G - 150) <= 1 && Math.Abs(oldPixel.B - 100) <= 1,
                "Moving the accelerated blue marker clears its previous position without retained trails.");
        }
        window.Height = 687;
        await Task.Delay(350);
        Check(browser.BackingScale > 1, "The second accelerated scale case is above one device pixel per logical pixel.");
        CheckLivePresentationRaster(browser, "accelerated-scale-above-one", Color.FromArgb(30, 150, 100));
        Save("accelerated-scale-above-one", State());
    }

    private static string PresentationColorDocument(string title, string color) =>
        "<!doctype html><title>" + title + "</title><style>html,body{margin:0;background:" + color +
        "}body{height:4000px}input{position:absolute;left:20px;top:20px}</style><input>";

    private void CheckNativeBoundsWithoutLogicalChange(WindowsWebKitView browser)
    {
        var geometry = State().GetProperty("webKit").GetProperty("logicalGeometry");
        void SetSameGeometry() => browser.SetGeometry(
            geometry.GetProperty("width").GetInt32(), geometry.GetProperty("height").GetInt32(), browser.BackingScale,
            geometry.GetProperty("top").GetInt32(), geometry.GetProperty("right").GetInt32(),
            geometry.GetProperty("bottom").GetInt32(), geometry.GetProperty("left").GetInt32(),
            geometry.GetProperty("minimumHeight").GetInt32(), geometry.GetProperty("maximumHeight").GetInt32());
        var original = browser.Size;
        try
        {
            // Physical rounding/ancestor clipping can change one axis while the
            // logical viewport and the scale that is limited by the other do not.
            browser.Size = new(original.Width, original.Height + 5);
            SetSameGeometry();
            PresentationGetClientRect(browser.BrowserWindow, out var bounds);
            Check(bounds.Right == browser.ClientSize.Width && bounds.Bottom == browser.ClientSize.Height,
                "An unchanged logical geometry key must not skip native HWND size synchronization.");
            Check(PresentationGetUpdateRect(browser.BrowserWindow, out var damage, false) &&
                damage.Left == 0 && damage.Top == 0 && damage.Right == bounds.Right && damage.Bottom == bounds.Bottom,
                "Resizing invalidates the complete existing source raster, including previously valid right/bottom pixels.");
            PresentationValidateRect(browser.BrowserWindow, 0);
            SetSameGeometry();
            Check(!PresentationGetUpdateRect(browser.BrowserWindow, out _, false),
                "Repeated unchanged geometry must not request another repaint.");
        }
        finally
        {
            browser.Size = original;
            SetSameGeometry();
        }
    }

    private void CheckPresentationMoveDamage(WindowsWebKitView browser)
    {
        var presenter = (WindowsScreenPresenter)Web.WindowsPanel!.Parent!;
        var original = presenter.Location;
        try
        {
            PresentationValidateRect(browser.BrowserWindow, 0);
            presenter.Location = new(original.X + 3, original.Y);
            Check(PresentationGetUpdateRect(browser.BrowserWindow, out var damage, false) &&
                damage.Left == 0 && damage.Top == 0 && damage.Right == browser.ClientSize.Width && damage.Bottom == browser.ClientSize.Height,
                "Moving an ancestor clip invalidates the entire live source without changing the page.");
            PresentationValidateRect(browser.BrowserWindow, 0);
            presenter.Location = presenter.Location;
            Check(!PresentationGetUpdateRect(browser.BrowserWindow, out _, false),
                "An unchanged presenter position must not request another repaint.");
        }
        finally { presenter.Location = original; }
    }

    private void CheckLivePresentationRaster(WindowsWebKitView browser, string name, Color? expectedColor = null)
    {
        using var bitmap = NativePaintEvidence.CaptureLiveRaster(browser);
        bitmap.Save(Path.Combine(Program.Output, name + "-live-raster.png"), ImageFormat.Png);
        var state = State();
        var flow = state.GetProperty("webKit").GetProperty("continuation").GetProperty("validFlowRectanglePx");
        var top = flow[1].GetInt32();
        var expected = expectedColor ?? Color.FromArgb(36, 104, 172);
        foreach (var fractionX in new[] { .15, .5, .85, .98 })
            foreach (var fractionY in new[] { .25, .6, .9, .98 })
            {
                var x = Math.Min(bitmap.Width - 1, (int)(bitmap.Width * fractionX));
                var y = Math.Min(bitmap.Height - 1, top + (int)((bitmap.Height - top) * fractionY));
                var pixel = bitmap.GetPixel(x, y);
                Check(Math.Abs(pixel.R - expected.R) <= 1 && Math.Abs(pixel.G - expected.G) <= 1 && Math.Abs(pixel.B - expected.B) <= 1,
                    $"The existing live source raster has no retained old-page pixels after {name}: point=({x},{y}), rgb={pixel}.");
            }
    }

    [StructLayout(LayoutKind.Sequential)] private struct PresentationRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", EntryPoint = "GetClientRect")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PresentationGetClientRect(nint window, out PresentationRect bounds);
    [DllImport("user32.dll", EntryPoint = "GetUpdateRect")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PresentationGetUpdateRect(nint window, out PresentationRect bounds, [MarshalAs(UnmanagedType.Bool)] bool erase);
    [DllImport("user32.dll", EntryPoint = "ValidateRect")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PresentationValidateRect(nint window, nint bounds);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint PresentationSendMessage(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")] private static extern nint PresentationGetForegroundWindow();
    [DllImport("user32.dll", EntryPoint = "GetActiveWindow")] private static extern nint PresentationGetActiveWindow();
    [DllImport("user32.dll", EntryPoint = "GetAncestor")] private static extern nint PresentationGetAncestor(nint window, uint flags);
    [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PresentationSetForegroundWindow(nint window);
    [DllImport("user32.dll", EntryPoint = "SetFocus")] private static extern nint PresentationSetFocus(nint window);
}
