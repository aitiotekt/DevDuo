using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DevDuo.Windows.Tests;

internal sealed partial class WindowsChecks
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SetCompositingPreference(nint preferences, [MarshalAs(UnmanagedType.I1)] bool enabled);

    private static void SetPageCompositing(DevDuo.WindowsWebKitView browser, bool enabled)
    {
        var configuration = (nint)typeof(DevDuo.WindowsWebKitView).GetField("configuration", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(browser)!;
        var library = NativeLibrary.Load(Path.Combine(DevDuo.WindowsWebKitApi.RuntimeDirectory(), "WebKit2.dll"));
        try
        {
            var set = Marshal.GetDelegateForFunctionPointer<SetCompositingPreference>(NativeLibrary.GetExport(library, "WKPreferencesSetAcceleratedCompositingEnabled"));
            set(DevDuo.WindowsWebKitApi.WKPageConfigurationGetPreferences(configuration), enabled);
        }
        finally { NativeLibrary.Free(library); }
    }

    private async Task CheckAtomicContinuation()
    {
        await Wait(state => Text(state, "initializationState", "ready"), "Atomic frame runtime");
        Select(Settings.Interaction, 1);
        foreach (var scale in new[] { 0, 1 })
        foreach (var pose in new[] { (Fold: 0, Angle: 0), (Fold: 1, Angle: 90), (Fold: 1, Angle: 270) })
        foreach (var accelerated in new[] { false, true })
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
            await Wait(state => Text(state, "initializationState", "ready"), "Atomic pose runtime");
            var browser = Web.WindowsPanel!.Browser;
            SetPageCompositing(browser, accelerated);
            var html = File.ReadAllText("tests/DevDuo.WebKitProbe/pages/frame-continuation.html");
            if (accelerated) html = html.Replace("const accelerated = false", "const accelerated = true");
            Web.WindowsPanel!.ShowHtml(html);
            await WaitForScript("String(frame > 5)", value => value == "true", "Changing fixed edge");
            await Wait(state => FixedExtension(state).GetProperty("bottomRgb").ValueKind == JsonValueKind.Array,
                "Frame-bound edge colors");
            var owner = PresentationGetAncestor(browser.Handle, 2);
            window.Activate();
            PresentationSetForegroundWindow(owner);
            await Task.Delay(100);
            var observed = new List<object>();
            for (var sample = 0; sample < 12; sample++)
            {
                if (sample % 3 == 0) NativeWheel(true);
                var state = State();
                var g = state.GetProperty("webKit").GetProperty("logicalGeometry");
                var width = g.GetProperty("width").GetInt32();
                var height = g.GetProperty("height").GetInt32();
                var top = g.GetProperty("top").GetInt32();
                var right = g.GetProperty("right").GetInt32();
                var bottom = g.GetProperty("bottom").GetInt32();
                var left = g.GetProperty("left").GetInt32();
                var points = new List<(string Side, Point Inside, Point Outside)>();
                if (top > 4) points.Add(("top", new(width / 2, top + 8), new(width / 2, top / 2)));
                if (right > 4) points.Add(("right", new(width - right - 8, height / 2), new(width - 8, height / 2)));
                if (bottom > 4) points.Add(("bottom", new(width / 2, height - bottom - 8), new(width / 2, height - 8)));
                if (left > 4) points.Add(("left", new(left + 8, height / 2), new(8, height / 2)));
                var bounds = browser.PresentationBounds!(browser.ClientRectangle);
                var capture = await Task.Run(() => DevDuo.WindowsWindowCapture.Capture(owner,
                    new Avalonia.PixelRect(bounds.X, bounds.Y, bounds.Width, bounds.Height)));
                using var stream = new MemoryStream(capture.Png);
                using var bitmap = new Bitmap(stream);
                // Open's top backdrop covers the whole inset. Verify that
                // edge in the native source, and record composed evidence
                // separately; do not accept a glass-tinted pixel as raw RGB.
                using var source = NativePaintEvidence.Capture(browser, state);
                Color Pixel(Point point, bool nativeSource)
                {
                    if (nativeSource) return source.GetPixel((int)Math.Round(point.X * browser.BackingScale),
                        (int)Math.Round(point.Y * browser.BackingScale));
                    var native = browser.PresentationBounds!(new Rectangle(
                        (int)Math.Round(point.X * browser.BackingScale), (int)Math.Round(point.Y * browser.BackingScale), 1, 1));
                    return bitmap.GetPixel(native.X - bounds.X, native.Y - bounds.Y);
                }
                foreach (var point in points)
                {
                    var nativeSource = point.Side == "top";
                    var inside = Pixel(point.Inside, nativeSource);
                    var outside = Pixel(point.Outside, nativeSource);
                    var delta = new[] { Math.Abs(inside.R - outside.R), Math.Abs(inside.G - outside.G), Math.Abs(inside.B - outside.B) }.Max();
                    observed.Add(new { sample, point.Side, source = nativeSource ? "presented page framebuffer" : "composed window",
                        inside = new[] { (int)inside.R, inside.G, inside.B }, outside = new[] { (int)outside.R, outside.G, outside.B }, delta });
                    if (delta > 3)
                    {
                        bitmap.Save(Path.Combine(Program.Output, "atomic-frame-failed.png"));
                        Save("atomic-frame-failed", JsonSerializer.SerializeToElement(new { observed, state }));
                    }
                    Check(delta <= 3, (nativeSource ? "Native" : "Presented") + " animated page and " + point.Side + " continuation come from the same frame");
                }
                if (sample is 0 or 11) bitmap.Save(Path.Combine(Program.Output, $"atomic-window-{scale}-{pose.Fold}-{pose.Angle}-{accelerated}-{sample}.png"));
            }
            var final = State();
            var actualGpuFrames = final.GetProperty("webKit").GetProperty("frameTiming").GetProperty("frames").EnumerateArray()
                .Count(frame => frame.GetProperty("gpuEnd").GetDouble() > 0);
            Check(accelerated ? actualGpuFrames > 0 : actualGpuFrames == 0,
                "The requested compositing path is confirmed by actual GPU-stage frame timestamps");
            var surface = final.GetProperty("webKit").GetProperty("frameSurface");
            Check(surface.GetProperty("gdiUploads").GetUInt64() == 0,
                "Native page presentation does not upload a GDI backing store");
            Check(surface.GetProperty("lastContentTransport").GetString() == (accelerated ? "shared-d3d11" : "native-cpu-bitmap"),
                "Actual page transport matches the verified rendering mode");
            var page = final.GetProperty("webKit").GetProperty("composition").GetProperty("resources").GetProperty("page");
            Check(page.GetProperty("presentations").GetUInt64() > 0
                && page.GetProperty("frameId").GetUInt64() == surface.GetProperty("presentedFrameId").GetUInt64()
                && page.GetProperty("documentEpoch").GetUInt64() == surface.GetProperty("presentedDocumentEpoch").GetUInt64(),
                "The presented GPU framebuffer acknowledges the same leased WebKit frame");
            await Web.EvaluateResultAsync("window.pauseFixture=true;edge.style.background='linear-gradient(to bottom,rgb(208,48,64) 50%,rgb(32,112,192) 50%)';'paused'");
            await Task.Delay(250);
            var orientationState = State();
            var geometry = orientationState.GetProperty("webKit").GetProperty("logicalGeometry");
            var gx = geometry.GetProperty("left").GetInt32();
            var gy = geometry.GetProperty("top").GetInt32();
            var gw = geometry.GetProperty("width").GetInt32() - gx - geometry.GetProperty("right").GetInt32();
            var gh = geometry.GetProperty("height").GetInt32() - gy - geometry.GetProperty("bottom").GetInt32();
            var orientationBounds = browser.PresentationBounds!(browser.ClientRectangle);
            var orientationCapture = await Task.Run(() => DevDuo.WindowsWindowCapture.Capture(owner,
                new Avalonia.PixelRect(orientationBounds.X, orientationBounds.Y, orientationBounds.Width, orientationBounds.Height)));
            using var orientationStream = new MemoryStream(orientationCapture.Png);
            using var orientationWindow = new Bitmap(orientationStream);
            using var orientationSource = NativePaintEvidence.Capture(browser, orientationState);
            orientationWindow.Save(Path.Combine(Program.Output, $"atomic-orientation-{scale}-{pose.Fold}-{pose.Angle}-{accelerated}.png"));
            orientationSource.Save(Path.Combine(Program.Output, $"atomic-orientation-source-{scale}-{pose.Fold}-{pose.Angle}-{accelerated}.png"));
            foreach (var (fraction, expected) in new[] { (.25, Color.FromArgb(208, 48, 64)), (.75, Color.FromArgb(32, 112, 192)) })
            {
                var px = (int)Math.Round((gx + gw / 2d) * browser.BackingScale);
                var py = (int)Math.Round((gy + gh * fraction) * browser.BackingScale);
                var native = browser.PresentationBounds!(new Rectangle(px, py, 1, 1));
                foreach (var actual in new[] { orientationSource.GetPixel(px, py),
                    orientationWindow.GetPixel(native.X - orientationBounds.X, native.Y - orientationBounds.Y) })
                    Check(Math.Abs(actual.R - expected.R) <= 3 && Math.Abs(actual.G - expected.G) <= 3 && Math.Abs(actual.B - expected.B) <= 3,
                        $"Asymmetric source and actual-window pixels preserve page direction and fractional scaling ({scale}/{pose}/{accelerated}, y={py}, expected={expected}, actual={actual})");
            }
            orientationWindow.Save(Path.Combine(Program.Output, $"atomic-orientation-{scale}-{pose.Fold}-{pose.Angle}-{accelerated}.png"));
            Save($"atomic-frame-{scale}-{pose.Fold}-{pose.Angle}-{accelerated}", JsonSerializer.SerializeToElement(new { observed, actualGpuFrames, state = final }));
        }
        Console.WriteLine($"PASS atomic continuation: {assertions} edge checks at current DPI, two scales and Closed/Open 90/270; obscured top sampled in native source.");
    }
}
