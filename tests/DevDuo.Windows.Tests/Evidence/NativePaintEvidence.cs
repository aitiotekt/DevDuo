using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using DevDuo;

namespace DevDuo.Windows.Tests;

// A one-shot diagnostic of WebKit's software painter, before DWM and host chrome.
// These pixels are evidence only and are never supplied to the compositor.
internal static class NativePaintEvidence
{
    internal static void Save(WindowsWebKitView browser, JsonElement state, string prefix)
    {
        using var bitmap = Capture(browser, state);
        bitmap.Save(prefix + "-native-paint.png", ImageFormat.Png);
        var flow = state.GetProperty("webKit").GetProperty("continuation").GetProperty("validFlowRectanglePx");
        var points = new List<object>();
        foreach (var edge in new[] { flow[0].GetInt32(), flow[2].GetInt32() })
            for (var x = Math.Max(0, edge - 3); x < Math.Min(bitmap.Width, edge + 3); x++)
                foreach (var y in new[] { Math.Min(bitmap.Height - 1, 20), bitmap.Height / 2 })
                {
                    var pixel = bitmap.GetPixel(x, y);
                    points.Add(new { x, y, rgb = new[] { (int)pixel.R, pixel.G, pixel.B } });
                }
        File.WriteAllText(prefix + "-native-paint.json", JsonSerializer.Serialize(new { scale = browser.BackingScale, points }, new JsonSerializerOptions { WriteIndented = true }));
    }

    internal static Bitmap Capture(WindowsWebKitView browser, JsonElement state)
    {
        var frame = state.GetProperty("nativeWebViewFramePixels");
        var bitmap = new Bitmap(frame.GetProperty("width").GetInt32(), frame.GetProperty("height").GetInt32(), PixelFormat.Format32bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Magenta);
            var dc = graphics.GetHdc();
            try { SendMessage(browser.BrowserWindow, 0x318, dc, 4); }
            finally { graphics.ReleaseHdc(dc); }
        }
        return bitmap;
    }

    // Read the source's existing raster without asking WebKit to paint it. Unlike
    // WM_PRINTCLIENT, this can expose stale pixels retained by a resize/clip change.
    internal static Bitmap CaptureLiveRaster(WindowsWebKitView browser)
    {
        var bitmap = new Bitmap(browser.ClientSize.Width, browser.ClientSize.Height, PixelFormat.Format32bppRgb);
        var source = GetDC(browser.BrowserWindow);
        if (source == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Magenta);
            var target = graphics.GetHdc();
            try
            {
                if (!BitBlt(target, 0, 0, bitmap.Width, bitmap.Height, source, 0, 0, 0x00cc0020))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
            finally { graphics.ReleaseHdc(target); }
        }
        catch { bitmap.Dispose(); throw; }
        finally { ReleaseDC(browser.BrowserWindow, source); }
        return bitmap;
    }

    [DllImport("user32.dll")] private static extern nint SendMessage(nint window, int message, nint wParam, nint lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(nint target, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint operation);
}
