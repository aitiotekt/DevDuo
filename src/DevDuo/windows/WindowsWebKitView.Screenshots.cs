#if WINDOWS
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace DevDuo;

internal sealed partial class WindowsWebKitView
{
    internal async Task<string> ReadScreenshotStateAsync() =>
        await EvaluateAsync("JSON.stringify({document:performance.timeOrigin,url:location.href,width:innerWidth,height:innerHeight,x:scrollX,y:scrollY,visual:window.visualViewport?[visualViewport.width,visualViewport.height,visualViewport.pageLeft,visualViewport.pageTop,visualViewport.scale]:null})")
        ?? throw new ControlApiException(409, "Page geometry is unavailable.");

    internal NativeScreenshot CaptureViewport()
    {
        if (disposed || geometry is not { } g || browserWindow == 0)
            throw new ControlApiException(409, "The native viewport is unavailable.");
        if (Width < 1 || Height < 1 || (long)Width * Height > 32_000_000)
            throw new ControlApiException(409, "The native drawing frame is empty or exceeds 32 million pixels.");
        // Read the native offscreen painter without resizing, scrolling or changing insets.
        using var source = new Bitmap(Width, Height, PixelFormat.Format32bppRgb);
        using (var graphics = Graphics.FromImage(source))
        {
            graphics.Clear(rootColor);
            var dc = graphics.GetHdc();
            try { ScreenshotSendMessage(browserWindow, 0x318, dc, 4); } // WM_PRINTCLIENT / PRF_CLIENT
            finally { graphics.ReleaseHdc(dc); }
        }
        var rect = Rectangle.FromLTRB(RasterClipEdge(g.Left * g.Scale), RasterClipEdge(g.Top * g.Scale),
            RasterClipEdge((g.Width - g.Right) * g.Scale), RasterClipEdge((g.Height - g.Bottom) * g.Scale));
        if (rect.Width < 1 || rect.Height < 1 || !new Rectangle(Point.Empty, source.Size).Contains(rect))
            throw new ControlApiException(409, "The current native viewport frame is unavailable.");
        using var page = source.Clone(rect, PixelFormat.Format32bppRgb);
        using var stream = new MemoryStream();
        page.Save(stream, ImageFormat.Png);
        return new NativeScreenshot(stream.ToArray(), g.Width - g.Left - g.Right, g.Height - g.Top - g.Bottom, 1);
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint ScreenshotSendMessage(nint window, int message, nint wParam, nint lParam);
}
#endif
