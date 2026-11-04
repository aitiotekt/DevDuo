using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DevDuo.Windows.Tests;

internal sealed partial class WindowsChecks
{
    private readonly List<string> modalPresentationFailures = [];
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    private async Task CheckModalSequence()
    {
        await Wait(state => Text(state, "initializationState", "ready"), "Modal runtime");
        window.WindowState = WindowState.Normal;
        window.Width = 1100;
        window.Height = 720;
        window.Activate();
        Select(Settings.Scale, 1);
        var poses = new[] { (Fold: 0, Angle: 0), (Fold: 1, Angle: 90), (Fold: 1, Angle: 270) };
        if (Environment.GetEnvironmentVariable("DEVDUO_TEST_MODAL_BODY_ANGLE") is { } requested)
        {
            if (!int.TryParse(requested, out var angle) || !poses.Any(pose => pose.Angle == angle))
                throw new ArgumentException("DEVDUO_TEST_MODAL_BODY_ANGLE must be 0, 90 or 270.");
            poses = poses.Where(pose => pose.Angle == angle).ToArray();
        }
        foreach (var pose in poses)
        {
            Select(Settings.Fold, pose.Fold);
            while (State().GetProperty("bodyRotationDegrees").GetInt32() != pose.Angle)
            {
                Settings.Rotate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(150);
            }
            await Wait(state => Text(state, "initializationState", "ready"), "Modal pose initialization");
            window.Activate();
            PresentationSetFocus(Web.WindowsPanel!.Browser.BrowserWindow);
            Web.WindowsPanel!.ShowHtml(File.ReadAllText("tests/DevDuo.WebKitProbe/pages/modal-sequence.html"));
            await WaitForScript("typeof setModal", value => value == "function", "Modal fixture");
            await Web.EvaluateResultAsync("scrollTo(0,200);'scrolled'");
            await Task.Delay(400);
            Check(State().GetProperty("webKit").GetProperty("backingScaleFactor").GetDouble() <
                State().GetProperty("platform").GetProperty("deviceDpi").GetDouble() / 96,
                "The Fit cohort uses a genuinely fractional presentation scale.");
            var identity = await Web.EvaluateResultAsync("modalIdentity()");
            var label = $"modal-{pose.Fold}-{pose.Angle}";
            foreach (var mode in new[] { "deep", "opacity", "transparent-wrapper", "pointer-none", "shadow", "iframe" })
            {
                Check(await Web.EvaluateResultAsync($"setModal('{mode}')") == mode, mode + " fixture executes in the current document");
                // Wait past the next paint/sample; adjacent fixtures deliberately
                // share a color, so a previous accepted value is not evidence.
                await Task.Delay(650);
                Save(label + "-" + mode + "-document", JsonSerializer.SerializeToElement(new {
                    observation = await Web.EvaluateResultAsync("JSON.stringify({origin:performance.timeOrigin,visibility:document.visibilityState,focused:document.hasFocus(),flow:document.querySelector('#flow')?.style.cssText,overlay:document.querySelector('.overlay')?.outerHTML,parentStyle:document.querySelector('.overlay')?.parentElement.style.cssText,parentOpacity:document.querySelector('.overlay')?getComputedStyle(document.querySelector('.overlay').parentElement).opacity:null})") }));
                await RecordModalFrames(label + "-" + mode, 1);
                // Upstream may select a CSS background before painting. That
                // scalar is distinct from the ancestor-alpha viewport pixel.
                var expected = new[] { 40, 80, 160 };
                await Wait(state => ModalColorMatches(state, expected), mode + " edge colors");
                CheckModalViewportPixel(label + "-" + mode + "-00.png", mode == "opacity" ? [140, 160, 200] : expected);
                Check(await Web.EvaluateResultAsync("modalIdentity()") == identity, mode + " preserves document geometry");
                Save(label + "-" + mode, State());
            }
            var before = State().GetProperty("webKit").GetProperty("composition").GetProperty("updates");
            var clockBefore = Web.WindowsPanel!.Browser.Chrome.FirstOrDefault(item => item.Kind == WindowsComposition.Kind.Clock).Text;
            await Web.EvaluateResultAsync("swapModal()");
            await RecordModalFrames(label + "-swap", 18);
            await Wait(state => ModalColorMatches(state, [160, 80, 40]), "Second popup replaces the first color");
            await Task.Delay(650);
            var after = State().GetProperty("webKit").GetProperty("composition").GetProperty("updates");
            Check(before.GetProperty("structure").GetUInt64() == after.GetProperty("structure").GetUInt64(),
                "Color changes retain glass visual structure.");
            var clockAfter = Web.WindowsPanel!.Browser.Chrome.FirstOrDefault(item => item.Kind == WindowsComposition.Kind.Clock).Text;
            var clockUpdates = clockBefore == clockAfter ? 0UL : 1UL;
            Check(before.GetProperty("foreground").GetUInt64() + clockUpdates == after.GetProperty("foreground").GetUInt64(),
                "Color changes retain text and icon pixels; only an actual clock-minute change redraws the foreground.");
            Check(await Web.EvaluateResultAsync("modalIdentity()") == identity, "Popup replacement preserves geometry");
            await Web.EvaluateResultAsync("crossfadeModal()");
            await RecordModalFrames(label + "-crossfade", 12);
            await Wait(state => ModalColorMatches(state, [160, 80, 40]), "Overlapping animation reaches the final popup color");
            await Web.EvaluateResultAsync("setModal('none')");
            await Wait(state => FixedExtension(state).GetProperty("bottomRgb").ValueKind == JsonValueKind.Null,
                "Closing all popups releases their extension");

            if (pose.Fold == 0)
            {
                await Web.EvaluateResultAsync("setModal('transparent-wrapper')");
                await Wait(state => ModalColorMatches(state, [40, 80, 160]), "Reusable popup initially blue");
                await Web.EvaluateResultAsync("document.querySelector('.overlay').firstElementChild.style.background='rgb(160,80,40)';'changed'");
                await Wait(state => ModalColorMatches(state, [160, 80, 40]), "Same container reads current descendant paint");
                Check(await Web.EvaluateResultAsync("modalIdentity()") == identity, "Descendant paint preserves geometry");
                Save(label + "-same-container", State());
                foreach (var mode in new[] { "transform", "filter", "contain", "partial", "mixed", "image" })
                {
                    await Web.EvaluateResultAsync($"setModal('{mode}')");
                    await Task.Delay(1000);
                    var state = State();
                    Check(FixedExtension(state).GetProperty("bottomRgb").ValueKind == JsonValueKind.Null,
                        mode + " must not invent a full-width solid extension");
                    Save(label + "-" + mode, state);
                }
                await Web.EvaluateResultAsync("setModal('dialog')");
                await Wait(state => ModalColorMatches(state, [40, 80, 160]), "Dialog backdrop edge");
            }
            await Web.EvaluateResultAsync("setModal('deep')");
            await Wait(state => ModalColorMatches(state, [40, 80, 160]), "Final static popup");
            await Task.Delay(1200);
            var stable = State();
            await Task.Delay(200);
            await RecordModalFrames(label + "-static", 8);
            var settled = State();
            Check(stable.GetProperty("contentColor").GetProperty("acceptedTargets").GetInt64() ==
                settled.GetProperty("contentColor").GetProperty("acceptedTargets").GetInt64(), "Static color does not oscillate");
            Check(!settled.GetProperty("contentColor").GetProperty("animating").GetBoolean(), "Static material animation stops");
            Save(label + "-settled", settled);
            window.WindowState = WindowState.Maximized;
            Select(Settings.Scale, 0);
            await Task.Delay(900);
            await Wait(state => ModalColorMatches(state, [40, 80, 160]), "Native-size popup colors");
            var nativeBrowser = Web.WindowsPanel!.Browser;
            if (System.Windows.Forms.SystemInformation.VirtualScreen.Contains(nativeBrowser.PresentationBounds!(nativeBrowser.ClientRectangle)))
                await RecordModalFrames(label + "-native-static", 6);
            else
                Save(label + "-native-clipped", JsonSerializer.SerializeToElement(new { status = "unverified", reason = "Full native-size scene does not fit the current desktop", host = State() }));
            Select(Settings.Scale, 1);
            window.WindowState = WindowState.Normal;
            window.Width = 1100;
            window.Height = 720;
        }
        await CheckRememberedDeveloperControls();
        Check(modalPresentationFailures.Count == 0, string.Join("; ", modalPresentationFailures));
        Console.WriteLine($"PASS modal sequence and service controls: {assertions} assertions; per-frame evidence in {Program.Output}");
    }

    private async Task CheckRememberedDeveloperControls()
    {
        Settings.ControlApiPort.Text = "invalid";
        Settings.ControlApi.IsChecked = true;
        await Task.Delay(500);
        Check(!new PreferenceStore(PreferenceStore.DefaultPath).Host.ControlApiEnabled,
            "Invalid listener input must not enable a previously disabled saved port.");
        Settings.ControlApiPort.Text = AvailableDeveloperPort().ToString();
        Settings.CdpPort.Text = AvailableDeveloperPort().ToString();
        Settings.NativePort.Text = AvailableDeveloperPort().ToString();
        Settings.ControlApi.IsChecked = true;
        Settings.Cdp.IsChecked = true;
        Settings.NativeRemote.IsChecked = true;
        await Wait(_ => !string.IsNullOrEmpty(Settings.ControlApiAddress.Text) &&
            !string.IsNullOrEmpty(Settings.CdpAddress.Text) && !string.IsNullOrEmpty(Settings.NativeRemoteAddress.Text), "Explicit services start");
        await Task.Delay(500);
        var saved = new PreferenceStore(PreferenceStore.DefaultPath).Host;
        Check(saved.ControlApiEnabled && saved.CdpEnabled && saved.NativeRemoteEnabled, "Explicit enablement is persisted");
        Settings.ControlApi.IsChecked = false;
        Settings.Cdp.IsChecked = false;
        Settings.NativeRemote.IsChecked = false;
        await Task.Delay(700);
        saved = new PreferenceStore(PreferenceStore.DefaultPath).Host;
        Check(!saved.ControlApiEnabled && !saved.CdpEnabled && !saved.NativeRemoteEnabled, "Explicit disablement clears startup intent");
        Settings.Cdp.IsChecked = true;
        Settings.ControlApi.IsChecked = true;
        Settings.ResetSettings.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Task.Delay(1000);
        saved = new PreferenceStore(PreferenceStore.DefaultPath).Host;
        Check(!saved.ControlApiEnabled && !saved.CdpEnabled && !saved.NativeRemoteEnabled &&
            Settings.ControlApi.IsChecked != true && Settings.Cdp.IsChecked != true &&
            string.IsNullOrEmpty(Settings.ControlApiAddress.Text) && string.IsNullOrEmpty(Settings.CdpAddress.Text),
            "Reset during asynchronous startup prevents late re-enablement");
    }

    private static bool ModalColorMatches(JsonElement state, int[] expected)
    {
        var colors = FixedExtension(state);
        return new[] { "top", "right", "bottom", "left" }.All(side =>
            colors.GetProperty(side + "Rgb") is var rgb && rgb.ValueKind == JsonValueKind.Array &&
            rgb.EnumerateArray().Select((channel, index) => Math.Abs(channel.GetInt32() - expected[index])).Max() <= 3);
    }

    private void CheckModalViewportPixel(string file, int[] expected)
    {
        var browser = Web.WindowsPanel!.Browser;
        var geometry = State().GetProperty("webKit").GetProperty("logicalGeometry");
        var left = geometry.GetProperty("left").GetDouble();
        var top = geometry.GetProperty("top").GetDouble();
        var x = (left + (geometry.GetProperty("width").GetDouble() - left - geometry.GetProperty("right").GetDouble()) / 2) * browser.BackingScale;
        var y = (top + (geometry.GetProperty("height").GetDouble() - top - geometry.GetProperty("bottom").GetDouble()) / 2) * browser.BackingScale;
        var scene = browser.PresentationBounds!(browser.ClientRectangle);
        var point = browser.PresentationBounds!(new Rectangle((int)x, (int)y, 1, 1)).Location;
        using var image = new Bitmap(Path.Combine(Program.Output, file));
        var pixel = image.GetPixel(point.X - scene.X, point.Y - scene.Y);
        Check(new[] { pixel.R, pixel.G, pixel.B }.Select((value, index) => Math.Abs(value - expected[index])).Max() <= 3,
            "Presented viewport pixels agree with the independently specified fixture color.");
    }

    private async Task RecordModalFrames(string name, int count)
    {
        var browser = Web.WindowsPanel!.Browser;
        var bounds = browser.PresentationBounds!(browser.ClientRectangle);
        Check(System.Windows.Forms.SystemInformation.VirtualScreen.Contains(bounds), "Presented fixture fits the desktop");
        var owner = PresentationGetAncestor(Web.WindowsPanel.Parent!.Handle, 2);
        var foregroundBefore = PresentationGetForegroundWindow();
        if (foregroundBefore != owner)
        {
            window.Activate();
            PresentationSetForegroundWindow(owner);
            await Task.Delay(200);
        }
        Save(name + "-activation", JsonSerializer.SerializeToElement(new { requiredActivation = foregroundBefore != owner }));
        Check(PresentationGetForegroundWindow() == owner, "Frame evidence belongs to the foreground application");
        using var initial = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppRgb);
        var staticFrames = name.EndsWith("static", StringComparison.Ordinal);
        var staticChanges = new List<int>();
        var clockAreas = State().GetProperty("chromeElements").EnumerateArray()
            .Where(element => element.GetProperty("kind").GetString() == "Clock")
            .Select(element => {
                var box = element.GetProperty("visualFrameSceneCssPx");
                var scale = browser.BackingScale;
                var rectangle = browser.PresentationBounds!(new Rectangle(
                    (int)(box.GetProperty("x").GetDouble() * scale) - 3,
                    (int)(box.GetProperty("y").GetDouble() * scale) - 3,
                    (int)Math.Ceiling(box.GetProperty("width").GetDouble() * scale) + 6,
                    (int)Math.Ceiling(box.GetProperty("height").GetDouble() * scale) + 6));
                rectangle.Offset(-bounds.X, -bounds.Y);
                return rectangle;
            }).ToArray();
        for (var index = 0; index < count; index++)
        {
            // Synchronize desktop readback with composition; do not request a
            // page repaint or mistake a queued pre-transition frame for idle output.
            Check(DwmFlush() >= 0, "Desktop composition reaches the capture barrier");
            using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppRgb);
            // Capture final composition, not GDI's layered-window readback.
            // Keep every frame, including the first, for temporal comparison.
            var capture = await Task.Run(() => WindowsWindowCapture.Capture(owner,
                new Avalonia.PixelRect(bounds.X, bounds.Y, bounds.Width, bounds.Height)));
            using (var stream = new MemoryStream(capture.Png))
            using (var decoded = new Bitmap(stream))
            using (var graphics = Graphics.FromImage(bitmap))
                graphics.DrawImageUnscaled(decoded, 0, 0);
            bitmap.Save(Path.Combine(Program.Output, $"{name}-{index:D2}.png"), ImageFormat.Png);
            Save($"{name}-{index:D2}", State());
            if (index == 0)
            {
                using var graphics = Graphics.FromImage(initial);
                graphics.DrawImageUnscaled(bitmap, 0, 0);
            }
            else if (staticFrames)
            {
                var changed = 0;
                for (var y = 4; y < bounds.Height - 4; y += 3)
                    for (var x = 4; x < bounds.Width - 4; x += 3)
                    {
                        if (clockAreas.Any(area => area.Contains(x, y))) continue;
                        var first = initial.GetPixel(x, y);
                        var current = bitmap.GetPixel(x, y);
                        if (Math.Max(Math.Abs(first.R - current.R), Math.Max(Math.Abs(first.G - current.G), Math.Abs(first.B - current.B))) > 3)
                            changed++;
                    }
                staticChanges.Add(changed);
            }
            await Task.Delay(40);
        }
        if (staticFrames)
        {
            Save(name + "-pixel-stability", JsonSerializer.SerializeToElement(new { changedPixels = staticChanges }));
            if (staticChanges.Any(changed => changed != 0))
                modalPresentationFailures.Add($"Static pixels changed: {name}, {string.Join(",", staticChanges)}");
            else Check(true, "Static presented pixels remain stable outside the clock");
        }
    }
}
