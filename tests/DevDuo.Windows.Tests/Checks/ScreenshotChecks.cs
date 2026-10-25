using System.Drawing;
using System.Drawing.Imaging;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using Avalonia.Controls;

namespace DevDuo.Windows.Tests;

internal sealed partial class WindowsChecks
{
    private async Task CheckScreenshots()
    {
        using var server = new BrowserTestServer();
        await Wait(state => Text(state, "initializationState", "ready"), "Screenshot runtime");
        window.WindowState = WindowState.Maximized;
        window.Activate();
        Select(Settings.Scale, 1);
        var port = AvailableDeveloperPort();
        Settings.ControlApiPort.Text = port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Settings.ControlApi.IsChecked = true;
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/"), Timeout = TimeSpan.FromSeconds(15) };
        await Wait(_ => Uri.TryCreate(Settings.ControlApiAddress.Text, UriKind.Absolute, out var address) && address.Port == port,
            "Screenshot REST listener");
        try
        {
            foreach (var fold in new[] { "closed", "open" })
                foreach (var angle in new[] { 0, 90, 180, 270 })
                {
                    using (var settings = await http.PatchAsJsonAsync("api/settings", new { fold, bodyAngle = angle, previewScale = "fit" }))
                        settings.EnsureSuccessStatusCode();
                    await Wait(state => Text(state, "initializationState", "ready"), "Screenshot scene ready");
                    var fixtureUrl = BrowserTestServer.Address + $"screenshot.html?fold={fold}&angle={angle}";
                    using (var navigation = await http.PostAsJsonAsync("api/navigation", new { action = "navigate", url = fixtureUrl }))
                        navigation.EnsureSuccessStatusCode();
                    await WaitForScript("location.href", value => value == fixtureUrl, "Screenshot navigation");
                    await WaitForScript("typeof screenshotFixtureState", value => value == "function", "Screenshot fixture");
                    await WaitForScript("document.readyState", value => value == "complete", "Screenshot fixture complete");
                    await Web.EvaluateResultAsync("scrollTo(0,800)");
                    await WaitForScript("String(scrollY)", value => double.TryParse(value, out var y) && y > 0, "Screenshot scrolled fixture");
                    var before = await ScreenshotFixture();
                    await WaitScreenshotPaint(before, $"{fold}-{angle}");
                    foreach (var target in new[] { "viewport", "device" })
                    {
                        using var response = await http.PostAsJsonAsync("api/screenshot", new { target });
                        response.EnsureSuccessStatusCode();
                        var bytes = await response.Content.ReadAsByteArrayAsync();
                        File.WriteAllBytes(Path.Combine(Program.Output, $"{fold}-{angle}-{target}.png"), bytes);
                        Save($"{fold}-{angle}-{target}-state", JsonSerializer.SerializeToElement(new { before, host = State() }));
                        using var stream = new MemoryStream(bytes);
                        using var bitmap = new Bitmap(stream);
                        Check(response.Content.Headers.ContentType?.MediaType == "image/png" &&
                            bitmap.Width == int.Parse(response.Headers.GetValues("X-DevDuo-Width").Single()) &&
                            bitmap.Height == int.Parse(response.Headers.GetValues("X-DevDuo-Height").Single()), "Native PNG dimensions match response metadata.");
                        if (target == "viewport")
                        {
                            Check(bitmap.Width == before.GetProperty("width").GetInt32() && bitmap.Height == before.GetProperty("height").GetInt32(),
                                "Viewport screenshots use one pixel per CSS pixel.");
                            var footer = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height - 4);
                            Check(Math.Abs(footer.R - 121) < 4 && Math.Abs(footer.G - 26) < 4 && Math.Abs(footer.B - 194) < 4,
                                "The captured native viewport contains its fixed footer at the actual bottom.");
                            var marker = before.GetProperty("fixed").EnumerateArray().Single(item => item.GetProperty("id").GetString() == "marker");
                            var markerPixel = bitmap.GetPixel((int)(marker.GetProperty("x").GetDouble() + marker.GetProperty("width").GetDouble() / 2),
                                (int)(marker.GetProperty("y").GetDouble() + marker.GetProperty("height").GetDouble() / 2));
                            Check(Math.Abs(markerPixel.R - 25) < 4 && Math.Abs(markerPixel.G - 181) < 4 && Math.Abs(markerPixel.B - 105) < 4,
                                "Captured fixed-marker pixels agree with the DOM viewport coordinates.");
                        }
                        Check((await ScreenshotFixture()).GetRawText() == before.GetRawText(), "Screenshot preserves document, viewport, scroll range/position and fixed rectangles.");
                    }
                }
            var controller = Web.DeveloperTools;
            controller.ApplyCdp(true, new(false, AvailableDeveloperPort()));
            await Wait(_ => controller.CdpStatus.Address is not null && controller.CdpFrontendUrl is not null, "Native screenshot CDP target");
            var baseAddress = new Uri(controller.CdpStatus.Address!);
            using var discovery = JsonDocument.Parse(await http.GetStringAsync(new Uri(baseAddress, "json/list")));
            using var socket = new ClientWebSocket();
            await socket.ConnectAsync(new Uri(discovery.RootElement[0].GetProperty("webSocketDebuggerUrl").GetString()!), CancellationToken.None);
            var state = await ScreenshotFixture();
            var result = await DeveloperCdpCommand(socket, 1, "Page.captureScreenshot", new { format = "jpeg", quality = 85, captureBeyondViewport = false });
            var jpeg = Convert.FromBase64String(result.GetProperty("result").GetProperty("data").GetString()!);
            using var jpegStream = new MemoryStream(jpeg);
            using var decoded = System.Drawing.Image.FromStream(jpegStream);
            Check(decoded.RawFormat.Guid == ImageFormat.Jpeg.Guid && (await ScreenshotFixture()).GetRawText() == state.GetRawText(),
                "CDP returns a real native JPEG without changing the page.");
            controller.ApplyCdp(false, new(false, baseAddress.Port));
            Save("screenshots", State());
            Console.WriteLine($"PASS screenshots: {assertions} assertions; eight poses, native viewport/device PNG, fixed geometry and CDP JPEG.");
        }
        finally { Settings.ControlApi.IsChecked = false; }
    }

    private async Task<JsonElement> ScreenshotFixture()
    {
        using var document = JsonDocument.Parse((await Web.EvaluateResultAsync("JSON.stringify(screenshotFixtureState())"))!);
        return document.RootElement.Clone();
    }

    private async Task WaitScreenshotPaint(JsonElement fixture, string name)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var marker = fixture.GetProperty("fixed").EnumerateArray().Single(item => item.GetProperty("id").GetString() == "marker");
        var attempts = 0;
        while (true)
        {
            var browser = Web.WindowsPanel!.Browser;
            var geometry = State().GetProperty("webKit").GetProperty("logicalGeometry");
            var left = geometry.GetProperty("left").GetDouble();
            var top = geometry.GetProperty("top").GetDouble();
            Point Pixel(double x, double y) => new(WindowsWebKitView.RasterClipEdge((left + x) * browser.BackingScale),
                WindowsWebKitView.RasterClipEdge((top + y) * browser.BackingScale));
            var footerPoint = Pixel(fixture.GetProperty("width").GetDouble() / 2, fixture.GetProperty("height").GetDouble() - 4);
            var markerPoint = Pixel(marker.GetProperty("x").GetDouble() + marker.GetProperty("width").GetDouble() / 2,
                marker.GetProperty("y").GetDouble() + marker.GetProperty("height").GetDouble() / 2);
            // Reading the retained source does not request a paint or alter the
            // page. DOM readiness alone can precede the fixture's first frame.
            using var source = NativePaintEvidence.CaptureLiveRaster(browser);
            attempts++;
            bool Matches(Point point, int red, int green, int blue)
            {
                if (!new Rectangle(Point.Empty, source.Size).Contains(point)) return false;
                var color = source.GetPixel(point.X, point.Y);
                return Math.Abs(color.R - red) < 4 && Math.Abs(color.G - green) < 4 && Math.Abs(color.B - blue) < 4;
            }
            var accepted = Matches(footerPoint, 121, 26, 194) && Matches(markerPoint, 25, 181, 105);
            if (!accepted && attempts == 1)
                source.Save(Path.Combine(Program.Output, name + "-pending-source.png"), ImageFormat.Png);
            if (accepted || elapsed.Elapsed > TimeSpan.FromSeconds(3))
            {
                Save(name + "-paint-readiness", JsonSerializer.SerializeToElement(new { accepted, attempts, elapsedMilliseconds = elapsed.Elapsed.TotalMilliseconds, fixture, host = State() }));
                Check(accepted, "The authored screenshot fixture reaches the live native source without forcing repaint.");
                return;
            }
            await Task.Delay(50);
        }
    }
}
