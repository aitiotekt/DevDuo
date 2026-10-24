using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Controls;

namespace DevDuo.Windows.Tests;

internal sealed partial class WindowsChecks
{
    private sealed record OverlaySample(double X, double Y, int[] Rgb, string Kind = "ordinary-flow");
    private sealed record OverlayEdgePoint(string Edge, double X, double Y);

    private async Task CheckFixedOverlay()
    {
        await Wait(state => Text(state, "initializationState", "ready"), "Fixed overlay runtime initialization");
        Select(Settings.Fold, 0);
        Select(Settings.Scale, 1);
        window.WindowState = WindowState.Maximized;
        window.Activate();
        await Task.Delay(350);
        Check(State().GetProperty("bodyRotationDegrees").GetInt32() == 0,
            "The scoped fixed-overlay matrix uses the initial Closed body pose.");
        Select(Settings.Scale, 0);
        await CheckInitialEdgeLoads();
        Select(Settings.Scale, 1);
        Web.WindowsPanel!.ShowHtml(File.ReadAllText("tests/DevDuo.WebKitProbe/pages/fixed-overlay.html"));
        await Wait(state => Text(state, "pageTitle", "Fixed translucent overlay acceptance") && !Web.Navigation.IsLoading,
            "Independent fixed overlay fixture");
        await WaitForScript("document.readyState", value => value == "complete", "Fixed overlay fixture completion");
        window.Activate();
        PresentationSetForegroundWindow(PresentationGetAncestor(Web.WindowsPanel.Browser.BrowserWindow, 2));
        PresentationSetFocus(Web.WindowsPanel.Browser.BrowserWindow);
        await Task.Delay(150);
        var captures = new List<string>();
        foreach (var mode in new[] { "fixed", "dialog" })
        {
            foreach (var compact in new[] { false, true })
            {
                // A wheel at the document limit has no motion. Give it room,
                // then expire input attribution before script positioning.
                await Web.EvaluateResultAsync("setFixedOverlay(false);scrollTo(0,480);'wheel-room'");
                await Task.Delay(400);
                await ScrollNativeUntil(compact);
                await Task.Delay(400);
                foreach (var position in new[] { "middle", "near-end", "end" })
                {
                    var bottom = State().GetProperty("webKit").GetProperty("logicalGeometry").GetProperty("bottom").GetInt32();
                    var target = position switch
                    {
                        "middle" => "480",
                        "near-end" => "document.scrollingElement.scrollHeight - innerHeight - " + bottom + " / 2",
                        _ => "document.scrollingElement.scrollHeight - innerHeight"
                    };
                    await Web.EvaluateResultAsync("scrollTo(0," + target + ");window.__devduoReportContainerObservation();'scrolled'");
                    await Task.Delay(250);
                    Check(Text(State(), "chromeState", compact ? "compact" : "expanded"),
                        "Programmatic positioning preserves the native wheel-selected chrome state.");
                    await CheckFixedOverlayToggle(mode + "-" + (compact ? "compact-" : "expanded-") + position, mode, false, captures);
                }
            }
        }
        await Web.EvaluateResultAsync("scrollTo(0,480);'positioned'");
        var equivalentOpacityFailures = new List<string>();
        foreach (var mode in new[] { "opacity", "passive" })
        {
            try { await CheckFixedOverlayToggle(mode, mode, false, captures); }
            catch (Exception error) when (mode == "opacity" && error is InvalidOperationException or TimeoutException)
            {
                // Pinned native opacity handling is a separate capability. Keep
                // its failure while still collecting the other independent cases.
                equivalentOpacityFailures.Add(error.ToString());
                Save("fixed-overlay-opacity-failure", JsonSerializer.SerializeToElement(new { error = error.ToString(), host = State() }));
            }
        }
        await CheckFixedOverlayToggle("opaque-footer", "fixed", true, captures);
        await CaptureFixedOverlay("fixed-overlay-footer-reopened", true, true, "fixed");
        await CaptureFixedOverlay("fixed-overlay-footer-reclosed", false, true, "fixed");
        foreach (var mode in new[] { "fixed", "dialog" })
        {
            await Web.EvaluateResultAsync("window.fixedOverlayRootRgb=[255,255,255];window.fixedOverlayRgb=[0,0,0];window.fixedOverlayAlpha=.5;'reset-colors'");
            var before = (await CaptureFixedOverlay("fixed-overlay-" + mode + "-before-color", true, false, mode)).Dom.GetProperty("identity").GetRawText();
            await Web.EvaluateResultAsync("window.fixedOverlayRootRgb=[64,128,192];window.fixedOverlayRgb=[40,80,160];window.fixedOverlayAlpha=.25;'colors'");
            var changed = await CaptureFixedOverlay("fixed-overlay-" + mode + "-changed-color", true, false, mode);
            Check(changed.Dom.GetProperty("identity").GetRawText() == before, "Changing root and mask paint colors preserves all document geometry and scroll.");
            await CaptureFixedOverlay("fixed-overlay-" + mode + "-changed-color-hidden", false, false, mode);
        }
        // Exercise the actual left inset, then the top inset in the Open shell.
        for (var index = 0; index < 3; index++) Rotate();
        await LoadFixedOverlayDocument();
        await CheckFixedOverlayToggle("left-inset", "fixed", false, captures);
        await CheckFixedOverlayToggle("left-inset-footer", "fixed", true, captures);
        Select(Settings.Fold, 1);
        await LoadFixedOverlayDocument();
        await CheckFixedOverlayToggle("top-inset", "dialog", false, captures);
        await CaptureFixedOverlay("fixed-overlay-before-navigation", true, false, "fixed");
        var oldDocument = State().GetProperty("pageDocumentId").GetString();
        await LoadFixedOverlayDocument();
        Check(State().GetProperty("pageDocumentId").GetString() != oldDocument, "Navigation establishes a new document before clearing old fixed-container colors.");
        await CaptureFixedOverlay("fixed-overlay-after-navigation", false, false, "fixed");
        await Task.Delay(300);
        var beforeIdle = FixedExtension(State()).GetProperty("revision").GetUInt64();
        await Task.Delay(800);
        var afterIdle = FixedExtension(State()).GetProperty("revision").GetUInt64();
        Check(afterIdle - beforeIdle <= 1, "A static page does not sustain a sample, paint and dirty-query feedback cycle.");
        Save("fixed-overlay-result", JsonSerializer.SerializeToElement(new {
            assertions = assertions + (equivalentOpacityFailures.Count == 0 ? 1 : 0),
            viewportAndInsetMatrixPassed = equivalentOpacityFailures.Count == 0, rgbaAndDialogInsetMatrixPassed = true,
            matrixCaptures = captures, equivalentOpacityFailures, beforeIdle, afterIdle,
            bottomInsetAlphaExtensionSupported = equivalentOpacityFailures.Count == 0,
            safariComparisonAccepted = false,
            limitation = "Native Windows acceptance of the supplied Safari behavior contract. Fully chrome-covered edges use engine RGB/state checks rather than claiming unobscured desktop pixel verification. No new Safari capture was performed by this test."
        }));
        Check(equivalentOpacityFailures.Count == 0, "Equivalent CSS opacity failed; its native pixel evidence is retained alongside the other completed cases.");
        Console.WriteLine($"PASS fixed overlay: {assertions} assertions; viewport alpha, native four-edge colors, unobscured inset pixels, dialog backdrop, equivalent masks, dynamic colors, footer priority and navigation cleanup. Safari captures remain independent evidence.");
    }

    private async Task LoadFixedOverlayDocument()
    {
        await Wait(state => Text(state, "initializationState", "ready"), "Overlay pose runtime");
        var previous = State().TryGetProperty("pageDocumentId", out var document) ? document.GetString() : null;
        Web.WindowsPanel!.ShowHtml(File.ReadAllText("tests/DevDuo.WebKitProbe/pages/fixed-overlay.html"));
        await Wait(state => Text(state, "pageTitle", "Fixed translucent overlay acceptance") && !Web.Navigation.IsLoading &&
            state.TryGetProperty("pageDocumentId", out var current) && current.GetString() != previous, "New overlay fixture document");
        await WaitForScript("document.readyState", value => value == "complete", "Overlay fixture completion");
    }

    private async Task CheckFixedOverlayToggle(string label, string mode, bool footer, List<string> captures)
    {
        string? identity = null;
        foreach (var phase in new[] { "off", "on", "restored" })
        {
            var name = "fixed-overlay-" + label + "-" + phase;
            var evidence = await CaptureFixedOverlay(name, phase == "on", footer, mode);
            var current = evidence.Dom.GetProperty("identity").GetRawText();
            identity ??= current;
            Check(current == identity, "Showing and hiding the overlay preserves document identity, viewport, scroll, range and normal-flow geometry.");
            captures.Add(name);
        }
    }

    private static JsonElement FixedExtension(JsonElement host) => host.GetProperty("webKit").GetProperty("continuation").GetProperty("fixedContainerColorExtension");

    private static int[] BlendOverlay(int[] background, int[] foreground, double alpha) => background
        .Select((value, index) => (int)Math.Round(value * (1 - alpha) + foreground[index] * alpha)).ToArray();

    private static int[] OverlayRgb(JsonElement value) => value.EnumerateArray().Select(channel => channel.GetInt32()).ToArray();

    private static bool FixedColorsMatch(JsonElement host, JsonElement dom, bool visible, bool footer)
    {
        var extension = FixedExtension(host);
        if (extension.GetProperty("inFlight").GetBoolean()) return false;
        var geometry = host.GetProperty("webKit").GetProperty("logicalGeometry");
        var mixed = BlendOverlay(OverlayRgb(dom.GetProperty("rootRgb")), OverlayRgb(dom.GetProperty("overlayRgb")), dom.GetProperty("overlayAlpha").GetDouble());
        foreach (var edge in new[] { "top", "right", "bottom", "left" })
        {
            if (geometry.GetProperty(edge).GetDouble() <= 0) continue;
            var status = extension.GetProperty(edge + "Status").GetString();
            var rgb = extension.GetProperty(edge + "Rgb");
            var expected = visible ? mixed : footer && edge == "bottom" ? new[] { 150, 80, 208 } : null;
            if (expected is null)
            {
                if (status != "none" || rgb.ValueKind != JsonValueKind.Null) return false;
            }
            else if (status != "accepted" || rgb.ValueKind != JsonValueKind.Array ||
                OverlayRgb(rgb).Where((value, index) => Math.Abs(value - expected[index]) > 1).Any()) return false;
        }
        return true;
    }

    private async Task<(JsonElement Dom, JsonElement Host, bool ViewportPixelsAccepted)> CaptureFixedOverlay(
        string name, bool visible, bool footer, string mode)
    {
        var revision = await Web.EvaluateResultAsync("String(setFixedOverlay(" + (visible ? "true" : "false") + "," +
            (footer ? "true" : "false") + "," + JsonSerializer.Serialize(mode) + "))");
        await WaitForScript("String(window.fixedOverlaySettled)", value => value == revision, "Overlay paint frames and observation");
        await Task.Delay(150);
        var browser = Web.WindowsPanel!.Browser;
        using var domDocument = JsonDocument.Parse((await Web.EvaluateResultAsync("JSON.stringify(fixedOverlayState())"))!);
        var dom = domDocument.RootElement.Clone();
        using var samplesDocument = JsonDocument.Parse((await Web.EvaluateResultAsync("JSON.stringify(fixedOverlaySamples())"))!);
        var samples = samplesDocument.RootElement.EnumerateArray().Select(item => new OverlaySample(
            item.GetProperty("x").GetDouble(), item.GetProperty("y").GetDouble(), OverlayRgb(item.GetProperty("rgb")))).ToArray();
        Check(samples.Length >= 8, "At least two complete rows provide independent real colored source-pixel samples outside the dialog and footer.");
        if (footer)
        {
            var inner = dom.GetProperty("identity").GetProperty("inner");
            samples = [.. samples, new OverlaySample(inner[0].GetDouble() / 2, inner[1].GetDouble() - 12, [150, 80, 208], "opaque-footer")];
        }
        if (visible)
        {
            var inner = dom.GetProperty("identity").GetProperty("inner");
            if (mode == "dialog")
                Check(dom.GetProperty("dialogOpen").GetBoolean() && dom.GetProperty("backdropPosition").GetString() == "fixed",
                    "The fixture uses the native modal dialog top layer and its fixed backdrop.");
            else
                Check(dom.GetProperty("mask").EnumerateArray().Select(value => value.GetDouble()).SequenceEqual(
                    new[] { 0.0, 0.0, inner[0].GetDouble(), inner[1].GetDouble() }), "The authored inset-zero mask covers the actual viewport.");
        }
        JsonElement host;
        var nativeColorsAccepted = true;
        try { host = await Wait(state => FixedColorsMatch(state, dom, visible, footer), "Native fixed-container colors: " + name); }
        catch (TimeoutException error)
        {
            host = State();
            nativeColorsAccepted = false;
            Save(name + "-native-color-failure", JsonSerializer.SerializeToElement(new { dom, host, error = error.Message }));
        }
        var geometry = host.GetProperty("webKit").GetProperty("logicalGeometry");
        var scale = browser.BackingScale;
        var top = geometry.GetProperty("top").GetDouble();
        var left = geometry.GetProperty("left").GetDouble();
        int PixelX(double x) => Math.Clamp((int)Math.Floor((left + x) * scale), 0, browser.ClientSize.Width - 1);
        int PixelY(double y) => Math.Clamp((int)Math.Floor((top + y) * scale), 0, browser.ClientSize.Height - 1);
        int[] ExpectedViewport(OverlaySample sample) => visible ? BlendOverlay(sample.Rgb,
            OverlayRgb(dom.GetProperty("overlayRgb")), dom.GetProperty("overlayAlpha").GetDouble()) : sample.Rgb;
        bool Accepted(Bitmap bitmap) => samples.All(sample => {
            var pixel = bitmap.GetPixel(PixelX(sample.X), PixelY(sample.Y));
            var channels = new[] { (int)pixel.R, pixel.G, pixel.B };
            var expected = ExpectedViewport(sample);
            return channels.Select((value, index) => Math.Abs(value - expected[index])).All(delta => delta <= 2);
        });
        var clock = Stopwatch.StartNew();
        Bitmap raster;
        bool accepted;
        do
        {
            await Task.Delay(50);
            raster = NativePaintEvidence.CaptureLiveRaster(browser);
            accepted = Accepted(raster);
            if (accepted || clock.Elapsed > TimeSpan.FromSeconds(3)) break;
            raster.Dispose();
        } while (true);
        using (raster)
        {
            var prefix = Path.Combine(Program.Output, name);
            var sourcePath = prefix + "-source.png";
            raster.Save(sourcePath, ImageFormat.Png);
            var sourceSamples = samples.Select(sample => {
                var x = PixelX(sample.X); var y = PixelY(sample.Y);
                var pixel = raster.GetPixel(x, y);
                return new { kind = sample.Kind, cssX = sample.X, cssY = sample.Y, x, y, sourceRgb = new[] { (int)pixel.R, pixel.G, pixel.B },
                    underlyingRgb = sample.Rgb, expectedRgb = ExpectedViewport(sample) };
            }).ToArray();
            var inner = dom.GetProperty("identity").GetProperty("inner");
            var viewportBottom = top + inner[1].GetDouble();
            var flow = host.GetProperty("webKit").GetProperty("continuation").GetProperty("validFlowRectanglePx");
            var insetSamples = new List<object>();
            var insetPoints = new List<OverlayEdgePoint>();
            foreach (var edge in new[] { "top", "right", "bottom", "left" })
            {
                if (geometry.GetProperty(edge).GetDouble() <= 0) continue;
                foreach (var fraction in new[] { .125, .375, .625, .875 })
                {
                    var cssX = edge switch { "left" => left - 2, "right" => left + inner[0].GetDouble() + 2, _ => left + inner[0].GetDouble() * fraction };
                    var cssY = edge switch { "top" => top - 2, "bottom" => viewportBottom + 2, _ => top + inner[1].GetDouble() * fraction };
                    var point = new OverlayEdgePoint(edge, cssX, cssY);
                    insetPoints.Add(point);
                    var x = Math.Clamp((int)Math.Floor(cssX * scale), 0, raster.Width - 1);
                    var y = Math.Clamp((int)Math.Floor(cssY * scale), 0, raster.Height - 1);
                    var pixel = raster.GetPixel(x, y);
                    insetSamples.Add(new { edge, x, y, cssX, cssY,
                        withinValidDocumentFlow = x >= flow[0].GetInt32() && x < flow[2].GetInt32() && y < flow[3].GetInt32(),
                        sourceRgb = new[] { (int)pixel.R, pixel.G, pixel.B } });
                }
            }
            var cornerY = geometry.GetProperty("height").GetDouble() - 12;
            if (geometry.GetProperty("bottom").GetDouble() > 0)
            {
                if (geometry.GetProperty("right").GetDouble() > 0)
                    insetPoints.Add(new("right-corner", left + inner[0].GetDouble() + 2, cornerY));
                if (left > 0) insetPoints.Add(new("left-corner", left - 2, cornerY));
            }
            var bounds = browser.PresentationBounds!(browser.ClientRectangle);
            var virtualScreen = System.Windows.Forms.SystemInformation.VirtualScreen;
            if (!virtualScreen.Contains(bounds))
                Save(name + "-desktop-bounds-failure", JsonSerializer.SerializeToElement(new {
                    host, dom, bounds, virtualScreen, windowPosition = window.Position, windowState = window.WindowState.ToString()
                }));
            Check(virtualScreen.Contains(bounds), "The full presented fixture must fit on the actual desktop for screenshot evidence.");
            var presenter = (DevDuo.WindowsScreenPresenter)Web.WindowsPanel.Parent!;
            var expectedForeground = PresentationGetAncestor(presenter.Handle, 2);
            if (PresentationGetForegroundWindow() != expectedForeground)
            {
                window.Activate();
                PresentationSetForegroundWindow(expectedForeground);
                await Task.Delay(200);
            }
            var actualForeground = PresentationGetForegroundWindow();
            var foregroundOwnedWindow = actualForeground == expectedForeground;
            var presentedPath = prefix + "-presented.png";
            var presentedInsetSamples = new List<object>();
            var visibleEdgeDeltas = new Dictionary<string, List<int>>();
            using (var presented = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppRgb))
            {
                using (var graphics = Graphics.FromImage(presented))
                    graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
                presented.Save(presentedPath, ImageFormat.Png);
                foreach (var point in insetPoints)
                {
                    var sourceX = Math.Clamp((int)Math.Floor(point.X * scale), 0, raster.Width - 1);
                    var sourceY = Math.Clamp((int)Math.Floor(point.Y * scale), 0, raster.Height - 1);
                    var screenPoint = browser.PresentationBounds!(new Rectangle(sourceX, sourceY, 1, 1)).Location;
                    var x = screenPoint.X - bounds.X; var y = screenPoint.Y - bounds.Y;
                    var pixel = presented.GetPixel(x, y);
                    var coveredByChrome = host.GetProperty("chromeElements").EnumerateArray().Any(item => {
                        var box = item.GetProperty("visualFrameSceneCssPx");
                        return point.X >= box.GetProperty("x").GetDouble() - 12 && point.Y >= box.GetProperty("y").GetDouble() - 12 &&
                            point.X < box.GetProperty("x").GetDouble() + box.GetProperty("width").GetDouble() + 12 &&
                            point.Y < box.GetProperty("y").GetDouble() + box.GetProperty("height").GetDouble() + 12;
                    });
                    var expected = ExpectedOverlayInset(point, dom, visible, footer, left, top);
                    var actual = new[] { (int)pixel.R, pixel.G, pixel.B };
                    var delta = actual.Select((value, index) => Math.Abs(value - expected[index])).Max();
                    if (!coveredByChrome)
                    {
                        if (!visibleEdgeDeltas.TryGetValue(point.Edge, out var deltas)) visibleEdgeDeltas.Add(point.Edge, deltas = []);
                        deltas.Add(delta);
                    }
                    presentedInsetSamples.Add(new { edge = point.Edge, sourceX, sourceY, x, y, coveredByChrome,
                        screenX = screenPoint.X, screenY = screenPoint.Y,
                        presentedRgb = actual, expectedRgb = expected, maximumChannelDelta = delta });
                }
            }
            static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
            Save(name, JsonSerializer.SerializeToElement(new {
                host, dom, sourceSamples, insetSamples, presentedInsetSamples, nativeColorsAccepted, viewportPixelsChecked = samples.Length > 0,
                foregroundOwnedWindow, expectedForeground = expectedForeground.ToInt64(), actualForeground = actualForeground.ToInt64(),
                viewportPixelsAccepted = samples.Length > 0 ? accepted : (bool?)null,
                source = new { path = Path.GetFileName(sourcePath), sha256 = Hash(sourcePath), method = "Existing source HWND raster; no paint requested" },
                presented = new { path = Path.GetFileName(presentedPath), sha256 = Hash(presentedPath), screenBounds = bounds,
                    method = "Desktop pixels; includes host continuation and chrome, may differ from source due to host materials" },
                insetCompatibility = "Native engine edge RGB is checked on every actual inset. Desktop colors are checked only outside chrome and its shadow margin; fully covered edges retain source/metadata evidence without a desktop-color claim.",
                paintWaitMilliseconds = clock.Elapsed.TotalMilliseconds
            }));
            Check(foregroundOwnedWindow, "The owned test window must be foreground for presented screenshot evidence.");
            Check(nativeColorsAccepted, "Native fixed-container colors must match the authored paint and clear stale edges: " + name);
            Check(accepted, "The real viewport preserves ordinary-flow pixels and composites the authored mask at its actual alpha.");
            foreach (var edge in insetPoints.Select(point => point.Edge).Distinct())
            {
                if (edge != "top") Check(visibleEdgeDeltas.ContainsKey(edge), "At least one presented sample outside chrome verifies the " + edge + " inset.");
                if (visibleEdgeDeltas.TryGetValue(edge, out var deltas))
                    Check(deltas.All(delta => delta <= 4), "The presented " + edge + " inset matches the expected pure overlay/root blend or restored flow (desktop sRGB tolerance 4).");
            }
        }
        return (dom, host, accepted);
    }

    private static int[] ExpectedOverlayInset(OverlayEdgePoint point, JsonElement dom, bool visible, bool footer, double left, double top)
    {
        var root = OverlayRgb(dom.GetProperty("rootRgb"));
        if (visible) return BlendOverlay(root, OverlayRgb(dom.GetProperty("overlayRgb")), dom.GetProperty("overlayAlpha").GetDouble());
        if (footer && point.Edge == "bottom") return [150, 80, 208];
        var identity = dom.GetProperty("identity");
        var documentX = point.X - left + identity.GetProperty("scroll")[0].GetDouble();
        var documentY = point.Y - top + identity.GetProperty("scroll")[1].GetDouble();
        if (documentX < 0 || documentY < 0 || documentX >= identity.GetProperty("document")[0].GetDouble() ||
            documentY >= identity.GetProperty("document")[1].GetDouble()) return root;
        int[][] palette = [[240, 176, 64], [48, 176, 224], [208, 64, 128], [80, 208, 112]];
        var column = (int)(documentX / (identity.GetProperty("inner")[0].GetDouble() / 4));
        return palette[(column + (int)(documentY / 48)) % 4];
    }
}
