using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DevDuo.Windows.Tests;

internal sealed partial class WindowsChecks
{
    private async Task CheckAsyncScrolling()
    {
        await Wait(state => Text(state, "initializationState", "ready"), "Async scrolling runtime");
        Check(WindowsWebKitApi.CopyHostScrollFrames is not null, "Async scrolling requires scroll-frame metadata v1");
        Check(System.Runtime.InteropServices.Marshal.SizeOf<WindowsWebKitView.HostScrollFrame>() == 128,
            "Scroll-frame metadata has the native ABI size");
        Select(Settings.Interaction, 1);
        var fixture = "tests/DevDuo.WebKitProbe/pages/async-scroll.html";
        File.Copy(fixture, Path.Combine(Program.Output, "async-scroll.html"));
        foreach (var scale in new[] { 0, 1 })
        foreach (var pose in new[] { (Fold: 0, Angle: 0), (Fold: 1, Angle: 90), (Fold: 1, Angle: 270) })
        {
            window.WindowState = scale == 0 ? WindowState.Maximized : WindowState.Normal;
            if (scale == 1) { window.Width = 1200; window.Height = 850; }
            await Task.Delay(150);
            Select(Settings.Scale, scale);
            Select(Settings.Fold, pose.Fold);
            while (State().GetProperty("bodyRotationDegrees").GetInt32() != pose.Angle)
            {
                Settings.Rotate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(100);
            }
            await Wait(state => Text(state, "initializationState", "ready"), "Async pose runtime");
            await CheckAsyncScrollingPose(fixture, $"{scale}-{pose.Fold}-{pose.Angle}");
        }
        Console.WriteLine($"PASS async scrolling: {assertions} assertions; native busy-main-thread wheel and composed-pixel evidence: {Program.Output}");
    }

    private async Task CheckAsyncScrollingPose(string fixture, string caseName)
    {
        var browser = Web.WindowsPanel!.Browser;
        SetPageCompositing(browser, true);
        Web.WindowsPanel.ShowHtml(File.ReadAllText(fixture));
        await WaitForScript("typeof scrollingResult", value => value == "function", "Busy-main-thread fixture");
        await Wait(state => state.GetProperty("webKit").GetProperty("frameSurface").GetProperty("lastContentTransport").GetString() == "shared-d3d11",
            "Actual shared-GPU fixture presentation");
        var owner = PresentationGetAncestor(browser.Handle, 2);
        window.Activate();
        PresentationSetForegroundWindow(owner);
        await Task.Delay(150);
        Check(PresentationGetForegroundWindow() == owner, "Native wheel checks require the independent test window in front");
        foreach (var blocking in new[] { false, true })
        {
            await Web.EvaluateResultAsync($"setBlockingWheel({(blocking ? "true" : "false")});scrollTo(0,400);'positioned'");
            await WaitForScript("String(scrollY)", value => value == "400", "Reset genuine root scroll position");
            await Task.Delay(300);
            var before = State();
            var nativeState = await browser.ReadNativeScrollingStateAsync();
            File.WriteAllText(Path.Combine(Program.Output, $"async-native-state-{caseName}-{blocking}.txt"), nativeState);
            var geometry = before.GetProperty("webKit").GetProperty("logicalGeometry");
            var left = geometry.GetProperty("left").GetInt32();
            var top = geometry.GetProperty("top").GetInt32();
            var width = geometry.GetProperty("width").GetInt32();
            var height = geometry.GetProperty("height").GetInt32();
            var bottom = geometry.GetProperty("bottom").GetInt32();
            var right = geometry.GetProperty("right").GetInt32();
            var x = left + (width - left - right) / 2;
            var samples = new List<object>();
            var sampledStates = new List<JsonElement>();
            var wallAnchor = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var qpcAnchor = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
            await Web.EvaluateResultAsync("armBusyInterval(2400);'armed'");
            await Task.Delay(200);
            for (var sample = 0; sample < 3; sample++)
            {
                NativeWheel(true);
                await Task.Delay(100);
                var state = State();
                sampledStates.Add(state);
                var bounds = browser.PresentationBounds!(browser.ClientRectangle);
                var capture = await Task.Run(() => WindowsWindowCapture.Capture(owner,
                    new Avalonia.PixelRect(bounds.X, bounds.Y, bounds.Width, bounds.Height)));
                using var stream = new MemoryStream(capture.Png);
                using var bitmap = new Bitmap(stream);
                Color Pixel(int logicalY)
                {
                    var point = browser.PresentationBounds!(new Rectangle((int)Math.Round(x * browser.BackingScale),
                        (int)Math.Round(logicalY * browser.BackingScale), 1, 1));
                    return bitmap.GetPixel(point.X - bounds.X, point.Y - bounds.Y);
                }
                // Stay inside a solid stripe at every tested wheel position;
                // fractional presentation legitimately filters a stripe boundary.
                const int patternOffset = 129;
                var pattern = Pixel(top + patternOffset);
                var fixedPixel = Pixel(height - bottom - 20);
                var insetPixel = Pixel(height - 8);
                bitmap.Save(Path.Combine(Program.Output, $"async-scroll-{caseName}-{blocking}-{sample}.png"));
                samples.Add(new { sample, wallTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), pattern = new[] { (int)pattern.R, pattern.G, pattern.B },
                    fixedPixel = new[] { (int)fixedPixel.R, fixedPixel.G, fixedPixel.B }, insetPixel = new[] { (int)insetPixel.R, insetPixel.G, insetPixel.B } });
                Save($"async-scroll-{caseName}-{blocking}-{sample}", JsonSerializer.SerializeToElement(new { state, samples }));
                using var source = NativePaintEvidence.Capture(browser, state);
                source.Save(Path.Combine(Program.Output, $"async-source-{caseName}-{blocking}-{sample}.png"));
                var edges = new List<(string Side, Point Inside, Point Outside)>();
                if (top > 4) edges.Add(("top", new(x, top + 8), new(x, top / 2)));
                if (bottom > 4) edges.Add(("bottom", new(x, height - bottom - 8), new(x, height - 8)));
                if (left > 4) edges.Add(("left", new(left + 8, height / 2), new(left / 2, height / 2)));
                if (right > 4) edges.Add(("right", new(width - right - 8, height / 2), new(width - right / 2, height / 2)));
                foreach (var edge in edges)
                {
                    Color SourcePixel(Point p) => source.GetPixel((int)Math.Round(p.X * browser.BackingScale), (int)Math.Round(p.Y * browser.BackingScale));
                    var inside = SourcePixel(edge.Inside);
                    var outside = SourcePixel(edge.Outside);
                    Check(Math.Abs(inside.R - 24) <= 3 && Math.Abs(inside.G - 144) <= 3 && Math.Abs(inside.B - 96) <= 3
                        && Math.Abs(inside.R - outside.R) <= 3 && Math.Abs(inside.G - outside.G) <= 3 && Math.Abs(inside.B - outside.B) <= 3,
                        $"Presented page framebuffer preserves fixed {edge.Side} edge and continuation ({caseName})");
                }
                var latestFrame = state.GetProperty("webKit").GetProperty("frameTiming").GetProperty("scrollFrames").EnumerateArray().Last();
                var scrollY = latestFrame.GetProperty("scrollY").GetDouble();
                var expectedPattern = (int)Math.Floor((scrollY + patternOffset) / 80) % 2 == 0 ? Color.FromArgb(208, 48, 64) : Color.FromArgb(32, 112, 192);
                Check(Math.Abs(pattern.R - expectedPattern.R) <= 3 && Math.Abs(pattern.G - expectedPattern.G) <= 3 && Math.Abs(pattern.B - expectedPattern.B) <= 3,
                    $"Composed pattern pixels follow the acknowledged real root position ({scrollY}, expected {expectedPattern}, actual {pattern})");
                Check(Math.Abs(fixedPixel.R - 24) <= 3 && Math.Abs(fixedPixel.G - 144) <= 3 && Math.Abs(fixedPixel.B - 96) <= 3,
                    "The presented fixed footer stays at the genuine viewport edge during a busy main thread");
                if (bottom > 4) Check(Math.Abs(fixedPixel.R - insetPixel.R) <= 3 && Math.Abs(fixedPixel.G - insetPixel.G) <= 3 && Math.Abs(fixedPixel.B - insetPixel.B) <= 3,
                    "The presented footer and obscured continuation remain aligned during native wheel input");
            }
            using var resultDocument = JsonDocument.Parse((await Web.EvaluateResultAsync("JSON.stringify(scrollingResult())"))!);
            var result = resultDocument.RootElement;
            var busy = result.GetProperty("busyIntervals").EnumerateArray().Last();
            var beginQpc = qpcAnchor + (busy.GetProperty("begin").GetInt64() - wallAnchor) / 1000d;
            var endQpc = qpcAnchor + (busy.GetProperty("end").GetInt64() - wallAnchor) / 1000d;
            var frames = sampledStates.SelectMany(state => state.GetProperty("webKit").GetProperty("frameTiming").GetProperty("scrollFrames").EnumerateArray())
                .Where(frame => frame.GetProperty("scrollRevision").GetUInt64() > 0
                    && frame.GetProperty("commit").GetDouble() > beginQpc + .05
                    && frame.GetProperty("paintEnd").GetDouble() < endQpc - .05)
                .DistinctBy(frame => frame.GetProperty("frameId").GetUInt64()).ToArray();
            var initialDomAfterBusy = result.Clone();
            var convergence = Stopwatch.StartNew();
            if (!blocking && frames.Length > 0)
            {
                var expectedScroll = frames.Last().GetProperty("scrollY").GetDouble();
                await WaitForScript("String(scrollY)", value => double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var position)
                    && Math.Abs(position - expectedScroll) < .05, "DOM convergence after the main thread resumes");
                using var converged = JsonDocument.Parse((await Web.EvaluateResultAsync("JSON.stringify(scrollingResult())"))!);
                result = converged.RootElement.Clone();
            }
            convergence.Stop();
            Save($"async-scroll-{caseName}-{blocking}", JsonSerializer.SerializeToElement(new { blocking, before, sampledStates, samples, result, initialDomAfterBusy,
                convergenceMilliseconds = convergence.Elapsed.TotalMilliseconds, beginQpc, endQpc, frames,
                scope = "Native input, independently acknowledged GPU frames and composed pixels during a measured busy interval; not arbitrary-site latency" }));
            if (blocking)
            {
                Check(frames.Length == 0, "A blocking wheel listener prevents speculative asynchronous movement");
                Check(result.GetProperty("scroll")[1].GetDouble() == 400, "preventDefault retains genuine document scroll position");
            }
            else
            {
                Check(frames.Length >= 2, "At least two GPU scroll frames are acknowledged while the Web process main thread is busy");
                Check(frames.All(frame => frame.GetProperty("sourceFrameId").GetUInt64() > 0
                    && frame.GetProperty("sourceFrameId").GetUInt64() != frame.GetProperty("frameId").GetUInt64()
                    && frame.GetProperty("inputSequence").GetUInt64() > 0), "Independent frames retain source paint identity and native input provenance");
                Check(frames.Any(frame => frame.GetProperty("scrollY").GetDouble() > 450), "The scrolling thread advances the actual root position during the busy interval");
                Check(result.GetProperty("scroll")[1].GetDouble() > 450, "The document catches up to the native asynchronous root position");
                Check(convergence.ElapsedMilliseconds < 500, "Native and DOM scroll state converge promptly after the busy interval");
            }
            Check(Math.Abs(result.GetProperty("fixed").GetProperty("bottom").GetDouble() - result.GetProperty("viewport")[1].GetDouble()) < .05,
                "Fixed layout remains anchored to the real final viewport");
            Check(Math.Abs(result.GetProperty("range").GetDouble() + result.GetProperty("viewport")[1].GetDouble() - 6000) < .05,
                "Asynchronous presentation does not fabricate document height or scroll range");
        }
        Console.WriteLine($"PASS async pose {caseName}");
    }
}
