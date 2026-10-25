using System.Drawing.Imaging;
using System.Text.Json;
using DevDuo;

namespace DevDuo.Windows.Tests;

internal sealed partial class WindowsChecks
{
    private async Task CheckInitialEdgeLoads()
    {
        using var server = new BrowserTestServer();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            foreach (var kind in new[] { "absolute", "mask" })
            {
                var label = "initial-edge-" + kind + "-" + attempt;
                var imageRequests = server.Requests.Count(request => request.Path.StartsWith("/initial-edge.svg", StringComparison.Ordinal));
                Web.Navigate(BrowserTestServer.Address + "initial-edge.html?kind=" + kind + "&case=" + Guid.NewGuid().ToString("N"));
                await Wait(state => Text(state, "pageTitle", "Initial edge " + kind) && !Web.Navigation.IsLoading,
                    "Uncached initial document and delayed background image: " + label);
                Check(server.Requests.Count(request => request.Path.StartsWith("/initial-edge.svg", StringComparison.Ordinal)) > imageRequests,
                    "Each initial document really completes a new delayed image request.");
                var expected = kind == "mask" ? 0 : 255;
                var state = await Wait(state => Text(state, "tintSource", "WebKit native edge average") &&
                    state.GetProperty("tintRgb").EnumerateArray().All(value => Math.Abs(value.GetInt32() - expected) <= 3),
                    "Final native tint without refreshing or changing DOM background: " + label);
                var json = await Web.EvaluateResultAsync("JSON.stringify({origin:performance.timeOrigin,viewport:[innerWidth,innerHeight],scroll:[scrollX,scrollY],range:document.scrollingElement.scrollHeight-innerHeight,banner:document.getElementById('banner').getBoundingClientRect().toJSON(),ready:document.readyState})");
                using var dom = JsonDocument.Parse(json ?? throw new InvalidOperationException("Initial edge DOM missing"));
                var viewport = dom.RootElement.GetProperty("viewport");
                var banner = dom.RootElement.GetProperty("banner");
                Check(viewport[0].GetDouble() == 382 && viewport[1].GetDouble() == 580,
                    "Initial edge acceptance retains the expanded Closed portrait viewport.");
                Check(Math.Abs(banner.GetProperty("bottom").GetDouble() - 580) < .02,
                    "Absolute bottom:0 is laid out at the real viewport edge.");
                Check(dom.RootElement.GetProperty("scroll")[1].GetDouble() == 0 &&
                    dom.RootElement.GetProperty("range").GetDouble() == 0,
                    "Boundary handling does not add page padding, scroll or a reload.");
                var browser = Web.WindowsPanel!.Browser;
                var geometry = state.GetProperty("webKit").GetProperty("logicalGeometry");
                var scale = browser.BackingScale;
                var bottom = WindowsWebKitView.RasterClipEdge((geometry.GetProperty("top").GetInt32() + 580) * scale);
                // DOM completion and snapshot-based tint arrive independently of
                // WM_PAINT. Observe the retained raster without requesting paint.
                using var raster = await WaitInitialEdgeRaster(browser, bottom, scale, expected, label);
                raster.Save(Path.Combine(Program.Output, label + "-live-raster.png"), ImageFormat.Png);
                Save(label, JsonSerializer.SerializeToElement(new { host = state, dom = dom.RootElement, scale }));
                foreach (var offset in new[] { 1, 2 })
                {
                    foreach (var x in new[] { 40, 191, 340 })
                    {
                        var pixel = raster.GetPixel(WindowsWebKitView.RasterClipEdge(x * scale), bottom - offset);
                        Check(Math.Abs(pixel.R - expected) <= 3 && Math.Abs(pixel.G - expected) <= 3 && Math.Abs(pixel.B - expected) <= 3,
                            "Initial live source has no one-pixel seam at the absolute/mask bottom: " + label);
                    }
                }
                if (kind == "mask")
                {
                    await Wait(state => new[] { "bottom", "right" }.All(edge =>
                        FixedExtension(state).GetProperty(edge + "Status").GetString() == "accepted" &&
                        OverlayRgb(FixedExtension(state).GetProperty(edge + "Rgb")).All(value => value == 0)),
                        "Initial black mask reaches native inset metadata without a refresh: " + label);
                }
                await Task.Delay(600);
                var idle = FixedExtension(State()).GetProperty("revision").GetInt64();
                await Task.Delay(600);
                Check(FixedExtension(State()).GetProperty("revision").GetInt64() == idle,
                    "A completed static initial document does not sustain color sampling IPC.");
            }
        }
    }

    private async Task<System.Drawing.Bitmap> WaitInitialEdgeRaster(WindowsWebKitView browser, int bottom, double scale, int expected, string label)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var attempts = 0;
        while (true)
        {
            var raster = NativePaintEvidence.CaptureLiveRaster(browser);
            attempts++;
            var accepted = true;
            foreach (var offset in new[] { 1, 2 })
                foreach (var x in new[] { 40, 191, 340 })
                {
                    var pixel = raster.GetPixel(WindowsWebKitView.RasterClipEdge(x * scale), bottom - offset);
                    accepted &= Math.Abs(pixel.R - expected) <= 3 && Math.Abs(pixel.G - expected) <= 3 && Math.Abs(pixel.B - expected) <= 3;
                }
            if (!accepted && attempts == 1)
                raster.Save(Path.Combine(Program.Output, label + "-pending-live-raster.png"), ImageFormat.Png);
            if (accepted || elapsed.Elapsed > TimeSpan.FromSeconds(2))
            {
                Save(label + "-readback", JsonSerializer.SerializeToElement(new { accepted, attempts, elapsedMilliseconds = elapsed.Elapsed.TotalMilliseconds, host = State() }));
                return raster;
            }
            raster.Dispose();
            await Task.Delay(50);
        }
    }
}
