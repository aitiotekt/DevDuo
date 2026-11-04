using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using Avalonia.Controls;

namespace DevDuo.Windows.Tests;

internal sealed partial class WindowsChecks
{
    private async Task CheckNativeTouchTracking()
    {
        await Wait(state => Text(state, "initializationState", "ready"), "Touch tracking runtime");
        window.WindowState = WindowState.Maximized;
        Select(Settings.Scale, 1);
        Select(Settings.Interaction, 0);
        var panel = Web.WindowsPanel!;
        var browser = panel.Browser;
        SetPageCompositing(browser, true);
        panel.ShowHtml(File.ReadAllText("tests/DevDuo.WebKitProbe/pages/native-touch.html"));
        await WaitForScript("typeof nativeTouchResult", value => value == "function", "Touch tracking fixture");
        await Web.EvaluateResultAsync("scrollTo(0, 1200); 'positioned'");
        await WaitForScript("String(scrollY)", value => value == "1200", "Initial scroll position");
        var owner = PresentationGetAncestor(browser.Handle, 2);
        window.Activate();
        PresentationSetForegroundWindow(owner);
        await Task.Delay(200);
        Check(PresentationGetForegroundWindow() == owner, "Tracking checks require their independent window in front");

        var scale = browser.BackingScale;
        Check(scale > 1 && Math.Abs(scale - Math.Round(scale)) > .01, "Tracking checks require a fractional scale above one physical pixel per CSS pixel");
        const int cssX = 240;
        const int cssY = 380;
        var local = new Point((int)Math.Round(cssX * scale), (int)Math.Round(cssY * scale));
        var screen = browser.PresentationBounds!(new Rectangle(local, new Size(1, 1))).Location;
        var samples = new List<(int Offset, double Movement, int PointerY, double ScrollY, double? PointerSubmitMs, double? PageSubmitMs)>();
        var frequency = (double)Stopwatch.Frequency;
        long initialScrollFrameCommits = State().GetProperty("webKit").GetProperty("touchPointer").GetProperty("scrollFrameCommits").GetInt64();

        await InteractionPointer(0x201, cssX, cssY, 30);
        try
        {
            System.Windows.Forms.Cursor.Position = new Point(screen.X, screen.Y - 40);
            await Wait(state => state.GetProperty("webKit").GetProperty("nativeTouch").GetProperty("panning").GetBoolean() && Text(state, "chromeState", "compact"),
                "The native drag crosses slop and the shell reaches compact state");
            await Task.Delay(260);
            double? previousY = null;
            var offsets = Enumerable.Range(40, 17).Concat(Enumerable.Range(40, 16).Reverse());
            foreach (var offset in offsets)
            {
                var before = Stopwatch.GetTimestamp() / frequency;
                System.Windows.Forms.Cursor.Position = new Point(screen.X, screen.Y - offset);
                await Task.Delay(72);
                var webKit = State().GetProperty("webKit");
                var pointer = webKit.GetProperty("touchPointer");
                var pointerY = pointer.GetProperty("frameY").GetInt32();
                var pointerSubmit = pointer.GetProperty("submittedQpcSeconds").GetDouble();
                var independent = webKit.GetProperty("frameTiming").GetProperty("scrollFrames").EnumerateArray()
                    .Where(frame => frame.GetProperty("scrollRevision").GetUInt64() > 0).ToArray();
                var row = independent.Last();
                var firstSubmission = independent.FirstOrDefault(frame => frame.GetProperty("inputEvent").GetDouble() >= before);
                var y = row.GetProperty("scrollY").GetDouble();
                double? pageSubmitMs = firstSubmission.ValueKind == JsonValueKind.Object
                    ? (firstSubmission.GetProperty("paintEnd").GetDouble() - before) * 1000 : null;
                double? pointerSubmitMs = pointerSubmit >= before ? (pointerSubmit - before) * 1000 : null;
                samples.Add((offset, previousY is { } previous ? y - previous : 0, pointerY, y, pointerSubmitMs, pageSubmitMs));
                previousY = y;
            }
        }
        finally { await InteractionPointer(0x202, cssX, cssY, 20); }

        long scrollFrameCommits = State().GetProperty("webKit").GetProperty("touchPointer").GetProperty("scrollFrameCommits").GetInt64() - initialScrollFrameCommits;
        Save("native-touch-tracking", JsonSerializer.SerializeToElement(new
        {
            scale, scrollFrameCommits,
            samples = samples.Select(sample => new { sample.Offset, sample.Movement, sample.PointerY, sample.ScrollY, sample.PointerSubmitMs, sample.PageSubmitMs })
        }));
        Check(samples.All(sample => sample.PointerY == local.Y - sample.Offset), "The presented pointer reaches each physical test position");
        Check(samples.Zip(samples.Skip(1)).All(pair =>
            Math.Abs(pair.Second.Movement - (pair.Second.Offset - pair.First.Offset) / scale) <= .13),
            "Each physical-pixel step produces the corresponding fractional scroll, including reversal");
        Check(samples.Skip(1).All(sample => sample.PointerSubmitMs >= 0 && sample.PageSubmitMs >= 0),
            "Pointer and page submissions share the native input time base for each step");
        Check(scrollFrameCommits > 0, "Accepted asynchronous frames submit pending pointer movement before its fallback timer");
        Console.WriteLine($"PASS native touch tracking: {samples.Count} positions: {Program.Output}");
    }
}
