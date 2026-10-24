#if WINDOWS
using System.Runtime.InteropServices;
using Avalonia;
using SkiaSharp;

namespace DevDuo;

internal static class WindowsWindowCapture
{
    internal static NativeScreenshot Capture(nint window, PixelRect rect)
    {
        if (window == 0 || rect.Width < 1 || rect.Height < 1 || (long)rect.Width * rect.Height > 32_000_000)
            throw new ControlApiException(409, "The device window or screenshot dimensions are unavailable.");
        var result = DDWindowCapture(window, rect.X, rect.Y, rect.Width, rect.Height, out var pixels);
        if (result < 0)
            throw new ControlApiException(409, Marshal.PtrToStringUni(DDWindowCaptureError()) ?? "Windows Graphics Capture failed.");
        try
        {
            using var bitmap = new SKBitmap(new SKImageInfo(rect.Width, rect.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
            var row = new byte[rect.Width * 4];
            for (var y = 0; y < rect.Height; y++)
            {
                Marshal.Copy(pixels + y * row.Length, row, 0, row.Length);
                Marshal.Copy(row, 0, bitmap.GetPixels() + y * bitmap.RowBytes, row.Length);
            }
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return new NativeScreenshot(data.ToArray(), rect.Width, rect.Height, GetDpiForWindow(window) / 96.0);
        }
        finally { Marshal.FreeCoTaskMem(pixels); }
    }

    [DllImport("DevDuo.Windows.Compositor.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int DDWindowCapture(nint window, int x, int y, int width, int height, out nint pixels);
    [DllImport("DevDuo.Windows.Compositor.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint DDWindowCaptureError();
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
}
#endif
