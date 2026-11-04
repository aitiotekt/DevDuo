using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using Avalonia.Controls;

namespace DevDuo.Windows.Tests;

internal sealed partial class WindowsChecks
{
    private async Task CheckNativeTouchSeam()
    {
        await Wait(state => Text(state, "initializationState", "ready"), "Touch seam runtime");
        Check(WindowsWebKitApi.SetHostTouchEnabled is not null, "Touch seam checks require the native contact API");
        window.WindowState = WindowState.Maximized;
        Select(Settings.Scale, 1);
        Select(Settings.Interaction, 0);
        var browser = Web.WindowsPanel!.Browser;
        SetPageCompositing(browser, true);
        var fixture = File.ReadAllText("tests/DevDuo.WebKitProbe/pages/native-touch.html")
            .Replace("rgb(208,48,64)", "rgb(208,224,240)")
            .Replace("rgb(32,112,192)", "rgb(240,208,224)")
            .Replace("height: 64px; background: rgb(24,144,96); pointer-events: none;", "height: 53px; z-index: 100;")
            .Replace("<div id=\"fixed\"></div>", """
                <div id="fixed"><div style="display:flex;align-items:center;justify-content:space-between;height:100%;padding:0 16px;background:rgb(0 0 0 / .8);color:white">
                    <span>Fixed footer</span><button>Continue</button>
                </div></div>
                """);
        File.WriteAllText(Path.Combine(Program.Output, "touch-seam.html"), fixture);
        Web.WindowsPanel.ShowHtml(fixture);
        await WaitForScript("typeof nativeTouchResult", value => value == "function", "Touch seam fixture");
        await Wait(state => FixedExtension(state).GetProperty("bottomRgb").ValueKind == JsonValueKind.Array, "Touch seam continuation");
        await Web.EvaluateResultAsync("scrollTo(0,1800);'positioned'");
        if (Environment.GetEnvironmentVariable("DEVDUO_TEST_TOUCH_SEAM_STATIC_ONLY") == "1")
        {
            await CheckStaticFooterAtFractionalFit(browser);
            Console.WriteLine($"PASS settled footer at fractional Fit: {assertions} assertions: {Program.Output}");
            return;
        }
        var owner = PresentationGetAncestor(browser.Handle, 2);
        window.Activate();
        PresentationSetForegroundWindow(owner);
        await Task.Delay(250);
        Check(PresentationGetForegroundWindow() == owner, "Touch seam checks require the independent foreground window");

        var first = State();
        var geometry = first.GetProperty("webKit").GetProperty("logicalGeometry");
        var x = (int)Math.Round(12 * browser.BackingScale);
        var top = (int)Math.Round((geometry.GetProperty("minimumHeight").GetInt32() - 8) * browser.BackingScale);
        var bounds = browser.PresentationBounds!(browser.ClientRectangle);
        var frames = new List<object>();
        var states = new HashSet<string>();
        var failures = 0;
        int Brightest(Bitmap bitmap) => Enumerable.Range(top, bitmap.Height - top - 4)
            .Select(y => bitmap.GetPixel(x, y)).Max(p => Math.Max(p.R, Math.Max(p.G, p.B)));
        var transitionFrames = new List<object>();
        var transitionFailures = 0;
        void InspectSubmitted(ulong frameId, bool asynchronous, WindowsComposition.Fill[] fills)
        {
            using var source = browser.CapturePageFramebuffer() ?? throw new InvalidOperationException("The submitted page framebuffer is unavailable");
            var maximum = Brightest(source);
            if (maximum > 65)
            {
                if (transitionFailures < 4) source.Save(Path.Combine(Program.Output, $"touch-seam-submitted-{frameId}.png"));
                transitionFailures++;
            }
            transitionFrames.Add(new { frameId, asynchronous, maximum,
                fills = fills.Select(fill => new { fill.X, fill.Y, fill.Width, fill.Height, fill.Color }).ToArray(),
                qpc = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency });
        }

        await InteractionPointer(0x201, 240, 380, 20);
        browser.PageFrameSubmitted += InspectSubmitted;
        try
        {
            foreach (var y in new[] { 340, 300, 260, 220, 260, 300, 340, 380, 340, 300, 260, 220, 260, 300, 340, 380 })
            {
                await InteractionPointer(0x200, 240, y, 0);
                await Task.Delay(12);
                var state = State();
                states.Add(state.GetProperty("chromeState").GetString()!);
                using var source = browser.CapturePageFramebuffer() ?? throw new InvalidOperationException("The live page framebuffer is unavailable");
                var sourceMax = Brightest(source);
                var capture = await Task.Run(() => WindowsWindowCapture.Capture(owner,
                    new Avalonia.PixelRect(bounds.X, bounds.Y, bounds.Width, bounds.Height)));
                using var stream = new MemoryStream(capture.Png);
                using var presented = new Bitmap(stream);
                var windowMax = Brightest(presented);
                // An 80% black footer over any RGB background is at most 51
                // per channel. A bright pixel in this strip exposes the page
                // between the footer and its extension in either shell state.
                var failed = sourceMax > 65 || windowMax > 65;
                if (failed || frames.Count == 0)
                {
                    source.Save(Path.Combine(Program.Output, $"touch-seam-source-{frames.Count}.png"));
                    File.WriteAllBytes(Path.Combine(Program.Output, $"touch-seam-window-{frames.Count}.png"), capture.Png);
                }
                if (failed) failures++;
                frames.Add(new { y, sourceMax, windowMax, state, qpc = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency });
            }
        }
        finally
        {
            browser.PageFrameSubmitted -= InspectSubmitted;
            await InteractionPointer(0x202, 240, 380, 20);
        }
        Save("native-touch-seam", JsonSerializer.SerializeToElement(new { frames, states, failures, transitionFrames, transitionFailures }));
        Check(states.SetEquals(new[] { "expanded", "compact" }), "Dragging traverses both actual Safari shell states");
        Check(State().GetProperty("webKit").GetProperty("frameSurface").GetProperty("asynchronousPresentations").GetInt64() >
            first.GetProperty("webKit").GetProperty("frameSurface").GetProperty("asynchronousPresentations").GetInt64(),
            "The drag presents actual independent scroll frames during the seam check");
        Check(failures == 0, $"A bright band appeared between the fixed footer and continuation in {failures} drag samples");
        Check(transitionFailures == 0, $"A bright band appeared in {transitionFailures} submitted drag frames");
        await CheckTouchSeamDuringTransitions(browser, x);
        await CheckStaticFooterAtFractionalFit(browser);
        Console.WriteLine($"PASS native touch seam: {assertions} assertions; {frames.Count} framebuffer and composed-window pairs: {Program.Output}");
    }

    private async Task CheckStaticFooterAtFractionalFit(WindowsWebKitView browser)
    {
        Select(Settings.Interaction, 1);
        for (var attempt = 0; attempt < 12 && !Text(State(), "chromeState", "compact"); attempt++)
        {
            InteractionWheel();
            await Task.Delay(120);
        }
        await Wait(state => Text(state, "chromeState", "compact") && !state.GetProperty("chromeAnimation").GetProperty("running").GetBoolean(),
            "Settled compact footer for fractional Fit");
        var measurements = new List<object>();
        var failures = 0;
        var owner = PresentationGetAncestor(browser.Handle, 2);
        window.WindowState = WindowState.Normal;
        foreach (var height in new[] { 720, 746, 783, 820, 869, 911 })
        {
            window.Width = 1250;
            window.Height = height;
            await Task.Delay(200);
            await Wait(state =>
            {
                var frame = state.GetProperty("webKit").GetProperty("frameSurface");
                return Text(frame, "lastContentTransport", "shared-d3d11") &&
                    frame.GetProperty("receivedWidth").GetInt32() == frame.GetProperty("expectedWidth").GetInt32() &&
                    frame.GetProperty("receivedHeight").GetInt32() == frame.GetProperty("expectedHeight").GetInt32();
            }, "Fractional-fit footer uses a matching native GPU frame");
            var scale = browser.BackingScale;
            var geometry = State().GetProperty("webKit").GetProperty("logicalGeometry");
            var edge = (int)Math.Floor((geometry.GetProperty("height").GetInt32() - geometry.GetProperty("bottom").GetInt32()) * (float)scale + .5f);
            var x = (int)Math.Round(12 * scale);
            using var source = browser.CapturePageFramebuffer() ?? throw new InvalidOperationException("The settled page framebuffer is unavailable");
            static int[] Brightness(Bitmap bitmap, int x, int top) => Enumerable.Range(top, 8).Select(y =>
            {
                var c = bitmap.GetPixel(x, y);
                return (int)Math.Max(c.R, Math.Max(c.G, c.B));
            }).ToArray();
            var sourceRows = Brightness(source, x, edge - 4);
            var strip = browser.PresentationBounds!(new Rectangle(x, edge - 4, 1, 8));
            var capture = await Task.Run(() => WindowsWindowCapture.Capture(owner,
                new Avalonia.PixelRect(strip.X, strip.Y, strip.Width, strip.Height)));
            using var stream = new MemoryStream(capture.Png);
            using var presented = new Bitmap(stream);
            var windowRows = Brightness(presented, 0, 0);
            measurements.Add(new { height, scale, edge, sourceRows, windowRows });
            static bool HasSeam(int[] rows) => rows.Take(4).Max() > 65 || rows.Skip(4).Any(value => value != 0);
            if (HasSeam(sourceRows) || HasSeam(windowRows))
            {
                failures++;
                source.Save(Path.Combine(Program.Output, $"touch-seam-settled-{height}.png"));
                File.WriteAllBytes(Path.Combine(Program.Output, $"touch-seam-settled-window-{height}.png"), capture.Png);
                File.WriteAllText(Path.Combine(Program.Output, $"touch-seam-settled-tree-{height}.txt"), await browser.ReadNativeScrollingStateAsync());
            }
        }
        Save("native-touch-seam-settled", JsonSerializer.SerializeToElement(new { measurements, failures }));
        Check(failures == 0, $"A bright footer boundary appeared at {failures} fractional Fit sizes");
    }

    private async Task CheckTouchSeamDuringTransitions(WindowsWebKitView browser, int x)
    {
        await Web.EvaluateResultAsync("""
            addEventListener('resize', () => {
                const until = performance.now() + 28;
                while (performance.now() < until) { /* Exercise an in-flight scroll during viewport relayout. */ }
            });
            scrollTo(0, 1800); 'ready'
            """);
        await Task.Delay(200);
        var samples = new List<object>();
        var edges = new HashSet<int>();
        var failed = 0;
        var independent = 0;
        var scale = browser.BackingScale;
        var startPoint = browser.PresentationBounds!(new Rectangle((int)Math.Round(240 * scale), (int)Math.Round(350 * scale), 1, 1)).Location;
        var footerHeight = (int)Math.Round(53 * scale);
        void Inspect(ulong id, bool asynchronous, WindowsComposition.Fill[] fills)
        {
            var fill = fills.FirstOrDefault(f => f.Color == 0 && f.X <= x && x < f.X + f.Width && f.Height > 30);
            if (fill.Height == 0) return;
            using var source = browser.CapturePageFramebuffer() ?? throw new InvalidOperationException("The committed page framebuffer is unavailable");
            var edge = (int)fill.Y;
            edges.Add(edge);
            if (asynchronous) independent++;
            bool Bright(int y)
            {
                var pixel = source.GetPixel(x, y);
                return Math.Max(pixel.R, Math.Max(pixel.G, pixel.B)) > 100;
            }
            var expectedTop = edge - footerHeight;
            var actualTop = expectedTop - 24;
            while (actualTop < expectedTop + 24 && Bright(actualTop)) actualTop++;
            var exposed = Enumerable.Range(edge - 20, 18).Count(Bright);
            if (exposed > 0 || Math.Abs(actualTop - expectedTop) > 2)
            {
                if (failed < 4) source.Save(Path.Combine(Program.Output, $"touch-seam-transition-{failed}.png"));
                failed++;
            }
            samples.Add(new { id, asynchronous, edge, expectedTop, actualTop, exposed });
        }

        await InteractionPointer(0x201, 240, 350, 20);
        browser.PageFrameSubmitted += Inspect;
        try
        {
            for (var pass = 0; pass < 10; pass++)
            {
                var direction = pass % 2 == 0 ? -1 : 1;
                var from = direction < 0 ? 0 : -160;
                for (var move = 1; move <= 32; move++)
                {
                    System.Windows.Forms.Cursor.Position = new Point(startPoint.X, startPoint.Y + from + direction * move * 5);
                    await Task.Delay(5);
                }
            }
        }
        finally
        {
            browser.PageFrameSubmitted -= Inspect;
            await InteractionPointer(0x202, 240, 350, 20);
        }
        Save("native-touch-seam-transitions", JsonSerializer.SerializeToElement(new { samples, edges, independent, failed }));
        Check(edges.Count == 2 && independent > 0, "Rapid drag captures both viewport boundaries and actual independent frames");
        Check(failed == 0, $"{failed} submitted frames had a fixed footer out of phase with the continuation boundary");
    }
}
