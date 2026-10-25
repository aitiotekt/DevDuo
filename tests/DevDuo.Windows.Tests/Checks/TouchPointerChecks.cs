using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json;
using Avalonia.Controls;

namespace DevDuo.Windows.Tests;

internal sealed partial class WindowsChecks
{
    private async Task CheckTouchPointerDamage()
    {
        await Wait(state => Text(state, "initializationState", "ready"), "Touch pointer runtime");
        window.WindowState = WindowState.Maximized;
        Select(Settings.Scale, 1);
        Settings.TabStrip.IsChecked = true;
        var saved = System.Windows.Forms.Cursor.Position;
        try
        {
            foreach (var (fold, angle) in new[] { (1, 90), (1, 270), (0, 0), (0, 180) })
            {
                Select(Settings.Fold, fold);
                while (State().GetProperty("bodyRotationDegrees").GetInt32() != angle) Rotate();
                await Wait(state => Text(state, "initializationState", "ready"), "Touch pointer scene");
                Web.WindowsPanel!.ShowHtml("""
                    <!doctype html><meta name="viewport" content="width=device-width,initial-scale=1">
                    <title>Touch pointer damage</title><style>
                    html { background:#e4eaf2 } body { margin:0;height:2400px;
                    background:linear-gradient(120deg,#48c9a2,#7851b0) }
                    #fixed { position:fixed;bottom:0;left:20px;width:80px;height:12px;background:#fff }
                    </style><div id="fixed"></div>
                    """);
                await WaitForScript("document.title", value => value == "Touch pointer damage", "Static pointer fixture");
                await Web.EvaluateResultAsync("scrollTo(0,320)");
                await Task.Delay(650);
                var browser = Web.WindowsPanel.Browser;
                window.Activate();
                PresentationSetForegroundWindow(PresentationGetAncestor(browser.Handle, 2));
                var name = $"pointer-{fold}-{angle}";
                Save(name + "-before", State());
                var geometry = await PointerPageGeometry();
                var points = new List<Point>();
                foreach (var frame in browser.Chrome.Where(frame => frame.Kind is WindowsComposition.Kind.Top or WindowsComposition.Kind.Address))
                    foreach (var fraction in new[] { .15f, .35f, .60f, .85f })
                        points.Add(new Point((int)(frame.X + frame.Width * fraction), (int)(frame.Y + frame.Height * .5)));
                Check(points.Count >= 4, "The scene exposes actual navigation/address chrome frames.");
                Select(Settings.Interaction, 0);
                browser.HideTouchPointer();
                System.Windows.Forms.Cursor.Position = new Point(window.Position.X + 20, window.Position.Y + 100);
                await Task.Delay(100);
                using var baseline = CapturePointerScene(name + "-baseline");
                using var deviceBaseline = fold == 1 && angle == 90 ? await CapturePointerDevice(name + "-device-baseline") : null;
                var bounds = browser.PresentationBounds!(browser.ClientRectangle);
                var clocks = browser.Chrome.Where(frame => frame.Kind == WindowsComposition.Kind.Clock)
                    .Select(frame => browser.PresentationBounds!(Rectangle.Ceiling(new RectangleF(frame.X, frame.Y, frame.Width, frame.Height))))
                    .Select(frame => new Rectangle(frame.X - bounds.X, frame.Y - bounds.Y, frame.Width, frame.Height)).ToArray();
                var radius = (int)Math.Ceiling(12 * browser.DeviceDpi / 96.0);
                var visited = new List<Rectangle>();
                foreach (var point in points)
                {
                    var screen = browser.PresentationBounds!(new Rectangle(point, new Size(1, 1))).Location;
                    System.Windows.Forms.Cursor.Position = screen;
                    browser.UpdateTouchPointer();
                    await Task.Delay(60);
                    using var current = CapturePointerScene(name + "-move-" + visited.Count);
                    var area = new Rectangle(screen.X - bounds.X - radius, screen.Y - bounds.Y - radius, radius * 2 + 1, radius * 2 + 1);
                    Check(PointerPixelChanges(baseline, current, area) > 30, "The presented circle actually paints at its current position.");
                    foreach (var previous in visited.Where(previous => !previous.IntersectsWith(area)))
                        Check(PointerPixelChanges(baseline, current, previous, clocks) <= 2, "Moving the pointer restores pixels at every previous chrome position.");
                    visited.Add(area);
                }
                var pendingBefore = State().GetProperty("webKit").GetProperty("touchPointer");
                var lastPoint = points[0];
                for (var sweep = 0; sweep < 4; sweep++)
                {
                    var from = points[sweep % 2 == 0 ? 0 : 3];
                    var to = points[sweep % 2 == 0 ? 3 : 0];
                    for (var step = 0; step <= 96; step++)
                    {
                        var point = new Point(from.X + (to.X - from.X) * step / 96, from.Y + (to.Y - from.Y) * step / 96);
                        System.Windows.Forms.Cursor.Position = browser.PresentationBounds!(new Rectangle(point, new Size(1, 1))).Location;
                        PresentationSendMessage(browser.BrowserWindow, 0x200, 0,
                            (nint)((point.X & 0xffff) | ((point.Y & 0xffff) << 16)));
                        lastPoint = point;
                    }
                }
                var pending = State().GetProperty("webKit").GetProperty("touchPointer");
                Check(pending.GetProperty("pending").GetBoolean() &&
                    pending.GetProperty("commits").GetInt64() == pendingBefore.GetProperty("commits").GetInt64(),
                    "A synchronous burst queues one latest-position update instead of one GPU commit per mouse message.");
                await Task.Delay(80);
                var flushed = State().GetProperty("webKit").GetProperty("touchPointer");
                var expectedPoint = browser.ScenePointFromScreen!(System.Windows.Forms.Cursor.Position);
                Save(name + "-burst", JsonSerializer.SerializeToElement(new { pendingBefore, pending, flushed, lastPoint, expectedPoint }));
                Check(!flushed.GetProperty("pending").GetBoolean() && flushed.GetProperty("frameX").GetInt32() == expectedPoint.X &&
                    flushed.GetProperty("frameY").GetInt32() == expectedPoint.Y,
                    "The scheduled update reads the latest cursor position and drains after motion stops.");
                using (var settled = CapturePointerScene(name + "-burst"))
                {
                    var lastScreen = browser.PresentationBounds!(new Rectangle(lastPoint, new Size(1, 1))).Location;
                    var lastArea = new Rectangle(lastScreen.X - bounds.X - radius, lastScreen.Y - bounds.Y - radius, radius * 2 + 1, radius * 2 + 1);
                    foreach (var previous in visited.Where(previous => !previous.IntersectsWith(lastArea)))
                        Check(PointerPixelChanges(baseline, settled, previous, clocks) <= 2, "A burst leaves no old circles over glass once the latest frame is presented.");
                    if (deviceBaseline is not null)
                    {
                        using var device = await CapturePointerDevice(name + "-device-pointer");
                        Check(PointerPixelChanges(deviceBaseline, device, lastArea) > 30,
                            "Native device screenshots retain the independently committed touch pointer.");
                    }
                }
                var cancelPoint = points[1];
                System.Windows.Forms.Cursor.Position = browser.PresentationBounds!(new Rectangle(cancelPoint, new Size(1, 1))).Location;
                PresentationSendMessage(browser.BrowserWindow, 0x200, 0, (nint)((cancelPoint.X & 0xffff) | ((cancelPoint.Y & 0xffff) << 16)));
                Select(Settings.Interaction, 1);
                await Task.Delay(100);
                var cancelled = State().GetProperty("webKit").GetProperty("touchPointer");
                Check(!cancelled.GetProperty("pending").GetBoolean() && !cancelled.GetProperty("visible").GetBoolean(),
                    "Switching to mouse cancels pending updates without resurrecting the circle.");
                using var hidden = CapturePointerScene(name + "-mouse");
                foreach (var previous in visited)
                    Check(PointerPixelChanges(baseline, hidden, previous, clocks) <= 2, "Mouse mode removes every touch pointer pixel.");
                Check(await PointerPageGeometry() == geometry, "Pointer movement/mode changes preserve document, viewport, fixed geometry and scroll range.");
                Save(name + "-after", State());
            }
        }
        finally { System.Windows.Forms.Cursor.Position = saved; Web.WindowsPanel?.Browser.UpdateTouchPointer(); }
        Save("touch-pointer-result", JsonSerializer.SerializeToElement(new { assertions, passed = true, dpi = State().GetProperty("platform"), source = "Actual foreground desktop pixels; static page; no forced page repaint" }));
        Console.WriteLine($"PASS touch pointer: {assertions} assertions; chrome movement, old-position restoration, mouse mode and unchanged page geometry.");
    }

    private Task<string?> PointerPageGeometry() => Web.EvaluateResultAsync("JSON.stringify({origin:performance.timeOrigin,size:[innerWidth,innerHeight],scroll:[scrollX,scrollY],range:[document.scrollingElement.scrollWidth,document.scrollingElement.scrollHeight],fixed:document.querySelector('#fixed').getBoundingClientRect().toJSON()})");

    private Bitmap CapturePointerScene(string name)
    {
        var browser = Web.WindowsPanel!.Browser;
        var bounds = browser.PresentationBounds!(browser.ClientRectangle);
        Check(PresentationGetForegroundWindow() == PresentationGetAncestor(browser.Handle, 2) &&
            System.Windows.Forms.SystemInformation.VirtualScreen.Contains(bounds), "Pointer pixel checks require the complete foreground scene on screen.");
        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppRgb);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        bitmap.Save(Path.Combine(Program.Output, name + ".png"), ImageFormat.Png);
        return bitmap;
    }

    private async Task<Bitmap> CapturePointerDevice(string name)
    {
        var browser = Web.WindowsPanel!.Browser;
        var bounds = browser.PresentationBounds!(browser.ClientRectangle);
        var owner = PresentationGetAncestor(browser.Handle, 2);
        var capture = await Task.Run(() => WindowsWindowCapture.Capture(owner, new Avalonia.PixelRect(bounds.X, bounds.Y, bounds.Width, bounds.Height)));
        File.WriteAllBytes(Path.Combine(Program.Output, name + ".png"), capture.Png);
        using var stream = new MemoryStream(capture.Png);
        using var decoded = new Bitmap(stream);
        return new Bitmap(decoded);
    }

    private static int PointerPixelChanges(Bitmap before, Bitmap after, Rectangle area, Rectangle[]? excluded = null)
    {
        area.Intersect(new Rectangle(Point.Empty, before.Size));
        var changed = 0;
        for (var y = area.Top; y < area.Bottom; y++)
            for (var x = area.Left; x < area.Right; x++)
            {
                if (excluded?.Any(rect => rect.Contains(x, y)) == true) continue;
                var a = before.GetPixel(x, y);
                var b = after.GetPixel(x, y);
                if (Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B))) > 3) changed++;
            }
        return changed;
    }
}
