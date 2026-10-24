#if WINDOWS
using System.Drawing;
using System.Runtime.InteropServices;

namespace DevDuo;

internal sealed partial class WindowsWebKitView
{
    private bool touchPointerVisible;
    private string? touchPointerError;
    private TouchPointerWindow? touchPointerWindow;
    private (Point Position, Rectangle Screen, int Diameter)? touchPointerFrame;

    internal void ObserveTouchPointer(ref System.Windows.Forms.Message message)
    {
        if (message.Msg == 0x200)
        {
            var tracking = new TrackMouse { Size = (uint)Marshal.SizeOf<TrackMouse>(), Flags = 2, Window = message.HWnd };
            TrackMouseEvent(ref tracking);
        }
        if (message.Msg is 0x200 or 0x20 or 0x2A3) UpdateTouchPointer();
        if (message.Msg == 0x8) HideTouchPointer();
    }
    internal void UpdateTouchPointer()
    {
        if (disposed || interactionMode != "touch" || composition is null || geometry is not { } g ||
            ScenePointFromScreen is null || !GetCursorPos(out var screen) || GetForegroundWindow() != GetAncestor(Handle, 2))
        { HideTouchPointer(); return; }
        var point = ScenePointFromScreen(screen);
        var screenBounds = Rectangle.FromLTRB(0, 0, RasterClipEdge(g.Width * g.Scale), RasterClipEdge(g.Height * g.Scale));
        if (!screenBounds.Contains(point) || PresentationBounds is null || !PresentationBounds(ClientRectangle).Contains(screen))
        { HideTouchPointer(); return; }
        var diameter = Math.Max(1, (int)Math.Round(20.0 * DeviceDpi / 96));
        var next = (point, screenBounds, diameter);
        if (!touchPointerVisible || touchPointerFrame != next)
        {
            try { composition.SetTouchPointer(point.X, point.Y, diameter, screenBounds); touchPointerError = null; }
            catch (InvalidOperationException failure) { touchPointerError = failure.Message; HideTouchPointer(); return; }
            touchPointerFrame = next;
        }
        if (!touchPointerVisible) { System.Windows.Forms.Cursor.Hide(); touchPointerVisible = true; }
    }
    internal void HideTouchPointer()
    {
        if (!touchPointerVisible) return;
        // Always restore the system cursor even if the composition device was lost.
        try { composition?.SetTouchPointer(0, 0, 0, Rectangle.Empty); }
        catch (InvalidOperationException failure) { touchPointerError = failure.Message; }
        finally { System.Windows.Forms.Cursor.Show(); touchPointerVisible = false; touchPointerFrame = null; }
    }
    private sealed class TouchPointerWindow : System.Windows.Forms.NativeWindow
    {
        private readonly Action hide;
        internal TouchPointerWindow(nint window, Action hide) { this.hide = hide; AssignHandle(window); }
        protected override void WndProc(ref System.Windows.Forms.Message message)
        {
            if (message.Msg == 0x1C && message.WParam == 0 || message.Msg == 0x6 && (message.WParam.ToInt64() & 0xffff) == 0 || message.Msg == 0x2)
                hide();
            base.WndProc(ref message);
        }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct TrackMouse { internal uint Size, Flags; internal nint Window; internal uint HoverTime; }
    [DllImport("user32.dll")] private static extern bool TrackMouseEvent(ref TrackMouse tracking);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
}
#endif
