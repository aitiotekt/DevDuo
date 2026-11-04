using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DevDuo.Windows.Tests;

internal sealed partial class WindowsChecks
{
    private async Task<JsonElement> NativeTouchResult()
    {
        using var result = JsonDocument.Parse((await Web.EvaluateResultAsync("JSON.stringify(nativeTouchResult())"))!);
        return result.RootElement.Clone();
    }

    private async Task NativeTouchDrag(double x, double from, double to)
    {
        await InteractionPointer(0x201, x, from);
        try
        {
            for (var step = 1; step <= 5; step++)
                await InteractionPointer(0x200, x, from + (to - from) * step / 5);
            // Stop before release so semantic checks do not include a fling.
            await Task.Delay(180);
        }
        finally { await InteractionPointer(0x202, x, to); }
        await Task.Delay(250);
    }

    private async Task CheckNativeTouch()
    {
        if (Environment.GetEnvironmentVariable("DEVDUO_TEST_TOUCH_TRACKING_ONLY") == "1")
        {
            await CheckNativeTouchTracking();
            return;
        }
        if (Environment.GetEnvironmentVariable("DEVDUO_TEST_TOUCH_SEAM_ONLY") == "1")
        {
            await CheckNativeTouchSeam();
            return;
        }
        if (Environment.GetEnvironmentVariable("DEVDUO_TEST_TOUCH_LIFECYCLE_ONLY") == "1")
        {
            await CheckNativeTouchLifecycle();
            return;
        }
        await Wait(state => Text(state, "initializationState", "ready"), "Native touch runtime");
        Check(WindowsWebKitApi.SetHostTouchEnabled is not null, "Native contact API v1 is required");
        window.WindowState = WindowState.Maximized;
        Select(Settings.Scale, 0);
        Select(Settings.Interaction, 0);
        var browser = Web.WindowsPanel!.Browser;
        SetPageCompositing(browser, true);
        var fixture = "tests/DevDuo.WebKitProbe/pages/native-touch.html";
        File.Copy(fixture, Path.Combine(Program.Output, "native-touch.html"));
        Web.WindowsPanel.ShowHtml(File.ReadAllText(fixture));
        await WaitForScript("typeof nativeTouchResult", value => value == "function", "Native contact fixture");
        var owner = PresentationGetAncestor(browser.Handle, 2);
        window.Activate();
        PresentationSetForegroundWindow(owner);
        await Task.Delay(150);
        Check(PresentationGetForegroundWindow() == owner, "Native contact checks require the independent foreground window");
        await InteractionPointer(0x201, 70, 40);
        await InteractionPointer(0x202, 70, 40);
        try { await WaitForScript("String(nativeTouchResult().clicks)", value => value == "1", "Native tap compatibility activation"); }
        finally { Save("native-touch-tap", JsonSerializer.SerializeToElement(new { result = await NativeTouchResult(), host = State() })); }
        var tap = await NativeTouchResult();
        Check(tap.GetProperty("events").EnumerateArray().Any(e => Text(e, "type", "touchstart") && e.GetProperty("trusted").GetBoolean()),
            "The page receives trusted engine touch events");
        Check(tap.GetProperty("events").EnumerateArray().Any(e => Text(e, "type", "pointerdown") && Text(e, "pointerType", "touch") && e.GetProperty("trusted").GetBoolean()),
            "WebCore generates trusted touch PointerEvents");
        Check(tap.GetProperty("maxTouchPoints").GetInt32() == 1, "Native single-contact capability matches the configured mode");
        await Web.EvaluateResultAsync("configureTouchFixture('prevent-pointer');'configured'");
        await Task.Delay(250);
        await InteractionPointer(0x201, 70, 36);
        await InteractionPointer(0x202, 70, 36);
        var canceledPointerTap = await NativeTouchResult();
        Save("native-touch-pointer-tap", canceledPointerTap);
        Check(canceledPointerTap.GetProperty("clicks").GetInt32() == 2,
            "Canceling pointerdown preserves native click activation");
        Check(canceledPointerTap.GetProperty("events").EnumerateArray().All(e => !Text(e, "type", "mousedown") && !Text(e, "type", "mouseup")),
            "Canceling pointerdown suppresses compatibility mouse down/up");
        Check(canceledPointerTap.GetProperty("events").EnumerateArray().Any(e => Text(e, "type", "click") && Text(e, "pointerType", "touch") && e.GetProperty("trusted").GetBoolean()),
            "The preserved click retains native trust and touch identity");
        await Web.EvaluateResultAsync("configureTouchFixture('auto');scrollTo(0,400);'positioned'");
        await Task.Delay(250);
        var contactStarted = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        await InteractionPointer(0x201, 240, 340);
        try
        {
            await Task.Delay(250);
            await InteractionPointer(0x200, 240, 200);
            await Task.Delay(180);
        }
        finally { await InteractionPointer(0x202, 240, 200); }
        var contactFinished = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        var timingHost = State();
        var panTiming = timingHost.GetProperty("webKit").GetProperty("nativeTouch");
        var panBegin = panTiming.GetProperty("lastPanBeginInputSeconds").GetDouble();
        var panEnd = panTiming.GetProperty("lastPanEndInputSeconds").GetDouble();
        Save("native-touch-contact-timing", JsonSerializer.SerializeToElement(new { contactStarted, contactFinished, panBegin, panEnd, host = timingHost }));
        Check(panBegin >= contactStarted && panBegin - contactStarted < .2,
            "The native pan baseline belongs to the actual OS contact, including the initial stationary interval");
        Check(panEnd <= contactFinished && contactFinished - panEnd < .2 && panEnd - panBegin > .5,
            "Native gesture phases preserve the real contact duration for kinetic velocity");
        foreach (var composited in new[] { true, false })
        {
            SetPageCompositing(browser, composited);
            await Task.Delay(250);
            foreach (var mode in new[] { "auto", "none", "pan-x", "prevent-start", "prevent-move", "prevent-pointer", "locked", "pan-y", "wheel-block", "nested" })
            {
                var evidenceName = (composited ? "gpu-" : "cpu-") + mode;
                await Web.EvaluateResultAsync($"configureTouchFixture('{mode}');scrollTo(0,0);document.getElementById('nested').scrollTop=0;'configured'");
                await Task.Delay(250);
                File.WriteAllText(Path.Combine(Program.Output, "native-touch-tree-" + evidenceName + ".txt"), await browser.ReadNativeScrollingStateAsync());
                var preventedBefore = State().GetProperty("webKit").GetProperty("nativeTouch").GetProperty("prevented").GetUInt64();
                await NativeTouchDrag(mode == "nested" ? 120 : 240, 340, 200);
                var result = await NativeTouchResult();
                var host = State();
                Save("native-touch-" + evidenceName, JsonSerializer.SerializeToElement(new { result, host }));
                File.WriteAllText(Path.Combine(Program.Output, "native-touch-tree-after-" + evidenceName + ".txt"), await browser.ReadNativeScrollingStateAsync());
                Check(host.GetProperty("webKit").GetProperty("frameSurface").GetProperty("lastContentTransport").GetString() ==
                    (composited ? "shared-d3d11" : "native-cpu-bitmap"), "Gesture policy uses the requested actual paint path: " + evidenceName);
                Check(result.GetProperty("trustedTouchStarts").GetInt32() == 1,
                    "Native touch dispatch reaches authored listeners on both paint paths: " + evidenceName);
                Check(result.GetProperty("events").EnumerateArray().All(e => !Text(e, "type", "pointermove") || !Text(e, "pointerType", "mouse")),
                    "Desktop hover timers do not inject ghost mouse pointers into a native touch gesture: " + evidenceName);
                var rootY = result.GetProperty("scrollY").GetDouble();
                var nestedY = result.GetProperty("nestedY").GetDouble();
                if (mode is "auto" or "pan-y" or "wheel-block" or "prevent-pointer")
                    Check(rootY > 100, mode + " grants the actual root native pan");
                else
                    Check(Math.Abs(rootY) < .05, mode + " preserves the root scroll position");
                if (mode == "nested") Check(nestedY > 100, "The native scrolling chain targets the nested scroller");
                if (mode is "prevent-start" or "prevent-move")
                    Check(host.GetProperty("webKit").GetProperty("nativeTouch").GetProperty("prevented").GetUInt64() > preventedBefore,
                        "WebCore's canceled touch event reaches native gesture authorization: " + mode);
                if (mode is "none" or "pan-x" or "prevent-start" or "prevent-move")
                    Check(result.GetProperty("touchMoves").GetInt32() is > 0 and <= 5,
                        "Stationary WM_MOUSEMOVE notifications do not amplify the five actual contact moves: " + evidenceName);
                Check(!host.GetProperty("webKit").GetProperty("nativeTouch").GetProperty("active").GetBoolean(), "Release ends the native contact: " + mode);
                Check(result.GetProperty("events").EnumerateArray().All(e => !Text(e, "type", "wheel")), "Touch gestures do not invent DOM wheel events: " + mode);
                Check(result.GetProperty("clicks").GetInt32() == 2, "Dragging does not activate the tap target: " + mode);
                if (mode == "auto")
                {
                    var bounds = browser.PresentationBounds!(browser.ClientRectangle);
                    var capture = await Task.Run(() => WindowsWindowCapture.Capture(owner,
                        new Avalonia.PixelRect(bounds.X, bounds.Y, bounds.Width, bounds.Height)));
                    File.WriteAllBytes(Path.Combine(Program.Output, "native-touch-" + evidenceName + ".png"), capture.Png);
                    using var stream = new MemoryStream(capture.Png);
                    using var bitmap = new Bitmap(stream);
                    var g = host.GetProperty("webKit").GetProperty("logicalGeometry");
                    var sampleY = 80 + ((80 - rootY % 80 + 40) % 80);
                    var local = new Rectangle((int)Math.Round((g.GetProperty("left").GetInt32() + 190) * browser.BackingScale),
                        (int)Math.Round((g.GetProperty("top").GetInt32() + sampleY) * browser.BackingScale), 1, 1);
                    var presented = browser.PresentationBounds!(local);
                    var pixel = bitmap.GetPixel(presented.X - bounds.X, presented.Y - bounds.Y);
                    var red = (int)Math.Floor((rootY + sampleY) / 80) % 2 == 0;
                    Check(Math.Abs(pixel.R - (red ? 208 : 32)) <= 3 && Math.Abs(pixel.G - (red ? 48 : 112)) <= 3 &&
                        Math.Abs(pixel.B - (red ? 64 : 192)) <= 3, "Native touch paints the actual window on both paths: " + evidenceName);
                    using var source = NativePaintEvidence.Capture(browser, host);
                    var fixedPixel = source.GetPixel(local.X, (int)Math.Round(
                        (g.GetProperty("height").GetInt32() - g.GetProperty("bottom").GetInt32() - 20) * browser.BackingScale));
                    var insetPixel = source.GetPixel(local.X, source.Height - 8);
                    Check(Math.Abs(fixedPixel.R - 24) <= 3 && Math.Abs(fixedPixel.G - 144) <= 3 && Math.Abs(fixedPixel.B - 96) <= 3,
                        "The fixed footer remains anchored on both paint paths: " + evidenceName);
                    Check(Math.Abs(insetPixel.R - 24) <= 3 && Math.Abs(insetPixel.G - 144) <= 3 && Math.Abs(insetPixel.B - 96) <= 3,
                        "Continuation uses the actual fixed container on both paint paths: " + evidenceName);
                }
            }
        }
        SetPageCompositing(browser, true);
        await Web.EvaluateResultAsync("configureTouchFixture('auto');scrollTo(0,400);'positioned'");
        await Task.Delay(300);
        await InteractionPointer(0x201, 240, 400);
        var states = new List<JsonElement>();
        var qpcAnchor = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        var wallAnchor = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try
        {
            await InteractionPointer(0x200, 240, 360);
            Check(State().GetProperty("webKit").GetProperty("nativeTouch").GetProperty("panning").GetBoolean(), "WebKit grants the pan before busy-page sampling");
            await Web.EvaluateResultAsync("armBusyInterval(2400);'armed'");
            await Task.Delay(200);
            for (var sample = 0; sample < 3; sample++)
            {
                await InteractionPointer(0x200, 240, 300 - sample * 50);
                var state = State();
                states.Add(state);
                var bounds = browser.PresentationBounds!(browser.ClientRectangle);
                var capture = await Task.Run(() => WindowsWindowCapture.Capture(owner, new Avalonia.PixelRect(bounds.X, bounds.Y, bounds.Width, bounds.Height)));
                File.WriteAllBytes(Path.Combine(Program.Output, $"native-touch-busy-{sample}.png"), capture.Png);
                Save("native-touch-busy-" + sample, state);
                using var source = NativePaintEvidence.Capture(browser, state);
                var g = state.GetProperty("webKit").GetProperty("logicalGeometry");
                var x = (int)Math.Round((g.GetProperty("left").GetInt32() + 190) * browser.BackingScale);
                var y = (int)Math.Round((g.GetProperty("height").GetInt32() - g.GetProperty("bottom").GetInt32() - 20) * browser.BackingScale);
                var fixedPixel = source.GetPixel(x, y);
                Check(Math.Abs(fixedPixel.R - 24) <= 3 && Math.Abs(fixedPixel.G - 144) <= 3 && Math.Abs(fixedPixel.B - 96) <= 3,
                    "Fixed footer stays anchored during independent native touch scrolling");
            }
        }
        finally { await InteractionPointer(0x202, 240, 200); }
        var firstMainResult = await NativeTouchResult();
        var final = firstMainResult;
        var busy = final.GetProperty("busyIntervals").EnumerateArray().Last();
        var begin = qpcAnchor + (busy.GetProperty("begin").GetInt64() - wallAnchor) / 1000d;
        var end = qpcAnchor + (busy.GetProperty("end").GetInt64() - wallAnchor) / 1000d;
        var frames = states.SelectMany(s => s.GetProperty("webKit").GetProperty("frameTiming").GetProperty("scrollFrames").EnumerateArray())
            .Where(f => f.GetProperty("scrollRevision").GetUInt64() > 0 && f.GetProperty("commit").GetDouble() > begin + .05 && f.GetProperty("paintEnd").GetDouble() < end - .05)
            .DistinctBy(f => f.GetProperty("frameId").GetUInt64()).ToArray();
        Check(frames.Length >= 2, "Native pan presents independent GPU frames while page JavaScript is blocked");
        Check(frames.Max(f => f.GetProperty("scrollY").GetDouble()) > 500, "The native pan advances the real root during page-main-thread work");
        var latestNativeY = frames.Last().GetProperty("scrollY").GetDouble();
        var convergence = Stopwatch.StartNew();
        while (Math.Abs(final.GetProperty("scrollY").GetDouble() - latestNativeY) > .05 && convergence.ElapsedMilliseconds < 500)
        {
            await Task.Delay(20);
            final = await NativeTouchResult();
        }
        Save("native-touch-busy", JsonSerializer.SerializeToElement(new { states, frames, firstMainResult, result = final, begin, end,
            convergenceMilliseconds = convergence.Elapsed.TotalMilliseconds }));
        Check(Math.Abs(final.GetProperty("scrollY").GetDouble() - latestNativeY) < .05,
            "The actual DOM converges to the independent native touch position within 500 ms after page work ends");
        Check(Math.Abs(final.GetProperty("fixed").GetProperty("bottom").GetDouble() - final.GetProperty("viewport")[1].GetDouble()) < .05,
            "Native input preserves actual fixed layout and viewport");
        Check(Math.Abs(final.GetProperty("range").GetDouble() + final.GetProperty("viewport")[1].GetDouble() - 6000) < .05,
            "Native input does not fabricate document height or range");
        foreach (var scale in new[] { 0, 1 })
        foreach (var pose in new[] { (Fold: 0, Angle: 0), (Fold: 1, Angle: 90), (Fold: 1, Angle: 270) })
        {
            if (scale == 0 && pose.Fold == 0) continue;
            window.WindowState = scale == 0 ? WindowState.Maximized : WindowState.Normal;
            if (scale == 1) { window.Width = 1200; window.Height = 850; }
            Select(Settings.Scale, scale);
            Select(Settings.Fold, pose.Fold);
            while (State().GetProperty("bodyRotationDegrees").GetInt32() != pose.Angle)
            {
                Settings.Rotate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(100);
            }
            await Wait(state => Text(state, "initializationState", "ready"), "Native touch presentation pose");
            browser = Web.WindowsPanel!.Browser;
            SetPageCompositing(browser, true);
            Web.WindowsPanel.ShowHtml(File.ReadAllText(fixture));
            await WaitForScript("typeof nativeTouchResult", value => value == "function", "Rotated native touch fixture");
            owner = PresentationGetAncestor(browser.Handle, 2);
            window.Activate();
            PresentationSetForegroundWindow(owner);
            await Web.EvaluateResultAsync("scrollTo(0,400);'positioned'");
            await Task.Delay(300);
            await NativeTouchDrag(240, 380, 220);
            var posed = await NativeTouchResult();
            var posedHost = State();
            var caseName = $"{scale}-{pose.Fold}-{pose.Angle}";
            Save("native-touch-pose-" + caseName, JsonSerializer.SerializeToElement(new { result = posed, host = posedHost }));
            Check(posed.GetProperty("scrollY").GetDouble() > 500, "Native drag follows the presented pose: " + caseName);
            Check(Math.Abs(posed.GetProperty("range").GetDouble() + posed.GetProperty("viewport")[1].GetDouble() - 6000) < .05,
                "Fit and rotation preserve the native touch document range: " + caseName);
            Check(Math.Abs(posed.GetProperty("fixed").GetProperty("bottom").GetDouble() - posed.GetProperty("viewport")[1].GetDouble()) < .05,
                "Fit and rotation preserve actual fixed layout: " + caseName);
            var bounds = browser.PresentationBounds!(browser.ClientRectangle);
            var presenter = (WindowsScreenPresenter)Web.WindowsPanel.Parent!;
            var visible = Rectangle.Intersect(bounds, System.Windows.Forms.Screen.FromHandle(presenter.Handle).WorkingArea);
            for (var ancestor = presenter.Handle; ancestor != 0; ancestor = WheelGetParent(ancestor))
            {
                if (!PresentationGetClientRect(ancestor, out var client)) throw new InvalidOperationException("Cannot read the presentation ancestor's client bounds.");
                var origin = new NativePoint();
                if (!ClientToScreen(ancestor, ref origin)) throw new InvalidOperationException("Cannot map the presentation ancestor to the desktop.");
                visible.Intersect(new(origin.X, origin.Y, client.Right, client.Bottom));
            }
            Check(visible.Width > 0 && visible.Height > 0, "The current monitor exposes an actual presented device region: " + caseName);
            Save("native-touch-pose-coverage-" + caseName, JsonSerializer.SerializeToElement(new {
                requested = bounds, captured = visible, completeDeviceVisible = bounds == visible,
                sourceFramebufferScope = "full logical device; final-window evidence covers only the recorded visible crop" }));
            var capture = await Task.Run(() => WindowsWindowCapture.Capture(owner, new Avalonia.PixelRect(visible.X, visible.Y, visible.Width, visible.Height)));
            File.WriteAllBytes(Path.Combine(Program.Output, "native-touch-pose-" + caseName + ".png"), capture.Png);
            using var stream = new MemoryStream(capture.Png);
            using var bitmap = new Bitmap(stream);
            var g = posedHost.GetProperty("webKit").GetProperty("logicalGeometry");
            var sampleY = 80 + ((80 - posed.GetProperty("scrollY").GetDouble() % 80 + 40) % 80);
            var sample = browser.PresentationBounds!(new Rectangle(
                (int)Math.Round((g.GetProperty("left").GetInt32() + 190) * browser.BackingScale),
                (int)Math.Round((g.GetProperty("top").GetInt32() + sampleY) * browser.BackingScale), 1, 1));
            Check(visible.Contains(sample.Location), "The actual-window pattern sample lies in the visible crop: " + caseName);
            var pixel = bitmap.GetPixel(sample.X - visible.X, sample.Y - visible.Y);
            var red = (int)Math.Floor((posed.GetProperty("scrollY").GetDouble() + sampleY) / 80) % 2 == 0;
            Check(Math.Abs(pixel.R - (red ? 208 : 32)) <= 3 && Math.Abs(pixel.G - (red ? 48 : 112)) <= 3 &&
                Math.Abs(pixel.B - (red ? 64 : 192)) <= 3, "Native touch presents actual page pixels at the rotated/Fit position: " + caseName);
            using var fullSource = NativePaintEvidence.Capture(browser, posedHost);
            var footer = fullSource.GetPixel((int)Math.Round((g.GetProperty("left").GetInt32() + 190) * browser.BackingScale),
                (int)Math.Round((g.GetProperty("height").GetInt32() - g.GetProperty("bottom").GetInt32() - 20) * browser.BackingScale));
            Check(Math.Abs(footer.R - 24) <= 3 && Math.Abs(footer.G - 144) <= 3 && Math.Abs(footer.B - 96) <= 3,
                "The complete native framebuffer preserves the fixed footer, including portions clipped from the window: " + caseName);
        }
        Select(Settings.Interaction, 1);
        await WaitForScript("String(navigator.maxTouchPoints)", value => value == "0", "Mouse capability restoration");
        Console.WriteLine($"PASS native touch: {assertions} assertions; native input/gesture semantics and busy-page GPU evidence: {Program.Output}");
    }
}
