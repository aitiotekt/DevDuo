#if WINDOWS
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace DevDuo;

// A physical screen in the canvas. The child WebKit HWND remains in its measured
// scene coordinates; only its live composition output and input mapping rotate.
internal sealed class WindowsScreenPresenter : System.Windows.Forms.Panel
{
    private readonly WindowsWebViewPanel source;
    private DeviceProfile scene;
    private readonly nint holder;
    private readonly Action focusRequested;
    private QuarterTurn turn;
    private int offsetX, offsetY;
    private bool forwarding;

    internal WindowsScreenPresenter(WindowsWebViewPanel source, DeviceProfile scene, nint holder, Action focusRequested)
    {
        this.source = source;
        this.scene = scene;
        this.holder = holder;
        this.focusRequested = focusRequested;
        SetStyle(System.Windows.Forms.ControlStyles.Selectable, true);
        TabStop = true;
        Controls.Add(source);
        source.Browser.PresentationBounds = ScreenBounds;
        source.Browser.PointerMessage = ForwardPointer;
        source.PointerMessage = ForwardPointer;
    }

    internal void Configure(double width, double height, double x, double y, double scale, int degrees)
    {
        var rotated = degrees % 180 != 0;
        var physicalWidth = (int)Math.Round(width * scale);
        var physicalHeight = (int)Math.Round(height * scale);
        offsetX = (int)Math.Round(x * scale);
        offsetY = (int)Math.Round(y * scale);
        turn = new(rotated ? physicalHeight : physicalWidth, rotated ? physicalWidth : physicalHeight, degrees);
        // The topmost DComp tree belongs to this physical window. No sibling HWND
        // covers the source: ordinary WM_PAINT must still reach WebKit's live surface.
        source.Bounds = new(degrees == 0 ? -offsetX : 0, degrees == 0 ? -offsetY : 0, (int)turn.Width, (int)turn.Height);
        source.Browser.SetPresentation(degrees == 0 ? source.Browser.Handle : Handle, degrees,
            degrees == 0 ? 0 : offsetX, degrees == 0 ? 0 : offsetY);
        ApplyMask();
    }

    internal void UpdateScene(DeviceProfile geometry) => scene = geometry;

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        source?.Browser.InvalidatePresentation();
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        // A moved clip can expose source pixels that were previously outside an
        // ancestor's visible region even when the logical scene stays identical.
        source?.Browser.InvalidatePresentation();
    }

    protected override void WndProc(ref System.Windows.Forms.Message message)
    {
        if (turn.Degrees != 0 && source is not null && source.Browser.BrowserWindow is var browser && browser != 0)
        {
            if (message.Msg == 0x87) { message.Result = (nint)0x85; return; }
            if (message.Msg is >= 0x100 and <= 0x109 && !(message.Msg == 0x104 && message.WParam == (nint)0x73))
            {
                message.Result = SendMessage(browser, message.Msg, message.WParam, message.LParam);
                return;
            }
            // WebKit queries GetFocus(), so a notification alone is insufficient.
            // Avalonia focuses this physical host; delegate to the actual source HWND.
            if (message.Msg == 0x7)
            {
                SetFocus(browser);
                message.Result = 0;
                return;
            }
        }
        if (!ForwardPointer(ref message)) base.WndProc(ref message);
    }

    private void ApplyMask()
    {
        if (scene.ScreenMaskPath is null || turn.Width <= 0 || turn.Height <= 0) return;
        using var path = WindowsWebViewPanel.ParsePath(scene.ScreenMaskPath);
        using (var scale = new Matrix((float)(turn.Width / scene.MaskCanvasWidth), 0, 0, (float)(turn.Height / scene.MaskCanvasHeight), 0, 0))
            path.Transform(scale);
        var a = turn.Forward(0, 0);
        var b = turn.Forward(1, 0);
        var c = turn.Forward(0, 1);
        using (var matrix = new Matrix((float)(b.X - a.X), (float)(b.Y - a.Y), (float)(c.X - a.X), (float)(c.Y - a.Y),
            (float)a.X - offsetX, (float)a.Y - offsetY)) path.Transform(matrix);
        var previous = Region;
        Region = new Region(path);
        previous?.Dispose();
        var origin = new Point();
        MapWindowPoints(Handle, holder, ref origin, 1);
        using var region = Region.Clone();
        region.Translate(origin.X, origin.Y);
        using var graphics = Graphics.FromHwnd(holder);
        var native = region.GetHrgn(graphics);
        if (SetWindowRgn(holder, native, true) == 0)
        {
            var error = Marshal.GetLastWin32Error();
            DeleteObject(native);
            throw new Win32Exception(error);
        }
    }

    private Rectangle ScreenBounds(Rectangle rect)
    {
        var a = turn.Forward(rect.Left, rect.Top);
        var b = turn.Forward(rect.Right, rect.Bottom);
        var origin = PointToScreen(new Point((int)Math.Floor(Math.Min(a.X, b.X)) - offsetX, (int)Math.Floor(Math.Min(a.Y, b.Y)) - offsetY));
        return new(origin, new Size((int)Math.Ceiling(Math.Abs(b.X - a.X)), (int)Math.Ceiling(Math.Abs(b.Y - a.Y))));
    }

    private bool ForwardPointer(ref System.Windows.Forms.Message message)
    {
        if (turn.Degrees == 0 || forwarding || !(message.Msg is >= 0x200 and <= 0x20E || message.Msg == 0x7B)) return false;
        var browser = source.Browser.BrowserWindow;
        if (browser == 0) return false;
        var packed = message.LParam.ToInt64();
        var point = new Point((short)packed, (short)(packed >> 16));
        var screenCoordinates = message.Msg is 0x20A or 0x20E or 0x7B;
        var parameter = message.LParam;
        if (!(message.Msg == 0x7B && packed == -1))
        {
            if (screenCoordinates) point = PointToClient(point);
            else MapWindowPoints(message.HWnd, Handle, ref point, 1);
            var mapped = turn.Inverse(point.X + offsetX + .5, point.Y + offsetY + .5);
            point = new((int)Math.Floor(mapped.X), (int)Math.Floor(mapped.Y));
            if (screenCoordinates) ClientToScreen(browser, ref point);
            parameter = (nint)((point.X & 0xffff) | ((point.Y & 0xffff) << 16));
        }
        try
        {
            forwarding = true;
            var down = message.Msg is 0x201 or 0x204 or 0x207 or 0x20B;
            if (down) { focusRequested(); SetFocus(browser); }
            message.Result = SendMessage(browser, message.Msg, message.WParam, parameter);
        }
        finally { forwarding = false; }
        return true;
    }
    [DllImport("user32.dll")] private static extern nint SendMessage(nint window, int message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern nint SetFocus(nint window);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint window, ref Point point);
    [DllImport("user32.dll")] private static extern int MapWindowPoints(nint from, nint to, ref Point point, uint count);
    [DllImport("user32.dll", SetLastError = true)] private static extern int SetWindowRgn(nint window, nint region, bool redraw);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint handle);
}
#endif
