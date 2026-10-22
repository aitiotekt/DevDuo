using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Controls;
using DevDuo;

namespace DevDuo.Windows.Tests;

internal sealed partial class WindowsChecks
{
    private sealed record IndicatorCapture(JsonElement Host, JsonElement Dom, string PresentedPath);
    private sealed record IndicatorIdleCounters(ulong NativeUpdates, long QueryCount, ulong SceneGeneration,
        long FixedRevision, long ContentRevision, string? ClockText);

    // These are implementation telemetry names, not Safari physical measurements.
    private static JsonElement Indicator(JsonElement host) => host.GetProperty("scrollIndicator");
    private static bool IndicatorShown(JsonElement host) => Indicator(host).GetProperty("visible").GetBoolean() &&
        Indicator(host).GetProperty("opacity").GetDouble() > 0;

    private async Task CheckScrollIndicators()
    {
        await Wait(state => Text(state, "initializationState", "ready"), "Indicator runtime initialization");
        Select(Settings.Fold, 0);
        Select(Settings.Scale, 1);
        window.WindowState = WindowState.Maximized;
        await Task.Delay(350);
        Check(State().GetProperty("bodyRotationDegrees").GetInt32() == 0, "The indicator contract uses the recorded Closed portrait pose.");
        await LoadIndicatorFixture();
        var initial = await CaptureIndicator("fresh-long-idle");
        Check(!IndicatorShown(initial.Host), "A fresh scrollable document starts with its indicator hidden.");
        Check(initial.Dom.GetProperty("maxY").GetDouble() > 0, "The positive fixture actually overflows.");

        var geometry = IndicatorGeometry(initial.Dom);
        await Web.EvaluateResultAsync("scrollTo(0,480);'instant'");
        await WaitForScript("String(scrollY)", value => double.TryParse(value, out var y) && y >= 479, "Actual instant DOM scroll");
        var instant = await CaptureIndicator("dom-instant-hidden");
        Check(!IndicatorShown(instant.Host), "Instant DOM scrolling alone does not identify native input.");
        Check(IndicatorGeometry(instant.Dom) == geometry, "Instant scrolling preserves viewport, fixed marker and scroll range.");
        await Web.EvaluateResultAsync("scrollTo({top:1000,behavior:'smooth'});'smooth'");
        var smoothClock = Stopwatch.StartNew();
        while (smoothClock.Elapsed < TimeSpan.FromSeconds(8))
        {
            await Task.Delay(50);
            Check(!IndicatorShown(State()), "Smooth DOM scroll events alone do not show the host indicator.");
            var dom = await IndicatorDom();
            if (Math.Abs(dom.GetProperty("scroll")[1].GetDouble() - 1000) < 1) break;
        }
        var smooth = await CaptureIndicator("dom-smooth-hidden");
        Check(Math.Abs(smooth.Dom.GetProperty("scroll")[1].GetDouble() - 1000) < 1, "The smooth-scroll negative control actually moved to its target.");
        Check(!IndicatorShown(smooth.Host) && IndicatorGeometry(smooth.Dom) == geometry,
            "Smooth scrolling retains the hidden indicator and unchanged page geometry.");
        Check(Indicator(initial.Host).GetProperty("inputSequence").GetUInt64() == Indicator(smooth.Host).GetProperty("inputSequence").GetUInt64() &&
            Indicator(initial.Host).GetProperty("confirmedMotionSequence").GetUInt64() == Indicator(smooth.Host).GetProperty("confirmedMotionSequence").GetUInt64(),
            "DOM-only scrolls advance neither native input nor renderer-confirmed user-motion sequences.");

        await LoadIndicatorFixture();
        var trapBefore = await CaptureIndicator("canceled-wheel-before");
        await Web.EvaluateResultAsync("indicatorFixture.trapWheel(true);'trapping'");
        await NativeIndicatorWheel();
        await Wait(state => state.GetProperty("scrollY").GetDouble() > 0, "Canceled trusted wheel causes same-direction programmatic movement");
        var trapClock = Stopwatch.StartNew();
        var trapSamples = new List<object>();
        var trapEverShown = false;
        while (trapClock.Elapsed < TimeSpan.FromMilliseconds(450))
        {
            var state = State();
            trapSamples.Add(new { elapsedMilliseconds = trapClock.Elapsed.TotalMilliseconds, indicator = Indicator(state).Clone() });
            if (IndicatorShown(state) && !trapEverShown)
            {
                trapEverShown = true;
                await CaptureIndicator("canceled-wheel-unexpected-visible");
            }
            await Task.Delay(30);
        }
        Save("scroll-indicator-canceled-wheel-samples", JsonSerializer.SerializeToElement(new { trapEverShown, trapSamples }));
        var trapAfter = await CaptureIndicator("canceled-wheel-programmatic-hidden");
        Check(trapAfter.Dom.GetProperty("receipts").EnumerateArray().Any(receipt =>
            receipt.GetProperty("type").GetString() == "wheel-programmatic" && receipt.GetProperty("trusted").GetBoolean() && receipt.GetProperty("prevented").GetBoolean()),
            "The fixture canceled a trusted wheel before synchronously scrolling the document itself.");
        Check(trapAfter.Dom.GetProperty("scroll")[1].GetDouble() > trapBefore.Dom.GetProperty("scroll")[1].GetDouble() &&
            Indicator(trapAfter.Host).GetProperty("inputSequence").GetUInt64() > Indicator(trapBefore.Host).GetProperty("inputSequence").GetUInt64(),
            "The causal negative control includes both native intent and real same-direction DOM displacement.");
        Check(Indicator(trapAfter.Host).GetProperty("confirmedMotionSequence").GetUInt64() == Indicator(trapBefore.Host).GetProperty("confirmedMotionSequence").GetUInt64() && !trapEverShown && !IndicatorShown(trapAfter.Host),
            "A time window and DOM delta cannot replace native User-motion confirmation.");
        Check(trapAfter.Dom.GetProperty("fixed").GetRawText() == trapBefore.Dom.GetProperty("fixed").GetRawText(),
            "The canceled-wheel fixture preserves fixed geometry while moving ordinary content.");

        await LoadIndicatorFixture();
        var beforeWheel = await CaptureIndicator("wheel-before-hidden");
        await NativeIndicatorWheel();
        await Wait(state => state.GetProperty("scrollY").GetDouble() > 0 && IndicatorShown(state), "Native wheel plus actual document motion shows the indicator");
        var wheel = await CaptureIndicator("wheel-visible");
        CheckIndicatorIntent(beforeWheel, wheel, "wheel");
        CheckIndicatorPaint(beforeWheel, wheel, "native wheel");
        var wheelFaded = await WaitIndicatorFade("wheel-faded");
        CheckIndicatorPaint(wheelFaded, wheel, "wheel fade removes the painted thumb");

        var beforeKeyboard = wheelFaded;
        var keyboardY = beforeKeyboard.Dom.GetProperty("scroll")[1].GetDouble();
        NativeIndicatorKey(0x22, 0x51); // Page Down.
        await Wait(state => state.GetProperty("scrollY").GetDouble() > keyboardY && IndicatorShown(state), "Keyboard plus actual document motion shows the indicator");
        var keyboard = await CaptureIndicator("keyboard-visible");
        CheckIndicatorIntent(beforeKeyboard, keyboard, "keydown");
        CheckIndicatorPaint(beforeKeyboard, keyboard, "native keyboard");
        await WaitIndicatorFade("keyboard-faded");
        NativeIndicatorKey(0x23, 0x4F); // End.
        await Wait(_ => IndicatorShown(State()), "End-key indicator visibility");
        await WaitForScript("String(Math.abs(scrollY-(document.scrollingElement.scrollHeight-document.scrollingElement.clientHeight))<1)",
            value => value == "true", "The End key reaches the actual document limit");
        var endpoint = await CaptureIndicator("endpoint-visible");
        Check(IndicatorShown(endpoint.Host), "Reaching maxScrollY does not immediately hide the indicator.");
        CheckIndicatorPaint(beforeKeyboard, endpoint, "document endpoint");
        var endpointFaded = await WaitIndicatorFade("endpoint-faded");
        CheckIndicatorPaint(endpointFaded, endpoint, "endpoint fade removes the painted thumb");

        foreach (var force in new[] { false, true })
        {
            await LoadIndicatorFixture(shortPage: true, forceScroll: force);
            var name = force ? "short-overflow-scroll" : "short-auto";
            var before = await CaptureIndicator(name + "-idle");
            Check(before.Dom.GetProperty("maxY").GetDouble() == 0, "The short-page negative control has zero actual scroll range.");
            await NativeIndicatorWheel();
            NativeIndicatorKey(0x22, 0x51);
            await Task.Delay(250);
            var after = await CaptureIndicator(name + "-native-input");
            Check(!IndicatorShown(after.Host) && after.Dom.GetProperty("scroll")[1].GetDouble() == 0,
                "Native input cannot show a thumb when the document has zero range, including overflow:scroll.");
            Check(IndicatorGeometry(before.Dom) == IndicatorGeometry(after.Dom), "Zero-range input preserves page geometry.");
        }

        foreach (var style in new[] { "none", "display-none", "zero-width", "subpixel-width" })
        {
            await LoadIndicatorFixture(suppress: style == "none", authorStyle: style == "none" ? "" : style);
            var suppressedBefore = await CaptureIndicator("css-" + style + "-idle");
            await NativeIndicatorWheel();
            await Wait(state => state.GetProperty("scrollY").GetDouble() > 0, "Author scrollbar hiding still allows real native scrolling: " + style);
            var suppressed = await CaptureIndicator("css-" + style + "-native-input");
            Check(suppressed.Dom.GetProperty("scrollbarWidth").GetString() == (style == "none" ? "none" : "auto") &&
                suppressed.Dom.GetProperty("authorScrollbarStyle").GetString() == (style == "none" ? "" : style),
                "The author control uses its requested rule without converting pseudo-element hiding to scrollbar-width:none: " + style);
            Check(Indicator(suppressed.Host).GetProperty("cssSuppressed").GetBoolean() && !IndicatorShown(suppressed.Host),
                "Author scrollbar hiding suppresses the indicator while preserving native scrolling: " + style);
            Check(suppressed.Dom.GetProperty("receipts").EnumerateArray().Any(receipt => receipt.GetProperty("type").GetString() == "wheel" &&
                receipt.GetProperty("trusted").GetBoolean()) &&
                Indicator(suppressed.Host).GetProperty("inputSequence").GetUInt64() > Indicator(suppressedBefore.Host).GetProperty("inputSequence").GetUInt64(),
                "The hidden-author-style control received real trusted wheel input: " + style);
            Check(suppressed.Dom.GetProperty("scroll")[1].GetDouble() > suppressedBefore.Dom.GetProperty("scroll")[1].GetDouble() &&
                suppressed.Dom.GetProperty("fixed").GetRawText() == suppressedBefore.Dom.GetProperty("fixed").GetRawText(),
                "CSS suppression retains actual scroll movement and the authored fixed marker: " + style);
        }

        // Safari's reused none→auto tab was an unlocated historical boundary.
        // Establish the positive auto control in a fresh native container instead.
        var previousView = Web;
        Select(Settings.Mode, 1);
        await Task.Delay(50);
        Select(Settings.Mode, 0);
        await Wait(state => !ReferenceEquals(previousView, Web) && Text(state, "initializationState", "ready"), "Fresh native indicator container");
        await LoadIndicatorFixture();
        var freshAuto = await CaptureIndicator("fresh-auto-idle");
        await NativeIndicatorWheel();
        await Wait(state => state.GetProperty("scrollY").GetDouble() > 0 && IndicatorShown(state), "Fresh auto positive control after CSS none");
        var freshAutoVisible = await CaptureIndicator("fresh-auto-visible");
        Check(freshAutoVisible.Dom.GetProperty("scrollbarWidth").GetString() == "auto" &&
            !Indicator(freshAutoVisible.Host).GetProperty("cssSuppressed").GetBoolean(), "Fresh auto retains the author's computed CSS setting.");
        CheckIndicatorPaint(freshAuto, freshAutoVisible, "fresh auto control");
        await WaitIndicatorFade("fresh-auto-faded");
        await CheckIndicatorStaticIdle();
        Save("scroll-indicator-result", JsonSerializer.SerializeToElement(new {
            assertions, passed = true, pose = "Closed portrait", source = "Actual native input, DOM observations, source raster and desktop pixels",
            limitation = "Thickness and fade durations are implementation observations, not exact Safari or physical-device calibration. Other poses, nested/horizontal scrollers and touch deceleration are not accepted by this scoped test."
        }));
        Console.WriteLine($"PASS scroll indicators: {assertions} assertions; fresh idle, native wheel/keyboard, endpoint fade, DOM-only negative controls, zero range, CSS none, screen-edge pixels and zero gutter.");
    }

    private async Task LoadIndicatorFixture(bool shortPage = false, bool forceScroll = false, bool suppress = false, string authorStyle = "")
    {
        var previous = State().TryGetProperty("pageDocumentId", out var id) ? id.GetString() : null;
        Web.WindowsPanel!.ShowHtml(File.ReadAllText("tests/DevDuo.WebKitProbe/pages/scroll-indicators.html"));
        await Wait(state => Text(state, "pageTitle", "Native scroll indicator acceptance") && !Web.Navigation.IsLoading &&
            state.TryGetProperty("pageDocumentId", out var current) && current.GetString() != previous, "New indicator fixture document");
        await Web.EvaluateResultAsync($"indicatorFixture.configure({shortPage.ToString().ToLowerInvariant()},{forceScroll.ToString().ToLowerInvariant()},{suppress.ToString().ToLowerInvariant()},{JsonSerializer.Serialize(authorStyle)});'configured'");
        await Task.Delay(450);
        window.Activate();
        var presenter = (WindowsScreenPresenter)Web.WindowsPanel.Parent!;
        PresentationSetForegroundWindow(PresentationGetAncestor(presenter.Handle, 2));
        PresentationSetFocus(Web.WindowsPanel.Browser.BrowserWindow);
        await Wait(state => !IndicatorShown(state), "Fresh fixture indicator starts hidden");
    }

    private async Task<JsonElement> IndicatorDom()
    {
        using var document = JsonDocument.Parse((await Web.EvaluateResultAsync("JSON.stringify(indicatorFixture.snapshot())"))!);
        return document.RootElement.Clone();
    }

    private static string IndicatorGeometry(JsonElement dom) => JsonSerializer.Serialize(new {
        widths = dom.GetProperty("widths"), height = dom.GetProperty("height"), clientHeight = dom.GetProperty("clientHeight"),
        document = dom.GetProperty("document"), fixedMarker = dom.GetProperty("fixed"), range = dom.GetProperty("maxY"),
        scrollbarWidth = dom.GetProperty("scrollbarWidth"), scrollbarGutter = dom.GetProperty("scrollbarGutter"), overflow = dom.GetProperty("overflowY")
    });

    private async Task NativeIndicatorWheel()
    {
        for (var index = 0; index < 3; index++) { NativeWheel(true); await Task.Delay(70); }
    }

    private void NativeIndicatorKey(int key, int scanCode)
    {
        var browser = Web.WindowsPanel!.Browser;
        PresentationSetFocus(browser.BrowserWindow);
        var flags = 1L | ((long)scanCode << 16) | (1L << 24);
        PresentationSendMessage(browser.BrowserWindow, 0x100, (nint)key, (nint)flags);
        PresentationSendMessage(browser.BrowserWindow, 0x101, (nint)key, (nint)(flags | (1L << 30) | (1L << 31)));
    }

    private void CheckIndicatorIntent(IndicatorCapture before, IndicatorCapture after, string receiptType)
    {
        Check(after.Dom.GetProperty("scroll")[1].GetDouble() > before.Dom.GetProperty("scroll")[1].GetDouble(), "Indicator visibility is backed by actual DOM movement.");
        Check(after.Dom.GetProperty("receipts").EnumerateArray().Any(receipt => receipt.GetProperty("type").GetString() == receiptType &&
            receipt.GetProperty("trusted").GetBoolean()), "The real document receives the trusted native input event.");
        Check(Indicator(after.Host).GetProperty("inputSequence").GetUInt64() > Indicator(before.Host).GetProperty("inputSequence").GetUInt64(),
            "Indicator telemetry advances its native input sequence.");
        Check(Indicator(after.Host).GetProperty("confirmedMotionSequence").GetUInt64() > Indicator(before.Host).GetProperty("confirmedMotionSequence").GetUInt64(),
            "The native renderer independently confirms user-originated scroll motion.");
        Check(before.Dom.GetProperty("fixed").GetRawText() == after.Dom.GetProperty("fixed").GetRawText(), "Indicator input does not alter the fixed marker's authored geometry.");
    }

    private async Task<IndicatorCapture> WaitIndicatorFade(string name)
    {
        var clock = Stopwatch.StartNew();
        var samples = new List<object>();
        while (clock.Elapsed < TimeSpan.FromSeconds(10))
        {
            var state = State();
            samples.Add(new { elapsedMilliseconds = clock.Elapsed.TotalMilliseconds, indicator = Indicator(state).Clone(), scrollY = state.GetProperty("scrollY") });
            if (!IndicatorShown(state)) break;
            await Task.Delay(50);
        }
        Save("scroll-indicator-" + name + "-timing", JsonSerializer.SerializeToElement(new {
            samples, boundMilliseconds = 10000, note = "Bounded acceptance wait, not a measured Safari fade constant"
        }));
        var capture = await CaptureIndicator(name);
        Check(!IndicatorShown(capture.Host), "After native motion, the indicator fades while idle at a nonzero scroll position.");
        return capture;
    }

    private static IndicatorIdleCounters IndicatorIdleState(JsonElement host) => new(
        host.GetProperty("webKit").GetProperty("renderingPipeline").GetProperty("updates").GetUInt64(),
        Indicator(host).GetProperty("queryCount").GetInt64(),
        host.GetProperty("webKit").GetProperty("composition").GetProperty("resources").GetProperty("sceneGeneration").GetUInt64(),
        host.GetProperty("webKit").GetProperty("continuation").GetProperty("fixedContainerColorExtension").GetProperty("revision").GetInt64(),
        host.GetProperty("contentColor").GetProperty("revision").GetInt64(),
        host.GetProperty("statusWidgets").GetProperty("clockText").GetString());

    private static bool IndicatorSamplesIdle(JsonElement host) => !IndicatorShown(host) &&
        !Indicator(host).GetProperty("inFlight").GetBoolean() &&
        !host.GetProperty("webKit").GetProperty("continuation").GetProperty("fixedContainerColorExtension").GetProperty("inFlight").GetBoolean() &&
        !host.GetProperty("contentColor").GetProperty("inFlight").GetBoolean();

    private async Task CheckIndicatorStaticIdle()
    {
        Check(State().GetProperty("webKit").GetProperty("renderingPipeline").GetProperty("available").GetBoolean(),
            "The static-idle check requires an available native rendering counter.");
        var attempts = new List<object>();
        var clockControlled = false;
        var nativeStable = false;
        var queriesStable = false;
        var sceneStable = false;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var settleClock = Stopwatch.StartNew();
            var previous = State();
            var stableSamples = 0;
            var settling = new List<object>();
            while (settleClock.Elapsed < TimeSpan.FromSeconds(5) && stableSamples < 2)
            {
                await Task.Delay(200);
                var current = State();
                var counters = IndicatorIdleState(current);
                stableSamples = IndicatorSamplesIdle(previous) && IndicatorSamplesIdle(current) &&
                    IndicatorIdleState(previous) == counters ? stableSamples + 1 : 0;
                settling.Add(new { elapsedMilliseconds = settleClock.Elapsed.TotalMilliseconds, counters, stableSamples });
                previous = current;
            }
            if (stableSamples < 2)
            {
                Save("scroll-indicator-static-idle", JsonSerializer.SerializeToElement(new { attempts, settling, lastHost = previous,
                    settled = false, note = "No stable idle interval; no counters were exempted." }));
                Check(false, "The faded static fixture settles native and color sampling within the bounded wait.");
            }
            var before = State();
            var pairClock = Stopwatch.StartNew();
            await Task.Delay(600);
            var after = State();
            var a = IndicatorIdleState(before);
            var b = IndicatorIdleState(after);
            clockControlled = a.ClockText == b.ClockText;
            nativeStable = a.NativeUpdates == b.NativeUpdates;
            queriesStable = a.QueryCount == b.QueryCount;
            sceneStable = a.SceneGeneration == b.SceneGeneration;
            attempts.Add(new { settling, before, after, beforeCounters = a, afterCounters = b,
                elapsedMilliseconds = pairClock.Elapsed.TotalMilliseconds, clockControlled, nativeStable, queriesStable, sceneStable });
            if (clockControlled) break;
            // A real minute change may legitimately repaint chrome. Retain that pair and retry once.
        }
        Save("scroll-indicator-static-idle", JsonSerializer.SerializeToElement(new { attempts, clockControlled, nativeStable, queriesStable, sceneStable,
            note = "Actual host snapshots after fade; a changed visible clock permits one recorded retry, not a counter exemption." }));
        Check(clockControlled, "The static-idle comparison excludes a visible clock-minute transition.");
        Check(nativeStable, "The static fixture has no native rendering update during the idle comparison.");
        Check(queriesStable, "An unchanged native rendering counter does not trigger permanent scroll-indicator IPC queries.");
        Check(sceneStable, "A faded static indicator does not trigger permanent full-scene composition refreshes.");
    }

    private async Task<IndicatorCapture> CaptureIndicator(string name)
    {
        await Task.Delay(40);
        await Wait(state => {
            var currentGeometry = state.GetProperty("webKit").GetProperty("logicalGeometry");
            return Indicator(state).GetProperty("nativeViewportHeightCssPx").GetDouble() == currentGeometry.GetProperty("height").GetDouble() -
                currentGeometry.GetProperty("top").GetDouble() - currentGeometry.GetProperty("bottom").GetDouble();
        }, "Native indicator viewport sampling catches up with the current chrome reservation");
        var host = State();
        var dom = await IndicatorDom();
        var browser = Web.WindowsPanel!.Browser;
        var geometry = host.GetProperty("webKit").GetProperty("logicalGeometry");
        var widths = dom.GetProperty("widths").EnumerateArray().Select(value => value.GetDouble()).ToArray();
        var expectedWidth = geometry.GetProperty("width").GetDouble() - geometry.GetProperty("left").GetDouble() - geometry.GetProperty("right").GetDouble();
        var zeroGutter = widths.All(value => value == expectedWidth) && expectedWidth == 382;
        var viewportHeightAccepted = dom.GetProperty("height").GetDouble() == geometry.GetProperty("height").GetDouble() -
            geometry.GetProperty("top").GetDouble() - geometry.GetProperty("bottom").GetDouble();
        var nativeViewportHeightAccepted = Indicator(host).GetProperty("nativeViewportHeightCssPx").GetDouble() == dom.GetProperty("height").GetDouble();
        var engineScrollbarsHidden = Indicator(host).GetProperty("engineScrollbarsHidden").GetBoolean();
        var fixedGeometryAccepted = dom.GetProperty("fixed").EnumerateArray().Select(value => value.GetDouble()).SequenceEqual(new[] { 12.0, 16.0, 24.0, 12.0 });
        var contentHeight = dom.GetProperty("configuredShortPage").GetBoolean() ? 200 : 2400;
        var scrollRangeAccepted = dom.GetProperty("contentHeight").GetDouble() == contentHeight &&
            dom.GetProperty("document")[1].GetDouble() == Math.Max(contentHeight, dom.GetProperty("clientHeight").GetDouble()) &&
            dom.GetProperty("maxY").GetDouble() == Math.Max(0, contentHeight - dom.GetProperty("clientHeight").GetDouble());
        var prefix = Path.Combine(Program.Output, "scroll-indicator-" + name);
        var sourcePath = prefix + "-source.png";
        var sourcePoints = new List<object>();
        var sourceStripAccepted = true;
        using (var source = NativePaintEvidence.CaptureLiveRaster(browser))
        {
            source.Save(sourcePath, ImageFormat.Png);
            foreach (var offset in new[] { 1, 2, 4, 8, 12, 16 })
                foreach (var fraction in new[] { .1, .25, .5, .75, .9 })
                {
                    var x = (int)Math.Floor((geometry.GetProperty("left").GetDouble() + expectedWidth - offset) * browser.BackingScale);
                    var y = (int)Math.Floor((geometry.GetProperty("top").GetDouble() + dom.GetProperty("height").GetDouble() * fraction) * browser.BackingScale);
                    var pixel = source.GetPixel(x, y);
                    var accepted = Math.Abs(pixel.R - 228) <= 1 && Math.Abs(pixel.G - 234) <= 1 && Math.Abs(pixel.B - 242) <= 1;
                    sourceStripAccepted &= accepted;
                    sourcePoints.Add(new { x, y, rgb = new[] { (int)pixel.R, pixel.G, pixel.B }, accepted });
                }
        }
        var bounds = browser.PresentationBounds!(browser.ClientRectangle);
        var presenter = (WindowsScreenPresenter)Web.WindowsPanel.Parent!;
        var expectedForeground = PresentationGetAncestor(presenter.Handle, 2);
        if (PresentationGetForegroundWindow() != expectedForeground)
        {
            window.Activate();
            PresentationSetForegroundWindow(expectedForeground);
            await Task.Delay(200);
        }
        var foreground = PresentationGetForegroundWindow();
        var validDesktop = System.Windows.Forms.SystemInformation.VirtualScreen.Contains(bounds) && foreground == expectedForeground;
        var presentedPath = prefix + "-presented.png";
        if (validDesktop)
        {
            using var desktop = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppRgb);
            using (var graphics = Graphics.FromImage(desktop)) graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            desktop.Save(presentedPath, ImageFormat.Png);
        }
        static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
        Save("scroll-indicator-" + name, JsonSerializer.SerializeToElement(new {
            host, dom, assertions, zeroGutter, viewportHeightAccepted, nativeViewportHeightAccepted, fixedGeometryAccepted, scrollRangeAccepted, engineScrollbarsHidden, sourcePoints, sourceStripAccepted, bounds, validDesktop,
            foreground = foreground.ToInt64(), expectedForeground = expectedForeground.ToInt64(),
            virtualScreen = System.Windows.Forms.SystemInformation.VirtualScreen,
            clientRectangle = browser.ClientRectangle, windowPosition = window.Position, windowState = window.WindowState.ToString(),
            source = new { path = Path.GetFileName(sourcePath), sha256 = Hash(sourcePath) },
            presented = new { path = Path.GetFileName(presentedPath), sha256 = validDesktop ? Hash(presentedPath) : null }
        }));
        Check(validDesktop, "Actual desktop indicator evidence requires the complete owned foreground scene on screen.");
        Check(zeroGutter, "Shown and hidden indicators reserve zero gutter in every actual DOM width.");
        Check(viewportHeightAccepted, "The renderer viewport follows the existing chrome reservation without an indicator inset.");
        Check(nativeViewportHeightAccepted, "Native indicator sampling uses the actual DOM viewport height, including compact chrome transitions.");
        Check(fixedGeometryAccepted && scrollRangeAccepted, "The authored fixed rectangle and content-derived scroll range remain unchanged by indicator presentation.");
        Check(engineScrollbarsHidden, "Safari presentation suppresses the underlying page scrollbar painter.");
        Check(sourceStripAccepted, "The actual viewport's right edge contains ordinary page pixels, not a second engine scrollbar.");
        return new IndicatorCapture(host, dom, presentedPath);
    }

    private void CheckIndicatorPaint(IndicatorCapture hidden, IndicatorCapture shown, string reason)
    {
        var thumb = Indicator(shown.Host).GetProperty("thumbFrameSceneCssPx");
        var geometry = shown.Host.GetProperty("webKit").GetProperty("logicalGeometry");
        var x = thumb.GetProperty("x").GetDouble(); var y = thumb.GetProperty("y").GetDouble();
        var width = thumb.GetProperty("width").GetDouble(); var height = thumb.GetProperty("height").GetDouble();
        var sceneWidth = geometry.GetProperty("width").GetDouble();
        var rightInset = geometry.GetProperty("right").GetDouble();
        Check(width > 0 && height > 0 && x >= sceneWidth - rightInset && x + width <= sceneWidth &&
            x + width > sceneWidth - rightInset / 10, "The thumb is painted at the whole screen's outer right edge, across the reserved side inset.");
        using var before = new Bitmap(hidden.PresentedPath);
        using var after = new Bitmap(shown.PresentedPath);
        Check(before.Size == after.Size, "Indicator pixel comparisons retain the same complete scene raster size.");
        var scale = shown.Host.GetProperty("webKit").GetProperty("backingScaleFactor").GetDouble();
        var points = new List<object>();
        var changed = 0;
        var matchingBlendPixels = 0;
        var matchingRows = new HashSet<int>();
        var opacity = Indicator(shown.Host).GetProperty("opacity").GetDouble();
        var color = Indicator(shown.Host).GetProperty("colorRgb").EnumerateArray().Select(value => value.GetInt32()).ToArray();
        foreach (var fractionY in new[] { .2, .35, .5, .65, .8 })
            foreach (var fractionX in new[] { .25, .5, .75 })
            {
                var px = Math.Clamp((int)Math.Floor((x + width * fractionX) * scale), 0, after.Width - 1);
                var py = Math.Clamp((int)Math.Floor((y + height * fractionY) * scale), 0, after.Height - 1);
                var a = before.GetPixel(px, py); var b = after.GetPixel(px, py);
                var delta = Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
                if (delta >= 12) changed++;
                var background = new[] { (int)a.R, a.G, a.B };
                var actual = new[] { (int)b.R, b.G, b.B };
                var knownFlatBackground = Math.Abs(a.R - 228) <= 5 && Math.Abs(a.G - 234) <= 5 && Math.Abs(a.B - 242) <= 5;
                var expected = background.Select((value, index) => (int)Math.Round(value * (1 - opacity) + color[index] * opacity)).ToArray();
                var blendDelta = actual.Select((value, index) => Math.Abs(value - expected[index])).Max();
                if (knownFlatBackground && blendDelta <= 8 && delta >= 12) { matchingBlendPixels++; matchingRows.Add(py); }
                points.Add(new { px, py, hiddenRgb = background, shownRgb = actual, expectedRgb = expected,
                    delta, blendDelta, knownFlatBackground });
            }
        Save("scroll-indicator-pixels-" + Path.GetFileNameWithoutExtension(shown.PresentedPath) + "-" + Path.GetFileNameWithoutExtension(hidden.PresentedPath),
            JsonSerializer.SerializeToElement(new { reason, points, changed, matchingBlendPixels, matchingRows = matchingRows.Count, opacity, color, thumb,
                note = "Visibility and location proof from native desktop pixels; no exact Safari thickness or timing calibration" }));
        Check(changed > 0, "The declared host thumb actually changes desktop pixels: " + reason);
        Check(matchingBlendPixels >= 3 && matchingRows.Count >= 2,
            "Multiple thumb-interior pixels match its declared color/opacity over the known flat background, rather than unrelated chrome changes: " + reason);
    }
}
